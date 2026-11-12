using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;

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
}
