using System;
using System.Linq;
using Grpc.Core;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// Names in friend requests, blocks and mail are looked up without case (GetCharacterId; the client's
// CBuddyManager::FindByName compares with CompareNoCase), so the own name or a listed name in another case is the
// same character. An accept checks the friend limit of both lists, answered with the client's errors 7 and 8
// (uibuddyeventlistener.cpp:1876-1905), and a refused accept, decline, unblock, remove or cancel is answered with
// its error (the client acts on these only for result 0, buddymanager.cpp:131-178).
public class BuddyNameLimitTests {
    private const byte Request = 2;
    private const byte Accept = 3;
    private const byte Decline = 4;
    private const byte Block = 5;
    private const byte Unblock = 6;
    private const byte Delete = 7;
    private const byte Cancel = 17;

    private readonly BuddyDeleteFault fault = new();
    private TestGameSession playerTest = null!;
    private TestGameSession otherTest = null!;
    private readonly BuddyHandler buddyHandler = new() { BanWordStorage = TestGameSession.BanWords() };
    private readonly MailHandler mailHandler = new();
    private GameSession player => playerTest.Session;
    private GameSession other => otherTest.Session;

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    [SetUp]
    public void Login() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(fault)
            .Options;
        playerTest = new TestGameSession(options);
        otherTest = new TestGameSession(options);
        foreach (TestGameSession test in new[] { playerTest, otherTest }) {
            Limit(test, 100);
            test.AddMap(TestGameSession.Map(2000023));
            test.Start();
            test.EnterMap(TestGameSession.Map(2000023));
        }
        // Names with letters, so that another case is another string.
        Rename(playerTest, "Alice");
        Rename(otherTest, "Bobby");
        foreach (TestGameSession test in new[] { playerTest, otherTest }) {
            test.World.AddPlayer(player.AccountId, player.CharacterId, player.PlayerName);
            test.World.AddPlayer(other.AccountId, other.CharacterId, other.PlayerName);
            test.World.BuddyWorld = Route;
            test.TakeSent();
        }
    }

    private static void Limit(TestGameSession test, int count) {
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { MaxBuddyCount = count, MaxBlockCount = 100 });
    }

    private static void Rename(TestGameSession test, string name) {
        using (var context = new Ms2Context(test.Options)) {
            context.Character.Find(test.CharacterId)!.Name = name;
            context.SaveChanges();
        }
        test.Session.Player.Value.Character.Name = name;
    }

    private BuddyResponse Route(BuddyRequest request) {
        GameSession receiver = request.ReceiverId == player.CharacterId ? player : other;
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
            case BuddyRequest.BuddyOneofCase.Block:
                receiver.Buddy.ReceiveBlock(request.Block.SenderId);
                break;
            case BuddyRequest.BuddyOneofCase.Delete:
                receiver.Buddy.ReceiveDelete(request.Delete.EntryId);
                break;
            case BuddyRequest.BuddyOneofCase.Cancel:
                receiver.Buddy.ReceiveCancel(request.Cancel.EntryId);
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

    private void BlockByName(GameSession session, string name) {
        buddyHandler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(Block);
            writer.WriteLong(0);
            writer.WriteUnicodeString(name);
            writer.WriteUnicodeString("");
        }));
    }

    private void Answer(GameSession session, byte command, long entryId) {
        buddyHandler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            writer.WriteLong(entryId);
        }));
    }

    private BuddyEntry[] Entries(long ownerId) {
        using GameStorage.Request db = playerTest.Storage.Context();
        return db.ListBuddies(ownerId).ToArray();
    }

    private long EntryId(long ownerId, long buddyId) => Entries(ownerId).Single(entry => entry.BuddyId == buddyId).Id;

    private static string[] BuddySent(TestGameSession test) =>
        test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Buddy).Select(Convert.ToHexString).ToArray();

    [Test]
    public void ARequestToTheOwnNameInAnotherCaseIsRefused() {
        Invite(player, "aLICE");

        Assert.That(BuddySent(playerTest), Is.EqualTo(new[] { Hex(BuddyPacket.Invite(error: BuddyError.s_buddy_err_my_id_ex)) }));
        Assert.That(Entries(player.CharacterId), Is.Empty);
    }

    [Test]
    public void BlockingTheOwnNameInAnotherCaseIsRefused() {
        BlockByName(player, "ALICE");

        Assert.That(BuddySent(playerTest), Is.EqualTo(new[] {
            Hex(BuddyPacket.Block(name: "ALICE", error: BuddyError.s_buddy_err_my_id_ex)),
        }));
        Assert.That(Entries(player.CharacterId), Is.Empty);
    }

    // The friend entry becomes the block entry; no second entry for the same character.
    [Test]
    public void BlockingAFriendByANameInAnotherCaseConvertsTheFriend() {
        Invite(player, "bobby");
        Answer(other, Accept, EntryId(other.CharacterId, player.CharacterId));
        long entryId = EntryId(player.CharacterId, other.CharacterId);
        playerTest.TakeSent();

        BlockByName(player, "BOBBY");

        BuddyEntry entry = Entries(player.CharacterId).Single();
        Assert.Multiple(() => {
            Assert.That((entry.Id, entry.Type), Is.EqualTo((entryId, BuddyType.Blocked)));
            Assert.That(Entries(other.CharacterId), Is.Empty);
        });

        // Blocked already, by any case: dropped.
        playerTest.TakeSent();
        BlockByName(player, "bObBy");
        Assert.That(BuddySent(playerTest), Is.Empty);
        Assert.That(Entries(player.CharacterId), Has.Length.EqualTo(1));
    }

    [Test]
    public void MailToTheOwnNameInAnotherCaseIsRefused() {
        mailHandler.Handle(player, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteUnicodeString("alice");
            writer.WriteUnicodeString("title");
            writer.WriteUnicodeString("text");
        }));

        Assert.That(playerTest.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Mail).Select(Convert.ToHexString),
            Is.EqualTo(new[] { Hex(MailPacket.Error(MailError.s_mail_error_recipient_equal_sender, 1)) }));
    }

    // Only friends count, not the request being accepted.
    [Test]
    public void AnAcceptWithinTheLimitMakesFriends() {
        Invite(other, player.PlayerName);
        Limit(playerTest, 1);
        Limit(otherTest, 1);

        Answer(player, Accept, EntryId(player.CharacterId, other.CharacterId));

        Assert.That(Entries(player.CharacterId).Single().Type, Is.EqualTo(BuddyType.Default));
    }

    [Test]
    public void AnAcceptWithAFullOwnListIsRefusedWithError7() {
        Invite(other, player.PlayerName);
        long entryId = EntryId(player.CharacterId, other.CharacterId);
        playerTest.TakeSent();
        Limit(playerTest, 0);

        Answer(player, Accept, entryId);

        Assert.Multiple(() => {
            Assert.That(BuddySent(playerTest), Is.EqualTo(new[] { Hex(BuddyPacket.Accept(entryId, BuddyError.s_buddy_err_max_buddy)) }));
            Assert.That(Entries(player.CharacterId).Single().Type, Is.EqualTo(BuddyType.InRequest)); // the request stays
            Assert.That(Entries(other.CharacterId).Single().Type, Is.EqualTo(BuddyType.OutRequest));
        });
    }

    [Test]
    public void AnAcceptWithTheRequestersListFullIsRefusedWithError8() {
        Invite(other, player.PlayerName);
        long entryId = EntryId(player.CharacterId, other.CharacterId);
        playerTest.TakeSent();
        Limit(playerTest, 1);
        using (GameStorage.Request db = playerTest.Storage.Context()) {
            db.CreateBuddy(other.CharacterId, 99999, BuddyType.Default);
        }

        Answer(player, Accept, entryId);

        Assert.Multiple(() => {
            Assert.That(BuddySent(playerTest), Is.EqualTo(new[] { Hex(BuddyPacket.Accept(entryId, BuddyError.s_buddy_err_target_full)) }));
            Assert.That(Entries(player.CharacterId).Single().Type, Is.EqualTo(BuddyType.InRequest));
        });
    }

    // The request was cancelled meanwhile (or never there): each answer says the entry is missing.
    [Test]
    public void AnswersToAMissingEntryAreRefusedWithError1() {
        const long missing = 4242;
        Answer(player, Accept, missing);
        Answer(player, Decline, missing);
        Answer(player, Unblock, missing);
        Answer(player, Delete, missing);
        Answer(player, Cancel, missing);

        Assert.That(BuddySent(playerTest), Is.EqualTo(new[] {
            Hex(BuddyPacket.Accept(missing, BuddyError.s_buddy_err_miss_id)),
            Hex(BuddyPacket.Decline(missing, BuddyError.s_buddy_err_miss_id)),
            Hex(BuddyPacket.Unblock(missing, BuddyError.s_buddy_err_miss_id)),
            Hex(BuddyPacket.Remove(missing, BuddyError.s_buddy_err_miss_id)),
            Hex(BuddyPacket.Cancel(missing, BuddyError.s_buddy_err_miss_id)),
        }));
    }

    // The layouts: the error, then the entry id; accept and remove keep the character, account and name fields.
    [Test]
    public void TheRefusalLayouts() {
        Assert.Multiple(() => {
            Assert.That(Hex(BuddyPacket.Accept(5, BuddyError.s_buddy_err_max_buddy)),
                Is.EqualTo("6100" + "03" + "07" + "0500000000000000" + "0000000000000000" + "0000000000000000" + "0000"));
            Assert.That(Hex(BuddyPacket.Decline(5, BuddyError.s_buddy_err_miss_id)), Is.EqualTo("6100" + "04" + "01" + "0500000000000000"));
            Assert.That(Hex(BuddyPacket.Unblock(5, BuddyError.s_buddy_err_miss_id)), Is.EqualTo("6100" + "06" + "01" + "0500000000000000"));
            Assert.That(Hex(BuddyPacket.Remove(5, BuddyError.s_buddy_err_miss_id)),
                Is.EqualTo("6100" + "07" + "01" + "0500000000000000" + "0000000000000000" + "0000000000000000" + "0000"));
            Assert.That(Hex(BuddyPacket.Cancel(5, BuddyError.s_buddy_err_miss_id)), Is.EqualTo("6100" + "11" + "01" + "0500000000000000"));
        });
    }

    // The accept is stored when the World call fails: the client is told it is a friend (it acts on result 0 only, so
    // an error left it showing a request that was a friend in storage).
    [Test]
    public void AnAcceptStoredWhileWorldFailsStillMakesFriends() {
        Invite(other, player.PlayerName);
        long entryId = EntryId(player.CharacterId, other.CharacterId);
        playerTest.World.BuddyWorld = _ => throw new RpcException(new Status(StatusCode.Unavailable, "test"));
        playerTest.TakeSent();

        Answer(player, Accept, entryId);

        string[] sent = BuddySent(playerTest);
        Assert.Multiple(() => {
            Assert.That(sent.First(), Does.StartWith("6100" + "03" + "00"));
            Assert.That(sent, Has.None.EqualTo(Hex(BuddyPacket.Accept(entryId, BuddyError.s_buddy_err_unknown))));
            Assert.That(Entries(player.CharacterId).Single().Type, Is.EqualTo(BuddyType.Default));
        });
    }

    // An unblock whose delete is not stored keeps the block in memory as in storage, so a second try goes through.
    [Test]
    public void AnUnblockNotStoredKeepsTheBlock() {
        BlockByName(player, other.PlayerName);
        long entryId = EntryId(player.CharacterId, other.CharacterId);
        playerTest.TakeSent();

        fault.Fail = true;
        Answer(player, Unblock, entryId);
        fault.Fail = false;
        Assert.That(BuddySent(playerTest), Is.EqualTo(new[] { Hex(BuddyPacket.Unblock(entryId, BuddyError.s_buddy_err_unknown)) }));
        Assert.That(Entries(player.CharacterId).Single().Type, Is.EqualTo(BuddyType.Blocked));

        Answer(player, Unblock, entryId);
        Assert.That(BuddySent(playerTest).First(), Does.StartWith("6100" + "06" + "00"));
        Assert.That(Entries(player.CharacterId), Is.Empty);
    }

    // A save that deletes a buddy row fails (TrySaveChanges turns it into false).
    private sealed class BuddyDeleteFault : SaveChangesInterceptor {
        public bool Fail;

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) {
            if (Fail && eventData.Context != null && eventData.Context.ChangeTracker.Entries<Maple2.Database.Model.Buddy>()
                    .Any(entry => entry.State == EntityState.Deleted)) {
                throw new InvalidOperationException("The test made this save fail");
            }
            return result;
        }
    }
}
