using System;
using System.Linq;
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

// The block list between two sessions that share one database: what a block refuses (friend requests, mail) in both
// directions, and the lookup the party, guild and club invites use.
public class BuddyBlockTwoSessionTests {
    private const byte Request = 2;
    private const byte Block = 5;
    private const byte Unblock = 6;
    private const byte Cancel = 17;

    private TestGameSession playerTest = null!;
    private TestGameSession otherTest = null!;
    private readonly BuddyHandler buddyHandler = new() { BanWordStorage = TestGameSession.BanWords() };
    private readonly MailHandler mailHandler = new() { BanWordStorage = TestGameSession.BanWords() };
    private GameSession player => playerTest.Session;
    private readonly FailingSaves saves = new();
    private GameSession other => otherTest.Session;

    // Every save of the shared database fails while Fail is set.
    private sealed class FailingSaves : SaveChangesInterceptor {
        public bool Fail;

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) =>
            Fail ? throw new DbUpdateException("The test made this save fail") : result;
    }

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    [SetUp]
    public void Login() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(saves)
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
            test.TakeSent();
        }
    }

    private void BlockByName(GameSession session, string name) {
        buddyHandler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(Block);
            writer.WriteLong(0);
            writer.WriteUnicodeString(name);
            writer.WriteUnicodeString("memo");
        }));
    }

    private void SendMail(GameSession session, string name) {
        mailHandler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteUnicodeString(name);
            writer.WriteUnicodeString("hello");
            writer.WriteUnicodeString("text");
        }));
    }

    private void Invite(GameSession session, string name) {
        buddyHandler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(Request);
            writer.WriteUnicodeString(name);
            writer.WriteUnicodeString("hi");
        }));
    }

    private void SendById(GameSession session, byte command, long entryId) {
        buddyHandler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            writer.WriteLong(entryId);
        }));
    }

    private BuddyEntry Entry(long ownerId, long buddyId) {
        using GameStorage.Request db = playerTest.Storage.Context();
        return db.ListBuddies(ownerId).Single(entry => entry.BuddyId == buddyId);
    }

    // The Unblock answers: (entry id, result).
    private static (long, byte)[] Unblocks(TestGameSession test) => Of(test, SendOp.Buddy)
        .Where(packet => packet[2] == Unblock)
        .Select(packet => (BitConverter.ToInt64(packet, 4), packet[3]))
        .ToArray();

    private void UnblockOk(long entryId) {
        playerTest.TakeSent();
        SendById(player, Unblock, entryId);

        Assert.That(Unblocks(playerTest), Is.EqualTo(new[] { (entryId, (byte) 0) }));
        Assert.That(player.Buddy.IsBlocked(other.CharacterId), Is.False);
        Assert.That(Rows(player.CharacterId, other.CharacterId), Is.EqualTo(0));
    }

    private int Rows(long ownerId, long buddyId) {
        using GameStorage.Request db = playerTest.Storage.Context();
        return db.ListBuddies(ownerId).Count(entry => entry.BuddyId == buddyId);
    }

    private static byte[][] Of(TestGameSession test, SendOp op) => test.TakeSent().Where(packet => TestGameSession.Op(packet) == op).ToArray();

    // Party, guild and club invites ask by the other character's id; the list is keyed by the entry id.
    [Test]
    public void IsBlocked_AsksByTheCharacterId() {
        BlockByName(player, other.PlayerName);

        Assert.That(player.Buddy.IsBlocked(other.CharacterId), Is.True);
        Assert.That(other.Buddy.IsBlocked(player.CharacterId), Is.False);
        Assert.That(Rows(player.CharacterId, other.CharacterId), Is.EqualTo(1));
    }

    // The client only offers to block a player it has not blocked; a repeat by name adds no second entry.
    [Test]
    public void BlockByName_TwiceKeepsOneEntry() {
        BlockByName(player, other.PlayerName);
        playerTest.TakeSent();

        BlockByName(player, other.PlayerName);

        Assert.That(Of(playerTest, SendOp.Buddy), Is.Empty);
        Assert.That(Rows(player.CharacterId, other.CharacterId), Is.EqualTo(1));
    }

    // A friend request to a player the sender has blocked is refused; no request entry is made beside the block.
    [Test]
    public void Invite_ToABlockedPlayerIsRefused() {
        BlockByName(player, other.PlayerName);
        playerTest.TakeSent();

        buddyHandler.Handle(player, TestGameSession.Packet(writer => {
            writer.WriteByte(Request);
            writer.WriteUnicodeString(other.PlayerName);
            writer.WriteUnicodeString("hi");
        }));

        Assert.That(Of(playerTest, SendOp.Buddy).Select(Convert.ToHexString),
            Is.EqualTo(new[] { Hex(BuddyPacket.Invite(other.PlayerName, error: BuddyError.s_buddy_err_request_somebody)) }));
        Assert.That(Rows(player.CharacterId, other.CharacterId), Is.EqualTo(1));
        Assert.That(Rows(other.CharacterId, player.CharacterId), Is.EqualTo(0));
    }

    // OnMail error 27: the sender blocked the recipient.
    [Test]
    public void Mail_ToAPlayerTheSenderBlockedIsRefused() {
        BlockByName(player, other.PlayerName);
        playerTest.TakeSent();

        SendMail(player, other.PlayerName);

        Assert.That(Of(playerTest, SendOp.Mail).Select(Convert.ToHexString),
            Is.EqualTo(new[] { Hex(MailPacket.Error(MailError.s_mail_error_block_from_me, 1)) }));
        Assert.That(playerTest.World.MailNotifications, Is.Empty);
    }

    // OnMail error 28: the recipient blocked the sender.
    [Test]
    public void Mail_FromABlockedPlayerIsRefused() {
        BlockByName(other, player.PlayerName);

        SendMail(player, other.PlayerName);

        Assert.That(Of(playerTest, SendOp.Mail).Select(Convert.ToHexString),
            Is.EqualTo(new[] { Hex(MailPacket.Error(MailError.s_mail_error_block_from_other, 1)) }));
        Assert.That(playerTest.World.MailNotifications, Is.Empty);
        using GameStorage.Request db = playerTest.Storage.Context();
        Assert.That(db.GetSentMail(player.CharacterId), Is.Empty);
    }

    // A cancelled request leaves the list: a block by name then makes a block row, and the unblock removes it (it
    // used to take the cancelled entry, whose row was gone, and the unblock answered s_buddy_err_unknown).
    [Test]
    public void RequestCancelBlockUnblock() {
        Invite(player, other.PlayerName);
        long requestId = Entry(player.CharacterId, other.CharacterId).Id;
        SendById(player, Cancel, requestId);
        Assert.That(Rows(player.CharacterId, other.CharacterId), Is.EqualTo(0));
        Assert.That(Rows(other.CharacterId, player.CharacterId), Is.EqualTo(0));

        BlockByName(player, other.PlayerName);
        BuddyEntry block = Entry(player.CharacterId, other.CharacterId);
        Assert.That(block.Type, Is.EqualTo(BuddyType.Blocked));
        Assert.That(block.Id, Is.Not.EqualTo(requestId));
        Assert.That(player.Buddy.IsBlocked(other.CharacterId), Is.True);

        UnblockOk(block.Id);
    }

    // An entry whose row is gone (one left by a cancel before the fix) is not converted: the block makes a new row.
    [Test]
    public void BlockByName_EntryWithoutARowMakesANewRow() {
        Invite(player, other.PlayerName);
        BuddyEntry request = Entry(player.CharacterId, other.CharacterId);
        using (GameStorage.Request db = playerTest.Storage.Context()) {
            Assert.That(db.RemoveBuddy(request, Entry(other.CharacterId, player.CharacterId)), Is.True);
        }

        BlockByName(player, other.PlayerName);
        BuddyEntry block = Entry(player.CharacterId, other.CharacterId);
        Assert.That(block.Type, Is.EqualTo(BuddyType.Blocked));
        Assert.That(player.Buddy.IsBlocked(other.CharacterId), Is.True);

        UnblockOk(block.Id);
    }

    // A block whose write fails on an entry that has a row is refused, and the entry stays as it was: no second
    // (Blocked) row is made beside it, and a block after that converts the one row.
    [Test]
    public void BlockByName_FailedWriteOnARealRowIsRefused() {
        Invite(player, other.PlayerName);
        BuddyEntry request = Entry(player.CharacterId, other.CharacterId);
        playerTest.TakeSent();

        saves.Fail = true;
        BlockByName(player, other.PlayerName);
        saves.Fail = false;

        Assert.That(Of(playerTest, SendOp.Buddy).Select(Convert.ToHexString),
            Is.EqualTo(new[] { Hex(BuddyPacket.Block(error: BuddyError.s_buddy_err_unknown)) }));
        Assert.That(Rows(player.CharacterId, other.CharacterId), Is.EqualTo(1));
        BuddyEntry kept = Entry(player.CharacterId, other.CharacterId);
        Assert.That((kept.Id, kept.Type), Is.EqualTo((request.Id, request.Type)));
        Assert.That(player.Buddy.IsBlocked(other.CharacterId), Is.False);

        BlockByName(player, other.PlayerName);
        BuddyEntry block = Entry(player.CharacterId, other.CharacterId);
        Assert.That((block.Id, block.Type), Is.EqualTo((request.Id, BuddyType.Blocked)));
        Assert.That(player.Buddy.IsBlocked(other.CharacterId), Is.True);

        UnblockOk(block.Id);
    }

    // The block list read at login unblocks as the one made in the session.
    [Test]
    public void BlockRelogUnblock() {
        BlockByName(player, other.PlayerName);
        long blockId = Entry(player.CharacterId, other.CharacterId).Id;

        player.Buddy.Dispose();
        using (GameStorage.Request db = playerTest.Storage.Context()) {
            player.Buddy = new BuddyManager(db, player);
        }
        Assert.That(player.Buddy.IsBlocked(other.CharacterId), Is.True);

        UnblockOk(blockId);
    }
}
