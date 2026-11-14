using System;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Server.Core.Constants;
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
}
