using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
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
    private static ItemMetadata Ranked(int id, int type, int rank, int constant, int slotMax = 100) {
        ItemMetadata data = TestGameSession.Metadata(id, type: type, slotMax: slotMax);
        return data with {
            Property = data.Property with { Rank = rank },
            Option = new ItemMetadataOption(0, default, 0, default, constant, default, 0, 0),
        };
    }

    private GameSession Start(params ItemMetadata[] items) {
        test = new TestGameSession();
        return Started(items);
    }

    // Starts the session made already (rows stored first).
    private GameSession Started(params ItemMetadata[] items) {
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

    // A stackable row stored at rarity 1 (made before the rule) loads at its default rarity, which its next save
    // stores; gear keeps the stored 1 (Item.LoadedRarity).
    [TestCase(13, 4, 40220131, 100, 4)] // a gem
    [TestCase(19, 3, 37030001, 999, 3)] // a type 19 catalyst
    [TestCase(2, 4, 40220131, 1, 1)] // not stacked
    [TestCase(2, 4, 3, 100, 3)] // a constant of 1 to 6 gives it (was 1)
    [TestCase(2, 4, 1, 100, 1)] // a constant of 1
    public void StoredAtRarityOne_StackableLoadsAtItsRank(int type, int rank, int constant, int slotMax, int loaded) {
        test = new TestGameSession();
        Item stored = test.Store(Ranked(40220131, type, rank, constant, slotMax));
        GameSession session = test.Start();

        Item? held = session.Item.Inventory.Filter(item => item.Uid == stored.Uid).SingleOrDefault();
        Assert.That(held, Is.Not.Null);
        Assert.That(held!.Rarity, Is.EqualTo(loaded));
    }

    // A row as the server wrote it before DefaultRarity: rarity 1 and its rule's transfer flag at 1, or the flag given.
    private Item StoreOld(ItemMetadata metadata, int amount, TransferFlag? flag = null) {
        Item stored = test.Store(metadata, amount);
        stored.Rarity = 1;
        stored.Transfer!.Flag = flag ?? Item.TransferFlagOf(metadata, 1);
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.SaveItems(test.CharacterId, stored), Is.True);
        return stored;
    }

    // KMS2 sends a bind-on-loot gem at rank 4 as 8 (bind, no trade bit). A row stored at rarity 1 had its rule's flag at
    // 1 (bind and trade); it loads with the flag at its rank, as a copy made now has it. A flag changed since (here the
    // trade bit taken) is kept.
    [TestCase(true, TransferFlag.Bind)]
    [TestCase(false, TransferFlag.Bind | TransferFlag.Split)]
    public void StoredAtRarityOne_TakesItsRanksTransferFlag(bool untouched, TransferFlag loaded) {
        test = new TestGameSession();
        ItemMetadata gem = Ranked(40220131, 13, 4, 40220131);
        gem = gem with { Limit = gem.Limit with { TransferType = TransferType.BindOnLoot, TradeMaxRarity = 4 } };
        Assert.That(Item.TransferFlagOf(gem, 1), Is.EqualTo(TransferFlag.Bind | TransferFlag.Trade | TransferFlag.Split));
        Item stored = StoreOld(gem, 2, untouched ? null : TransferFlag.Bind | TransferFlag.Split);
        GameSession session = test.Start();

        Item held = session.Item.Inventory.Filter(item => item.Uid == stored.Uid).Single();
        Assert.That(held.Rarity, Is.EqualTo(4));
        Assert.That(held.Transfer!.Flag, Is.EqualTo(loaded));
    }

    // A tradable catalyst of rank 3 and trade rarity 3 with no trades: made at 1 it was freely tradable, made at 3 it
    // is not; the old row now stacks with a new copy.
    [Test]
    public void StoredAtRarityOne_StacksWithANewCopy() {
        test = new TestGameSession();
        ItemMetadata catalyst = Ranked(37030001, 19, 3, 37030001, 999);
        catalyst = catalyst with { Limit = catalyst.Limit with { TransferType = TransferType.Tradable, TradeMaxRarity = 3 } };
        Item stored = StoreOld(catalyst, 2);
        GameSession session = Started(catalyst);

        Item? made = session.Field!.ItemDrop.CreateItem(37030001, amount: 3);
        Assert.That(made, Is.Not.Null);
        Assert.That(made!.Transfer!.Flag, Is.EqualTo(TransferFlag.None));
        Assert.That(session.Item.Inventory.Add(made), Is.True);

        Item[] held = session.Item.Inventory.Filter(item => item.Id == 37030001).ToArray();
        Assert.That(held, Has.Length.EqualTo(1));
        Assert.That(held[0].Uid, Is.EqualTo(stored.Uid));
        Assert.That(held[0].Amount, Is.EqualTo(5));
    }

    // Chaos Onyx 40100024 as KR has it: rank 4, option constant 4, black market only, trade rarity 4. A row the old GM
    // item command made at rarity 1 (x245, flag 4) loads at 4 with its flag, a new copy stacks onto it, and the next
    // save stores rarity 4: one row (it was two, the old one kept at 1).
    [Test]
    public void StoredAtRarityOne_ConstantRarityStackableJoinsANewCopy() {
        test = new TestGameSession();
        ItemMetadata onyx = Ranked(40100024, 15, 4, 4, 9999);
        onyx = onyx with { Limit = onyx.Limit with { TransferType = TransferType.BlackMarketOnly, TradeMaxRarity = 4 } };
        Item stored = StoreOld(onyx, 245);
        Assert.That(stored.Transfer!.Flag, Is.EqualTo(TransferFlag.Trade));
        GameSession session = Started(onyx);

        Item held = session.Item.Inventory.Filter(item => item.Uid == stored.Uid).Single();
        Assert.That(held.Rarity, Is.EqualTo(4));
        Assert.That(held.Transfer!.Flag, Is.EqualTo(TransferFlag.Trade));
        Item? made = session.Field!.ItemDrop.CreateItem(40100024, amount: 2);
        Assert.That(made, Is.Not.Null);
        Assert.That(made!.Rarity, Is.EqualTo(4));
        Assert.That(session.Item.Inventory.Add(made), Is.True);
        Assert.That(session.Item.Inventory.Filter(item => item.Id == 40100024).Select(item => (item.Uid, item.Amount)).ToArray(),
            Is.EqualTo(new[] { (stored.Uid, 247) }));

        using (GameStorage.Request write = test.Storage.Context()) {
            session.Item.Save(write);
        }
        using GameStorage.Request db = test.Storage.Context();
        Item saved = db.GetItem(stored.Uid)!;
        Assert.That(saved.Rarity, Is.EqualTo(4));
        Assert.That(saved.Amount, Is.EqualTo(247));
    }
}
