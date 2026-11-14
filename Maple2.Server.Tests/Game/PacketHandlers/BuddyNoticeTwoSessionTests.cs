using System;
using System.Collections.Generic;
using System.Linq;
using Grpc.Core;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The friend notices (Buddy sub-type 13) between two sessions that share one database: a friend request accepted or
// declined, or a friend removed, while the other player is offline is kept and sent at that player's next login.
public class BuddyNoticeTwoSessionTests {
    private const byte Request = 2;
    private const byte Accept = 3;
    private const byte Decline = 4;
    private const byte Delete = 7;

    private TestGameSession playerTest = null!;
    private TestGameSession otherTest = null!;
    private readonly BuddyHandler buddyHandler = new() { BanWordStorage = TestGameSession.BanWords() };
    private GameSession player => playerTest.Session;
    private GameSession other => otherTest.Session;
    // Whether the world finds each player online (on game server 1) or offline.
    private bool playerOnline = true;
    private bool otherOnline = true;
    // An unreachable world: each call throws.
    private bool worldDown;

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    [SetUp]
    public void Login() {
        playerOnline = true;
        otherOnline = true;
        worldDown = false;
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        playerTest = new TestGameSession(options);
        otherTest = new TestGameSession(options);
        foreach (TestGameSession test in new[] { playerTest, otherTest }) {
            test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { MaxBuddyCount = 100, MaxBlockCount = 100 });
            test.AddMap(TestGameSession.Map(2000023));
            test.Start();
            test.EnterMap(TestGameSession.Map(2000023));
        }
        foreach (TestGameSession test in new[] { playerTest, otherTest }) {
            test.World.AddPlayer(player.AccountId, player.CharacterId, player.PlayerName);
            test.World.AddPlayer(other.AccountId, other.CharacterId, other.PlayerName);
            test.World.BuddyWorld = Route;
            test.TakeSent();
        }
    }

    // As the world and the receiver's game server (ChannelService.Buddy): an offline receiver gets nothing.
    private BuddyResponse Route(BuddyRequest request) {
        if (worldDown) {
            throw new RpcException(new Status(StatusCode.Unavailable, "down"));
        }
        GameSession receiver = request.ReceiverId == player.CharacterId ? player : other;
        if (!(receiver == player ? playerOnline : otherOnline)) {
            return new BuddyResponse();
        }
        switch (request.BuddyCase) {
            case BuddyRequest.BuddyOneofCase.Invite:
                receiver.Buddy.ReceiveInvite(request.Invite.SenderId);
                break;
            case BuddyRequest.BuddyOneofCase.Accept:
                receiver.Buddy.ReceiveAccept(request.Accept.EntryId);
                break;
            case BuddyRequest.BuddyOneofCase.Decline:
                receiver.Buddy.ReceiveDecline(request.Decline.EntryId);
                break;
            case BuddyRequest.BuddyOneofCase.Delete:
                receiver.Buddy.ReceiveDelete(request.Delete.EntryId);
                break;
        }
        return new BuddyResponse { Channel = 1 };
    }

    private void Invite(GameSession session, string name) {
        buddyHandler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(Request);
            writer.WriteUnicodeString(name);
            writer.WriteUnicodeString("hi");
        }));
    }

    private void Answer(GameSession session, byte command, long entryId) {
        buddyHandler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            writer.WriteLong(entryId);
        }));
    }

    private long EntryId(long ownerId, long buddyId) {
        using GameStorage.Request db = playerTest.Storage.Context();
        return db.ListBuddies(ownerId).Single(entry => entry.BuddyId == buddyId).Id;
    }

    // The player logs in again: a new friend list read from the database, then the list packets.
    private string[] Relogin() {
        using (GameStorage.Request db = playerTest.Storage.Context()) {
            player.Buddy = new BuddyManager(db, player);
        }
        playerTest.TakeSent();
        player.Buddy.Load();
        return playerTest.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Buddy).Select(Convert.ToHexString).ToArray();
    }

    private void BecomeFriends() {
        Invite(player, other.PlayerName);
        Answer(other, Accept, EntryId(other.CharacterId, player.CharacterId));
        playerTest.TakeSent();
        otherTest.TakeSent();
    }

    [Test]
    public void Accept_WhileTheRequesterIsOffline_IsNoticedAtItsNextLogin() {
        Invite(player, other.PlayerName);
        long entryId = EntryId(player.CharacterId, other.CharacterId);
        playerOnline = false;

        Answer(other, Accept, EntryId(other.CharacterId, player.CharacterId));

        string[] sent = Relogin();
        string notice = Hex(BuddyPacket.Notices(new[] { new BuddyNotice(other.PlayerName, BuddyNoticeType.Approved, entryId) }));
        Assert.That(sent, Does.Contain(notice));
        // After the list: "approved" marks the friend in it.
        Assert.That(Array.IndexOf(sent, notice), Is.EqualTo(sent.Length - 1));
        // Sent once.
        Assert.That(Relogin(), Does.Not.Contain(notice));
    }

    [Test]
    public void Decline_WhileTheRequesterIsOffline_IsNoticedAtItsNextLogin() {
        Invite(player, other.PlayerName);
        long entryId = EntryId(player.CharacterId, other.CharacterId);
        playerOnline = false;

        Answer(other, Decline, EntryId(other.CharacterId, player.CharacterId));

        Assert.That(Relogin(), Does.Contain(
            Hex(BuddyPacket.Notices(new[] { new BuddyNotice(other.PlayerName, BuddyNoticeType.Declined, entryId) }))));
    }

    [Test]
    public void Delete_WhileTheFriendIsOffline_IsNoticedAtItsNextLogin() {
        BecomeFriends();
        long entryId = EntryId(player.CharacterId, other.CharacterId);
        playerOnline = false;

        Answer(other, Delete, EntryId(other.CharacterId, player.CharacterId));

        Assert.That(Relogin(), Does.Contain(
            Hex(BuddyPacket.Notices(new[] { new BuddyNotice(other.PlayerName, BuddyNoticeType.Removed, entryId) }))));
    }

    // An online player gets the change at once (Accept, Remove); nothing is kept for its next login.
    [Test]
    public void Changes_WhileOnline_KeepNoNotice() {
        BecomeFriends();
        Answer(other, Delete, EntryId(other.CharacterId, player.CharacterId));

        // The sub-type follows the two opcode bytes.
        Assert.That(Relogin().Select(hex => hex.Substring(4, 2)), Does.Not.Contain("0D"));
        using GameStorage.Request db = playerTest.Storage.Context();
        Assert.That(db.TakeBuddyNotices(player.CharacterId), Is.Empty);
    }

    // Several notices go in one packet, in the order they happened.
    [Test]
    public void Notices_KeptInOrder() {
        using (GameStorage.Request db = playerTest.Storage.Context()) {
            db.CreateBuddyNotice(player.CharacterId, new BuddyNotice("A", BuddyNoticeType.Removed, 5));
            db.CreateBuddyNotice(player.CharacterId, new BuddyNotice("B", BuddyNoticeType.Approved, 6));
        }

        Assert.That(Relogin(), Does.Contain(Hex(BuddyPacket.Notices(new List<BuddyNotice> {
            new("A", BuddyNoticeType.Removed, 5), new("B", BuddyNoticeType.Approved, 6),
        }))));
    }

    // World down after the change is stored: the session stays, the actor gets its answer, and the other player is
    // told at the next login, as when offline.
    [TestCase(Accept, BuddyNoticeType.Approved)]
    [TestCase(Decline, BuddyNoticeType.Declined)]
    [TestCase(Delete, BuddyNoticeType.Removed)]
    public void WorldDown_TheChangeIsNoticedAtTheNextLogin(byte command, BuddyNoticeType type) {
        if (command == Delete) {
            BecomeFriends();
        } else {
            Invite(player, other.PlayerName);
        }
        long entryId = EntryId(player.CharacterId, other.CharacterId);
        long otherEntryId = EntryId(other.CharacterId, player.CharacterId);
        otherTest.TakeSent();
        worldDown = true;

        Assert.DoesNotThrow(() => Answer(other, command, otherEntryId));

        Assert.That(otherTest.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Buddy), Is.Not.Empty);
        Assert.That(Relogin(), Does.Contain(Hex(BuddyPacket.Notices(new[] { new BuddyNotice(other.PlayerName, type, entryId) }))));
    }

    // A request, a block and a cancel with the world down are stored and answered; the session stays.
    [Test]
    public void WorldDown_RequestIsStoredAndAnswered() {
        worldDown = true;

        Assert.DoesNotThrow(() => Invite(player, other.PlayerName));

        Assert.That(playerTest.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Buddy).Select(Convert.ToHexString),
            Does.Contain(Hex(BuddyPacket.Invite(other.PlayerName, "hi"))));
        EntryId(player.CharacterId, other.CharacterId);
        EntryId(other.CharacterId, player.CharacterId);
    }

    [Test]
    public void WorldDown_CancelIsStored() {
        Invite(player, other.PlayerName);
        long entryId = EntryId(player.CharacterId, other.CharacterId);
        worldDown = true;

        Assert.DoesNotThrow(() => Answer(player, 17, entryId));

        using GameStorage.Request db = playerTest.Storage.Context();
        Assert.That(db.ListBuddies(player.CharacterId), Is.Empty);
        Assert.That(db.ListBuddies(other.CharacterId), Is.Empty);
    }

    [Test]
    public void WorldDown_BlockIsStored() {
        BecomeFriends();
        worldDown = true;

        Assert.DoesNotThrow(() => buddyHandler.Handle(player, TestGameSession.Packet(writer => {
            writer.WriteByte(5);
            writer.WriteLong(0);
            writer.WriteUnicodeString(other.PlayerName);
            writer.WriteUnicodeString("");
        })));

        using GameStorage.Request db = playerTest.Storage.Context();
        Assert.That(db.ListBuddies(player.CharacterId).Single().Type, Is.EqualTo(BuddyType.Blocked));
        Assert.That(db.ListBuddies(other.CharacterId), Is.Empty);
    }

    // A memo with a banned word (KR string/kr/banword.xml row 1) in a request, a block or a block edit: the client
    // checks it before sending (CheckBuddyMemo); one that arrives anyway is refused with the unknown error and
    // nothing is stored.
    [TestCase(2)]
    [TestCase(5)]
    [TestCase(10)]
    public void BannedMemo_IsRefused(byte command) {
        const string banned = "\uAC1C\uC0C8\uB07C";
        var handler = new BuddyHandler { BanWordStorage = TestGameSession.BanWords(banned) };
        long blockId = 0;
        if (command == 10) {
            handler.Handle(player, TestGameSession.Packet(writer => {
                writer.WriteByte(5);
                writer.WriteLong(0);
                writer.WriteUnicodeString(other.PlayerName);
                writer.WriteUnicodeString("old");
            }));
            blockId = EntryId(player.CharacterId, other.CharacterId);
            playerTest.TakeSent();
        }

        handler.Handle(player, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            if (command != 2) {
                writer.WriteLong(blockId);
            }
            writer.WriteUnicodeString(other.PlayerName);
            writer.WriteUnicodeString("a " + banned);
        }));

        string expected = command switch {
            2 => Hex(BuddyPacket.Invite(error: BuddyError.s_buddy_err_unknown)),
            5 => Hex(BuddyPacket.Block(error: BuddyError.s_buddy_err_unknown)),
            _ => Hex(BuddyPacket.UpdateBlock(error: BuddyError.s_buddy_err_unknown)),
        };
        Assert.That(playerTest.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Buddy).Select(Convert.ToHexString),
            Is.EqualTo(new[] { expected }));
        using GameStorage.Request db = playerTest.Storage.Context();
        BuddyEntry[] entries = db.ListBuddies(player.CharacterId).ToArray();
        if (command == 10) {
            Assert.That(entries.Single().Message, Is.EqualTo("old"));
        } else {
            Assert.That(entries, Is.Empty);
        }
        Assert.That(db.ListBuddies(other.CharacterId), Is.Empty);
    }
}
