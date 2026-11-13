using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// MeretMarket 30 with the gift flag: a design bought for another player goes to the receiver's mailbox, paid in
// merets by the buyer, and a refused gift answers 0x1F with the client's own error code. The ids are made up.
public class MeretMarketGiftTests {
    private const int Design = 11300001;
    private const int Town = 90000100;
    private const byte PayMeret = 3;
    private const byte PayMeso = 1;

    private TestGameSession buyerTest = null!;
    private TestGameSession receiverTest = null!;
    private TestGameSession sellerTest = null!;
    private GameSession buyer => buyerTest.Session;
    private string receiverName = null!;

    [SetUp]
    public void SetUp() {
        steps.Clear();
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        buyerTest = new TestGameSession(options);
        receiverTest = new TestGameSession(options);
        sellerTest = new TestGameSession(options);
        receiverName = receiverTest.StoredCharacter().Name;

        buyerTest.SetTable(new UgcDesignTable(new Dictionary<int, UgcDesignTable.Entry> {
            [Design] = new(1, MeretMarketCurrencyType.Meret, 390, 0, 0),
        }));
        buyerTest.Register(TestGameSession.Metadata(Design, EquipSlot.CP, slotMax: 1));
        buyerTest.Start();
        buyerTest.EnterMap(TestGameSession.Map(Town));
        buyer.Currency.Meret = 5000;
        buyerTest.TakeSent();
    }

    private long List(long sellerAccountId, long price = 3000) {
        buyer.ItemMetadata.TryGet(Design, out ItemMetadata? metadata);
        using GameStorage.Request db = buyerTest.Storage.Context();
        return db.CreateUgcMarketItem(new UgcMarketItem(metadata!) {
            Price = price,
            TabId = 1,
            Status = UgcMarketListingStatus.Active,
            ListingEndTime = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds(),
            PromotionEndTime = 0,
            SellerAccountId = sellerAccountId,
            SellerCharacterId = sellerTest.CharacterId,
            SellerCharacterName = "Seller",
            Description = string.Empty,
            Tags = [],
            Look = new UgcItemLook { Name = "Design", Url = "item/ugc/design.png" },
            Blueprint = new ItemBlueprint(),
        })!.Id;
    }

    // BuyPacket::Write: kind, the product key, flag, child id, a long, index, count, shop id, the gift fields, the
    // price, a flag and the pay type, then four ints.
    private void Buy(long ugcId, string receiver, byte payType, int premiumId = 0, string message = "for you") {
        new MeretMarketHandler { TableMetadata = buyerTest.TableMetadata, ServerTableMetadata = buyerTest.ServerTableMetadata }
            .Handle(buyer, TestGameSession.Packet(writer => {
                writer.WriteByte(30);
                writer.WriteByte(1);
                writer.WriteInt(premiumId);
                writer.WriteLong(ugcId);
                writer.WriteInt(0);
                writer.WriteInt(0);
                writer.WriteLong(0);
                writer.WriteInt(4);
                writer.WriteInt(1);
                writer.WriteInt(0);
                writer.WriteBool(true);
                writer.WriteUnicodeString(receiver);
                writer.WriteUnicodeString(message);
                writer.WriteLong(3000);
                writer.WriteBool(false);
                writer.WriteByte(payType);
                writer.WriteInt(0);
                writer.WriteInt(0);
                writer.WriteInt(0);
                writer.WriteInt(0);
            }));
    }

    private byte[] Answer() {
        List<byte[]> sent = buyerTest.TakeSent().Where(p => TestGameSession.Op(p) == SendOp.MeretMarket).ToList();
        Assert.That(sent, Has.Count.EqualTo(1));
        return sent[0];
    }

    private ICollection<Mail> ReceiverMail() {
        using GameStorage.Request db = buyerTest.Storage.Context();
        return db.GetAllMail(receiverTest.CharacterId);
    }

    private void AssertRefused(int code) {
        byte[] answer = Answer();
        Assert.Multiple(() => {
            Assert.That(answer[2], Is.EqualTo(0x1F));
            Assert.That(BitConverter.ToInt32(answer, answer.Length - 4), Is.EqualTo(code));
            Assert.That(buyer.Currency.Meret, Is.EqualTo(5000));
            Assert.That(ReceiverMail(), Is.Empty);
            Assert.That(buyerTest.World.MailNotifications, Is.Empty);
        });
    }

    [Test]
    public void AGift_GoesToTheReceiversMailbox_PaidInMerets() {
        long id = List(sellerTest.AccountId);

        Buy(id, receiverName, PayMeret);

        byte[] answer = Answer();
        Mail mail = ReceiverMail().Single();
        Assert.Multiple(() => {
            Assert.That(buyerTest.Errors, Is.Empty);
            Assert.That(answer[2], Is.EqualTo(0x1E));
            // The echoed record: the gift byte after the 41 bytes of kind, key, flag, ids, index, count and shop id.
            Assert.That(answer[3 + 41], Is.EqualTo(1));
            Assert.That(buyer.Currency.Meret, Is.EqualTo(5000 - 3000));
            Assert.That(mail.Type, Is.EqualTo(MailType.System));
            Assert.That(mail.Items.Single().Id, Is.EqualTo(Design));
            Assert.That(mail.Items.Single().Template?.Name, Is.EqualTo("Design"));
            Assert.That(mail.ContentArgs, Is.EqualTo(new List<(string, string)> { ("str", buyer.PlayerName), ("str", "for you") }));
            Assert.That(buyer.Item.Inventory.Find(Design), Is.Empty);
        });
        MailNotificationRequest notice = buyerTest.World.MailNotifications.Single();
        Assert.Multiple(() => {
            Assert.That(notice.CharacterId, Is.EqualTo(receiverTest.CharacterId));
            Assert.That(notice.MailId, Is.EqualTo(mail.Id));
            Assert.That(notice.Gift.Sender, Is.EqualTo(buyer.PlayerName));
            Assert.That(notice.Gift.ItemId, Is.EqualTo(Design));
            Assert.That(notice.Gift.UgcUrl, Is.EqualTo("item/ugc/design.png"));
            Assert.That(notice.Gift.UgcTitle, Is.EqualTo("Design"));
        });
        // The sale is the seller's like any other sale.
        using GameStorage.Request db = buyerTest.Storage.Context();
        Assert.That(db.GetUgcMarketItem(id)!.SalesCount, Is.EqualTo(1));
    }

    [Test]
    public void AGiftMessage_IsEscapedInTheMailArgs() {
        Buy(List(sellerTest.AccountId), receiverName, PayMeret, message: "a \"<b>\" & c");

        Assert.That(ReceiverMail().Single().ContentArgs.Last().Value, Is.EqualTo("a &quot;&lt;b&gt;&quot; &amp; c"));
    }

    // The client offers a gift only on a design; a premium product as a gift is refused.
    [Test]
    public void APremiumProductGift_IsRefused() {
        Buy(0, receiverName, PayMeret, premiumId: 5);
        AssertRefused(0xA4);
    }

    // A design's currency is merets: a gift paid any other way is s_meratmarket_error_buy_invalid_price_type.
    [Test]
    public void AGiftPaidInMesos_IsRefused() {
        Buy(List(sellerTest.AccountId), receiverName, PayMeso);
        AssertRefused(0x87);
    }

    [Test]
    public void AGiftToNoOne_IsRefused() {
        Buy(List(sellerTest.AccountId), "nobody-here", PayMeret);
        AssertRefused(0xA6);
    }

    // A character whose deletion has begun takes no gift.
    [Test]
    public void AGiftToACharacterBeingDeleted_IsRefused() {
        using (GameStorage.Request db = buyerTest.Storage.Context()) {
            Assert.That(db.UpdateDelete(receiverTest.AccountId, receiverTest.CharacterId, DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds()), Is.True);
        }

        Buy(List(sellerTest.AccountId), receiverName, PayMeret);
        AssertRefused(0xA6);
    }

    // The echoed record after the 41 bytes: the gift flag, the receiver, the message, the price, a byte and the pay type.
    private static (bool Gift, string Receiver, string Message, long Price, byte PayType) Echo(byte[] answer) {
        var reader = new ByteReader(answer, 3 + 41);
        bool gift = reader.ReadBool();
        string receiver = reader.ReadUnicodeString();
        string message = reader.ReadUnicodeString();
        long price = reader.ReadLong();
        reader.ReadByte();
        return (gift, receiver, message, price, reader.ReadByte());
    }

    [Test]
    public void AGift_EchoesTheReceiverAndPayType() {
        Buy(List(sellerTest.AccountId), receiverName, PayMeret);

        byte[] answer = Answer();
        Assert.That(answer[2], Is.EqualTo(0x1E));
        Assert.That(Echo(answer), Is.EqualTo((true, receiverName, "for you", 3000L, PayMeret)));
    }

    [Test]
    public void ARefusedGift_EchoesTheReceiverAndPayType() {
        Buy(List(sellerTest.AccountId), receiverName, PayMeso);

        byte[] answer = Answer();
        Assert.That(answer[2], Is.EqualTo(0x1F));
        Assert.That(Echo(answer), Is.EqualTo((true, receiverName, "for you", 3000L, PayMeso)));
    }

    [Test]
    public void AGiftToTheBuyersOwnAccount_IsRefused() {
        using (GameStorage.Request db = buyerTest.Storage.Context()) {
            db.CreateCharacter(new Character {
                AccountId = buyerTest.AccountId,
                Name = "SecondOfBuyer",
                Gender = Gender.Female,
                Job = Job.Newbie,
                Mastery = new Mastery(),
                MapId = 2000023,
                ReturnMaps = new LimitedStack<int>(3, 2000023),
            });
        }

        Buy(List(sellerTest.AccountId), "SecondOfBuyer", PayMeret);
        AssertRefused(0xB3);
    }

    [Test]
    public void AGiftOfTheBuyersOwnDesign_IsRefused() {
        Buy(List(buyerTest.AccountId), receiverName, PayMeret);
        AssertRefused(0x91);
    }

    [Test]
    public void AGiftWithoutEnoughMerets_IsRefused() {
        buyer.Currency.Meret = 100;
        Buy(List(sellerTest.AccountId), receiverName, PayMeret);

        byte[] answer = Answer();
        Assert.That(BitConverter.ToInt32(answer, answer.Length - 4), Is.EqualTo(0x88));
        Assert.That(buyer.Currency.Meret, Is.EqualTo(100));
        Assert.That(ReceiverMail(), Is.Empty);
    }

    #region DeliverGift
    private readonly List<string> steps = [];

    private (Mail? Mail, bool UndoFailed) Deliver(bool mailWritten, bool paid, bool saleStored, bool undone = true) {
        return MeretMarketHandler.DeliverGift(
            writeMail: () => { steps.Add("mail"); return mailWritten ? new Mail() : null; },
            pay: () => { steps.Add("pay"); return paid; },
            recordSale: () => { steps.Add("sale"); return saleStored; },
            refund: () => steps.Add("refund"),
            undoMail: _ => { steps.Add("undo"); return undone; });
    }

    // A mail that cannot be written charges nothing.
    [Test]
    public void DeliverGift_MailNotWritten_NothingIsPaid() {
        Assert.That(Deliver(false, true, true), Is.EqualTo(((Mail?) null, false)));
        Assert.That(steps, Is.EqualTo(new[] { "mail" }));
    }

    [Test]
    public void DeliverGift_NotPaid_TheMailIsTakenBack() {
        Assert.That(Deliver(true, false, true), Is.EqualTo(((Mail?) null, false)));
        Assert.That(steps, Is.EqualTo(new[] { "mail", "pay", "undo" }));
    }

    // Nothing was paid, so nothing is given back; the failed take-back is reported.
    [Test]
    public void DeliverGift_NotPaid_TakeBackFails_IsReported() {
        Assert.That(Deliver(true, false, true, undone: false), Is.EqualTo(((Mail?) null, true)));
        Assert.That(steps, Is.EqualTo(new[] { "mail", "pay", "undo" }));
    }

    // The merets come back only after the mail is gone.
    [Test]
    public void DeliverGift_SaleNotStored_TakesTheMailBackThenRefunds() {
        Assert.That(Deliver(true, true, false), Is.EqualTo(((Mail?) null, false)));
        Assert.That(steps, Is.EqualTo(new[] { "mail", "pay", "sale", "undo", "refund" }));
    }

    // The receiver keeps the mail: the payment is kept, so the gift is paid for.
    [Test]
    public void DeliverGift_SaleNotStored_TakeBackFails_KeepsThePayment() {
        (Mail? mail, bool undoFailed) = Deliver(true, true, false, undone: false);
        Assert.That(mail, Is.Not.Null);
        Assert.That(undoFailed, Is.True);
        Assert.That(steps, Is.EqualTo(new[] { "mail", "pay", "sale", "undo" }));
    }

    [Test]
    public void DeliverGift_AllWritten_KeepsTheMail() {
        (Mail? mail, bool undoFailed) = Deliver(true, true, true);
        Assert.That(mail, Is.Not.Null);
        Assert.That(undoFailed, Is.False);
        Assert.That(steps, Is.EqualTo(new[] { "mail", "pay", "sale" }));
    }
    #endregion

    // Mail 0x16 as ReadGiftNew reads it.
    [Test]
    public void MailGift_Layout() {
        byte[] bytes = MailPacket.Gift("Ab", 11300001, 4, 1, "u", "T").ToArray();

        var expected = new ByteWriter();
        expected.WriteShort((short) SendOp.Mail);
        expected.WriteByte(0x16);
        expected.WriteUnicodeString("Ab");
        expected.WriteByte(0);
        expected.WriteInt(11300001);
        expected.WriteByte(4);
        expected.WriteInt(1);
        expected.WriteString("u");
        expected.WriteUnicodeString("T");
        Assert.That(bytes, Is.EqualTo(expected.ToArray()));
    }
}
