using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// InventoryManager.ClearSurvivalItems: the match-only items are deleted on entering a field outside the match (no
// survival element) or a survival map that starts empty (plusInven_Zero=1), unless the player comes back into its running
// game (SurvivalItemRules.ClearReason). Other items stay. Made-up item ids.
public class SurvivalItemClearTests {
    private TestGameSession test = null!;
    private GameSession session = null!;
    private Item matchItem = null!;
    private Item plainItem = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        ItemMetadata survival = TestGameSession.Metadata(20000091);
        matchItem = test.Store(survival with { Property = survival.Property with { Survival = true } });
        plainItem = test.Store(TestGameSession.Metadata(20000092));
        test.Start();
        session = test.Session;
    }

    private static MapMetadataSurvival Survival(int plusInvenZero) => new(0, 0, 0, 0, plusInvenZero);

    [TestCase(false)]
    [TestCase(true)]
    public void OutsideAMatch_TheMatchOnlyItemsAreDeletedAndOthersKept(bool inRunningGame) {
        Assert.Multiple(() => {
            Assert.That(session.Item.Inventory.ClearSurvivalItems(null, 2000001, inRunningGame), Is.EqualTo(1));
            Assert.That(session.Item.Inventory.Get(matchItem.Uid), Is.Null);
            Assert.That(session.Item.Inventory.Get(plainItem.Uid), Is.Not.Null);
        });
    }

    // A map that starts empty clears the leftovers, but not the return into the running game of the same field.
    [Test]
    public void AMapThatStartsEmpty_ClearsExceptInTheRunningGame() {
        Assert.Multiple(() => {
            Assert.That(session.Item.Inventory.ClearSurvivalItems(Survival(1), 2000001, inRunningGame: true), Is.Zero);
            Assert.That(session.Item.Inventory.Get(matchItem.Uid), Is.Not.Null);
            Assert.That(session.Item.Inventory.ClearSurvivalItems(Survival(1), 2000001), Is.EqualTo(1));
            Assert.That(session.Item.Inventory.Get(matchItem.Uid), Is.Null);
        });
    }

    [Test]
    public void AMatchMapThatDoesNotStartEmpty_KeepsTheItems() {
        Assert.Multiple(() => {
            Assert.That(session.Item.Inventory.ClearSurvivalItems(Survival(0), 2000001), Is.Zero);
            Assert.That(session.Item.Inventory.Get(matchItem.Uid), Is.Not.Null);
        });
    }
}
