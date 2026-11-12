using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game;

namespace Maple2.Server.Tests.Game;

// The Meret Market custom shop: each additionalQuantity child (a bigger pack, ParentId set) goes under its
// parent whatever order the table's rows come in, and in that order; the ingest adds the children before the parent.
public class PremiumMarketTests {
    private const int ItemId = 20300036;

    private static MeretMarketItemMetadata Row(int id, int parentId = 0, int itemId = 0, int quantity = 1) =>
        new(id, parentId, 1, string.Empty, (MeretMarketBannerTag) 0, itemId, 1, quantity, 0, 0, (MeretMarketItemSaleTag) 0,
            (MeretMarketCurrencyType) 0, 100, 100, 0, 0, (JobFilterFlag) 0, false, 0, 0, 0, 0, false, true, false, string.Empty, 0, 0);

    private static Dictionary<int, PremiumMarketItem> Market(params MeretMarketItemMetadata[] rows) =>
        GameServer.PremiumMarket(new MeretMarketTable(rows.ToDictionary(row => row.Id)),
                itemId => itemId == ItemId ? TestGameSession.Metadata(itemId) : null)
            .ToDictionary(entry => entry.Key, entry => entry.Value);

    private static int[] Children(PremiumMarketItem item) => item.AdditionalQuantities.Select(child => child.Id).ToArray();

    // The ingest's order (children first), the stored key order (parent first) and a child id that sorts first.
    [Test]
    public void ChildrenBeforeTheirParent() {
        Dictionary<int, PremiumMarketItem> market = Market(Row(143051, 143050, quantity: 10), Row(143052, 143050, quantity: 50),
            Row(143050, itemId: ItemId));

        Assert.That(market.Keys, Is.EquivalentTo(new[] { 143050 }));
        Assert.That(Children(market[143050]), Is.EqualTo(new[] { 143051, 143052 }));
    }

    [Test]
    public void ParentBeforeItsChildren() {
        Dictionary<int, PremiumMarketItem> market = Market(Row(143050, itemId: ItemId), Row(143051, 143050), Row(143052, 143050));

        Assert.That(Children(market[143050]), Is.EqualTo(new[] { 143051, 143052 }));
    }

    [Test]
    public void ChildIdBelowTheParent() {
        Dictionary<int, PremiumMarketItem> market = Market(Row(9001, 143140), Row(143140, itemId: ItemId));

        Assert.That(Children(market[143140]), Is.EqualTo(new[] { 9001 }));
    }

    // The child takes the parent's item; a child of a missing parent or of a parent whose item is unknown is dropped.
    [Test]
    public void ChildUsesTheParentItem() {
        Dictionary<int, PremiumMarketItem> market = Market(Row(143051, 143050), Row(143050, itemId: ItemId));

        Assert.That(market[143050].AdditionalQuantities[0].Id, Is.EqualTo(143051));
        Assert.That(market[143050].AdditionalQuantities[0].ItemMetadata.Id, Is.EqualTo(ItemId));
    }

    [Test]
    public void OrphanChildDropped() {
        Dictionary<int, PremiumMarketItem> market = Market(Row(143051, 143050), Row(143060, itemId: ItemId + 1), Row(143061, 143060));

        Assert.That(market, Is.Empty);
    }
}
