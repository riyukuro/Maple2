using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Maple2.File.Ingest;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using System.Reflection;

namespace Maple2.Server.Tests.Game;

// The life skill quest conditions (MasteryManager's gather reports, MasteryHandler's craft), with the KR quest rows'
// conditions as the ingest reads them (ConvertCodes). Each test fails when its report is taken out.
public class MasteryConditionTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int QuestId = 91000001;
    private TestGameSession test = null!;

    private static MasteryRecipeTable.Entry Recipe(int id, MasteryType type, int questId = 0) =>
        new(id, type, NoRewardExp: true, RequiredMastery: 0, RequiredMeso: 0, RequiredQuests: questId == 0 ? [] : [questId],
            RequiredLevel: 0, RewardExp: 0, RewardMastery: 0, HighRateLimitCount: 5, NormalRateLimitCount: 10,
            RequiredItems: [], HabitatMapId: [], RewardItems: []);

    private GameSession Start(params MasteryRecipeTable.Entry[] recipes) {
        test = new TestGameSession();
        test.SetTable(new MasteryRecipeTable(recipes.ToDictionary(recipe => recipe.Id)));
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

    private Quest StartQuest(int index, ConditionType type, long value, string[] code) =>
        test.StartQuest(TestGameSession.QuestData(QuestId + index) with {
            Conditions = [new ConditionMetadata(type, value, code.ConvertCodes(), new string[0].ConvertCodes())],
        });

    private static void Gather(GameSession session, MasteryRecipeTable.Entry recipe) {
        session.Mastery.GatherSuccessRate(recipe); // the recipe's gather count, as GatherCommon starts it
        session.Mastery.Gather(recipe, Vector3.Zero, Vector3.Zero);
    }

    // KR 70500013: mastery_harvest code 50000002, value 3. A farming or a breeding harvest of the recipe counts; a
    // gather of a gathering or mining recipe, or a harvest of another recipe, does not.
    [TestCase(MasteryType.Farming, 50000002, 1)]
    [TestCase(MasteryType.Breeding, 50000002, 1)]
    [TestCase(MasteryType.Farming, 50000001, 0)]
    [TestCase(MasteryType.Gathering, 50000002, 0)]
    [TestCase(MasteryType.Mining, 50000002, 0)]
    public void MasteryHarvest_70500013_TheRecipesHarvests(MasteryType type, int recipeId, int counted) {
        MasteryRecipeTable.Entry recipe = Recipe(recipeId, type);
        GameSession session = Start(recipe);
        Quest quest = StartQuest(0, ConditionType.mastery_harvest, 3, ["50000002"]);

        Gather(session, recipe);

        Assert.That(test.Errors, Is.Empty);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(counted));
    }

    // KR 60001121: mastery_harvest_try code 50000001, value 1. The start of a farming or breeding harvest counts,
    // before its roll: a mining or gathering start, or another recipe, does not.
    [TestCase(MasteryType.Farming, 50000001, 1)]
    [TestCase(MasteryType.Breeding, 50000001, 1)]
    [TestCase(MasteryType.Farming, 50000002, 0)]
    [TestCase(MasteryType.Mining, 50000001, 0)]
    [TestCase(MasteryType.Gathering, 50000001, 0)]
    public void MasteryHarvestTry_60001121_TheRecipesStarts(MasteryType type, int recipeId, int counted) {
        MasteryRecipeTable.Entry recipe = Recipe(recipeId, type);
        GameSession session = Start(recipe);
        Quest quest = StartQuest(0, ConditionType.mastery_harvest_try, 1, ["50000001"]);

        session.Mastery.BeforeGather(recipe);

        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(counted));
    }

    // KR 70500007: mastery_harvest_otherhouse codes 60000011-69999999, value 3 (70500008: 50000001-59999999). A
    // harvest in another player's home counts; one in the player's own home, or outside a home, does not.
    [TestCase("other", 1)]
    [TestCase("own", 0)]
    [TestCase("field", 0)]
    public void MasteryHarvestOtherHouse_70500007_InAnothersHome(string where, int counted) {
        MasteryRecipeTable.Entry breeding = Recipe(60000011, MasteryType.Breeding);
        MasteryRecipeTable.Entry farming = Recipe(50000001, MasteryType.Farming);
        GameSession session = Start(breeding, farming);
        if (where != "field") {
            session.Housing.InitNewHome("home", null);
            test.EnterHome();
            if (where == "other") {
                typeof(HomeFieldManager).GetField("<Home>k__BackingField", Any)!.SetValue(session.Field, new Home { AccountId = session.AccountId + 1 });
            }
        }
        Quest quest = StartQuest(0, ConditionType.mastery_harvest_otherhouse, 3, ["60000011-69999999"]);
        Quest farmingQuest = StartQuest(1, ConditionType.mastery_harvest_otherhouse, 3, ["50000001-59999999"]);

        Gather(session, breeding);
        Gather(session, farming);

        Assert.That(test.Errors, Is.Empty);
        Assert.That((quest.Conditions[0].Counter, farmingQuest.Conditions[0].Counter), Is.EqualTo((counted, counted)));
    }

    // KR 70400063: mastery_manufacturing code 10000015, value 1. A craft of the recipe counts; another recipe's, or
    // a refused craft (its quest not in progress), does not.
    [Test]
    public void MasteryManufacturing_70400063_TheRecipesCraft() {
        const int lockedId = 10000016, questId = 90000600;
        GameSession session = Start(Recipe(10000015, MasteryType.Blacksmithing), Recipe(10000014, MasteryType.Blacksmithing),
            Recipe(lockedId, MasteryType.Blacksmithing, questId));
        Quest quest = StartQuest(0, ConditionType.mastery_manufacturing, 1, ["10000015"]);
        Quest locked = StartQuest(1, ConditionType.mastery_manufacturing, 1, [$"{lockedId}"]);
        var handler = new MasteryHandler { ItemMetadata = session.ItemMetadata, TableMetadata = test.TableMetadata };
        void Craft(int recipeId) => handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(2);
            writer.WriteInt(recipeId);
        }));

        Craft(10000014);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(0));
        Craft(lockedId);
        Craft(10000015);

        Assert.That(test.Errors, Is.Empty);
        Assert.That((quest.Conditions[0].Counter, locked.Conditions[0].Counter), Is.EqualTo((1, 0)));
    }
}
