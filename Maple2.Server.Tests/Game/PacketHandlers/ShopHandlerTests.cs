using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Game.Shop;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

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
    private const int ShopItemId = 20000001;
    private const int ProductItemId = 20000002;

    private TestGameSession test = null!;
    private Item bagItem = null!;
    private Item shopItem = null!;

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    private static ShopItemMetadata Product(int id, int price, int stock, short sellUnit = 1, ShopItemMetadata.QuestAlliance? fame = null) {
        var requirements = new ShopItemMetadata.Requirement(0, new ShopItemMetadata.Achievement(0, 0), new ShopItemMetadata.Championship(0, 0),
            new ShopItemMetadata.GuildNpc(default, 0), fame ?? new ShopItemMetadata.QuestAlliance(default, 0));
        return new ShopItemMetadata(id, ShopId, ProductItemId, 1, new ShopCost { Type = ShopCurrencyType.Meso, Amount = price }, stock, "", requirements,
            null, sellUnit, default, "", false, false, 10000, false);
    }

    private GameSession Login(long meso = 10000) {
        test = new TestGameSession();
        test.SetServerTable(new ShopTable(new Dictionary<int, ShopMetadata> {
            [ShopId] = new(ShopId, 0, "shop", default, false, false, false, false, false, false, false, 0, false,
                new ShopRestockData(ResetType.Default, ShopCurrencyType.Meso, ShopCurrencyType.Meso, 0, 0, 0, false, true, false)),
        }));
        test.SetServerTable(new ShopItemTable(new Dictionary<int, Dictionary<int, ShopItemMetadata>> {
            [ShopId] = new() {
                [ProductId] = Product(ProductId, price: 30, stock: 0),
                [StockProductId] = Product(StockProductId, price: 20, stock: 50),
                [PackProductId] = Product(PackProductId, price: 40, stock: 50, sellUnit: 10),
                [FameProductId] = Product(FameProductId, price: 10, stock: 0, fame: new ShopItemMetadata.QuestAlliance(AllianceType.MapleAlliance, 3)),
                [AnyFameProductId] = Product(AnyFameProductId, price: 10, stock: 0, fame: new ShopItemMetadata.QuestAlliance(AllianceType.AnyFaction, 4)),
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

    private void SystemShop(GameSession session, bool open) {
        var handler = new SystemShopHandler { ServerTableMetadata = test.ServerTableMetadata };
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(10);
            writer.WriteBool(open);
            if (open) {
                writer.WriteInt(ShopItemId);
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

    [Test]
    public void SystemShop_WithoutTheItemOpensNothing() {
        GameSession session = Login();
        session.Shop.ClearActiveShop();
        Assert.That(session.Item.Inventory.Remove(shopItem.Uid, out _), Is.True);
        test.TakeSent();

        SystemShop(session, open: true);

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
}
