using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// The rarity an item is made at when its source names none (a drop, a reward or shop row of grade 0, mail, GM):
// the item's rank (GetItemRank, commonfiles/itemdata.cpp:2084), as the client's shop and manufacture lists show
// it (npcshopproductbase.cpp:475, manufacturemanager.cpp:310). An option constant of 1 to 6 still gives it.
public class ItemDefaultRarityTests {
    private TestGameSession test = null!;

    // id 40220131: a KR gem, rank 4, its option constant its own id (no rarity).
    private static ItemMetadata Ranked(int id, int type, int rank, int constant) {
        ItemMetadata data = TestGameSession.Metadata(id, type: type);
        return data with {
            Property = data.Property with { Rank = rank },
            Option = new ItemMetadataOption(0, default, 0, default, constant, default, 0, 0),
        };
    }

    private GameSession Start(params ItemMetadata[] items) {
        test = new TestGameSession();
        foreach (ItemMetadata item in items) {
            test.Register(item);
        }
        // CreateItem rolls the item's options; the items have none here.
        test.SetTable(new ItemOptionPickTable(new Dictionary<int, IReadOnlyDictionary<int, ItemOptionPickTable.Option>>()));
        test.SetTable(new ItemOptionConstantTable(new Dictionary<int, IReadOnlyDictionary<int, ItemOptionConstant>>()));
        test.SetTable(new ItemOptionStaticTable(new Dictionary<int, IReadOnlyDictionary<int, ItemOption>>()));
        test.SetTable(new ItemOptionRandomTable(new Dictionary<int, IReadOnlyDictionary<int, ItemOption>>()));
        return test.Start();
    }

    [TestCase(40220131, 13, 4, 40220131, 4)] // a gem: its rank (was 1)
    [TestCase(11200005, 1, 3, 0, 3)] // ranked gear with no constant: its rank (was 1)
    [TestCase(20000001, 2, 1, 6, 6)] // a constant of 6 gives 6 (was 1)
    [TestCase(20000002, 2, 1, 3, 3)] // a constant of 1 to 5 gives it
    [TestCase(20000003, 2, 0, 0, 1)] // neither: 1
    public void CreateItem_NoRarity_MadeAtItsRank(int id, int type, int rank, int constant, int rarity) {
        GameSession session = Start(Ranked(id, type, rank, constant));

        Item? made = session.Field!.ItemDrop.CreateItem(id);

        Assert.That(made, Is.Not.Null);
        Assert.That(made!.Rarity, Is.EqualTo(rarity));
    }

    [Test]
    public void CreateItem_NamedRarity_IsKept() {
        GameSession session = Start(Ranked(40220131, 13, 4, 40220131));

        Assert.That(session.Field!.ItemDrop.CreateItem(40220131, rarity: 2)!.Rarity, Is.EqualTo(2));
    }

    // A gem row stored at rarity 1 (made before the rule) loads at its rank, which its next save stores; a row of
    // another tab keeps its rarity (Item.LoadedRarity).
    [TestCase(13, 4)]
    [TestCase(2, 1)]
    public void StoredAtRarityOne_GemLoadsAtItsRank(int type, int loaded) {
        test = new TestGameSession();
        Item stored = test.Store(Ranked(40220131, type, 4, 40220131));
        GameSession session = test.Start();

        Item? held = session.Item.Inventory.Filter(item => item.Uid == stored.Uid).SingleOrDefault();
        Assert.That(held, Is.Not.Null);
        Assert.That(held!.Rarity, Is.EqualTo(loaded));
    }
}
