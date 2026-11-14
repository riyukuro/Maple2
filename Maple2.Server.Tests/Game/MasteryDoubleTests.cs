using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// The double mastery and double reward abilities (112, 113, 131 to 146): the roll of MasteryRules.DoubleFactor, the
// ability of each mastery type, and a craft and a gather at a rate of 100% (each doubled) and 0% (none).
public class MasteryDoubleTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int RewardItemId = 20099901;
    private TestGameSession test = null!;

    [TestCase(0L, 0.0, 1)]
    [TestCase(-100L, 0.0, 1)]
    [TestCase(50L, 0.049, 2)]
    [TestCase(50L, 0.05, 1)]
    [TestCase(1000L, 0.999, 2)]
    public void DoubleFactor(long total, double roll, int expected) {
        Assert.That(MasteryRules.DoubleFactor(total, roll), Is.EqualTo(expected));
    }

    [Test]
    public void EachTypeHasItsOwnAbilities() {
        Assert.Multiple(() => {
            Assert.That(MasteryRules.DoubleMastery(MasteryType.Fishing), Is.EqualTo(SpecialAttribute.FishingDoubleMastery));
            Assert.That(MasteryRules.DoubleMastery(MasteryType.Music), Is.EqualTo(SpecialAttribute.PlayInstrumentDoubleMastery));
            Assert.That(MasteryRules.DoubleMastery(MasteryType.Mining), Is.EqualTo(SpecialAttribute.MiningDoubleMastery));
            Assert.That(MasteryRules.DoubleMastery(MasteryType.Cooking), Is.EqualTo(SpecialAttribute.CookingDoubleMastery));
            Assert.That(MasteryRules.DoubleMastery(MasteryType.PetTaming), Is.Null);
            Assert.That(MasteryRules.DoubleReward(MasteryType.Gathering), Is.EqualTo(SpecialAttribute.GatheringDoubleReward));
            Assert.That(MasteryRules.DoubleReward(MasteryType.Blacksmithing), Is.EqualTo(SpecialAttribute.SmithingDoubleReward));
            Assert.That(MasteryRules.DoubleReward(MasteryType.Fishing), Is.Null);
            Assert.That(MasteryRules.DoubleReward(MasteryType.Music), Is.Null);
        });
        MasteryType[] types = [MasteryType.Fishing, MasteryType.Music, MasteryType.Mining, MasteryType.Gathering, MasteryType.Breeding,
            MasteryType.Farming, MasteryType.Blacksmithing, MasteryType.Engraving, MasteryType.Alchemist, MasteryType.Cooking];
        Assert.That(types.Select(MasteryRules.DoubleMastery).Distinct().Count(), Is.EqualTo(10));
        Assert.That(types.Select(MasteryRules.DoubleReward).Where(ability => ability != null).Distinct().Count(), Is.EqualTo(8));
    }

    private static MasteryRecipeTable.Entry Recipe(int id, MasteryType type) =>
        new(id, type, NoRewardExp: false, RequiredMastery: 0, RequiredMeso: 0, RequiredQuests: [],
            RequiredLevel: 0, RewardExp: 0, RewardMastery: 10, HighRateLimitCount: 5, NormalRateLimitCount: 10,
            RequiredItems: [], HabitatMapId: [], RewardItems: [new ItemComponent(RewardItemId, 1, 3, ItemTag.None)]);

    private GameSession Start(MasteryRecipeTable.Entry recipe) {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(RewardItemId));
        test.SetTable(new MasteryRecipeTable(new Dictionary<int, MasteryRecipeTable.Entry> { [recipe.Id] = recipe }));
        test.SetTable(new MasteryRewardTable(new Dictionary<MasteryType, IReadOnlyDictionary<int, MasteryRewardTable.Entry>>()));
        test.SetTable(new CommonExpTable(new Dictionary<ExpType, CommonExpTable.Entry> { [ExpType.manufacturing] = new(1, 1f) }));
        test.SetTable(new ExpTable(new Dictionary<int, IReadOnlyDictionary<int, long>> {
            [1] = Enumerable.Range(1, 99).ToDictionary(level => level, _ => 100L),
        }, Enumerable.Range(1, 99).ToDictionary(level => level, _ => 1_000_000L)));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        GameSession session = test.Start();
        test.SetGatheringCounts();
        test.EnterMap(TestGameSession.Map(2000001));
        test.TakeSent();
        return session;
    }

    private static void SetRate(GameSession session, SpecialAttribute ability, long total) {
        Stat stat = session.Player.Stats.Values[ability];
        stat.AddTotal(total - stat.Total);
    }

    [TestCase(1000L, 20, 6)]
    [TestCase(0L, 10, 3)]
    public void Craft(long rate, int mastery, int count) {
        GameSession session = Start(Recipe(10000015, MasteryType.Blacksmithing));
        SetRate(session, SpecialAttribute.SmithingDoubleMastery, rate);
        SetRate(session, SpecialAttribute.SmithingDoubleReward, rate);
        var handler = new MasteryHandler { ItemMetadata = session.ItemMetadata, TableMetadata = test.TableMetadata };
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(2);
            writer.WriteInt(10000015);
        }));

        Assert.That(test.Errors, Is.Empty);
        Assert.That(session.Mastery[MasteryType.Blacksmithing], Is.EqualTo(mastery));
        Assert.That(session.Item.Inventory.Find(RewardItemId).Sum(item => item.Amount), Is.EqualTo(count));
    }

    [TestCase(1000L, 6)]
    [TestCase(0L, 3)]
    public void GatherItems(long rate, int count) {
        MasteryRecipeTable.Entry recipe = Recipe(50000002, MasteryType.Gathering);
        GameSession session = Start(recipe);
        SetRate(session, SpecialAttribute.GatheringDoubleReward, rate);
        session.Mastery.GatherSuccessRate(recipe);
        session.Mastery.Gather(recipe, Vector3.Zero, Vector3.Zero);

        var ground = ((ConcurrentDictionary<int, FieldItem>) typeof(FieldManager).GetField("fieldItems", Any)!.GetValue(session.Field)!).Values;
        Assert.That(ground.Where(item => item.Value.Id == RewardItemId).Sum(item => item.Value.Amount), Is.EqualTo(count));
    }
}
