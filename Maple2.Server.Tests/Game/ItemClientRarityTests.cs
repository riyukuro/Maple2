using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// The client takes its item data's nonzero rank as the rarity of every copy it is sent, whatever rarity the packet
// names (CItem::FUN_140523c40, commonfiles/item.cpp). The server holds its copies at that rank (ClientRank, from the
// played client at ingest), so a test that sends or compares its rarity (an enchant's fodder by id and rank) agrees
// with the copy the client shows. KR 15300307: client and server rank 6, its copies stored at rarity 4.
public class ItemClientRarityTests {
    private TestGameSession test = null!;

    private static ItemMetadata Ranked(int id, int type, int? clientRank, int tradeMaxRarity = 5) {
        ItemMetadata data = TestGameSession.Metadata(id, type: type, slotMax: 1);
        return data with {
            Property = data.Property with { Rank = 6, ClientRank = clientRank },
            Limit = data.Limit with { TransferType = TransferType.Tradable, TradeMaxRarity = tradeMaxRarity },
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

    // Made at 4: held at the client's rank 6, with the transfer flag of rarity 6 (trade rarity 5: no free trade).
    [Test]
    public void CreateItem_TakesTheClientRank() {
        GameSession session = Start(Ranked(15300307, 1, 6));

        Item? made = session.Field!.ItemDrop.CreateItem(15300307, rarity: 4);

        Assert.That(made, Is.Not.Null);
        Assert.That(made!.Rarity, Is.EqualTo(6));
        Assert.That(made.Transfer!.Flag, Is.EqualTo(Item.TransferFlagOf(made.Metadata, 6)));
        Assert.That(made.Transfer.Flag, Is.Not.EqualTo(Item.TransferFlagOf(made.Metadata, 4)));
    }

    // No client rank (no played client, or one with no rank for the id): the client shows the packet's rarity.
    [Test]
    public void CreateItem_WithoutAClientRank_KeepsTheRarity() {
        GameSession session = Start(Ranked(15300004, 1, null));

        Assert.That(session.Field!.ItemDrop.CreateItem(15300004, rarity: 4)!.Rarity, Is.EqualTo(4));
    }

    // A pet keeps its rarity: the client sets it from the pet update (CPetManager::OnPetItemDataUpdate) and an
    // evolution raises it.
    [Test]
    public void APet_KeepsItsRarity() {
        ItemMetadata pet = Ranked(60000001, 11, 1);

        Assert.That(new Item(pet, 3).Rarity, Is.EqualTo(3));
    }

    // A copy cloned or mutated takes the rank too.
    [Test]
    public void CloneAndMutate_TakeTheClientRank() {
        ItemMetadata plain = Ranked(15300004, 1, null);
        ItemMetadata ranked = Ranked(15300307, 1, 6);
        var item = new Item(plain, 4);

        Assert.That(item.Mutate(ranked).Rarity, Is.EqualTo(6));
        Assert.That(new Item(ranked, 4).Clone().Rarity, Is.EqualTo(6));
    }

    // A row stored at 4 with its rule's flag at 4 loads at 6 with the flag at 6, as a copy made now has it; the next
    // save stores 6 (a load-time override, no migration).
    [Test]
    public void StoredAtTheOldRarity_LoadsAtTheClientRank() {
        test = new TestGameSession();
        ItemMetadata cannon = Ranked(15300307, 1, 6);
        Item stored = test.Store(cannon);
        stored.Rarity = 4;
        stored.Transfer!.Flag = Item.TransferFlagOf(cannon, 4);
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.SaveItems(test.CharacterId, stored), Is.True);
        }
        GameSession session = Started(cannon);

        Item held = session.Item.Inventory.Filter(item => item.Uid == stored.Uid).Single();
        Assert.That(held.Rarity, Is.EqualTo(6));
        Assert.That(held.Transfer!.Flag, Is.EqualTo(Item.TransferFlagOf(cannon, 6)));

        using (GameStorage.Request write = test.Storage.Context()) {
            session.Item.Save(write);
        }
        using GameStorage.Request read = test.Storage.Context();
        Assert.That(read.GetItem(stored.Uid)!.Rarity, Is.EqualTo(6));
    }

    // A row that names a ranked id at another rarity (KR: bonus game 30000869 consumes it at 1, client rank 4; the
    // exchange recipe scrolls 36000386-36000390 at 3, client rank 4) still takes the copies, which are all held at the
    // client rank: the rarity a row asks for is the one a copy made from it has.
    [Test]
    public void ARowAtThePackRarity_TakesTheCopiesAtTheClientRank() {
        test = new TestGameSession();
        ItemMetadata ticket = Ranked(30000869, 3, 4) with { Property = Ranked(30000869, 3, 4).Property with { Rank = 1, SlotMax = 100 } };
        Item stored = test.Store(ticket, amount: 3);
        GameSession session = Started(ticket);
        Assert.That(session.Item.Inventory.Get(stored.Uid)!.Rarity, Is.EqualTo(4));

        Assert.That(session.Item.Inventory.Find(30000869, 1).Select(item => item.Uid), Is.EqualTo(new[] { stored.Uid }));
        Assert.That(session.Item.Inventory.ConsumeItemComponents([new ItemComponent(30000869, 1, 2, ItemTag.None)]), Is.True);
        Assert.That(session.Item.Inventory.Get(stored.Uid)!.Amount, Is.EqualTo(1));
    }
}
