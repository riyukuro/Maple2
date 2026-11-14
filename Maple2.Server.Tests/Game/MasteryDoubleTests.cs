using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using System.Runtime.CompilerServices;
using Maple2.Server.Game.Manager;
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

    // A whole gather (GatherCommon) with 2 production efficiency makes 3 gathers; each rolls its own double mastery and
    // double reward, so at 100% every gather's 10 mastery and 3 items are doubled, and at 0% none is.
    [TestCase(1000L, 60, 18)]
    [TestCase(0L, 30, 9)]
    public void GatherWithExtraGathers(long rate, int mastery, int count) {
        MasteryRecipeTable.Entry recipe = Recipe(50000002, MasteryType.Gathering);
        GameSession session = Start(recipe);
        test.SetTable(new MasteryDifferentialFactorTable(new Dictionary<int, MasteryDifferentialFactorTable.Entry> { [0] = new(0, 10000) }));
        SetRate(session, SpecialAttribute.GatheringEfficiency, 2000);
        SetRate(session, SpecialAttribute.GatheringDoubleMastery, rate);
        SetRate(session, SpecialAttribute.GatheringDoubleReward, rate);

        object?[] args = [recipe.Id, Vector3.Zero, Vector3.Zero, null];
        int gathers = (int) typeof(MasteryManager).GetMethod("GatherCommon", Any)!.Invoke(session.Mastery, args)!;

        Assert.That(gathers, Is.EqualTo(3));
        Assert.That(session.Mastery[MasteryType.Gathering], Is.EqualTo(mastery));
        var ground = ((ConcurrentDictionary<int, FieldItem>) typeof(FieldManager).GetField("fieldItems", Any)!.GetValue(session.Field)!).Values;
        Assert.That(ground.Where(item => item.Value.Id == RewardItemId).Sum(item => item.Value.Amount), Is.EqualTo(count));
    }

    // A first catch gives twice the fish's mastery (FishingManager.AddFish); fishing double mastery (112) at 100% doubles
    // that again. A running MasteryReward event for the map then adds its share of the doubled gain (5000 = half).
    [TestCase(1000L, 0, 4)]
    [TestCase(0L, 0, 2)]
    [TestCase(1000L, 5000, 6)]
    [TestCase(0L, 5000, 3)]
    public void FishingCatch(long rate, int eventRate, int factor) {
        test = new TestGameSession();
        test.SetTable(new MasteryRewardTable(new Dictionary<MasteryType, IReadOnlyDictionary<int, MasteryRewardTable.Entry>>()));
        if (eventRate > 0) {
            test.AddEvent(1, GameEventType.MasteryReward, new MasteryRewardEvent(1, string.Empty, new Dictionary<int, int> { [2000001] = eventRate }));
        }
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        session.Fishing = new FishingManager(session, test.TableMetadata, test.ServerTableMetadata);
        var fish = new FishTable.Fish(10000001, LiquidType.water, 80, 6, 1, 3, 1, 54, 15000, 10000, 8000,
            new FishTable.Range<int>(20, 30), new FishTable.Range<int>(40, 50), [], 0, false);
        session.Fishing.Cast((FishingTile) RuntimeHelpers.GetUninitializedObject(typeof(FishingTile)), fish, fishBites: true, fishBiteTick: 0, fightGame: false);
        SetRate(session, SpecialAttribute.FishingDoubleMastery, rate);
        int before = session.Mastery[MasteryType.Fishing];

        typeof(FishingManager).GetMethod("AddFish", Any)!.Invoke(session.Fishing, [25, false]);

        Assert.That(session.Mastery[MasteryType.Fishing] - before, Is.EqualTo(fish.MasteryExp * factor));
    }

    // A score's stop (PlayInstrument 4) gives its mastery, here 1; performing double mastery
    // (113) at 100% doubles it. A running MasteryReward event for the map adds its share after the double, rounded
    // down: half of 2 is 1, half of 1 is 0 (were the event first, the double of 1 + 0 would give 2, not 3).
    [TestCase(1000L, 0, 2)]
    [TestCase(0L, 0, 1)]
    [TestCase(1000L, 5000, 3)]
    [TestCase(0L, 5000, 1)]
    public void InstrumentScore(long rate, int eventRate, int mastery) {
        const int instrumentItemId = 34000003, scoreItemId = 35000033;
        test = new TestGameSession();
        Item instrument = test.Store(TestGameSession.Metadata(instrumentItemId, type: 6,
            function: new ItemMetadataFunction(ItemFunction.OpenInstrument, "OpenInstrument", "1", false)));
        // A ready-made score of 60 s (every KR one has a playTime) giving 1 mastery a second, up to 1.
        Item score = test.Store(TestGameSession.Metadata(scoreItemId, type: 6) with {
            Music = new ItemMetadataMusic(10, 1, 1, false, 0, string.Empty, 60000),
        });
        test.SetTable(new InstrumentTable(new Dictionary<int, InstrumentMetadata> { [1] = new(1, 0, 1, 1, 1, 0) }));
        test.SetTable(new CommonExpTable(new Dictionary<ExpType, CommonExpTable.Entry> { [ExpType.musicMastery1] = new(1, 1f) }));
        test.SetTable(new ExpTable(new Dictionary<int, IReadOnlyDictionary<int, long>> {
            [1] = Enumerable.Range(1, 99).ToDictionary(level => level, _ => 100L),
        }, Enumerable.Range(1, 99).ToDictionary(level => level, _ => 1_000_000L)));
        test.SetTable(new MasteryRewardTable(new Dictionary<MasteryType, IReadOnlyDictionary<int, MasteryRewardTable.Entry>>()));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        if (eventRate > 0) {
            test.AddEvent(1, GameEventType.MasteryReward, new MasteryRewardEvent(2, string.Empty, new Dictionary<int, int> { [2000001] = eventRate }));
        }
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        Item held = session.Item.Inventory.Get(instrument.Uid, InventoryType.FishingMusic)!;
        held.ExpiryTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600;
        Item heldScore = session.Item.Inventory.Get(score.Uid, InventoryType.FishingMusic)!;
        heldScore.RemainUses = 5;
        SetRate(session, SpecialAttribute.PlayInstrumentDoubleMastery, rate);
        var handler = new InstrumentHandler { TableMetadata = test.TableMetadata, BanWordStorage = null! };
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteLong(held.Uid);
            writer.WriteLong(heldScore.Uid);
        }));
        Assume.That(session.Instrument, Is.Not.Null, "started");
        session.Instrument!.StartTick -= 10_500;

        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(4)));

        Assert.That(session.Instrument, Is.Null);
        Assert.That(session.Mastery[MasteryType.Music], Is.EqualTo(mastery));
    }
}
