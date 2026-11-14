using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Game.Shop;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.World.Service;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The NPC shop window (ShopHandler), the shops opened without an NPC (SystemShopHandler) and the bag requests
// (ItemInventoryHandler), in the packet order KMS2 answers them with.
public class ShopHandlerTests {
    private const int ShopId = 109;
    private const int ProductId = 11;
    private const int StockProductId = 12;
    private const int PackProductId = 13;
    private const int FameProductId = 14;
    private const int AnyFameProductId = 15;
    private const int TrevaProductId = 16;
    private const int ValorProductId = 17;
    private const int FurnishingProductId = 18;
    private const int EventMeretProductId = 19;
    private const int GameMeretProductId = 20;
    private const int GuildFundProductId = 21;
    private const long GuildId = 39990009;
    private const int FurnishingItemId = 50400001;
    private const int ShopItemId = 20000001;
    private const int ProductItemId = 20000002;

    private TestGameSession test = null!;
    private Item bagItem = null!;
    private Item shopItem = null!;
    // The shop's restock rule; a test of the instant restock sets it before Login.
    private ShopRestockData restockData = new(ResetType.Default, ShopCurrencyType.Meso, ShopCurrencyType.Meso, 0, 0, 0, false, true, false);
    private bool enableReset;
    private Action? beforeStart;

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    private static ShopItemMetadata Product(int id, int price, int stock, short sellUnit = 1, ShopItemMetadata.QuestAlliance? fame = null,
                                            ShopCurrencyType currency = ShopCurrencyType.Meso, int itemId = ProductItemId) {
        var requirements = new ShopItemMetadata.Requirement(0, new ShopItemMetadata.Achievement(0, 0), new ShopItemMetadata.Championship(0, 0),
            new ShopItemMetadata.GuildNpc(default, 0), fame ?? new ShopItemMetadata.QuestAlliance(default, 0));
        return new ShopItemMetadata(id, ShopId, itemId, 1, new ShopCost { Type = currency, Amount = price }, stock, "", requirements,
            null, sellUnit, default, "", false, false, 10000, false);
    }

    private GameSession Login(long meso = 10000, params AchievementMetadata[] trophies) {
        test = new TestGameSession();
        test.AddAchievement(trophies);
        test.SetServerTable(new ShopTable(new Dictionary<int, ShopMetadata> {
            [ShopId] = new(ShopId, 0, "shop", default, false, false, false, false, false, false, false, 0, enableReset,
                restockData),
        }));
        test.SetServerTable(new ShopItemTable(new Dictionary<int, Dictionary<int, ShopItemMetadata>> {
            [ShopId] = new() {
                [ProductId] = Product(ProductId, price: 30, stock: 0),
                [StockProductId] = Product(StockProductId, price: 20, stock: 50),
                [PackProductId] = Product(PackProductId, price: 40, stock: 50, sellUnit: 10),
                [FameProductId] = Product(FameProductId, price: 10, stock: 0, fame: new ShopItemMetadata.QuestAlliance(AllianceType.MapleAlliance, 3)),
                [AnyFameProductId] = Product(AnyFameProductId, price: 10, stock: 0, fame: new ShopItemMetadata.QuestAlliance(AllianceType.AnyFaction, 4)),
                [TrevaProductId] = Product(TrevaProductId, price: 25, stock: 0, currency: ShopCurrencyType.Treva),
                [ValorProductId] = Product(ValorProductId, price: 7, stock: 0, currency: ShopCurrencyType.ValorToken),
                [FurnishingProductId] = Product(FurnishingProductId, price: 30, stock: 0, itemId: FurnishingItemId),
                [EventMeretProductId] = Product(EventMeretProductId, price: 40, stock: 0, currency: ShopCurrencyType.EventMeret),
                [GameMeretProductId] = Product(GameMeretProductId, price: 40, stock: 0, currency: ShopCurrencyType.GameMeret),
                [GuildFundProductId] = Product(GuildFundProductId, price: 500, stock: 0, currency: ShopCurrencyType.GuildFund),
            },
        }));
        // The KR grade points of Maple Union and the Royal Guard (2: 10000, 3: 30000, 4: 60000, 5: 120000).
        var grades = new List<FameTable.Grade>();
        foreach (AllianceType alliance in new[] { AllianceType.MapleAlliance, AllianceType.RoyalGuard }) {
            grades.Add(new FameTable.Grade(alliance, 1, 0));
            grades.Add(new FameTable.Grade(alliance, 2, 10000));
            grades.Add(new FameTable.Grade(alliance, 3, 30000));
            grades.Add(new FameTable.Grade(alliance, 4, 60000));
            grades.Add(new FameTable.Grade(alliance, 5, 120000));
        }
        test.SetTable(new FameTable([], new Dictionary<int, AllianceType>(), grades));
        ItemMetadata sold = TestGameSession.Metadata(20000003);
        bagItem = test.Store(sold with { Property = sold.Property with { SellPrices = [70, 0, 0, 0, 0, 0] } }, amount: 5);
        ItemMetadata opener = TestGameSession.Metadata(ShopItemId);
        shopItem = test.Store(opener with { Property = opener.Property with { ShopId = ShopId } });
        test.Register(TestGameSession.Metadata(ProductItemId));
        test.Register(TestGameSession.Metadata(FurnishingItemId, slotMax: 2));
        beforeStart?.Invoke();
        GameSession session = test.Start();
        session.Currency.Meso = meso;
        session.Shop.Load(ShopId);
        test.TakeSent();
        return session;
    }

    private void Buy(GameSession session, int productId, int quantity, int shopId = ShopId) {
        new ShopHandler { GameStorage = test.Storage }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(4);
            writer.WriteInt(productId);
            writer.WriteInt(quantity);
            writer.WriteInt(shopId);
        }));
    }

    private void Sell(GameSession session, long uid, int quantity) {
        new ShopHandler { GameStorage = test.Storage }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(5);
            writer.WriteLong(uid);
            writer.WriteInt(quantity);
        }));
    }

    private static byte Sub(byte[] packet) => packet[2];

    // The price paid counts for the trophy of the currency paid only: shop_buy for mesos, shop_buy_karma_token for
    // Treva and shop_buy_honor_token for Valor Tokens (trophies 23200004, 23200014, 23200012).
    [TestCase(ProductId, 0, 3 * 30)]
    [TestCase(TrevaProductId, 1, 3 * 25)]
    [TestCase(ValorProductId, 2, 3 * 7)]
    public void Buy_CountsThePriceForTheTrophyOfItsCurrency(int productId, int paid, int price) {
        ConditionType[] types = [ConditionType.shop_buy, ConditionType.shop_buy_karma_token, ConditionType.shop_buy_honor_token];
        GameSession session = Login(trophies: types.Select((type, i) => TestGameSession.Trophy(23200100 + i, type, [100000])).ToArray());
        test.SetServerTable(new ConstantsTable(HonorTokenMax: 30000, KarmaTokenMax: 30000));
        session.Currency[CurrencyType.Treva] = 1000;
        session.Currency[CurrencyType.ValorToken] = 1000;

        Buy(session, productId, 3);

        Assert.That(test.Errors, Is.Empty);
        Assert.That(session.Currency.Meso, Is.EqualTo(paid == 0 ? 10000 - price : 10000));
        Assert.That(session.Currency[CurrencyType.Treva], Is.EqualTo(paid == 1 ? 1000 - price : 1000));
        Assert.That(session.Currency[CurrencyType.ValorToken], Is.EqualTo(paid == 2 ? 1000 - price : 1000));
        for (int i = 0; i < types.Length; i++) {
            Assert.That(test.TrophyCounter(23200100 + i) ?? 0, Is.EqualTo(i == paid ? price : 0), types[i].ToString());
        }
    }

    // The client maps shop cost 9 (EventMeret) to its currency 10, the red merets when they cover the price, else the
    // plain merets, and shop cost 10 (GameMeret) to its currency 9, the red merets only (FUN_1403baee0, uicommon.cpp
    // :972/1020). One wallet pays the whole price, or nothing is taken.
    [TestCase(EventMeretProductId, 100, 50, 100, 10)]
    [TestCase(EventMeretProductId, 100, 30, 60, 30)]
    [TestCase(EventMeretProductId, 20, 30, 20, 30)]
    [TestCase(GameMeretProductId, 100, 50, 100, 10)]
    [TestCase(GameMeretProductId, 100, 30, 100, 30)]
    public void Buy_TakesTheMeretCostFromTheWalletTheClientChecks(int productId, long meret, long red, long meretAfter, long redAfter) {
        GameSession session = Login();
        session.Currency.Meret = meret;
        session.Currency.GameMeret = red;

        Buy(session, productId, 1);

        Assert.That(session.Currency.Meret, Is.EqualTo(meretAfter));
        Assert.That(session.Currency.GameMeret, Is.EqualTo(redAfter));
        Assert.That(session.Currency.Meso, Is.EqualTo(10000));
    }

    // A furnishing product goes into furnishing storage, all of it or none (InventoryManager.Add; no second stack of
    // an item there). Storage room for all of it is checked before the payment: a full stack, or more than the stack
    // has room for, is refused with s_err_inventory and nothing is paid.
    [TestCase(0, 2, true)]
    [TestCase(1, 1, true)]
    [TestCase(1, 2, false)]
    [TestCase(2, 1, false)]
    public void Buy_OfAFurnishingNeedsStorageRoomForAllOfIt(int stored, int quantity, bool bought) {
        GameSession session = Login();
        for (int i = 0; i < stored; i++) {
            Assert.That(session.Item.Furnishing.AddCube(FurnishingItemId), Is.Not.Zero);
        }
        test.TakeSent();

        Buy(session, FurnishingProductId, quantity);

        List<byte[]> sent = test.TakeSent();
        Assert.Multiple(() => {
            Assert.That(session.Currency.Meso, Is.EqualTo(bought ? 10000 - 30 * quantity : 10000));
            Assert.That(session.Item.Furnishing.GetItem(FurnishingItemId)?.Amount ?? 0, Is.EqualTo(bought ? stored + quantity : stored));
            Assert.That(session.Item.Inventory.Find(FurnishingItemId), Is.Empty);
            Assert.That(test.Errors, Is.Empty);
            if (!bought) {
                Assert.That(sent.Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(ShopPacket.Error(ShopError.s_err_inventory)) }));
            }
        });
    }

    // Two slots' worth bought next to a locked stack of the product (which does not take it) with exactly two open
    // slots: the room check before the payment and the add count the same slots, so all of it is delivered (the add
    // filled the locked stack's slot count first, gave 100 and dropped the rest after the payment).
    [Test]
    public void Buy_OfMoreThanSlotMax_DeliversAllThatWasPaidFor() {
        GameSession session = Login();
        Item sample = session.Field!.ItemDrop.CreateItem(ProductItemId)!;
        Manager.BagSplitTests.Bag(session, sample, (short) (session.Item.Inventory.Filter(_ => true, sample.Inventory).Count + 3), locked: 50);
        test.TakeSent();

        Buy(session, ProductId, 200);

        Assert.Multiple(() => {
            Assert.That(session.Currency.Meso, Is.EqualTo(10000 - 30 * 200));
            Assert.That(Manager.BagSplitTests.Held(session, ProductItemId), Is.EqualTo(200));
            Assert.That(Manager.BagSplitTests.Held(session, ProductItemId, locked: true), Is.EqualTo(50));
        });
    }

    // KMS2: Meso, the bag add (ItemInventory 0 and 7), then Shop 4.
    [Test]
    public void Buy_PaysAddsThenAnswers() {
        GameSession session = Login();

        Buy(session, ProductId, 3);

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(packet => (TestGameSession.Op(packet), Sub(packet))), Is.EqualTo(new[] {
            (SendOp.Meso, sent[0][2]), (SendOp.ItemInventory, (byte) 0), (SendOp.ItemInventory, (byte) 7), (SendOp.Shop, (byte) 4),
        }));
        Assert.That(session.Currency.Meso, Is.EqualTo(10000 - 90));
        Assert.That(session.Item.Inventory.Find(ProductItemId).Sum(item => item.Amount), Is.EqualTo(3));
        // Shop 4: the item id, the count, the price, the rarity, not to the guild storage.
        Assert.That(Convert.ToHexString(sent[3]), Does.EndWith("04" + "022D3101" + "03000000" + "5A000000" + "01" + "00"));
    }

    // An ended NpcShopShowItem row names the shop's product and no running row does: the reloaded shop does not
    // hold it, so a buy pays nothing and adds nothing.
    [Test]
    public void Buy_OfAnItemTheEventHidesDoesNothing() {
        GameSession session = Login();
        test.SetServerTable(new GameEventTable(new Dictionary<int, GameEventMetadata> {
            [1] = new(1, GameEventType.NpcShopShowItem, DateTime.Now.AddDays(-30), DateTime.Now.AddDays(-1), TimeSpan.Zero, TimeSpan.Zero, [],
                new NpcShopShowItem(ShopId, [ProductItemId]), "", "", "", ""),
        }));
        session.Shop.Load(ShopId);
        test.TakeSent();

        Buy(session, ProductId, 1);

        Assert.That(session.Currency.Meso, Is.EqualTo(10000));
        Assert.That(session.Item.Inventory.Find(ProductItemId), Is.Empty);
    }

    // A product with a stock count: Shop 2 (the product and the count bought) follows the bag add.
    [Test]
    public void Buy_SendsTheBoughtCountAfterTheItem() {
        GameSession session = Login();

        Buy(session, StockProductId, 2);

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(packet => (TestGameSession.Op(packet), Sub(packet))), Is.EqualTo(new[] {
            (SendOp.Meso, sent[0][2]), (SendOp.ItemInventory, (byte) 0), (SendOp.ItemInventory, (byte) 7), (SendOp.Shop, (byte) 2), (SendOp.Shop, (byte) 4),
        }));
        Assert.That(Convert.ToHexString(sent[3]), Is.EqualTo(Hex(ShopPacket.Update(StockProductId, 2))));
        Assert.That(session.Currency.Meso, Is.EqualTo(10000 - 40));
        Assert.That(session.Item.Inventory.Find(ProductItemId).Sum(item => item.Amount), Is.EqualTo(2));
    }

    // A product sold in packs of 10: the client sends a number of packs and shows the price per pack
    // (CNpcShopProductCli, npcshopproductbase.cpp:163-222). Two packs cost two prices, use two of the stock, put
    // 20 items in the bag, and Shop 2 and Shop 4 carry 20 (the client divides the bought count by the pack size,
    // SetBuyItemCount).
    [Test]
    public void Buy_OfAPackGivesThePackSizePerQuantity() {
        GameSession session = Login();

        Buy(session, PackProductId, 2);

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(packet => (TestGameSession.Op(packet), Sub(packet))), Is.EqualTo(new[] {
            (SendOp.Meso, sent[0][2]), (SendOp.ItemInventory, (byte) 0), (SendOp.ItemInventory, (byte) 7), (SendOp.Shop, (byte) 2), (SendOp.Shop, (byte) 4),
        }));
        Assert.That(Convert.ToHexString(sent[3]), Is.EqualTo(Hex(ShopPacket.Update(PackProductId, 20))));
        Assert.That(session.Currency.Meso, Is.EqualTo(10000 - 80));
        Assert.That(session.Item.Inventory.Find(ProductItemId).Sum(item => item.Amount), Is.EqualTo(20));
        Assert.That(Convert.ToHexString(sent[4]), Does.EndWith("04" + "022D3101" + "14000000" + "50000000" + "01" + "00"));
    }

    // The client sends its window's shop id with a buy (UIShopDialog::SendPurchase).
    [Test]
    public void Buy_ForAnotherShopDoesNothing() {
        GameSession session = Login();

        Buy(session, ProductId, 1, shopId: ShopId + 1);

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(session.Currency.Meso, Is.EqualTo(10000));
    }

    [Test]
    public void Buy_WithoutMesosIsRefused() {
        GameSession session = Login(meso: 29);

        Buy(session, ProductId, 1);

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(ShopPacket.Error(ShopError.s_err_lack_meso)) }));
        Assert.That(session.Item.Inventory.Find(ProductItemId), Is.Empty);
    }

    // KMS2: the item leaves the bag, Shop 7 lists the buy-back entry, then Meso. The buy back: Shop 8, the bag
    // add, then Meso.
    [Test]
    public void SellAndBuyBack() {
        GameSession session = Login();

        Sell(session, bagItem.Uid, 5);

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(packet => (TestGameSession.Op(packet), Sub(packet))), Is.EqualTo(new[] {
            (SendOp.ItemInventory, (byte) 1), (SendOp.Shop, (byte) 7), (SendOp.Meso, sent[2][2]),
        }));
        Assert.That(session.Currency.Meso, Is.EqualTo(10000 + 350));
        Assert.That(session.Item.Inventory.Get(bagItem.Uid), Is.Null);
        int entryId = BitConverter.ToInt32(sent[1], 5);

        new ShopHandler { GameStorage = test.Storage }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteInt(entryId);
        }));

        sent = test.TakeSent();
        Assert.That(Convert.ToHexString(sent[0]), Is.EqualTo(Hex(ShopPacket.RemoveBuyBackItem(entryId))));
        Assert.That(sent.Skip(1).Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.ItemInventory, SendOp.ItemInventory, SendOp.Meso }));
        Assert.That(session.Currency.Meso, Is.EqualTo(10000));
        Assert.That(session.Item.Inventory.Find(20000003).Sum(item => item.Amount), Is.EqualTo(5));
    }

    [Test]
    public void BuyBack_WithoutMesosKeepsTheEntry() {
        GameSession session = Login();
        Sell(session, bagItem.Uid, 5);
        int entryId = BitConverter.ToInt32(test.TakeSent()[1], 5);
        session.Currency.Meso = 349;
        test.TakeSent();

        new ShopHandler { GameStorage = test.Storage }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteInt(entryId);
        }));

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(ShopPacket.Error(ShopError.s_err_lack_meso)) }));
        Assert.That(session.Item.Inventory.Find(20000003), Is.Empty);
    }

    // A buy back whose bag write fails: the entry is listed again with its id and no meso is taken. Before, the
    // mesos were taken and the item, out of the list and not in the bag, was lost.
    [Test]
    public void BuyBack_FailedWriteKeepsTheEntryAndTheMesos() {
        GameSession session = Login();
        Sell(session, bagItem.Uid, 5);
        int entryId = BitConverter.ToInt32(test.TakeSent()[1], 5);
        long mesos = session.Currency.Meso;
        test.FailSave = context => context.ChangeTracker.Entries().Any(entry => entry.Entity.GetType().Name == "Item");

        new ShopHandler { GameStorage = test.Storage }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteInt(entryId);
        }));
        test.FailSave = null;

        List<byte[]> sent = test.TakeSent();
        Assert.Multiple(() => {
            Assert.That(session.Currency.Meso, Is.EqualTo(mesos));
            Assert.That(session.Item.Inventory.Find(20000003), Is.Empty);
            Assert.That(sent.Select(packet => (TestGameSession.Op(packet), Sub(packet))), Is.EqualTo(new[] {
                // The give-back sends the balance (the price was taken without a packet).
                (SendOp.Shop, (byte) 8), (SendOp.ItemInventory, (byte) 13), (SendOp.Meso, sent[2][2]), (SendOp.Shop, (byte) 7),
            }));
            Assert.That(Convert.ToHexString(sent[1]), Is.EqualTo(Hex(ItemInventoryPacket.Error(ItemInventoryError.s_err_inventory))));
            Assert.That(BitConverter.ToInt32(sent[3], 5), Is.EqualTo(entryId));
        });

        // Bought back again with the write working, it comes back once.
        new ShopHandler { GameStorage = test.Storage }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteInt(entryId);
        }));
        Assert.Multiple(() => {
            Assert.That(session.Item.Inventory.Find(20000003).Sum(item => item.Amount), Is.EqualTo(5));
            Assert.That(session.Currency.Meso, Is.EqualTo(mesos - 350));
        });
    }

    // The buy back runs under the bag lock from its room check to the meso take, so an item or meso change made under
    // that lock on another thread (a field reward, a trade) cannot come between them. Before, only the bag calls held
    // it: the mesos were checked, the item added, and then taken with the lock released, so a spend in between made
    // the take throw with the item already given. Its save takes the account lock after the bag lock is released, the
    // order of a session save (account lock, then bag lock).
    [Test]
    public void BuyBack_RunsUnderTheBagLock() {
        GameSession session = Login();
        Sell(session, bagItem.Uid, 5);
        int entryId = BitConverter.ToInt32(test.TakeSent()[1], 5);
        long mesos = session.Currency.Meso;
        var sent = new CapturingQueue(() => System.Threading.Monitor.IsEntered(session.Item));
        FieldInfo queueField = typeof(Maple2.Server.Core.Network.Session).GetField("sendQueue", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object queue = queueField.GetValue(session)!;
        queueField.SetValue(session, new BlockingCollection<(byte[] packet, int length)>(sent));
        var lockedAtAcquire = new List<bool>();
        test.World.OnAcquire = () => lockedAtAcquire.Add(System.Threading.Monitor.IsEntered(session.Item));

        new ShopHandler { GameStorage = test.Storage }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteInt(entryId);
        }));
        test.World.OnAcquire = null;
        queueField.SetValue(session, queue);

        Assert.Multiple(() => {
            Assert.That(session.Item.Inventory.Find(20000003).Sum(item => item.Amount), Is.EqualTo(5));
            Assert.That(session.Currency.Meso, Is.LessThan(mesos));
            Assert.That(sent.Sent.Select(entry => TestGameSession.Op(entry.Packet)), Does.Contain(SendOp.Shop).And.Contain(SendOp.Meso));
            Assert.That(sent.Sent.Select(entry => entry.Locked), Is.All.True);
            Assert.That(lockedAtAcquire, Is.Not.Empty);
            Assert.That(lockedAtAcquire, Is.All.False);
        });
    }

    // A meso spend that lands between the buy back's meso check and its take (here made from the entry's removal
    // packet, as another change could come while the item is added): it finds the price already taken, so it fails,
    // and the buy back takes the price once with the item given. Before, the spend went through and the take then
    // threw: the entry was gone and the item given with no meso taken.
    [Test]
    public void BuyBack_SpendDuringTheAdd_PriceStillTaken() {
        GameSession session = Login();
        Sell(session, bagItem.Uid, 5);
        int entryId = BitConverter.ToInt32(test.TakeSent()[1], 5);
        long mesos = session.Currency.Meso;
        long price = mesos - 10000;
        Assert.That(price, Is.GreaterThan(0));
        bool? spent = null;
        var queue = new HookQueue(packet => {
            if (spent == null && TestGameSession.Op(packet) == SendOp.Shop) {
                try {
                    session.Currency.Meso = session.Currency.Meso - (mesos - price + 1);
                    spent = true;
                } catch (ArgumentException) {
                    spent = false;
                }
            }
        });
        FieldInfo queueField = typeof(Maple2.Server.Core.Network.Session).GetField("sendQueue", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object old = queueField.GetValue(session)!;
        queueField.SetValue(session, new BlockingCollection<(byte[] packet, int length)>(queue));
        try {
            Assert.DoesNotThrow(() => new ShopHandler { GameStorage = test.Storage }.Handle(session, TestGameSession.Packet(writer => {
                writer.WriteByte(3);
                writer.WriteInt(entryId);
            })));
        } finally {
            queueField.SetValue(session, old);
        }

        Assert.Multiple(() => {
            Assert.That(spent, Is.False);
            Assert.That(session.Item.Inventory.Find(20000003).Sum(item => item.Amount), Is.EqualTo(5));
            Assert.That(session.Currency.Meso, Is.EqualTo(mesos - price));
        });
    }

    // A send queue that runs a hook on each packet as it is sent.
    private sealed class HookQueue(Action<byte[]> hook) : IProducerConsumerCollection<(byte[] packet, int length)> {
        private readonly ConcurrentQueue<(byte[] packet, int length)> queue = new();

        public bool TryAdd((byte[] packet, int length) item) {
            hook(item.packet);
            queue.Enqueue(item);
            return true;
        }
        public bool TryTake(out (byte[] packet, int length) item) => queue.TryDequeue(out item);
        public int Count => queue.Count;
        public bool IsSynchronized => false;
        public object SyncRoot => this;
        public void CopyTo((byte[] packet, int length)[] array, int index) => queue.CopyTo(array, index);
        public void CopyTo(Array array, int index) => ((ICollection) queue).CopyTo(array, index);
        public (byte[] packet, int length)[] ToArray() => queue.ToArray();
        public IEnumerator<(byte[] packet, int length)> GetEnumerator() => queue.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // A send queue that records each packet with whether the bag lock was held when it was sent.
    private sealed class CapturingQueue(Func<bool> locked) : IProducerConsumerCollection<(byte[] packet, int length)> {
        private readonly ConcurrentQueue<(byte[] packet, int length)> queue = new();
        public readonly List<(byte[] Packet, bool Locked)> Sent = [];

        public bool TryAdd((byte[] packet, int length) item) {
            Sent.Add((item.packet, locked()));
            queue.Enqueue(item);
            return true;
        }
        public bool TryTake(out (byte[] packet, int length) item) => queue.TryDequeue(out item);
        public int Count => queue.Count;
        public bool IsSynchronized => false;
        public object SyncRoot => this;
        public void CopyTo((byte[] packet, int length)[] array, int index) => queue.CopyTo(array, index);
        public void CopyTo(Array array, int index) => ((ICollection) queue).CopyTo(array, index);
        public (byte[] packet, int length)[] ToArray() => queue.ToArray();
        public IEnumerator<(byte[] packet, int length)> GetEnumerator() => queue.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private void SystemShop(GameSession session, bool open, int itemId = ShopItemId) {
        var handler = new SystemShopHandler { ServerTableMetadata = test.ServerTableMetadata };
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(10);
            writer.WriteBool(open);
            if (open) {
                writer.WriteInt(itemId);
            }
        }));
    }

    // KMS2: Shop 0 (no npc), Shop 1 per product, Shop 6 (no buy-back entry), then SystemShop 10 open.
    [Test]
    public void SystemShop_ItemOpensItsShop() {
        GameSession session = Login();
        session.Shop.ClearActiveShop();

        SystemShop(session, open: true);

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(packet => (TestGameSession.Op(packet), Sub(packet))), Is.EqualTo(new[] {
            (SendOp.Shop, (byte) 0), (SendOp.Shop, (byte) 1), (SendOp.Shop, (byte) 6), (SendOp.SystemShop, (byte) 10),
        }));
        Assert.That(BitConverter.ToInt32(sent[0], 3), Is.EqualTo(0));
        Assert.That(BitConverter.ToInt32(sent[0], 7), Is.EqualTo(ShopId));
        Assert.That(Convert.ToHexString(sent[2]), Is.EqualTo(Hex(ShopPacket.BuyBackItemCount(0))));
        Assert.That(Convert.ToHexString(sent[3]), Is.EqualTo(Hex(SystemShopPacket.Item())));
    }

    // KMS2 answers the close with nothing; the shop is closed.
    [Test]
    public void SystemShop_CloseAnswersNothing() {
        GameSession session = Login();

        SystemShop(session, open: false);
        Buy(session, ProductId, 1);

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(session.Currency.Meso, Is.EqualTo(10000));
    }

    // The Meret Market home product's Coin Shop button and the gacha box dialog send the gacha's coin item, which
    // the player need not hold (uimeratmarketdialog.cpp:4145, uiitemgachaboxdialog.cpp:422): the item's shop opens.
    [Test]
    public void SystemShop_WithoutTheItemOpensItsShop() {
        GameSession session = Login();
        session.Shop.ClearActiveShop();
        Assert.That(session.Item.Inventory.Remove(shopItem.Uid, out _), Is.True);
        test.TakeSent();

        SystemShop(session, open: true);

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(packet => (TestGameSession.Op(packet), Sub(packet))), Is.EqualTo(new[] {
            (SendOp.Shop, (byte) 0), (SendOp.Shop, (byte) 1), (SendOp.Shop, (byte) 6), (SendOp.SystemShop, (byte) 10),
        }));
        Assert.That(BitConverter.ToInt32(sent[0], 7), Is.EqualTo(ShopId));
    }

    // An unknown item or an item with no shop opens nothing.
    [TestCase(99999999)]
    [TestCase(ProductItemId)]
    public void SystemShop_ItemWithoutAShopOpensNothing(int itemId) {
        GameSession session = Login();
        session.Shop.ClearActiveShop();
        test.TakeSent();

        SystemShop(session, open: true, itemId: itemId);

        Assert.That(test.TakeSent(), Is.Empty);
    }

    // KMS2 Move: no swapped item (uid 0), the old slot, the item and its new slot.
    [Test]
    public void Inventory_MoveToAnEmptySlot() {
        GameSession session = Login();
        short from = session.Item.Inventory.Get(bagItem.Uid)!.Slot;

        new ItemInventoryHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteLong(bagItem.Uid);
            writer.WriteShort(6);
        }));

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(ItemInventoryPacket.Move(0, from, bagItem.Uid, 6)) }));
        Assert.That(session.Item.Inventory.Get(bagItem.Uid)!.Slot, Is.EqualTo(6));
    }

    // KMS2 sort: the stack emptied by the merge is removed, the grown one updated, then the tab is reset and
    // sent again.
    [Test]
    public void Inventory_SortMergesStacks() {
        GameSession session = Login();
        Item second = new Item(bagItem.Metadata, amount: 2);
        Assert.That(session.Item.Inventory.Add(second), Is.True);
        Item? added = session.Item.Inventory.Find(20000003).FirstOrDefault(item => item.Uid != bagItem.Uid);
        Assume.That(added, Is.Not.Null, "the second stack was stacked at the add");
        test.TakeSent();

        new ItemInventoryHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(8);
            writer.WriteByte(bagItem.Inventory.ToClient());
            writer.WriteBool(false);
        }));

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Take(3).Select(Convert.ToHexString), Is.EqualTo(new[] {
            Hex(ItemInventoryPacket.Remove(added!.Uid)),
            Hex(ItemInventoryPacket.UpdateAmount(bagItem.Uid, 7)),
            Hex(ItemInventoryPacket.Reset(bagItem.Inventory)),
        }));
        Assert.That(sent.Skip(3).Select(packet => (TestGameSession.Op(packet), Sub(packet))), Is.All.EqualTo((SendOp.ItemInventory, (byte) 8)));
    }

    // KMS2 drop of a whole stack that cannot go to the field: the item leaves the bag, nothing else.
    [Test]
    public void Inventory_DropAllDiscardsABoundItem() {
        GameSession session = Login();
        session.Item.Inventory.Get(bagItem.Uid)!.Transfer = null;

        new ItemInventoryHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(5);
            writer.WriteLong(bagItem.Uid);
        }));

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(ItemInventoryPacket.Remove(bagItem.Uid)) }));
        Assert.That(session.Item.Inventory.Get(bagItem.Uid), Is.Null);
    }

    // The client's fame product check (FUN_141b04af0, uishopdialog.cpp:3612): below the alliance's grade the buy
    // is dropped with no answer (the client shows its own notice); at the grade it is sold.
    [Test]
    public void Buy_OfAFameProductNeedsTheAlliancesGrade() {
        GameSession session = Login();
        session.Player.Value.Unlock.FamePoints[AllianceType.MapleAlliance] = 29999;
        session.Player.Value.Unlock.FamePoints[AllianceType.GreenHoods] = 500000;

        Buy(session, FameProductId, 1);
        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(session.Currency.Meso, Is.EqualTo(10000));

        session.Player.Value.Unlock.FamePoints[AllianceType.MapleAlliance] = 30000;
        Buy(session, FameProductId, 1);
        Assert.That(test.TakeSent().Select(TestGameSession.Op), Does.Contain(SendOp.Shop));
        Assert.That(session.Currency.Meso, Is.EqualTo(10000 - 10));
        Assert.That(session.Item.Inventory.Find(ProductItemId).Sum(item => item.Amount), Is.EqualTo(1));
    }

    // A fame product of no alliance (0) takes the best grade of the alliances (GetGeneralFameGrade).
    [Test]
    public void Buy_OfAFameProductOfNoAllianceTakesTheBestGrade() {
        GameSession session = Login();
        session.Player.Value.Unlock.FamePoints[AllianceType.MapleAlliance] = 30000;

        Buy(session, AnyFameProductId, 1);
        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(session.Currency.Meso, Is.EqualTo(10000));

        session.Player.Value.Unlock.FamePoints[AllianceType.RoyalGuard] = 60000;
        Buy(session, AnyFameProductId, 1);
        Assert.That(session.Currency.Meso, Is.EqualTo(10000 - 10));
        Assert.That(session.Item.Inventory.Find(ProductItemId).Sum(item => item.Amount), Is.EqualTo(1));
    }

    // The session as the master of a guild, without the World's guild load.
    private void JoinAsMaster(GameSession session) {
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var manager = (GuildManager) RuntimeHelpers.GetUninitializedObject(typeof(GuildManager));
        GC.SuppressFinalize(manager);
        typeof(GuildManager).GetField("session", any)!.SetValue(manager, session);
        typeof(GuildManager).GetField("tokenSource", any)!.SetValue(manager, new CancellationTokenSource());
        typeof(GuildManager).GetField("logger", any)!.SetValue(manager, Log.Logger);
        var info = new PlayerInfo(new CharacterInfo(session.AccountId, session.CharacterId, "Self", "", "", Gender.Male, Job.Newbie, 20), "",
            new AchievementInfo(), []);
        var self = new GuildMember { GuildId = GuildId, Info = info, Rank = 0, JoinTime = 1000 };
        var guild = new Guild(GuildId, "Guild", self) { Funds = 10000 };
        guild.Members[self.CharacterId] = self;
        typeof(GuildManager).GetProperty("Guild", any)!.SetValue(manager, guild);
        session.Guild = manager;
    }

    // A guild-fund product goes to the guild storage: the World takes the fund and stores the item in one call
    // (SpendFund with the item), nothing goes in the bag, and Shop 4 ends with its to-guild-storage flag set
    // (UIShopDialog::ReadPurchaseResult shows s_msg_take_item_to_guild_storage).
    [Test]
    public void Buy_OfAGuildFundProduct_GoesToTheGuildStorage() {
        GameSession session = Login();
        JoinAsMaster(session);
        test.World.GuildWorld = _ => new GuildResponse { GuildId = GuildId };

        Buy(session, GuildFundProductId, 2);

        GuildRequest.Types.SpendFund spend = test.World.GuildRequests.Single().SpendFund;
        Assert.Multiple(() => {
            Assert.That((spend.GuildId, spend.Amount, spend.ItemId, spend.Rarity, spend.ItemAmount),
                Is.EqualTo((GuildId, 2 * 500, ProductItemId, 1, 2)));
            Assert.That(session.Item.Inventory.Find(ProductItemId), Is.Empty);
            Assert.That(session.Currency.Meso, Is.EqualTo(10000));
            List<byte[]> sent = test.TakeSent();
            Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.Shop }));
            Assert.That(Convert.ToHexString(sent[0]), Does.EndWith("04" + "022D3101" + "02000000" + "E8030000" + "01" + "01"));
        });
    }

    // The World refuses (not the master, not enough fund): the shop error, nothing stored, nothing in the bag.
    [TestCase(GuildError.s_guild_err_not_enough_guild_fund, ShopError.s_guild_err_not_enough_guild_fund)]
    [TestCase(GuildError.s_guild_err_no_authority, ShopError.s_guild_err_buy_no_master)]
    public void Buy_OfAGuildFundProduct_RefusedByTheWorld_GivesNothing(GuildError error, ShopError shopError) {
        GameSession session = Login();
        JoinAsMaster(session);
        test.World.GuildWorld = _ => new GuildResponse { Error = (int) error };

        Buy(session, GuildFundProductId, 1);

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(ShopPacket.Error(shopError)) }));
        Assert.That(session.Item.Inventory.Find(ProductItemId), Is.Empty);
    }

    // The World failed (its save) or its answer was lost (the buy may be stored; the members' storage list shows it):
    // the shop unlocks with no notice of its own and the guild's unknown error is shown. It used to show the "master
    // only" notice to the master.
    [TestCase(false)]
    [TestCase(true)]
    public void Buy_OfAGuildFundProduct_WorldFailedOrAnswerLost_ShowsTheUnknownError(bool lost) {
        GameSession session = Login();
        JoinAsMaster(session);
        test.World.GuildWorld = _ => lost
            ? throw new Grpc.Core.RpcException(new Grpc.Core.Status(Grpc.Core.StatusCode.Unavailable, "lost"))
            : new GuildResponse { Error = (int) GuildError.s_guild_err_unknown };

        Buy(session, GuildFundProductId, 1);

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] {
            Hex(ShopPacket.Error(ShopError.s_err_no_notice)), Hex(GuildPacket.Error(GuildError.s_guild_err_unknown)),
        }));
        Assert.That(Convert.ToHexString(ShopPacket.Error(ShopError.s_err_no_notice).ToArray()), Does.Contain("08000000"));
        Assert.That(session.Item.Inventory.Find(ProductItemId), Is.Empty);
    }

    // The instant restock (Shop 9) pays the shop's restock price in one step, and a price below 0 is refused. The
    // Treva price was checked with `held < price` and taken with Change(type, -price), so a negative price added.
    [TestCase(-100, 1000)]
    [TestCase(100, 900)]
    public void InstantRestock_PaysThePriceAndRefusesANegativeOne(int price, int treva) {
        restockData = new ShopRestockData(ResetType.Default, ShopCurrencyType.Treva, ShopCurrencyType.Treva, 0, 0, price, false, false, false);
        GameSession session = Login();
        test.SetServerTable(new ConstantsTable(KarmaTokenMax: 30000));
        session.Currency[CurrencyType.Treva] = 1000;

        new ShopHandler { GameStorage = test.Storage }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(9);
            writer.WriteInt(price);
        }));

        Assert.That(session.Currency[CurrencyType.Treva], Is.EqualTo(treva));
    }

    // The restock part the shop packet ends with (Shop.WriteTo): the cost's currency, the excess currency, the
    // item id, the price, the multiplier flag, the count, the reset type and two flags.
    private static string RestockTail(ShopCurrencyType type, int price, int count, ResetType reset) {
        var writer = new Maple2.PacketLib.Tools.ByteWriter();
        writer.Write<ShopCurrencyType>(type);
        writer.Write<ShopCurrencyType>(ShopCurrencyType.Treva);
        writer.WriteInt();
        writer.WriteInt(price);
        writer.WriteBool(true);
        writer.WriteInt(count);
        writer.Write<ResetType>(reset);
        writer.WriteBool(false);
        writer.WriteBool(false);
        return Convert.ToHexString(writer.ToArray());
    }

    private static void Restock(GameSession session, int price) {
        new ShopHandler { GameStorage = session.GameStorage }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(9);
            writer.WriteInt(price);
        }));
    }

    // With the multiplier, the first five restocks cost 50000 mesos and the sixth 10 of the excess currency;
    // each shop packet after a restock shows the next one's cost. Before, every restock took the table's price
    // (300) in mesos and the packet showed it.
    [Test]
    public void InstantRestock_WithTheMultiplier_TakesTheCostTheShopShows() {
        restockData = new ShopRestockData(ResetType.Default, ShopCurrencyType.Meso, ShopCurrencyType.Treva, 0, 0, 300, true, false, false);
        enableReset = true;
        GameSession session = Login(meso: 300000);
        test.SetServerTable(new ConstantsTable(KarmaTokenMax: 30000));
        session.Currency[CurrencyType.Treva] = 1000;

        for (int i = 0; i < 6; i++) {
            Restock(session, 0);
        }

        Assert.That(session.Currency.Meso, Is.EqualTo(50000));
        Assert.That(session.Currency[CurrencyType.Treva], Is.EqualTo(990));
        Assert.That(test.TakeSent().Select(Convert.ToHexString).Count(hex => hex.EndsWith(RestockTail(ShopCurrencyType.Treva, 20, 6, ResetType.Default))), Is.EqualTo(1));
    }

    // A stored shop (a login after restocks the day before reset) keeps its restock count: the shop packet shows
    // it and the next cost follows it. Before, the count read 0.
    [Test]
    public void Load_OfAStoredShop_KeepsItsRestockCount() {
        restockData = new ShopRestockData(ResetType.Day, ShopCurrencyType.Meso, ShopCurrencyType.Treva, 0, 0, 300, true, false, false);
        enableReset = true;
        beforeStart = () => {
            using GameStorage.Request db = test.Storage.Context();
            db.CreateCharacterShopData(test.CharacterId, new CharacterShopData {
                ShopId = ShopId,
                Interval = ResetType.Day,
                RestockTime = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds(),
                RestockCount = 5,
            });
        };
        GameSession session = Login(meso: 300000);
        test.SetServerTable(new ConstantsTable(KarmaTokenMax: 30000));
        session.Currency[CurrencyType.Treva] = 1000;

        session.Shop.Load(ShopId);
        Assert.That(test.TakeSent().Select(Convert.ToHexString).Count(hex => hex.EndsWith(RestockTail(ShopCurrencyType.Treva, 10, 5, ResetType.Day))), Is.EqualTo(1));

        Restock(session, 10);
        Assert.That(session.Currency.Meso, Is.EqualTo(300000));
        Assert.That(session.Currency[CurrencyType.Treva], Is.EqualTo(990));
    }
}
