using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// A mastery craft (Mastery 2) stores its fee and materials before it gives the product: the material's row is lowered
// in storage at once, and a save that fails keeps the materials and the fee and gives nothing. The ids are made up.
public class MasteryCraftClaimTests {
    private const int RecipeId = 10000015;
    private const int Material = 20099902;
    private const int Product = 20099901;
    private TestGameSession test = null!;

    private (GameSession Session, Item Stack) Start() {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(Product));
        Item stack = test.Store(TestGameSession.Metadata(Material, type: 4, slotMax: 99), amount: 5);
        var recipe = new MasteryRecipeTable.Entry(RecipeId, MasteryType.Blacksmithing, NoRewardExp: true, RequiredMastery: 0,
            RequiredMeso: 100, RequiredQuests: [], RequiredLevel: 0, RewardExp: 0, RewardMastery: 10, HighRateLimitCount: 5,
            NormalRateLimitCount: 10, RequiredItems: [new ItemComponent(Material, 1, 3, ItemTag.None)], HabitatMapId: [],
            RewardItems: [new ItemComponent(Product, 1, 1, ItemTag.None)]);
        test.SetTable(new MasteryRecipeTable(new Dictionary<int, MasteryRecipeTable.Entry> { [RecipeId] = recipe }));
        test.SetTable(new MasteryRewardTable(new Dictionary<MasteryType, IReadOnlyDictionary<int, MasteryRewardTable.Entry>>()));
        GameSession session = test.Start();
        test.SetGatheringCounts();
        test.EnterMap(TestGameSession.Map(2000001));
        session.Currency.Meso = 1000;
        test.TakeSent();
        return (session, stack);
    }

    private void Craft(GameSession session) {
        new MasteryHandler { ItemMetadata = session.ItemMetadata, TableMetadata = test.TableMetadata }
            .Handle(session, TestGameSession.Packet(writer => {
                writer.WriteByte(2);
                writer.WriteInt(RecipeId);
            }));
    }

    private static int Held(GameSession session, int itemId) => session.Item.Inventory.Find(itemId).Sum(item => item.Amount);

    [Test]
    public void TheMaterialsAreStoredAtOnce() {
        (GameSession session, Item stack) = Start();

        Craft(session);

        Assert.Multiple(() => {
            Assert.That(test.Errors, Is.Empty);
            Assert.That(test.Row(stack.Uid)!.Amount, Is.EqualTo(2));
            Assert.That(Held(session, Material), Is.EqualTo(2));
            Assert.That(session.Currency.Meso, Is.EqualTo(900));
            Assert.That(Held(session, Product), Is.EqualTo(1));
        });
    }

    [Test]
    public void AFailedSaveKeepsTheMaterialsAndTheFeeAndGivesNothing() {
        (GameSession session, Item stack) = Start();
        test.FailSave = _ => true;

        Craft(session);
        test.FailSave = null;

        Assert.Multiple(() => {
            Assert.That(test.Row(stack.Uid)!.Amount, Is.EqualTo(5));
            Assert.That(Held(session, Material), Is.EqualTo(5));
            Assert.That(session.Currency.Meso, Is.EqualTo(1000));
            Assert.That(Held(session, Product), Is.EqualTo(0));
        });
    }
}
