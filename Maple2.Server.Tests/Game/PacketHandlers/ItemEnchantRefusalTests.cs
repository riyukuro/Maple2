using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The enchant request itself: what a refusal leaves, and a request that follows a result without a new stage.
public class ItemEnchantRefusalTests {
    private TestGameSession test = null!;
    private Item gear = null!;

    private static ItemMetadata Material(int id, ItemTag tag) {
        ItemMetadata metadata = TestGameSession.Metadata(id, type: 2, slotMax: 30000);
        return metadata with { Property = metadata.Property with { Tag = tag } };
    }

    private GameSession Login(int onyx, int chaosOnyx, int fragments, long meso) {
        test = new TestGameSession();
        ItemMetadata gearMetadata = TestGameSession.Metadata(11400001, EquipSlot.CL, type: 1);
        gear = test.Store(gearMetadata with { Limit = gearMetadata.Limit with { Level = 50 } });
        if (onyx > 0) {
            test.Store(Material(40100001, ItemTag.Onix), amount: onyx);
        }
        if (chaosOnyx > 0) {
            test.Store(Material(40100002, ItemTag.ChaosOnix), amount: chaosOnyx);
        }
        if (fragments > 0) {
            test.Store(Material(40100003, ItemTag.CrystalPiece), amount: fragments);
        }
        test.SetServerTable(new EnchantOptionTable(new Dictionary<int, EnchantOptionMetadata>()));
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { enchantSuccessBroadcastingLevel = 15 });
        GameSession session = test.Start();
        session.Item.Inventory.Get(gear.Uid)!.Rarity = 4;
        session.Currency.Meso = meso;
        test.TakeSent();
        return session;
    }

    private static int Held(GameSession session, ItemTag tag) => session.Item.Inventory.Find(tag).Sum(item => item.Amount);

    private Dictionary<ItemTag, int> Cost(GameSession session) {
        return Maple2.Server.Core.Formulas.Enchant.GetEnchantCost(session.Item.Inventory.Get(gear.Uid)!)
            .ToDictionary(cost => cost.Tag, cost => cost.Amount);
    }

    // A catalyst is lacking: nothing is taken and the grade stays.
    [TestCase(0, 30000, 30000, 0)] // no onyx
    [TestCase(30000, 30000, 0, 0)] // no fragments and no meso for them
    public void Ophelia_LackingACatalystTakesNothing(int onyx, int chaosOnyx, int fragments, long meso) {
        GameSession session = Login(onyx, chaosOnyx, fragments, meso);
        Assert.That(session.ItemEnchant.StageItem(EnchantType.Ophelia, gear.Uid), Is.True);
        Assert.That(Cost(session)[ItemTag.Onix], Is.GreaterThan(0));
        Assert.That(Cost(session)[ItemTag.CrystalPiece], Is.GreaterThan(0));
        test.TakeSent();

        Assert.That(session.ItemEnchant.Enchant(gear.Uid), Is.False);

        Assert.Multiple(() => {
            Assert.That(test.TakeSentOps(), Has.No.Member(SendOp.ItemInventory).And.No.Member(SendOp.Meso));
            Assert.That(Held(session, ItemTag.Onix), Is.EqualTo(onyx));
            Assert.That(Held(session, ItemTag.ChaosOnix), Is.EqualTo(chaosOnyx));
            Assert.That(Held(session, ItemTag.CrystalPiece), Is.EqualTo(fragments));
            Assert.That(session.Currency.Meso, Is.EqualTo(meso));
            Assert.That(session.Item.Inventory.Get(gear.Uid)!.Enchant!.Enchants, Is.EqualTo(0));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // An item that is not the staged one, and a request with nothing staged: nothing is taken.
    [Test]
    public void Ophelia_OfAnItemThatIsNotStagedTakesNothing() {
        GameSession session = Login(30000, 30000, 30000, 0);

        Assert.That(session.ItemEnchant.Enchant(gear.Uid), Is.False);
        Assert.That(session.ItemEnchant.StageItem(EnchantType.Ophelia, gear.Uid), Is.True);
        Assert.That(session.ItemEnchant.Enchant(gear.Uid + 1), Is.False);

        Assert.That(Held(session, ItemTag.Onix), Is.EqualTo(30000));
        Assert.That(session.Item.Inventory.Get(gear.Uid)!.Enchant!.Enchants, Is.EqualTo(0));
    }

    // Two requests in a row on one staged item (grades 0 and 1 always succeed): each pays its own grade's cost,
    // and the result is the last packet of each.
    [Test]
    public void Ophelia_TwiceOnOneStagePaysEachGradesCost() {
        GameSession session = Login(30000, 30000, 30000, 0);
        // Grade 2 to 3 always succeeds, and grade 3 costs more than grade 2; the second roll may fail, its cost is paid
        // either way.
        session.Item.Inventory.Get(gear.Uid)!.Enchant = new ItemEnchant(enchants: 2);
        Assert.That(session.ItemEnchant.StageItem(EnchantType.Ophelia, gear.Uid), Is.True);
        test.TakeSent();
        Dictionary<ItemTag, int> first = Cost(session);

        Assert.That(session.ItemEnchant.Enchant(gear.Uid), Is.True);
        List<SendOp> ops = test.TakeSentOps().ToList();
        Assert.That(ops[^1], Is.EqualTo(SendOp.ItemEnchant));
        Assert.That(ops.Take(ops.Count - 1), Is.All.EqualTo(SendOp.ItemInventory));
        Dictionary<ItemTag, int> second = Cost(session);
        Assert.That(second[ItemTag.Onix], Is.Not.EqualTo(first[ItemTag.Onix]), "the two grades cost the same");

        Assert.That(session.ItemEnchant.Enchant(gear.Uid), Is.True);

        Assert.Multiple(() => {
            Assert.That(session.Item.Inventory.Get(gear.Uid)!.Enchant!.Enchants, Is.InRange(3, 4));
            foreach (ItemTag tag in new[] { ItemTag.Onix, ItemTag.ChaosOnix, ItemTag.CrystalPiece }) {
                Assert.That(Held(session, tag), Is.EqualTo(30000 - first.GetValueOrDefault(tag) - second.GetValueOrDefault(tag)), tag.ToString());
            }
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // The second request finds too little left for its grade: the first result stays, nothing more is taken.
    [Test]
    public void Ophelia_TheSecondRequestWithoutItsCostTakesNothing() {
        GameSession session = Login(30000, 30000, 30000, 0);
        Assert.That(session.ItemEnchant.StageItem(EnchantType.Ophelia, gear.Uid), Is.True);
        int onyx = Cost(session)[ItemTag.Onix];
        Item stack = session.Item.Inventory.Find(ItemTag.Onix).Single();
        Assert.That(session.Item.Inventory.Consume(stack.Uid, 30000 - onyx), Is.True);

        Assert.That(session.ItemEnchant.Enchant(gear.Uid), Is.True);
        int chaosOnyx = Held(session, ItemTag.ChaosOnix);
        int fragments = Held(session, ItemTag.CrystalPiece);
        Assert.That(session.ItemEnchant.Enchant(gear.Uid), Is.False);

        Assert.Multiple(() => {
            Assert.That(session.Item.Inventory.Get(gear.Uid)!.Enchant!.Enchants, Is.EqualTo(1));
            Assert.That(Held(session, ItemTag.Onix), Is.EqualTo(0));
            Assert.That(Held(session, ItemTag.ChaosOnix), Is.EqualTo(chaosOnyx));
            Assert.That(Held(session, ItemTag.CrystalPiece), Is.EqualTo(fragments));
        });
    }
}
