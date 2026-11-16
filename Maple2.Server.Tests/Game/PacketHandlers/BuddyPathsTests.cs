using System;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The friend list paths between two online players that share one database: what the other side's list does on a
// decline, a cancel and a block (Buddy 7, Remove: the client drops the entry of that id on result 0,
// buddymanager.cpp:134), the friend's status (Buddy 8 and 14: online 0x608, offline 0x609, buddymanager.cpp:175),
// and the refusals of requests, blocks and block edits, each answered with its error and changing nothing.
public class BuddyPathsTests {
    private const byte Request = 2;
    private const byte Accept = 3;
    private const byte Decline = 4;
    private const byte Block = 5;
    private const byte Delete = 7;
    private const byte UpdateBlock = 10;
    private const byte Cancel = 17;

    private const byte RemoveOp = 7;
    private const byte UpdateInfoOp = 8;
    private const byte NotifyOnlineOp = 14;

    private TestGameSession playerTest = null!;
    private TestGameSession otherTest = null!;
    private readonly BuddyHandler buddyHandler = new() { BanWordStorage = TestGameSession.BanWords() };
    private readonly FailingSaves saves = new();
    private GameSession player => playerTest.Session;
    private GameSession other => otherTest.Session;

    // Every save of the shared database fails while Fail is set.
    private sealed class FailingSaves : SaveChangesInterceptor {
        public bool Fail;

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) =>
            Fail ? throw new DbUpdateException("The test made this save fail") : result;
    }

    [SetUp]
    public void Login() {
        saves.Fail = false;
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(saves)
            .Options;
        playerTest = new TestGameSession(options);
        otherTest = new TestGameSession(options);
        foreach (TestGameSession test in new[] { playerTest, otherTest }) {
            Limit(test, 100, 100);
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

    private static void Limit(TestGameSession test, int buddies, int blocks) {
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { MaxBuddyCount = buddies, MaxBlockCount = blocks });
    }

    // As the world and the receiver's game server (ChannelService.Buddy), both players online.
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

    private void Invite(GameSession session, string name, string memo = "hi") {
        buddyHandler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(Request);
            writer.WriteUnicodeString(name);
            writer.WriteUnicodeString(memo);
        }));
    }

    private void BlockEntry(GameSession session, long entryId, string name, string memo = "") {
        buddyHandler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(Block);
            writer.WriteLong(entryId);
            writer.WriteUnicodeString(name);
            writer.WriteUnicodeString(memo);
        }));
    }

    private void EditBlock(GameSession session, long entryId, string name, string memo) {
        buddyHandler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(UpdateBlock);
            writer.WriteLong(entryId);
            writer.WriteUnicodeString(name);
            writer.WriteUnicodeString(memo);
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

    private static byte[][] BuddySent(TestGameSession test) =>
        test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Buddy).ToArray();

    // (command, result, entry id) of a Buddy packet that starts with them.
    private static (byte, byte, long) Head(byte[] packet) => (packet[2], packet[3], BitConverter.ToInt64(packet, 4));

    private void BecomeFriends() {
        Invite(player, other.PlayerName);
        Answer(other, Accept, EntryId(other.CharacterId, player.CharacterId));
        playerTest.TakeSent();
        otherTest.TakeSent();
    }

    private void FriendUpdate(PlayerUpdateRequest request) {
        request.AccountId = other.AccountId;
        request.CharacterId = other.CharacterId;
        player.PlayerInfo.ReceiveUpdate(request);
    }

    [Test]
    public void Decline_TheRequesterDropsItsRequest() {
        Invite(player, other.PlayerName);
        long playerEntry = EntryId(player.CharacterId, other.CharacterId);
        playerTest.TakeSent();

        Answer(other, Decline, EntryId(other.CharacterId, player.CharacterId));

        byte[] remove = BuddySent(playerTest).Single();
        Assert.Multiple(() => {
            Assert.That(Head(remove), Is.EqualTo((RemoveOp, (byte) 0, playerEntry)));
            Assert.That(BitConverter.ToInt64(remove, 12), Is.EqualTo(other.CharacterId));
            Assert.That(Entries(player.CharacterId), Is.Empty);
        });
        // Gone from the list too: a cancel of it now finds no entry.
        Answer(player, Cancel, playerEntry);
        Assert.That(Head(BuddySent(playerTest).Single()), Is.EqualTo(((byte) Cancel, (byte) BuddyError.s_buddy_err_miss_id, playerEntry)));
    }

    [Test]
    public void Cancel_TheReceiverDropsTheRequest() {
        Invite(player, other.PlayerName);
        long otherEntry = EntryId(other.CharacterId, player.CharacterId);
        otherTest.TakeSent();

        Answer(player, Cancel, EntryId(player.CharacterId, other.CharacterId));

        Assert.Multiple(() => {
            Assert.That(BuddySent(otherTest).Select(Head), Is.EqualTo(new[] { (RemoveOp, (byte) 0, otherEntry) }));
            Assert.That(Entries(other.CharacterId), Is.Empty);
        });
        Answer(other, Accept, otherEntry);
        Assert.That(Head(BuddySent(otherTest).Single()), Is.EqualTo(((byte) Accept, (byte) BuddyError.s_buddy_err_miss_id, otherEntry)));
    }

    // A friend blocked by name: the player's entry becomes the block, the friend's entry is dropped on both sides.
    [Test]
    public void Block_AFriend_TheFriendDropsItsEntry() {
        BecomeFriends();
        long playerEntry = EntryId(player.CharacterId, other.CharacterId);
        long otherEntry = EntryId(other.CharacterId, player.CharacterId);

        BlockEntry(player, 0, other.PlayerName, "memo");

        Assert.Multiple(() => {
            Assert.That(BuddySent(otherTest).Select(Head), Is.EqualTo(new[] { (RemoveOp, (byte) 0, otherEntry) }));
            Assert.That(Entries(other.CharacterId), Is.Empty);
            Assert.That(Entries(player.CharacterId).Select(entry => (entry.Id, entry.Type, entry.Message)),
                Is.EqualTo(new[] { (playerEntry, BuddyType.Blocked, "memo") }));
            Assert.That(other.Buddy.IsBuddy(player.CharacterId), Is.False);
            Assert.That(player.Buddy.IsBlocked(other.CharacterId), Is.True);
        });
        // The friend is no longer listened to: its status changes send nothing.
        playerTest.TakeSent();
        FriendUpdate(new PlayerUpdateRequest { Channel = -1 });
        Assert.That(BuddySent(playerTest), Is.Empty);
    }

    // The friend's status: every change is an UpdateInfo, a change of online state also the online/offline notice
    // (true = offline). Once the friend is deleted, nothing is sent.
    [Test]
    public void FriendStatus_UpdatesUntilDeleted() {
        BecomeFriends();
        long playerEntry = EntryId(player.CharacterId, other.CharacterId);

        FriendUpdate(new PlayerUpdateRequest { Channel = -1 });
        byte[][] offline = BuddySent(playerTest);
        FriendUpdate(new PlayerUpdateRequest { Level = 30 });
        byte[][] level = BuddySent(playerTest);

        Assert.Multiple(() => {
            Assert.That(offline.Select(packet => packet[2]), Is.EqualTo(new[] { UpdateInfoOp, NotifyOnlineOp }));
            Assert.That(offline[1][3], Is.EqualTo(1), "offline");
            Assert.That(BitConverter.ToInt64(offline[1], 4), Is.EqualTo(playerEntry));
            Assert.That(level.Select(packet => packet[2]), Is.EqualTo(new[] { UpdateInfoOp }));
        });

        FriendUpdate(new PlayerUpdateRequest { Channel = 2 });
        byte[][] online = BuddySent(playerTest);
        Assert.That(online.Select(packet => packet[2]), Is.EqualTo(new[] { UpdateInfoOp, NotifyOnlineOp }));
        Assert.That(online[1][3], Is.EqualTo(0), "online");

        Answer(player, Delete, playerEntry);
        playerTest.TakeSent();
        FriendUpdate(new PlayerUpdateRequest { Channel = -1 });
        Assert.That(BuddySent(playerTest), Is.Empty);
    }

    // A name or memo longer than the client's boxes, a name that is no character, a player the sender blocked
    // (a request beside the block would be two entries for one character), and a full own list.
    [Test]
    public void Invite_Refusals_StoreNothing() {
        BlockEntry(player, 0, other.PlayerName);
        playerTest.TakeSent();

        Invite(player, new string('a', Constant.CharacterNameLengthMax + 1));
        Invite(player, other.PlayerName, new string('a', Constant.BuddyMessageLengthMax + 1));
        Invite(player, "NoSuchName");
        Invite(player, other.PlayerName);
        Limit(playerTest, 0, 100); // the block is not counted: no friend or request yet
        Invite(player, "NoSuchName");

        Assert.Multiple(() => {
            Assert.That(BuddySent(playerTest).Select(packet => (packet[2], packet[3])), Is.EqualTo(new[] {
                (Request, (byte) BuddyError.s_buddy_err_unknown),
                (Request, (byte) BuddyError.s_buddy_err_unknown),
                (Request, (byte) BuddyError.s_buddy_err_miss_id),
                (Request, (byte) BuddyError.s_buddy_err_request_somebody),
                (Request, (byte) BuddyError.s_buddy_err_max_buddy),
            }));
            Assert.That(Entries(player.CharacterId).Select(entry => entry.Type), Is.EqualTo(new[] { BuddyType.Blocked }));
            Assert.That(Entries(other.CharacterId), Is.Empty);
        });
    }

    // The receiver's list is full: refused with error 8 and no row on either side.
    [Test]
    public void Invite_TheReceiversListFull_IsRefusedWithError8() {
        Limit(playerTest, 1, 100);
        using (GameStorage.Request db = playerTest.Storage.Context()) {
            db.CreateBuddy(other.CharacterId, 99999, BuddyType.Default);
        }

        Invite(player, other.PlayerName);

        Assert.Multiple(() => {
            Assert.That(BuddySent(playerTest).Select(packet => (packet[2], packet[3])),
                Is.EqualTo(new[] { (Request, (byte) BuddyError.s_buddy_err_target_full) }));
            Assert.That(Entries(player.CharacterId), Is.Empty);
            Assert.That(Entries(other.CharacterId).Length, Is.EqualTo(1));
        });
    }

    // A name or memo too long, a full block list, a name that is no character; a repeat by entry id is dropped.
    [Test]
    public void Block_Refusals_StoreNothing() {
        BlockEntry(player, 0, new string('a', Constant.CharacterNameLengthMax + 1));
        BlockEntry(player, 0, other.PlayerName, new string('a', Constant.BuddyMessageLengthMax + 1));
        BlockEntry(player, 0, "NoSuchName");
        Assert.That(BuddySent(playerTest).Select(packet => (packet[2], packet[3])), Is.EqualTo(new[] {
            (Block, (byte) BuddyError.s_buddy_err_unknown),
            (Block, (byte) BuddyError.s_buddy_err_unknown),
            (Block, (byte) BuddyError.s_buddy_err_miss_id),
        }));
        Assert.That(Entries(player.CharacterId), Is.Empty);

        BlockEntry(player, 0, other.PlayerName);
        long blockEntry = EntryId(player.CharacterId, other.CharacterId);
        playerTest.TakeSent();
        BlockEntry(player, blockEntry, other.PlayerName);
        Assert.That(BuddySent(playerTest), Is.Empty);

        Limit(playerTest, 100, 1);
        BlockEntry(player, 0, "NoSuchName");
        Assert.Multiple(() => {
            Assert.That(BuddySent(playerTest).Select(packet => (packet[2], packet[3])),
                Is.EqualTo(new[] { (Block, (byte) BuddyError.s_buddy_err_max_block) }));
            Assert.That(Entries(player.CharacterId).Length, Is.EqualTo(1));
        });
    }

    // A block's memo edit is stored; an edit of an entry that is no block, or with a memo too long, is refused and
    // keeps the memo.
    [Test]
    public void UpdateBlock_StoresTheMemo_RefusalsKeepIt() {
        BlockEntry(player, 0, other.PlayerName, "old");
        long blockEntry = EntryId(player.CharacterId, other.CharacterId);
        playerTest.TakeSent();

        EditBlock(player, 4242, other.PlayerName, "new");
        EditBlock(player, blockEntry, other.PlayerName, new string('a', Constant.BuddyMessageLengthMax + 1));
        Assert.That(BuddySent(playerTest).Select(packet => (packet[2], packet[3])), Is.EqualTo(new[] {
            (UpdateBlock, (byte) BuddyError.s_buddy_err_unknown),
            (UpdateBlock, (byte) BuddyError.s_buddy_err_unknown),
        }));
        Assert.That(Entries(player.CharacterId).Single().Message, Is.EqualTo("old"));

        saves.Fail = true;
        EditBlock(player, blockEntry, other.PlayerName, "new");
        saves.Fail = false;
        Assert.That(Head(BuddySent(playerTest).Single()), Is.EqualTo((UpdateBlock, (byte) BuddyError.s_buddy_err_unknown, 0L)));
        Assert.That(Entries(player.CharacterId).Single().Message, Is.EqualTo("old"));

        EditBlock(player, blockEntry, other.PlayerName, "new");
        Assert.That(Head(BuddySent(playerTest).Single()), Is.EqualTo((UpdateBlock, (byte) 0, blockEntry)));
        Assert.That(Entries(player.CharacterId).Single().Message, Is.EqualTo("new"));
    }

    // The other side's row is gone (removed from another session meanwhile): accept, decline, delete and cancel are
    // refused with error 1, and the answering entry stays as it was.
    [TestCase(Accept, false)]
    [TestCase(Decline, false)]
    [TestCase(Cancel, true)]
    public void Answer_WithoutThePairedRow_IsRefusedWithError1(byte command, bool asSender) {
        Invite(player, other.PlayerName);
        GameSession answering = asSender ? player : other;
        TestGameSession answeringTest = asSender ? playerTest : otherTest;
        long entry = EntryId(answering.CharacterId, (asSender ? other : player).CharacterId);
        using (GameStorage.Request db = playerTest.Storage.Context()) {
            Assert.That(db.RemoveBuddy(db.GetBuddy((asSender ? other : player).CharacterId, answering.CharacterId)!), Is.True);
        }
        answeringTest.TakeSent();

        Answer(answering, command, entry);

        Assert.Multiple(() => {
            Assert.That(Head(BuddySent(answeringTest).Single()), Is.EqualTo((command, (byte) BuddyError.s_buddy_err_miss_id, entry)));
            Assert.That(Entries(answering.CharacterId).Select(row => row.Id), Is.EqualTo(new[] { entry }));
        });
    }

    [Test]
    public void Delete_WithoutThePairedRow_IsRefusedWithError1() {
        BecomeFriends();
        long playerEntry = EntryId(player.CharacterId, other.CharacterId);
        using (GameStorage.Request db = playerTest.Storage.Context()) {
            Assert.That(db.RemoveBuddy(db.GetBuddy(other.CharacterId, player.CharacterId)!), Is.True);
        }

        Answer(player, Delete, playerEntry);

        Assert.Multiple(() => {
            Assert.That(Head(BuddySent(playerTest).Single()), Is.EqualTo((RemoveOp, (byte) BuddyError.s_buddy_err_miss_id, playerEntry)));
            Assert.That(player.Buddy.IsBuddy(other.CharacterId), Is.True);
        });
    }

    // A removal that is not stored: the answer is the unknown error, both rows and both entries stay, and the other
    // player is not told.
    [TestCase(Decline, false)]
    [TestCase(Cancel, true)]
    public void Answer_NotStored_KeepsBothSides(byte command, bool asSender) {
        Invite(player, other.PlayerName);
        GameSession answering = asSender ? player : other;
        TestGameSession answeringTest = asSender ? playerTest : otherTest;
        TestGameSession tellTest = asSender ? otherTest : playerTest;
        long entry = EntryId(answering.CharacterId, (asSender ? other : player).CharacterId);
        answeringTest.TakeSent();
        tellTest.TakeSent();

        saves.Fail = true;
        Answer(answering, command, entry);
        saves.Fail = false;

        Assert.Multiple(() => {
            Assert.That(Head(BuddySent(answeringTest).Single()), Is.EqualTo((command, (byte) BuddyError.s_buddy_err_unknown, entry)));
            Assert.That(BuddySent(tellTest), Is.Empty);
            Assert.That(Entries(player.CharacterId).Length + Entries(other.CharacterId).Length, Is.EqualTo(2));
        });
        // The entry is still there: the answer can be sent again.
        Answer(answering, command, entry);
        Assert.That(Head(BuddySent(answeringTest).Single()), Is.EqualTo((command, (byte) 0, entry)));
    }

    [Test]
    public void Delete_NotStored_KeepsTheFriend() {
        BecomeFriends();
        long playerEntry = EntryId(player.CharacterId, other.CharacterId);

        saves.Fail = true;
        Answer(player, Delete, playerEntry);
        saves.Fail = false;

        Assert.Multiple(() => {
            Assert.That(Head(BuddySent(playerTest).Single()), Is.EqualTo((RemoveOp, (byte) BuddyError.s_buddy_err_unknown, playerEntry)));
            Assert.That(BuddySent(otherTest), Is.Empty);
            Assert.That(player.Buddy.IsBuddy(other.CharacterId) && other.Buddy.IsBuddy(player.CharacterId), Is.True);
            Assert.That(Entries(player.CharacterId).Length + Entries(other.CharacterId).Length, Is.EqualTo(2));
        });
    }

    // An accept that is not stored: refused with the unknown error, the request stays for a later accept.
    [Test]
    public void Accept_NotStored_KeepsTheRequest() {
        Invite(player, other.PlayerName);
        long otherEntry = EntryId(other.CharacterId, player.CharacterId);
        otherTest.TakeSent();
        playerTest.TakeSent();

        saves.Fail = true;
        Answer(other, Accept, otherEntry);
        saves.Fail = false;

        Assert.Multiple(() => {
            Assert.That(BuddySent(otherTest).Select(packet => (packet[2], packet[3])),
                Is.EqualTo(new[] { (Accept, (byte) BuddyError.s_buddy_err_unknown) }));
            Assert.That(BuddySent(playerTest), Is.Empty);
            Assert.That(Entries(other.CharacterId).Single().Type, Is.EqualTo(BuddyType.InRequest));
            Assert.That(Entries(player.CharacterId).Single().Type, Is.EqualTo(BuddyType.OutRequest));
        });

        Answer(other, Accept, otherEntry);
        Assert.That(other.Buddy.IsBuddy(player.CharacterId) && player.Buddy.IsBuddy(other.CharacterId), Is.True);
    }

    // The invite errors from the receiver's entry of the sender: a second request (2), a request to a friend (3), a
    // request back to a player whose request is waiting (5; the client offers accept there), and a player who
    // blocked the sender (9). None adds a row.
    [Test]
    public void Invite_PairErrors() {
        Invite(player, other.PlayerName);
        playerTest.TakeSent();
        otherTest.TakeSent();

        Invite(player, other.PlayerName);
        Invite(other, player.PlayerName);
        Assert.That(BuddySent(playerTest).Select(packet => (packet[2], packet[3])),
            Is.EqualTo(new[] { (Request, (byte) BuddyError.s_buddy_err_already_request_somebody) }));
        Assert.That(BuddySent(otherTest).Select(packet => (packet[2], packet[3])),
            Is.EqualTo(new[] { (Request, (byte) BuddyError.s_buddy_err_request_somebody) }));

        Answer(other, Accept, EntryId(other.CharacterId, player.CharacterId));
        playerTest.TakeSent();
        Invite(player, other.PlayerName);
        Assert.That(BuddySent(playerTest).Select(packet => (packet[2], packet[3])),
            Is.EqualTo(new[] { (Request, (byte) BuddyError.s_buddy_err_already_buddy) }));

        BlockEntry(other, 0, player.PlayerName);
        playerTest.TakeSent();
        Invite(player, other.PlayerName);
        Assert.Multiple(() => {
            Assert.That(BuddySent(playerTest).Select(packet => (packet[2], packet[3])),
                Is.EqualTo(new[] { (Request, (byte) BuddyError.s_buddy_refused_request_from_somebody) }));
            Assert.That(Entries(player.CharacterId), Is.Empty);
            Assert.That(Entries(other.CharacterId).Select(entry => entry.Type), Is.EqualTo(new[] { BuddyType.Blocked }));
        });
    }

    // "Refuse friend requests" on (Buddy 20, 1): the receiver's server declines the request as it arrives. The
    // receiver's client is shown no entry, the sender's request is dropped (Remove) and no row is left.
    [Test]
    public void Invite_ToAPlayerRefusingRequests_IsDeclinedOnArrival() {
        buddyHandler.Handle(other, TestGameSession.Packet(writer => {
            writer.WriteByte(20);
            writer.WriteInt(1);
        }));
        Assert.That(other.Buddy.AutoReject, Is.True);

        Invite(player, other.PlayerName);

        Assert.Multiple(() => {
            Assert.That(BuddySent(otherTest).Select(packet => packet[2]), Has.No.Member((byte) 9), "no Append");
            // The request answer, the request's entry, then its removal.
            Assert.That(BuddySent(playerTest).Select(packet => packet[2]), Is.EqualTo(new[] { Request, (byte) 9, RemoveOp }));
            Assert.That(Entries(player.CharacterId), Is.Empty);
            Assert.That(Entries(other.CharacterId), Is.Empty);
        });
    }

    // A block whose row is not stored: the unknown error, no entry.
    [Test]
    public void Block_NotStored_IsRefused() {
        saves.Fail = true;
        BlockEntry(player, 0, other.PlayerName);
        saves.Fail = false;

        Assert.Multiple(() => {
            Assert.That(BuddySent(playerTest).Select(packet => (packet[2], packet[3])),
                Is.EqualTo(new[] { (Block, (byte) BuddyError.s_buddy_err_unknown) }));
            Assert.That(player.Buddy.IsBlocked(other.CharacterId), Is.False);
            Assert.That(Entries(player.CharacterId), Is.Empty);
        });
    }
}
