using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Game;

// The transfer block a new item gets, and what binding does to it, as KMS2 sends them.
public class ItemTransferRulesTests {
    private static Item ItemOf(TransferType type, int tradableCount, int tradeMaxRarity, int rarity) {
        var property = new ItemMetadataProperty(false, 0, 100, 18, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, tradableCount, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 0, type, tradeMaxRarity, true, true, true, true, true, false, false, 0, [], []);
        var metadata = new ItemMetadata(11000001, "11000001", [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [], null, null, null, null);
        return new Item(metadata, rarity);
    }

    // A black-market-only item is always 4 (trade), also at or past its trade rarity.
    [TestCase(1, 4)]
    [TestCase(4, 4)]
    [TestCase(6, 4)]
    public void BlackMarketOnly_IsTradeable(int rarity, int tradeMaxRarity) {
        Item item = ItemOf(TransferType.BlackMarketOnly, 0, tradeMaxRarity, rarity);
        Assert.That(item.Transfer!.Flag, Is.EqualTo(TransferFlag.Trade));
    }

    // Hand to hand (trade, field drop) a black-market-only item goes only below its trade rarity or with trades
    // left; the black market still takes it by its trade bit.
    [TestCase(1, 4, 0, true)]
    [TestCase(3, 4, 0, true)]
    [TestCase(4, 4, 0, false)]
    [TestCase(6, 4, 0, false)]
    [TestCase(4, 4, 1, true)]
    public void BlackMarketOnly_HandTrade(int rarity, int tradeMaxRarity, int tradableCount, bool expected) {
        Item item = ItemOf(TransferType.BlackMarketOnly, tradableCount, tradeMaxRarity, rarity);
        Assert.That(TradeRules.IsHandTradeable(item), Is.EqualTo(expected));
        Assert.That(BlackMarketRules.CanList(false, true, item.Transfer!.Flag, item.Transfer.RemainTrades, false), Is.True);
    }

    // Every other transfer type is left to the flag.
    [TestCase(TransferType.Tradable)]
    [TestCase(TransferType.Untradeable)]
    [TestCase(TransferType.BindOnLoot)]
    public void OtherTypes_HandTradeByTheFlag(TransferType type) {
        Assert.That(TradeRules.IsHandTradeable(type, 6, 4, 0), Is.True);
    }

    // Binding keeps the trades left (KMS2 sends bound items with 1); they are of no use while bound.
    [Test]
    public void Bind_KeepsTheTradeCount() {
        Item item = ItemOf(TransferType.BindOnEquip, 1, 4, 4);
        Assert.That(item.Transfer!.Flag, Is.EqualTo(TransferFlag.Bind | TransferFlag.LimitTrade));
        Assert.That(item.Transfer.RemainTrades, Is.EqualTo(1));

        Assert.That(item.Transfer.Bind(new Character { Id = 5, Name = "a", Mastery = new Mastery() }), Is.True);

        Assert.That(item.Transfer.RemainTrades, Is.EqualTo(1));
        Assert.That(item.Transfer.Binding!.CharacterId, Is.EqualTo(5));
        Assert.That(TradeRules.IsTradeable(item.Transfer.Flag, item.Transfer.RemainTrades, true), Is.False);
    }

    // The rule and trade count a new unbound item is sent with, one case per transfer type, as KMS2 sent the same
    // class of items in builds 2497-2549 (row counts from the session item records). The tooltip
    // (ASTooltip::SetItemSellPrice, FUN_1409e6120) picks its trade, trade-count and bind lines from these two
    // values and the binding, so equal values give equal lines.
    [TestCase(TransferType.Tradable, 0, 1, 4, 0)]        // 9703 rows
    [TestCase(TransferType.Tradable, 0, 4, 0, 0)]        // 11
    [TestCase(TransferType.Tradable, 1, 4, 16, 1)]       // 13
    [TestCase(TransferType.Untradeable, 0, 1, 0, 0)]     // 9834
    [TestCase(TransferType.Untradeable, 1, 1, 16, 1)]    // 328
    [TestCase(TransferType.BindOnEquip, 1, 4, 24, 1)]    // 100
    [TestCase(TransferType.BindPet, 1, 1, 24, 1)]        // 4306
    [TestCase(TransferType.BlackMarketOnly, 0, 4, 4, 0)] // 1306
    public void NewItem_WritesTheKms2Rule(TransferType type, int tradableCount, int rarity, int rule, int count) {
        Item item = ItemOf(type, tradableCount, 4, rarity);

        var writer = new ByteWriter();
        writer.WriteClass<ItemTransfer>(item.Transfer!);
        var reader = new ByteReader(writer.ToArray(), 0);

        Assert.That(reader.ReadInt(), Is.EqualTo(rule));
        reader.ReadBool();
        Assert.That(reader.ReadInt(), Is.EqualTo(count));
    }
}
