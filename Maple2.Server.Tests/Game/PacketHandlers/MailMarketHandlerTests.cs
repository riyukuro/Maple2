using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// Mail, the black market and the badge slots through their handlers, in the order KMS2 sends the answers.
public class MailMarketHandlerTests {
    private const int ItemId = 20000005;
    private const int BadgeId = 70400001;

    private TestGameSession test = null!;

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());
    private static byte Sub(byte[] packet) => packet[2];
    private static IEnumerable<(SendOp, byte)> Shape(IEnumerable<byte[]> sent) => sent.Select(packet => (TestGameSession.Op(packet), Sub(packet)));

    private GameSession Login(long meso = 100000) {
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with {
            BlackMarketSellMinPrice = 1, BlackMarketSellMaxPrice = 1000000, BlackMarketSellEndDay = 2,
        });
        GameSession session = test.Start();
        session.Currency.Meso = meso;
        test.TakeSent();
        return session;
    }

    private Mail StoreMail(long meso = 0) {
        using GameStorage.Request db = test.Storage.Context();
        return db.CreateMail(new Mail { ReceiverId = test.CharacterId, Type = MailType.System, Title = "t", Content = "c", Meso = meso })
               ?? throw new InvalidOperationException("mail row not created");
    }

    private static void MailRequest(GameSession session, byte command, long mailId) {
        new MailHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            writer.WriteLong(mailId);
        }));
    }

    #region Mail
    // The list: start, the mails, end.
    [Test]
    public void Mail_LoadListsTheInbox() {
        test = new TestGameSession();
        StoreMail();
        GameSession session = Login();

        new MailHandler().Handle(session, TestGameSession.Packet(writer => writer.WriteByte(0)));

        Assert.That(Shape(test.TakeSent()), Is.EqualTo(new[] { (SendOp.Mail, (byte) 16), (SendOp.Mail, (byte) 0), (SendOp.Mail, (byte) 17) }));
    }

    // KMS2 answers a refused collect with the error packet that names the request: 14, the command (0b), the error.
    // The client reads the two bytes in this order (OnMail mode 0x14).
    [Test]
    public void Mail_RefusedCollectNamesTheCommand() {
        test = new TestGameSession();
        GameSession session = Login();

        MailRequest(session, 11, 987654);

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That(TestGameSession.Op(sent[0]), Is.EqualTo(SendOp.Mail));
        Assert.That(sent[0].Skip(2).Take(2), Is.EqualTo(new byte[] { 20, 11 }));
    }

    [Test]
    public void Mail_RefusedReadNamesTheCommand() {
        test = new TestGameSession();
        Mail mail = StoreMail();
        GameSession session = Login();
        MailRequest(session, 2, mail.Id);
        Assert.That(Shape(test.TakeSent()), Is.EqualTo(new[] { (SendOp.Mail, (byte) 2) }));

        MailRequest(session, 2, mail.Id);

        List<byte[]> sent = test.TakeSent();
        Assert.That(Convert.ToHexString(sent.Single()), Is.EqualTo(Hex(MailPacket.Error(MailError.s_mail_error_alreadyread, 2))));
        Assert.That(sent[0].Skip(2).Take(2), Is.EqualTo(new byte[] { 20, 2 }));
    }

    // Collecting an unread mail with mesos: read, the mesos, then the collected answer. The read and the collect
    // times are stored in one request.
    [Test]
    public void Mail_CollectGivesTheMesos() {
        test = new TestGameSession();
        Mail mail = StoreMail(meso: 500);
        GameSession session = Login(meso: 1000);

        MailRequest(session, 11, mail.Id);

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(TestGameSession.Op), Does.Contain(SendOp.Meso));
        Assert.That(Shape(sent).First(), Is.EqualTo((SendOp.Mail, (byte) 2)));
        Assert.That(session.Currency.Meso, Is.EqualTo(1500));
        Assert.That(test.Errors, Is.Empty);
    }
    #endregion

    #region Black market
    private BlackMarketHandler Market() => new() { TableMetadata = test.TableMetadata, World = test.World };

    // KMS2: Meso (the deposit), the item leaving the bag, then BlackMarket 2 with the listing.
    [Test]
    public void BlackMarket_AddPaysRemovesThenLists() {
        test = new TestGameSession();
        Item item = test.Store(TestGameSession.Metadata(ItemId), amount: 5);
        GameSession session = Login();

        Market().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(2);
            writer.WriteLong(item.Uid);
            writer.WriteLong(1000);
            writer.WriteInt(2);
        }));

        List<byte[]> sent = test.TakeSent();
        Assert.That(Shape(sent), Is.EqualTo(new[] { (SendOp.Meso, sent[0][2]), (SendOp.ItemInventory, (byte) 2), (SendOp.BlackMarket, (byte) 2) }));
        Assert.That(session.Currency.Meso, Is.LessThan(100000));
        Assert.That(session.Item.Inventory.Get(item.Uid)?.Amount, Is.EqualTo(3));
        Assert.That(test.World.BlackMarketRequests, Has.Count.EqualTo(1));
        using GameStorage.Request db = test.Storage.Context();
        BlackMarketListing listing = db.GetBlackMarketListings(test.CharacterId).Single();
        Assert.That((listing.Price, listing.Quantity, listing.Deposit), Is.EqualTo((1000L, 2, 100000 - session.Currency.Meso)));
    }

    [Test]
    public void BlackMarket_AddWithoutTheDepositKeepsTheItem() {
        test = new TestGameSession();
        Item item = test.Store(TestGameSession.Metadata(ItemId), amount: 5);
        GameSession session = Login(meso: 0);

        Market().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(2);
            writer.WriteLong(item.Uid);
            writer.WriteLong(1000);
            writer.WriteInt(5);
        }));

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(BlackMarketPacket.Error(BlackMarketError.s_err_lack_meso, 2)) }));
        Assert.That(session.Item.Inventory.Get(item.Uid)?.Amount, Is.EqualTo(5));
    }

    // KMS2: Meso, BlackMarket 5 (the listing and the count), then the mail notice (Mail 14).
    [Test]
    public void BlackMarket_PurchasePaysAnswersThenNotifies() {
        test = new TestGameSession();
        ItemMetadata metadata = TestGameSession.Metadata(ItemId);
        test.Register(metadata);
        BlackMarketListing listing;
        Item item;
        using (GameStorage.Request db = test.Storage.Context()) {
            item = db.CreateItem(0, new Item(metadata, amount: 4)) ?? throw new InvalidOperationException("item row not created");
        }
        using (GameStorage.Request db = test.Storage.Context()) {
            listing = db.CreateBlackMarketingListing(new BlackMarketListing(item) {
                AccountId = test.AccountId + 100,
                CharacterId = test.CharacterId + 100,
                Deposit = 10,
                ExpiryTime = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds(),
                Price = 300,
                Quantity = 4,
            }) ?? throw new InvalidOperationException("listing row not created");
        }
        GameSession session = Login(meso: 5000);

        Market().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(5);
            writer.WriteLong(listing.Id);
            writer.WriteInt(4);
        }));

        List<byte[]> sent = test.TakeSent();
        Assert.That(Shape(sent), Is.EqualTo(new[] { (SendOp.Meso, sent[0][2]), (SendOp.BlackMarket, (byte) 5), (SendOp.Mail, (byte) 14) }));
        Assert.That(Convert.ToHexString(sent[1]), Is.EqualTo(Hex(BlackMarketPacket.Purchase(listing.Id, 4))));
        Assert.That(Convert.ToHexString(sent[2]), Is.EqualTo(Hex(MailPacket.Notify(1, true))));
        Assert.That(session.Currency.Meso, Is.EqualTo(5000 - 1200));
        using GameStorage.Request check = test.Storage.Context();
        Assert.That(check.GetBlackMarketListing(listing.Id), Is.Null);
        // The seller is told of the sale mail through the world.
        Assert.That(test.World.MailNotifications.Select(notice => notice.CharacterId), Is.EqualTo(new[] { test.CharacterId + 100 }));
        Assert.That(test.Errors, Is.Empty);
    }
    #endregion

    #region Badge
    // KMS2: the badge goes on after it left the bag; taking it off sends BadgeEquip 1 before the bag add.
    [Test]
    public void Badge_EquipAndUnequipOrder() {
        test = new TestGameSession();
        Item badge = test.Store(TestGameSession.Metadata(BadgeId, type: 7));
        GameSession session = Login();
        var handler = new BadgeEquipHandler();

        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteLong(badge.Uid);
            writer.WriteByte((byte) BadgeType.Damage);
        }));

        Assert.That(Shape(test.TakeSent()), Is.EqualTo(new[] { (SendOp.ItemInventory, (byte) 1), (SendOp.BadgeEquip, (byte) 0) }));
        Assert.That(session.Item.Equips.Get(BadgeType.Damage)?.Uid, Is.EqualTo(badge.Uid));

        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteByte((byte) BadgeType.Damage);
        }));

        List<byte[]> sent = test.TakeSent();
        Assert.That(Shape(sent).Take(2), Is.EqualTo(new[] { (SendOp.BadgeEquip, (byte) 1), (SendOp.ItemInventory, (byte) 0) }));
        Assert.That(Convert.ToHexString(sent[0]), Is.EqualTo(Hex(EquipPacket.UnequipBadge(session.Player, BadgeType.Damage))));
        Assert.That(session.Item.Equips.Get(BadgeType.Damage), Is.Null);
        Assert.That(session.Item.Inventory.Get(badge.Uid), Is.Not.Null);
    }
    #endregion
}
