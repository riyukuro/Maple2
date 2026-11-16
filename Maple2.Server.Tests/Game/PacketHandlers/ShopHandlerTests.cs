using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
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
using Maple2.Server.Game.Model;
using Maple2.Tools.VectorMath;
using Maple2.Server.World.Service;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
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
    private const int StarPointProductId = 22;
    private const int ItemCostProductId = 23;
    private const int GuildFundStockProductId = 24;
    private const int PaymentItemId = 20000004;
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
    private ShopFrameType frameType;
    private Action? beforeStart;
    // The store of Login's session: null for the in-memory one (the MySQL test sets it).
    private DbContextOptions? storeOptions;

    // NUnit keeps one fixture for all its tests: the shop's settings start anew for each.
    [SetUp]
    public void ResetShop() {
        restockData = new ShopRestockData(ResetType.Default, ShopCurrencyType.Meso, ShopCurrencyType.Meso, 0, 0, 0, false, true, false);
        enableReset = false;
        frameType = ShopFrameType.Default;
        beforeStart = null;
        storeOptions = null;
    }

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());
    // The command of a refused buy and buy-back: the client's selection to reset (UIShopDialog::ReadShopError).
    private const byte BuyCommand = 4;
    private const byte BuyBackCommand = 3;
    private static readonly string NoNotice = Hex(ShopPacket.Error(ShopError.s_err_no_notice, BuyCommand));

    private static ShopItemMetadata Product(int id, int price, int stock, short sellUnit = 1, ShopItemMetadata.QuestAlliance? fame = null,
                                            ShopCurrencyType currency = ShopCurrencyType.Meso, int itemId = ProductItemId, int costItemId = 0) {
        var requirements = new ShopItemMetadata.Requirement(0, new ShopItemMetadata.Achievement(0, 0), new ShopItemMetadata.Championship(0, 0),
            new ShopItemMetadata.GuildNpc(default, 0), fame ?? new ShopItemMetadata.QuestAlliance(default, 0));
        return new ShopItemMetadata(id, ShopId, itemId, 1, new ShopCost { Type = currency, ItemId = costItemId, Amount = price }, stock, "", requirements,
            null, sellUnit, default, "", false, false, 10000, false);
    }

    private GameSession Login(long meso = 10000, params AchievementMetadata[] trophies) {
        test = new TestGameSession(storeOptions);
        test.AddAchievement(trophies);
        test.SetServerTable(new ShopTable(new Dictionary<int, ShopMetadata> {
            [ShopId] = new(ShopId, 0, "shop", frameType, false, false, false, false, false, false, false, 0, enableReset,
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
                [StarPointProductId] = Product(StarPointProductId, price: 5, stock: 0, currency: ShopCurrencyType.StarPoint),
                [ItemCostProductId] = Product(ItemCostProductId, price: 3, stock: 0, currency: ShopCurrencyType.Item, costItemId: PaymentItemId),
                [GuildFundStockProductId] = Product(GuildFundStockProductId, price: 500, stock: 5, currency: ShopCurrencyType.GuildFund),
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

    // An item the shop does not buy (its limit's shopSell off), one with a gemstone in a socket and one locked are refused
    // with code 5 and the sell command's number (KMS2's answer); the item stays and no mesos are paid.
    [TestCase("notForSale")]
    [TestCase("gemstone")]
    [TestCase("locked")]
    public void Sell_OfAnItemTheShopWillNotBuyIsRefusedAndKept(string reason) {
        Item? notForSale = null;
        beforeStart = () => {
            ItemMetadata metadata = TestGameSession.Metadata(20000005);
            notForSale = test.Store(metadata with { Limit = metadata.Limit with { ShopSell = reason != "notForSale" } });
        };
        GameSession session = Login();
        Item item = session.Item.Inventory.Get(notForSale!.Uid)!;
        switch (reason) {
            case "gemstone":
                item.Socket = new ItemSocket(1, [new ItemGemstone(20000001, stats: new ItemStats())]);
                break;
            case "locked":
                item.IsLocked = true;
                item.UnlockTime = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds();
                break;
        }
        long meso = session.Currency.Meso;

        Sell(session, notForSale!.Uid, 1);

        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(ShopPacket.Error(ShopError.s_err_cannot_sell_item, 5)) }));
            Assert.That(session.Item.Inventory.Get(notForSale!.Uid), Is.Not.Null);
            Assert.That(session.Currency.Meso, Is.EqualTo(meso));
        });
    }

    // A buy the wallet cannot pay is answered with the lack notice of its currency and the buy's command number, and
    // takes nothing: merets (event and game merets too) s_err_lack_merat; a payment item the item's own id; any other
    // currency s_err_lack_meso with the currency kind in the last int (uishopdialog.cpp ReadShopError).
    [Test]
    public void Buy_WithoutTheCurrencyIsRefusedWithItsLackNotice() {
        GameSession session = Login();
        session.Currency.Meret = 0;
        test.TakeSent();
        long meso = session.Currency.Meso;
        var expected = new List<string> {
            Hex(ShopPacket.Error(ShopError.s_err_lack_merat, BuyCommand)),
            Hex(ShopPacket.Error(ShopError.s_err_lack_merat, BuyCommand)),
            Hex(ShopPacket.Error(ShopError.s_err_lack_merat, BuyCommand)),
            Hex(ShopPacket.Error(ShopError.s_err_lack_payment_item, BuyCommand, PaymentItemId)),
            Hex(ShopPacket.Error(ShopError.s_err_lack_meso, BuyCommand, (int) ShopCurrencyType.Treva)),
            Hex(ShopPacket.Error(ShopError.s_err_lack_meso, BuyCommand, (int) ShopCurrencyType.ValorToken)),
        };

        foreach (int product in new[] { EventMeretProductId, GameMeretProductId, EventMeretProductId, ItemCostProductId, TrevaProductId, ValorProductId }) {
            Buy(session, product, 1);
        }

        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString).ToList(), Is.EqualTo(expected));
            Assert.That(session.Currency.Meso, Is.EqualTo(meso));
            Assert.That(session.Item.Inventory.Find(ProductItemId), Is.Empty);
        });
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
                Assert.That(sent.Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(ShopPacket.Error(ShopError.s_err_inventory, BuyCommand)) }));
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

    // A shop opened by an npc talk, the npc standing in the player's field at the given distance.
    private FieldNpc TalkShop(GameSession session, float distance, string[]? propertyTags = null) {
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var basic = new NpcMetadataBasic(0, 0, 0, 0, 0, 0, 0, 1, 1, false, 0, 0, 0, 0, [], [], 0, PropertyTags: propertyTags);
        var metadata = new NpcMetadata(11000001, "npc", "", null!, null!, basic, null!, null!, null!, null!, null!, null!, null, null!);
        var npc = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        typeof(Actor<Npc>).GetField("<Value>k__BackingField", any)!.SetValue(npc, new Npc(metadata, null));
        typeof(Actor<Npc>).GetField("<ObjectId>k__BackingField", any)!.SetValue(npc, 7001);
        typeof(Actor<Npc>).GetField("<Transform>k__BackingField", any)!.SetValue(npc, new Transform());
        npc.Position = session.Player.Position + new Vector3(distance, 0, 0);
        session.Field!.Npcs[npc.ObjectId] = npc;
        session.Shop.Load(ShopId, metadata.Id, npc);
        test.TakeSent();
        return npc;
    }

    // The client keeps an npc's shop only while the talk is on (UIShopDialog::vfunc_72) and ends the talk once the
    // npc is out of talk range or gone (UITalkManager::EndTalkIfNpcGone): a buy or a sale after that does nothing,
    // even back in range.
    [TestCase(100f, false, ExpectedResult = 9970L)]
    [TestCase(1000f, false, ExpectedResult = 10000L)]
    [TestCase(100f, true, ExpectedResult = 10000L)]
    public long Buy_FromATalkShopNeedsItsNpcInTalkRange(float distance, bool npcGone) {
        GameSession session = Login();
        FieldNpc npc = TalkShop(session, distance);
        if (npcGone) {
            session.Field!.Npcs.TryRemove(npc.ObjectId, out _);
        }

        Buy(session, ProductId, 1);
        npc.Position = session.Player.Position;
        session.Field!.Npcs[npc.ObjectId] = npc;
        if (distance > 400f || npcGone) {
            Buy(session, ProductId, 1); // the shop stays closed
        }

        return session.Currency.Meso;
    }

    [TestCase(100f, ExpectedResult = 4)]
    [TestCase(1000f, ExpectedResult = 5)]
    public int Sell_ToATalkShopNeedsItsNpcInTalkRange(float distance) {
        GameSession session = Login();
        TalkShop(session, distance);

        Sell(session, bagItem.Uid, 1);

        return session.Item.Inventory.Find(bagItem.Id).Sum(item => item.Amount);
    }

    // An npc with the ignore_talk_distance tag is in talk range anywhere in the field (the client's IsInTalkRange).
    [Test]
    public void Buy_FromATalkShopWhoseNpcIgnoresTalkDistance() {
        GameSession session = Login();
        TalkShop(session, 5000f, [FieldNpc.IgnoreTalkDistanceTag]);

        Buy(session, ProductId, 1);

        Assert.That(session.Currency.Meso, Is.EqualTo(9970));
    }

    // A field change takes the player away from the npc: the shop is closed.
    [Test]
    public void Buy_FromATalkShopAfterAFieldChangeDoesNothing() {
        GameSession session = Login();
        FieldNpc npc = TalkShop(session, 100f);
        test.EnterMap(TestGameSession.Map(2000001));
        session.Player.Position = npc.Position;

        Buy(session, ProductId, 1);

        Assert.That(session.Currency.Meso, Is.EqualTo(10000));
    }

    // The buy back and the list refresh (Shop 3, Shop 10) close the shop the same way.
    [TestCase(100f, ExpectedResult = 5)]
    [TestCase(1000f, ExpectedResult = 0)]
    public int BuyBack_FromATalkShopNeedsItsNpcInTalkRange(float distance) {
        GameSession session = Login();
        FieldNpc npc = TalkShop(session, 100f);
        Sell(session, bagItem.Uid, 5);
        int entryId = BitConverter.ToInt32(test.TakeSent()[1], 5);
        npc.Position = session.Player.Position + new Vector3(distance, 0, 0);

        new ShopHandler { GameStorage = test.Storage }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteInt(entryId);
        }));

        return session.Item.Inventory.Find(20000003).Sum(item => item.Amount);
    }

    [TestCase(100f, ExpectedResult = true)]
    [TestCase(1000f, ExpectedResult = false)]
    public bool Refresh_OfATalkShopNeedsItsNpcInTalkRange(float distance) {
        enableReset = true;
        GameSession session = Login();
        TalkShop(session, distance);

        new ShopHandler { GameStorage = test.Storage }.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(10)));

        return test.TakeSent().Exists(packet => TestGameSession.Op(packet) == SendOp.Shop);
    }

    // A shop opened without an npc (an item, a system shop) has no talk to keep.
    [Test]
    public void Buy_FromAShopWithoutNpcNeedsNoRange() {
        GameSession session = Login();

        Buy(session, ProductId, 1);

        Assert.That(session.Currency.Meso, Is.EqualTo(9970));
    }

    // The client sends its window's shop id with a buy (UIShopDialog::SendPurchase). The buy is refused with no
    // notice, which unlocks the window (UIShopDialog::ReadShopError).
    [Test]
    public void Buy_ForAnotherShopIsRefusedWithNoNotice() {
        GameSession session = Login();

        Buy(session, ProductId, 1, shopId: ShopId + 1);

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { NoNotice }));
        Assert.That(session.Currency.Meso, Is.EqualTo(10000));
    }

    [Test]
    public void Buy_WithoutMesosIsRefused() {
        GameSession session = Login(meso: 29);

        Buy(session, ProductId, 1);

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(ShopPacket.Error(ShopError.s_err_lack_meso, BuyCommand)) }));
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

    // A sale is stored at once, the sold row (owner 0) with the wallet. Before, a whole stack kept the character as
    // owner until the next session save while a later purchase stored the sale's mesos: a stop in between gave
    // back the item and kept the mesos. A part sold is stored the same way.
    [TestCase(5)]
    [TestCase(2)]
    public void Sell_StoresTheRowWithTheMesos(int quantity) {
        GameSession session = Login();

        Sell(session, bagItem.Uid, quantity);

        Assert.Multiple(() => {
            Assert.That(StoredMeso(), Is.EqualTo(10000 + 70 * quantity));
            Assert.That(ItemRows(20000003).OrderBy(row => row.Owner).ToList(), quantity == 5
                ? Is.EqualTo(new[] { (0L, 5) })
                : Is.EqualTo(new[] { (0L, quantity), (test.CharacterId, 5 - quantity) }));
        });
    }

    // The sale's store may come back 2 s later (a reset being stored): by then the item may be bought back, and its
    // row, the character's again, is not written to owner 0.
    [Test]
    public void Sell_ARetriedStoreAfterABuyBack_LeavesTheRow() {
        GameSession session = Login();
        Item sold = session.Item.Inventory.Get(bagItem.Uid)!;
        Sell(session, bagItem.Uid, 5);
        int entryId = BitConverter.ToInt32(test.TakeSent()[1], 5);
        new ShopHandler { GameStorage = test.Storage }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteInt(entryId);
        }));
        Assume.That(ItemOwner(bagItem.Uid), Is.EqualTo(test.CharacterId));

        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(session.Shop.StoreSold(db, entryId, sold), Is.False);
        }

        Assert.That(ItemOwner(bagItem.Uid), Is.EqualTo(test.CharacterId));
    }

    // The shop sell rate (SpecialAttribute.ShopBuyingMeso, +20% here) raises the sale; the buy back costs the raised
    // price, so selling and buying back gains nothing.
    [Test]
    public void ShopSellRate_RaisesTheSaleAndTheBuyBackPrice() {
        GameSession session = Login();
        session.Player.Stats.Values[SpecialAttribute.ShopBuyingMeso].AddTotal(200);

        Sell(session, bagItem.Uid, 5);

        Assert.That(session.Currency.Meso, Is.EqualTo(10000 + 420));
        int entryId = BitConverter.ToInt32(test.TakeSent()[1], 5);
        new ShopHandler { GameStorage = test.Storage }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteInt(entryId);
        }));
        Assert.That(session.Currency.Meso, Is.EqualTo(10000));
        Assert.That(session.Item.Inventory.Find(20000003).Sum(item => item.Amount), Is.EqualTo(5));
    }

    // A buy back with the shop closed is refused with no notice, which turns the client's window on again.
    [Test]
    public void BuyBack_WithTheShopClosedIsRefusedWithNoNotice() {
        GameSession session = Login();
        Sell(session, bagItem.Uid, 5);
        int entryId = BitConverter.ToInt32(test.TakeSent()[1], 5);
        session.Shop.ClearActiveShop();
        test.TakeSent();

        new ShopHandler { GameStorage = test.Storage }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteInt(entryId);
        }));

        Assert.That(test.TakeSent().Select(Convert.ToHexString),
            Is.EqualTo(new[] { Hex(ShopPacket.Error(ShopError.s_err_no_notice, BuyBackCommand)) }));
        Assert.That(session.Currency.Meso, Is.EqualTo(10350));
    }

    // The client counts a payment item at its item data's rank (GetMaxAffordableCount, npcshopproductbase.cpp:475,
    // with GetItemRank): the buy takes copies of that rank only, and copies of another rank do not pay. Before, any
    // rank was taken, the other rank's copies first here.
    [Test]
    public void Buy_PaidWithAnItem_TakesCopiesOfItsRankOnly() {
        ItemMetadata payment = TestGameSession.Metadata(PaymentItemId);
        payment = payment with { Property = payment.Property with { Rank = 4 } };
        Item other = null!;
        Item ranked = null!;
        beforeStart = () => {
            other = test.Store(payment, amount: 5, rarity: 2);
            ranked = test.Store(payment, amount: 5, rarity: 4);
        };
        GameSession session = Login();

        Buy(session, ItemCostProductId, 1);
        Assert.Multiple(() => {
            Assert.That(session.Item.Inventory.Find(ProductItemId).Sum(item => item.Amount), Is.EqualTo(1));
            Assert.That(session.Item.Inventory.Get(other.Uid)?.Amount, Is.EqualTo(5));
            Assert.That(session.Item.Inventory.Get(ranked.Uid)?.Amount, Is.EqualTo(2));
        });
        test.TakeSent();

        Buy(session, ItemCostProductId, 1);
        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString),
                Does.Contain(Hex(ShopPacket.Error(ShopError.s_err_lack_payment_item, BuyCommand, PaymentItemId))));
            Assert.That(session.Item.Inventory.Find(ProductItemId).Sum(item => item.Amount), Is.EqualTo(1));
            Assert.That(session.Item.Inventory.Get(other.Uid)?.Amount, Is.EqualTo(5));
            Assert.That(session.Item.Inventory.Get(ranked.Uid)?.Amount, Is.EqualTo(2));
        });
    }

    // A full buy-back list drops its oldest entry by id. The entries' added time is in seconds, so the old rule
    // (the earliest time) dropped the first entry the list enumerated, which after a buy back was the newest one.
    [Test]
    public void Sell_WithAFullBuyBackList_DropsTheOldestEntry() {
        Item stack = null!;
        beforeStart = () => {
            ItemMetadata sold = TestGameSession.Metadata(20000005);
            stack = test.Store(sold with { Property = sold.Property with { SellPrices = [70, 0, 0, 0, 0, 0] } }, amount: 20);
        };
        GameSession session = Login();
        var entries = new List<int>();
        for (int i = 0; i < Constant.MaxBuyBackItems; i++) {
            Sell(session, stack.Uid, 1);
            entries.Add(BitConverter.ToInt32(test.TakeSent()[1], 5));
        }
        new ShopHandler { GameStorage = test.Storage }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteInt(entries[0]);
        }));
        Sell(session, stack.Uid, 1);
        test.TakeSent();

        Sell(session, stack.Uid, 1);

        List<string> sent = test.TakeSent().Select(Convert.ToHexString).ToList();
        Assert.That(sent, Does.Contain(Hex(ShopPacket.RemoveBuyBackItem(entries[1]))));
        Assert.That(sent.Count(hex => hex.StartsWith(Hex(ShopPacket.RemoveBuyBackItem(0))[..6])), Is.EqualTo(1));
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

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(ShopPacket.Error(ShopError.s_err_lack_meso, BuyBackCommand)) }));
        Assert.That(session.Item.Inventory.Find(20000003), Is.Empty);
    }

    // A buy back whose store fails (the item's row or the wallet): the entry stays listed and no meso is taken, and
    // the answer is the error with no notice. Before, the bag's write came first and the wallet was stored after it.
    [TestCase(false)]
    [TestCase(true)]
    public void BuyBack_NotStoredKeepsTheEntryAndTheMesos(bool wallet) {
        GameSession session = Login();
        Sell(session, bagItem.Uid, 5);
        int entryId = BitConverter.ToInt32(test.TakeSent()[1], 5);
        SaveShop(session);
        long mesos = session.Currency.Meso;
        long stored = StoredMeso();
        test.FailSave = wallet ? WalletSave : context => context.ChangeTracker.Entries().Any(entry => entry.Entity.GetType().Name == "Item");

        BuyBack(session, entryId);
        test.FailSave = null;

        Assert.Multiple(() => {
            Assert.That(session.Currency.Meso, Is.EqualTo(mesos));
            // With the item's write failing, the in-memory store kept the wallet written before it (no transaction).
            if (wallet) {
                Assert.That(StoredMeso(), Is.EqualTo(stored));
            }
            Assert.That(session.Item.Inventory.Find(20000003), Is.Empty);
            Assert.That(ItemOwner(bagItem.Uid), Is.EqualTo(0));
            Assert.That(test.TakeSent().Select(Convert.ToHexString),
                Is.EqualTo(new[] { Hex(ShopPacket.Error(ShopError.s_err_no_notice, BuyBackCommand)) }));
        });

        // Bought back again with the store working, it comes back once, stored with the wallet.
        BuyBack(session, entryId);
        Assert.Multiple(() => {
            Assert.That(session.Item.Inventory.Find(20000003).Sum(item => item.Amount), Is.EqualTo(5));
            Assert.That(session.Currency.Meso, Is.EqualTo(mesos - 350));
            Assert.That(StoredMeso(), Is.EqualTo(mesos - 350));
            Assert.That(ItemOwner(bagItem.Uid), Is.EqualTo(test.CharacterId));
        });
    }

    // A buy back stores the item's row back to the character with the wallet, before the bag shows it.
    [Test]
    public void BuyBack_StoresTheItemWithTheWallet() {
        GameSession session = Login();
        Sell(session, bagItem.Uid, 5);
        int entryId = BitConverter.ToInt32(test.TakeSent()[1], 5);
        SaveShop(session);
        Assert.That(ItemOwner(bagItem.Uid), Is.EqualTo(0));

        BuyBack(session, entryId);

        Assert.Multiple(() => {
            Assert.That(StoredMeso(), Is.EqualTo(10000));
            Assert.That(ItemOwner(bagItem.Uid), Is.EqualTo(test.CharacterId));
        });
    }

    // The session save of the shop and the wallet: the buy-back entries' rows go to owner 0.
    private void SaveShop(GameSession session) {
        using (GameStorage.Request db = test.Storage.Context()) {
            session.Shop.Save(db);
        }
        session.SavePurchase();
    }

    // The bought count the open shop shows for a product.
    private static int? Purchased(GameSession session, int productId) {
        var shop = (Shop?) typeof(ShopManager).GetField("activeShop", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session.Shop);
        return shop != null && shop.Items.TryGetValue(productId, out ShopItem? item) ? item.StockPurchased : null;
    }

    private void BuyBack(GameSession session, int entryId) {
        new ShopHandler { GameStorage = test.Storage }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteInt(entryId);
        }));
    }

    private static bool WalletSave(DbContext context) =>
        context.ChangeTracker.Entries().Any(entry => entry.State == EntityState.Modified && entry.Entity is Maple2.Database.Model.Account or Maple2.Database.Model.Character);

    private long StoredMeso() {
        using var context = new Maple2.Database.Context.Ms2Context(test.Options);
        return context.Character.AsNoTracking().Single(row => row.Id == test.CharacterId).Currency.Meso;
    }

    private long? ItemOwner(long uid) {
        using var context = new Maple2.Database.Context.Ms2Context(test.Options);
        return context.Item.AsNoTracking().SingleOrDefault(row => row.Id == uid)?.OwnerId;
    }

    // Item rows of an item id: (owner, amount).
    private List<(long Owner, int Amount)> ItemRows(int itemId) {
        using var context = new Maple2.Database.Context.Ms2Context(test.Options);
        return context.Item.AsNoTracking().Where(row => row.ItemId == itemId).AsEnumerable().Select(row => (row.OwnerId, row.Amount)).ToList();
    }

    // The character's stored bought count of a product (null: no row).
    private int? StoredStock(int productId) {
        using var context = new Maple2.Database.Context.Ms2Context(test.Options);
        return context.CharacterShopItemData.AsNoTracking()
            .SingleOrDefault(row => row.ShopId == ShopId && row.ShopItemId == productId && row.OwnerId == test.CharacterId)?.StockPurchased;
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

    // KMS2 answers the close with nothing; the shop is closed and a later buy is refused with no notice.
    [Test]
    public void SystemShop_CloseClosesTheShop() {
        GameSession session = Login();

        SystemShop(session, open: false);
        Assert.That(test.TakeSent(), Is.Empty);
        Buy(session, ProductId, 1);

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { NoNotice }));
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
    // is refused with no notice of the server's (the client shows its own) to unlock the window; at the grade it is sold.
    [Test]
    public void Buy_OfAFameProductNeedsTheAlliancesGrade() {
        GameSession session = Login();
        session.Player.Value.Unlock.FamePoints[AllianceType.MapleAlliance] = 29999;
        session.Player.Value.Unlock.FamePoints[AllianceType.GreenHoods] = 500000;

        Buy(session, FameProductId, 1);
        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { NoNotice }));
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
        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { NoNotice }));
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

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(ShopPacket.Error(shopError, BuyCommand)) }));
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
            NoNotice, Hex(GuildPacket.Error(GuildError.s_guild_err_unknown)),
        }));
        Assert.That(Convert.ToHexString(ShopPacket.Error(ShopError.s_err_no_notice).ToArray()), Does.Contain("08000000"));
        Assert.That(session.Item.Inventory.Find(ProductItemId), Is.Empty);
    }

    // A guild-fund product with a stock in a shop without a reset: the bought count is shown (Shop 5) and stored in
    // the product's own row, made at the first buy (the shop has no restock row); a buy past the stock is refused
    // before the World is asked, so no fund is taken for it.
    [Test]
    public void Buy_OfAGuildFundProductWithStock_CountsAndStoresTheStock() {
        GameSession session = Login();
        JoinAsMaster(session);
        test.World.GuildWorld = _ => new GuildResponse { GuildId = GuildId };

        Buy(session, GuildFundStockProductId, 2);
        Assert.That((Purchased(session, GuildFundStockProductId), StoredStock(GuildFundStockProductId)), Is.EqualTo((2, 2)));
        Assert.That(test.TakeSent().Select(Convert.ToHexString), Does.Contain(Hex(ShopPacket.Update(GuildFundStockProductId, 2))));

        Buy(session, GuildFundStockProductId, 1);
        Assert.That((Purchased(session, GuildFundStockProductId), StoredStock(GuildFundStockProductId)), Is.EqualTo((3, 3)));

        test.TakeSent();
        Buy(session, GuildFundStockProductId, 3);
        Assert.That(test.World.GuildRequests, Has.Count.EqualTo(2));
        Assert.That((Purchased(session, GuildFundStockProductId), StoredStock(GuildFundStockProductId)), Is.EqualTo((3, 3)));
    }

    // The instant restock (Shop 9) pays the shop's restock price in one step, and a price below 0 is refused. The
    // Treva price was checked with `held < price` and taken with Change(type, -price), so a negative price added.
    [TestCase(-100, 1000)]
    [TestCase(100, 900)]
    public void InstantRestock_PaysThePriceAndRefusesANegativeOne(int price, int treva) {
        restockData = new ShopRestockData(ResetType.Default, ShopCurrencyType.Treva, ShopCurrencyType.Treva, 0, 0, price, false, false, false);
        enableReset = true;
        GameSession session = Login();
        test.SetServerTable(new ConstantsTable(KarmaTokenMax: 30000));
        session.Currency[CurrencyType.Treva] = 1000;

        new ShopHandler { GameStorage = test.Storage }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(9);
            writer.WriteInt(price);
        }));

        Assert.That(session.Currency[CurrencyType.Treva], Is.EqualTo(treva));
    }

    // The client sends the instant restock only from the reset button, shown with the shop's reset on, a reset type
    // other than 99 and the hide flag off (uishopdialog.cpp:912-920); otherwise nothing is paid. Before, only the hide
    // flag was asked.
    [TestCase(true, ResetType.Default, false, ExpectedResult = 900)]
    [TestCase(false, ResetType.Default, false, ExpectedResult = 1000)]
    [TestCase(true, ResetType.Unlimited, false, ExpectedResult = 1000)]
    [TestCase(true, ResetType.Default, true, ExpectedResult = 1000)]
    public long InstantRestock_OnlyWhereTheClientShowsTheButton(bool reset, ResetType resetType, bool hidden) {
        restockData = new ShopRestockData(resetType, ShopCurrencyType.Treva, ShopCurrencyType.Treva, 0, 0, 100, false, hidden, false);
        enableReset = reset;
        GameSession session = Login();
        test.SetServerTable(new ConstantsTable(KarmaTokenMax: 30000));
        session.Currency[CurrencyType.Treva] = 1000;

        Restock(session, 100);

        return session.Currency[CurrencyType.Treva];
    }

    // An NpcShopResetPriceSale event of the shop lowers the restock price the window shows and sends back (300 at
    // 2500 of 10000: 225); the shop packet keeps the full price, as the client lowers it itself. An event of another
    // shop or one that ended takes nothing off. Before, the full price was taken whatever the window showed.
    [TestCase(ShopId, false, ExpectedResult = 1000 - 225)]
    [TestCase(ShopId + 1, false, ExpectedResult = 1000 - 300)]
    [TestCase(ShopId, true, ExpectedResult = 1000 - 300)]
    public long InstantRestock_UnderTheResetSale_TakesTheLoweredPrice(int saleShopId, bool over) {
        restockData = new ShopRestockData(ResetType.Default, ShopCurrencyType.Meso, ShopCurrencyType.Treva, 0, 0, 300, false, false, false);
        enableReset = true;
        beforeStart = () => test.AddEvent(990001, GameEventType.NpcShopResetPriceSale, new NpcShopResetPriceSale(saleShopId, 2500),
            over ? DateTime.Now.AddDays(-9) : DateTime.Now.AddDays(-1), over ? DateTime.Now.AddDays(-2) : DateTime.Now.AddDays(1));
        GameSession session = Login(meso: 1000);

        Restock(session, 225);

        Assert.That(test.TakeSent().Select(Convert.ToHexString).Count(hex => hex.EndsWith(RestockTail(ShopCurrencyType.Meso, 300, 1, ResetType.Default, multiplier: false))), Is.EqualTo(1));
        return session.Currency.Meso;
    }

    // A shop with its reset on and reset type 99 (the KR event shops 226 and 260 to 280) opens and is stored with a
    // time the database holds. Before, its first opening threw on long.MaxValue and the shop did not open.
    [Test]
    public void Load_OfAShopThatNeverRestocks_Opens() {
        restockData = new ShopRestockData(ResetType.Unlimited, ShopCurrencyType.Meso, ShopCurrencyType.Meso, 0, 0, 0, false, true, false);
        enableReset = true;
        GameSession session = Login();

        session.Shop.Load(ShopId);

        using GameStorage.Request db = test.Storage.Context();
        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(packet => TestGameSession.Op(packet)), Does.Contain(SendOp.Shop));
            Assert.That(db.GetCharacterShopData(test.CharacterId, ShopId)!.RestockTime, Is.EqualTo(ShopRules.NoRestockTime));
        });
    }

    private const int StarNpcId = 11003775;

    // A star point shop (frame 2) with or without a StarShop event, the event's npc known to the server or not.
    private GameSession StarShopLogin(ShopFrameType frame, int? eventNpcId) {
        frameType = frame;
        beforeStart = () => {
            if (eventNpcId != null) {
                test.AddEvent(1, GameEventType.StarShop, new StarShop(eventNpcId.Value), DateTime.Now.AddDays(-1), DateTime.Now.AddDays(1));
            }
        };
        GameSession session = Login();
        typeof(GameSession).GetField("<NpcMetadata>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session,
            TriggerNpcSpawnTests.NpcTable(TriggerNpcSpawnTests.Npc(StarNpcId, 0)));
        return session;
    }

    // The client buys and sells in a star point shop only while the StarShop event names an npc it knows
    // (UIShopDialog::HasStarEvent with IsStarPointEventActive) and refuses with s_shop_no_star_point_event; other
    // frames do not ask. Before, the server sold and bought there with no event. (An unknown npc: ShopRulesTests.)
    [TestCase(ShopFrameType.Star, StarNpcId, ExpectedResult = true)]
    [TestCase(ShopFrameType.Star, null, ExpectedResult = false)]
    [TestCase(ShopFrameType.Star, 0, ExpectedResult = false)]
    [TestCase(ShopFrameType.Default, null, ExpectedResult = true)]
    public bool StarShop_BuysAndSellsOnlyWhileTheEventRuns(ShopFrameType frame, int? eventNpcId) {
        GameSession session = StarShopLogin(frame, eventNpcId);

        Buy(session, ProductId, 1);
        bool bought = session.Item.Inventory.Find(ProductItemId).Any();
        List<byte[]> sent = test.TakeSent();
        Sell(session, bagItem.Uid, 5);
        bool sold = session.Item.Inventory.Get(bagItem.Uid) == null;
        sent.AddRange(test.TakeSent());

        Assert.Multiple(() => {
            Assert.That(sold, Is.EqualTo(bought));
            Assert.That(session.Currency.Meso, Is.EqualTo(bought ? 10000 - 30 + 350 : 10000));
            Assert.That(sent.Select(Convert.ToHexString).Count(hex => hex == Hex(ShopPacket.Error(ShopError.s_shop_no_star_point_event, BuyCommand))
                || hex == Hex(ShopPacket.Error(ShopError.s_shop_no_star_point_event))),
                Is.EqualTo(bought ? 0 : 2));
        });
        return bought;
    }

    // A product paid in star points needs the event in any shop (CNpcShopProductCli::CheckStarPointEvent).
    [Test]
    public void StarPointProduct_NeedsTheEvent() {
        GameSession session = StarShopLogin(ShopFrameType.Default, null);

        Buy(session, StarPointProductId, 1);

        Assert.Multiple(() => {
            Assert.That(session.Item.Inventory.Find(ProductItemId), Is.Empty);
            Assert.That(test.TakeSent().Select(Convert.ToHexString),
                Is.EqualTo(new[] { Hex(ShopPacket.Error(ShopError.s_shop_no_star_point_event, BuyCommand)) }));
        });
    }

    // The restock part the shop packet ends with (Shop.WriteTo): the cost's currency, the excess currency, the
    // item id, the price, the multiplier flag, the count, the reset type and two flags.
    private static string RestockTail(ShopCurrencyType type, int price, int count, ResetType reset, bool multiplier = true) {
        var writer = new Maple2.PacketLib.Tools.ByteWriter();
        writer.Write<ShopCurrencyType>(type);
        writer.Write<ShopCurrencyType>(ShopCurrencyType.Treva);
        writer.WriteInt();
        writer.WriteInt(price);
        writer.WriteBool(multiplier);
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

    // The client sells no summoned pet (RequestSellItem, its notice s_err_cannot_sell_petitem_summon) and no score
    // being played (UIShopDialog::CanSellItem); the server refuses both, the item stays and no meso is paid. Before,
    // both were sold while in use.
    [TestCase("none", ExpectedResult = true)]
    [TestCase("pet", ExpectedResult = false)]
    [TestCase("score", ExpectedResult = false)]
    [TestCase("petbag", ExpectedResult = false)]
    public bool Sell_AnItemInUseIsRefused(string inUse) {
        GameSession session = Login();
        Item held = session.Item.Inventory.Get(bagItem.Uid)!;
        const BindingFlags any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        if (inUse == "pet") {
            var fieldPet = (FieldPet) RuntimeHelpers.GetUninitializedObject(typeof(FieldPet));
            typeof(FieldPet).GetField(nameof(FieldPet.Pet), any)!.SetValue(fieldPet, held);
            var manager = (PetManager) RuntimeHelpers.GetUninitializedObject(typeof(PetManager));
            typeof(PetManager).GetField("pet", any)!.SetValue(manager, fieldPet);
            session.Pet = manager;
        } else if (inUse == "score") {
            var instrument = (FieldInstrument) RuntimeHelpers.GetUninitializedObject(typeof(FieldInstrument));
            instrument.Score = held;
            session.Instrument = instrument;
        } else if (inUse == "petbag") {
            // A pet whose bag holds items: error 31, its bag's rows would be left with no pet.
            held.Pet = new ItemPet { HasItems = true };
        }

        Sell(session, bagItem.Uid, 5);

        bool sold = session.Item.Inventory.Get(bagItem.Uid) == null;
        Assert.That(session.Currency.Meso, Is.EqualTo(sold ? 10000 + 350 : 10000));
        if (inUse == "pet") {
            Assert.That(test.TakeSent().Select(packet => Convert.ToHexString(packet)),
                Is.EqualTo(new[] { Hex(ShopPacket.Error(ShopError.s_err_cannot_sell_petitem_summon)) }));
        } else if (inUse == "petbag") {
            Assert.That(test.TakeSent().Select(packet => Convert.ToHexString(packet)),
                Is.EqualTo(new[] { Hex(ItemInventoryPacket.Error(ItemInventoryError.s_err_cannot_destroy_petitem_hasitem)) }));
        }
        session.Pet = null;
        session.Instrument = null;
        return sold;
    }
    // A rolled shop (reset on, a day's restock) keeps the products it rolled until the restock: a login before it
    // shows the stored list with its bought counts. Before, a login rolled the list anew, and a product of the new roll
    // had no stored row, so its buys were never counted and its stock never ran out.
    // A shop without a reset stores its bought count in a row made at the first buy, in the buy's transaction. When
    // that row cannot be stored, nothing is bought and nothing paid. Before, the buy went through with its count
    // kept only in memory, lost at the next login.
    [Test]
    public void Buy_FromAShopWithoutAReset_WhoseRowIsNotStored_BuysNothing() {
        GameSession session = Login();
        test.FailSave = context => context.ChangeTracker.Entries()
            .Any(entry => entry.State == EntityState.Added && entry.Entity.GetType().Name == "CharacterShopItemData");

        Buy(session, StockProductId, 50);
        test.FailSave = null;

        Assert.Multiple(() => {
            // The stored wallet: the in-memory store has no transaction to roll back (see
            // Buy_WhoseStockRowFails_StoresNothing_MySql).
            Assert.That(session.Currency.Meso, Is.EqualTo(10000));
            Assert.That(session.Item.Inventory.Find(ProductItemId), Is.Empty);
            Assert.That(Purchased(session, StockProductId), Is.Zero);
            Assert.That(StoredStock(StockProductId), Is.Null);
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { NoNotice }));
        });

        // With the store working, the row is made and the stock counted.
        Buy(session, StockProductId, 50);
        test.TakeSent();
        Buy(session, StockProductId, 1);
        Assert.Multiple(() => {
            Assert.That(session.Currency.Meso, Is.EqualTo(10000 - 50 * 20));
            Assert.That(StoredStock(StockProductId), Is.EqualTo(50));
            Assert.That(test.TakeSent().Select(Convert.ToHexString),
                Is.EqualTo(new[] { Hex(ShopPacket.Error(ShopError.s_err_lack_shopitem, BuyCommand)) }));
        });
    }

    // The wallet, the payment items' rows and the bought stock are stored in one transaction before the item is given:
    // a buy whose wallet cannot be stored gives nothing, takes nothing and counts no stock. Before, the item was given
    // and the stock counted first, the wallet stored after (a crash in between kept the item with the price unpaid).
    [TestCase(StockProductId)]
    [TestCase(TrevaProductId)]
    [TestCase(GameMeretProductId)]
    [TestCase(ItemCostProductId)]
    public void Buy_WhoseWalletIsNotStored_GivesNothing(int productId) {
        Item payment = null!;
        beforeStart = () => payment = test.Store(TestGameSession.Metadata(PaymentItemId), amount: 5);
        GameSession session = Login();
        test.SetServerTable(new ConstantsTable(HonorTokenMax: 30000, KarmaTokenMax: 30000));
        session.Currency[CurrencyType.Treva] = 100;
        session.Currency.GameMeret = 100;
        session.SavePurchase();
        test.TakeSent();
        test.FailSave = WalletSave;

        Buy(session, productId, 1);
        test.FailSave = null;

        Assert.Multiple(() => {
            Assert.That(session.Currency.Meso, Is.EqualTo(10000));
            Assert.That(StoredMeso(), Is.EqualTo(10000));
            Assert.That(session.Currency[CurrencyType.Treva], Is.EqualTo(100));
            Assert.That(session.Currency.GameMeret, Is.EqualTo(100));
            Assert.That(session.Item.Inventory.Get(payment.Uid)?.Amount, Is.EqualTo(5));
            Assert.That(ItemRows(PaymentItemId), Is.EqualTo(new[] { (test.CharacterId, 5) }));
            Assert.That(session.Item.Inventory.Find(ProductItemId), Is.Empty);
            Assert.That(ItemRows(ProductItemId), Is.Empty);
            Assert.That(Purchased(session, StockProductId), Is.Zero);
            Assert.That(StoredStock(StockProductId), Is.Null);
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Does.Contain(NoNotice));
        });
    }

    // A stored buy: the wallet, the payment rows and the stock row hold it at once.
    [TestCase(StockProductId)]
    [TestCase(ItemCostProductId)]
    public void Buy_StoresThePaymentAndTheStockWithTheBuy(int productId) {
        Item payment = null!;
        beforeStart = () => payment = test.Store(TestGameSession.Metadata(PaymentItemId), amount: 5);
        GameSession session = Login();

        Buy(session, productId, 1);

        bool items = productId == ItemCostProductId;
        Assert.Multiple(() => {
            Assert.That(StoredMeso(), Is.EqualTo(items ? 10000 : 10000 - 20));
            Assert.That(session.Item.Inventory.Get(payment.Uid)?.Amount, Is.EqualTo(items ? 2 : 5));
            Assert.That(ItemRows(PaymentItemId), Is.EqualTo(new[] { (test.CharacterId, items ? 2 : 5) }));
            Assert.That(StoredStock(StockProductId), Is.EqualTo(items ? null : 1));
            Assert.That(session.Item.Inventory.Find(ProductItemId).Sum(item => item.Amount), Is.EqualTo(1));
        });
    }

    // A stored buy whose item can be neither added nor mailed (both writes fail): the price is given back and the
    // bought stock uncounted, both stored, and the answer is the error with no notice (an error log names the
    // character, the item and the price). Before, it was logged and the price kept.
    [TestCase(StockProductId)]
    [TestCase(TrevaProductId)]
    [TestCase(ItemCostProductId)]
    public void Buy_WhoseItemIsNeitherAddedNorMailed_GivesThePriceBack(int productId) {
        Item payment = null!;
        beforeStart = () => payment = test.Store(TestGameSession.Metadata(PaymentItemId), amount: 5);
        GameSession session = Login();
        test.SetServerTable(new ConstantsTable(HonorTokenMax: 30000, KarmaTokenMax: 30000));
        session.Currency[CurrencyType.Treva] = 100;
        session.SavePurchase();
        test.TakeSent();
        test.FailSave = context => context.ChangeTracker.Entries().Any(entry => entry.State == EntityState.Added
            && (entry.Entity is Maple2.Database.Model.Mail || entry.Entity is Maple2.Database.Model.Item { ItemId: ProductItemId }));

        Buy(session, productId, 1);
        test.FailSave = null;

        using GameStorage.Request db = test.Storage.Context();
        Assert.Multiple(() => {
            Assert.That(session.Currency.Meso, Is.EqualTo(10000));
            Assert.That(StoredMeso(), Is.EqualTo(10000));
            Assert.That(session.Currency[CurrencyType.Treva], Is.EqualTo(100));
            Assert.That(session.Item.Inventory.Find(PaymentItemId).Sum(item => item.Amount), Is.EqualTo(5));
            Assert.That(ItemRows(PaymentItemId).Where(row => row.Owner == test.CharacterId).Sum(row => row.Amount), Is.EqualTo(5));
            Assert.That(session.Item.Inventory.Find(ProductItemId), Is.Empty);
            Assert.That(ItemRows(ProductItemId), Is.Empty);
            Assert.That(db.GetAllMail(test.CharacterId), Is.Empty);
            Assert.That(Purchased(session, StockProductId), Is.Zero);
            Assert.That(StoredStock(StockProductId), Is.EqualTo(productId == StockProductId ? 0 : null));
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Does.Contain(NoNotice));
        });
    }

    [Test]
    public void Login_BeforeTheRestock_KeepsTheRolledListAndItsBuys() {
        const int NewProductId = 30;
        restockData = new ShopRestockData(ResetType.Day, ShopCurrencyType.Meso, ShopCurrencyType.Meso, 1, 20, 0, false, false, false);
        enableReset = true;
        GameSession session = Login(meso: 100000);
        Buy(session, StockProductId, 50);

        // A product the table gets after the roll (it would be in a new roll).
        Dictionary<int, ShopItemMetadata> products = test.Session.ServerTableMetadata.ShopItemTable.Entries[ShopId];
        products[NewProductId] = Product(NewProductId, price: 10, stock: 1);
        test.SetServerTable(new ShopItemTable(new Dictionary<int, Dictionary<int, ShopItemMetadata>> { [ShopId] = products }));

        session.Shop = new ShopManager(session);
        session.Shop.Load(ShopId);
        test.TakeSent();
        long meso = session.Currency.Meso;

        Buy(session, NewProductId, 1);
        Buy(session, StockProductId, 1);

        Assert.That(session.Currency.Meso, Is.EqualTo(meso), "the new product is not in the stored list; the bought-out product stays bought out");
        Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Shop).Select(Convert.ToHexString).ToList(), Is.EqualTo(new[] {
            Hex(ShopPacket.Error(ShopError.s_err_invalid_item, BuyCommand)),
            Hex(ShopPacket.Error(ShopError.s_err_lack_shopitem, BuyCommand)),
        }));
    }
    // A pack above SlotMax goes into the bag in pieces (here 3 packs of 10 with SlotMax 10: 3 pieces), and a failed
    // piece write leaves the pieces given before it in the bag. The rest goes by mail; when that mail is refused too,
    // only the rest's share of the price comes back and only its packs are uncounted. Before this fix, the whole
    // price came back and the whole count was taken off, so the pieces given were free.
    [TestCase(false)]
    [TestCase(true)]
    public void Buy_OfAPackWhoseLastPieceIsNotWritten_PaysBackOnlyTheRest(bool mailFails) {
        beforeStart = () => test.Register(TestGameSession.Metadata(ProductItemId, slotMax: 10));
        GameSession session = Login();
        int productRows = 0;
        test.FailSave = context => {
            if (mailFails && context.ChangeTracker.Entries().Any(entry => entry.State == EntityState.Added && entry.Entity is Maple2.Database.Model.Mail)) {
                return true;
            }
            if (context.ChangeTracker.Entries().Any(entry => entry.State == EntityState.Added && entry.Entity is Maple2.Database.Model.Item { ItemId: ProductItemId })) {
                return ++productRows == 3;
            }
            return false;
        };

        Buy(session, PackProductId, 3);
        test.FailSave = null;

        using GameStorage.Request db = test.Storage.Context();
        List<Mail> mails = db.GetAllMail(test.CharacterId).ToList();
        Assert.Multiple(() => {
            Assert.That(session.Item.Inventory.Find(ProductItemId).Sum(item => item.Amount), Is.EqualTo(20));
            if (mailFails) {
                Assert.That(mails, Is.Empty);
                Assert.That(session.Currency.Meso, Is.EqualTo(10000 - 120 + 40));
                Assert.That(StoredMeso(), Is.EqualTo(10000 - 120 + 40));
                Assert.That(Purchased(session, PackProductId), Is.EqualTo(2));
                Assert.That(StoredStock(PackProductId), Is.EqualTo(2));
                Assert.That(test.TakeSent().Select(Convert.ToHexString), Does.Contain(NoNotice));
            } else {
                // One mail with the rest: 30 stored in all, 20 of them in the bag.
                Assert.That(mails, Has.Count.EqualTo(1));
                Assert.That(ItemRows(ProductItemId).Sum(row => row.Amount), Is.EqualTo(30));
                Assert.That(session.Currency.Meso, Is.EqualTo(10000 - 120));
                Assert.That(Purchased(session, PackProductId), Is.EqualTo(3));
                Assert.That(StoredStock(PackProductId), Is.EqualTo(3));
            }
        });
    }

    // Merets given back are not counted as spent (use_merat): a meret buy whose item is neither added nor mailed
    // counts nothing; a delivered one counts its price.
    [TestCase(true, 0)]
    [TestCase(false, 40)]
    public void Buy_WithMeretsGivenBack_CountsNoMeretSpent(bool fails, long counted) {
        GameSession session = Login(trophies: TestGameSession.Trophy(23200200, ConditionType.use_merat, [100000]));
        session.Currency.GameMeret = 100;
        session.SavePurchase();
        test.TakeSent();
        if (fails) {
            test.FailSave = context => context.ChangeTracker.Entries().Any(entry => entry.State == EntityState.Added
                && (entry.Entity is Maple2.Database.Model.Mail || entry.Entity is Maple2.Database.Model.Item { ItemId: ProductItemId }));
        }

        Buy(session, GameMeretProductId, 1);
        test.FailSave = null;

        Assert.Multiple(() => {
            Assert.That(session.Currency.GameMeret, Is.EqualTo(fails ? 100 : 60));
            Assert.That(test.TrophyCounter(23200200) ?? 0, Is.EqualTo(counted));
        });
    }

    // On MySQL the buy's wallet and stock row are one transaction (the in-memory store cannot show it): a stock row
    // whose insert fails (here a row already stored under the product's key) leaves the stored wallet as it was, and
    // the buy gives nothing. Runs only when MS2_TEST_GAME_DB holds the connection string of a scratch MySQL game
    // database.
    [Test]
    public void Buy_WhoseStockRowFails_StoresNothing_MySql() {
        string? connection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");
        if (connection == null) {
            Assert.Ignore("the purchase save's rollback needs a real database: set MS2_TEST_GAME_DB");
        }
        ServerVersion version = ServerVersion.AutoDetect(connection);
        storeOptions = new DbContextOptionsBuilder().UseMySql(connection, version).Options;
        IDisposable? held = null;
        beforeStart = () => held = GameStorage.HoldOwners(test.AccountId, test.CharacterId);
        try {
            GameSession session = Login();
            Assume.That(session.SavePurchase(), Is.True, "the wallet stored before the buy");
            using (var context = new Maple2.Database.Context.Ms2Context(storeOptions)) {
                Maple2.Database.Model.Shop.CharacterShopItemData row = new CharacterShopItemData {
                    ShopId = ShopId,
                    ShopItemId = StockProductId,
                    Item = new Item(TestGameSession.Metadata(ProductItemId)),
                    StockPurchased = 7,
                };
                row.OwnerId = test.CharacterId;
                context.CharacterShopItemData.Add(row);
                context.SaveChanges();
            }

            Buy(session, StockProductId, 2);

            Assert.Multiple(() => {
                Assert.That(session.Currency.Meso, Is.EqualTo(10000));
                Assert.That(StoredMeso(), Is.EqualTo(10000));
                Assert.That(StoredStock(StockProductId), Is.EqualTo(7));
                Assert.That(session.Item.Inventory.Find(ProductItemId), Is.Empty);
                Assert.That(ItemRows(ProductItemId).Where(row => row.Owner == test.CharacterId), Is.Empty);
                Assert.That(Purchased(session, StockProductId), Is.Zero);
                Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { NoNotice }));
            });
        } finally {
            held?.Dispose();
        }
    }

    // The same sale sent twice: the first sells the stack, so the second finds no item and pays nothing. A part
    // sold twice sells only while enough is left; the second part of 3 out of the 2 left is refused.
    [TestCase(5, 5, 0, 5)]
    [TestCase(3, 3, 2, 3)]
    [TestCase(2, 2, 1, 4)]
    public void Sell_TheSameStackTwice(int first, int second, int left, int sold) {
        GameSession session = Login();

        Sell(session, bagItem.Uid, first);
        Sell(session, bagItem.Uid, second);

        Assert.Multiple(() => {
            Assert.That(session.Currency.Meso, Is.EqualTo(10000 + 70 * sold));
            Assert.That(StoredMeso(), Is.EqualTo(10000 + 70 * sold));
            Assert.That(session.Item.Inventory.Find(20000003).Sum(item => item.Amount), Is.EqualTo(left));
            Assert.That(ItemRows(20000003).Sum(row => row.Amount), Is.EqualTo(5), "no row is made or lost");
        });
    }

    // The same buy back sent twice: the entry leaves the list with the first, so the second is refused as an invalid
    // item and takes no mesos.
    [Test]
    public void BuyBack_Twice_BuysOnce() {
        GameSession session = Login();
        Sell(session, bagItem.Uid, 5);
        int entryId = BitConverter.ToInt32(test.TakeSent()[1], 5);

        for (int i = 0; i < 2; i++) {
            new ShopHandler { GameStorage = test.Storage }.Handle(session, TestGameSession.Packet(writer => {
                writer.WriteByte(BuyBackCommand);
                writer.WriteInt(entryId);
            }));
        }

        List<byte[]> sent = test.TakeSent();
        Assert.Multiple(() => {
            Assert.That(Convert.ToHexString(sent[^1]), Is.EqualTo(Hex(ShopPacket.Error(ShopError.s_err_invalid_item, BuyBackCommand))));
            Assert.That(session.Currency.Meso, Is.EqualTo(10000));
            Assert.That(StoredMeso(), Is.EqualTo(10000));
            Assert.That(session.Item.Inventory.Find(20000003).Sum(item => item.Amount), Is.EqualTo(5));
            Assert.That(ItemRows(20000003), Is.EqualTo(new[] { (test.CharacterId, 5) }));
        });
    }
}
