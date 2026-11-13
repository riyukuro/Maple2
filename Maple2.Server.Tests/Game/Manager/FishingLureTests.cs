using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.File.Ingest.Mapper;
using Maple2.File.Parser.Xml.Table.Server;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;

namespace Maple2.Server.Tests.Game.Manager;

// fishLure.xml rows (KR values) and the cast's use of them.
public class FishingLureTests {
    private static FishTable.Fish Fish(int id, short rank, LiquidType habitat = LiquidType.water, int bait = 4000) =>
        new(id, habitat, 1, 1, rank, 1, 1, 0, 15000, 10000, bait, new FishTable.Range<int>(1, 3), new FishTable.Range<int>(5, 8), [], 0, true);

    private static FishTable.Spot Spot() => new(2000001, 1, 100, [LiquidType.water], 1, 0, 0, 0, 1, 0);

    private static FishTable Table(IEnumerable<FishTable.Fish> fishes, Dictionary<int, IReadOnlyDictionary<short, FishTable.Lure>> lures) =>
        new(fishes.ToDictionary(fish => fish.Id), new Dictionary<int, FishTable.Spot>(), lures,
            new Dictionary<int, FishTable.FishBox>(), new Dictionary<int, FishTable.FishBox>());

    private static FishTable.Lure Lure(int id, FishTable.Lure.RankRate[]? catches = null, FishTable.Lure.RankRate[]? spawns = null,
        FishTable.Lure.Spawn[]? fishes = null, int sizeClass = 0, int sizeRate = 0) =>
        new(id, 1, catches ?? [], spawns ?? [], fishes ?? [], sizeClass, sizeRate, null, null);

    [Test]
    public void Ingest_KeysEveryLevelAndMapsTheRows() {
        Dictionary<int, IReadOnlyDictionary<short, FishTable.Lure>> lures = ServerTableMapper.MapLures([
            new FishLure { additionalEffectCode = 90000309, additionalEffectLevel = 1, fishSize = 3, fishSizeProp = 100 },
            new FishLure { additionalEffectCode = 90000309, additionalEffectLevel = 2, fishSize = 3, fishSizeProp = 300 },
            new FishLure { additionalEffectCode = 90000268, additionalEffectLevel = 1, catchProp = [1000] },
            new FishLure { additionalEffectCode = 90000266, additionalEffectLevel = 1, spawnRank = [3, 4], spawnProp = [100000, 100000] },
            new FishLure { additionalEffectCode = 90000305, additionalEffectLevel = 1, spawnFish = [99000001, 99000002], spawnFishRate = [443, 50] },
            new FishLure {
                additionalEffectCode = 90000281, additionalEffectLevel = 1, globalDropBoxID = 41001001, globalDropRank = 1,
                globalDropFishingSpotMastery = [0, 1189], individualDropBoxID = 381000001,
            },
            new FishLure { additionalEffectCode = 90001150, additionalEffectLevel = 1, individualDropBoxID = 381000038, _feature = "NoSuchFeature" },
        ]);

        Assert.That(lures[90000309].Keys, Is.EquivalentTo(new short[] { 1, 2 }));
        Assert.That(lures[90000309][2].BuffId, Is.EqualTo(90000309));
        Assert.That(lures[90000309][2].FishSizeClass, Is.EqualTo(3));
        Assert.That(lures[90000309][2].FishSizeProbability, Is.EqualTo(300));
        Assert.That(lures[90000268][1].Catches, Is.EqualTo(new[] { new FishTable.Lure.RankRate(0, 1000) }));
        Assert.That(lures[90000266][1].Spawns, Is.EqualTo(new[] { new FishTable.Lure.RankRate(3, 100000), new FishTable.Lure.RankRate(4, 100000) }));
        Assert.That(lures[90000305][1].SpawnFishes, Is.EqualTo(new[] { new FishTable.Lure.Spawn(99000001, 443), new FishTable.Lure.Spawn(99000002, 50) }));
        Assert.That(lures[90000281][1].GlobalDrop, Is.EqualTo(new FishTable.Lure.Drop(41001001, 1, new FishTable.Range<int>(0, 1189))));
        Assert.That(lures[90000281][1].IndividualDrop, Is.EqualTo(new FishTable.Lure.Drop(381000001, 0, null)));
        Assert.That(lures.ContainsKey(90001150), Is.False);
    }

    [Test]
    public void Ingest_ReadsEveryLevelOfTheFile() {
        // The parser's own row list resolves per additionalEffectCode and keeps one level of 90000309.
        var root = new FishLureRoot {
            __lure = [
                new FishLure { additionalEffectCode = 90000309, additionalEffectLevel = 1, fishSize = 3, fishSizeProp = 100 },
                new FishLure { additionalEffectCode = 90000309, additionalEffectLevel = 2, fishSize = 3, fishSizeProp = 300 },
                new FishLure { additionalEffectCode = 90000309, additionalEffectLevel = 3, fishSize = 3, fishSizeProp = 500 },
                new FishLure { additionalEffectCode = 90000262, additionalEffectLevel = 1, catchRank = [1, 2], catchProp = [1000, 1000] },
            ],
        };

        Dictionary<int, IReadOnlyDictionary<short, FishTable.Lure>> lures = ServerTableMapper.MapLures(ServerTableMapper.LureRows(root));

        Assert.That(lures[90000309].Keys, Is.EquivalentTo(new short[] { 1, 2, 3 }));
        Assert.That(lures[90000309][1].FishSizeProbability, Is.EqualTo(100));
        Assert.That(lures[90000262].Keys, Is.EquivalentTo(new short[] { 1 }));
    }

    [Test]
    public void HeldLures_MatchIdAndLevel() {
        FishTable table = Table([], new Dictionary<int, IReadOnlyDictionary<short, FishTable.Lure>> {
            [90000309] = new Dictionary<short, FishTable.Lure> { [2] = Lure(90000309) with { BuffLevel = 2 } },
        });

        Assert.That(FishingManager.HeldLures(table, [(90000309, 1)]), Is.Empty);
        Assert.That(FishingManager.HeldLures(table, [(90000309, 2), (12345, 1)]).Single().BuffLevel, Is.EqualTo(2));
    }

    [Test]
    public void LureFish_RollsOverTheRatesOutOf1000() {
        FishTable.Fish bait1 = Fish(99000001, 1, LiquidType.all);
        FishTable.Fish bait2 = Fish(99000002, 2, LiquidType.all);
        FishTable table = Table([bait1, bait2], []);
        FishTable.Lure lure = Lure(90000305, fishes: [new FishTable.Lure.Spawn(99000001, 443), new FishTable.Lure.Spawn(99000002, 50), new FishTable.Lure.Spawn(99000027, 7)]);

        Assert.That(FishingManager.LureFish(table, [lure], Spot(), LiquidType.water, 0), Is.SameAs(bait1));
        Assert.That(FishingManager.LureFish(table, [lure], Spot(), LiquidType.lava, 442), Is.SameAs(bait1));
        Assert.That(FishingManager.LureFish(table, [lure], Spot(), LiquidType.water, 443), Is.SameAs(bait2));
        // 99000027 is not in the table, and 500 and up is a miss: the boxes decide.
        Assert.That(FishingManager.LureFish(table, [lure], Spot(), LiquidType.water, 495), Is.Null);
        Assert.That(FishingManager.LureFish(table, [lure], Spot(), LiquidType.water, 500), Is.Null);
        Assert.That(FishingManager.LureFish(table, [], Spot(), LiquidType.water, 0), Is.Null);
    }

    [Test]
    public void Habitat_AllLivesInAnyLiquid() {
        Assert.That(FishingManager.LivesIn(Fish(99000001, 1, LiquidType.all), LiquidType.seawater), Is.True);
        Assert.That(FishingManager.LivesIn(Fish(10000001, 1, LiquidType.water), LiquidType.seawater), Is.False);
    }

    [Test]
    public void BoxWeight_ScaledByRankAndZeroLeftOut() {
        FishTable.Lure lure = Lure(90000266, spawns: [new FishTable.Lure.RankRate(3, 100000), new FishTable.Lure.RankRate(4, 100000)]);

        Assert.That(FishingManager.LureWeight(800000, Fish(1, 3), [lure]), Is.EqualTo(8000000));
        Assert.That(FishingManager.LureWeight(800000, Fish(1, 2), [lure]), Is.EqualTo(800000));
        Assert.That(FishingManager.LureWeight(800000, Fish(1, 3), []), Is.EqualTo(800000));
        Assert.That(FishingManager.LureWeight(0, Fish(1, 3), [lure]), Is.EqualTo(0));
    }

    [Test]
    public void BiteChance_RaisedForTheRanks() {
        FishTable.Lure ranks = Lure(90000263, catches: [new FishTable.Lure.RankRate(1, 1000), new FishTable.Lure.RankRate(2, 1000)]);
        FishTable.Lure every = Lure(90000268, catches: [new FishTable.Lure.RankRate(0, 1000)]);

        Assert.That(FishingManager.BiteChance(Fish(1, 1), []), Is.EqualTo(4000));
        Assert.That(FishingManager.BiteChance(Fish(1, 1), [ranks]), Is.EqualTo(5000));
        Assert.That(FishingManager.BiteChance(Fish(1, 3), [ranks]), Is.EqualTo(4000));
        Assert.That(FishingManager.BiteChance(Fish(1, 3), [every]), Is.EqualTo(5000));
        Assert.That(FishingManager.BiteChance(Fish(1, 1, bait: 9500), [ranks, every]), Is.EqualTo(10000));
    }

    [Test]
    public void LureSize_PutsTheCatchInTheClass() {
        FishTable.Fish fish = Fish(1, 1);
        FishTable.Lure prize = Lure(90000309, sizeClass: 3, sizeRate: 300);
        FishTable.Lure gold = Lure(90000267, sizeClass: 2, sizeRate: 500);
        var random = new Random(1);

        for (int i = 0; i < 50; i++) {
            Assert.That(FishingManager.LureSize(fish, [prize], 299, random), Is.InRange(8, 15));
            Assert.That(FishingManager.LureSize(fish, [gold], 499, random), Is.InRange(5, 7));
        }
        Assert.That(FishingManager.LureSize(fish, [prize], 300, random), Is.Null);
        Assert.That(FishingManager.LureSize(fish, [], 0, random), Is.Null);
    }

    [Test]
    public void DropBoxes_SpotFishAndLures() {
        FishTable.Spot low = new(2000001, 0, 79, [LiquidType.water], 1, 0, 41000001, 0, 10, 1);
        FishTable.Spot top = low with { MinMastery = 1000, MaxMastery = 1189 };
        FishTable.Fish bait = Fish(99000001, 1, LiquidType.all) with { IndividualDropBoxId = 381000005 };
        FishTable.Lure lure = Lure(90000281) with {
            GlobalDrop = new FishTable.Lure.Drop(41001001, 1, new FishTable.Range<int>(0, 1189)),
            IndividualDrop = new FishTable.Lure.Drop(381000001, 0, new FishTable.Range<int>(1000, 1000)),
        };
        FishTable.Lure anySpot = Lure(90000298) with { IndividualDrop = new FishTable.Lure.Drop(381000004, 0, null) };

        (List<int> global, List<int> individual) = FishingManager.DropBoxes(low, Fish(10000001, 1), []);
        Assert.That(global, Is.EqualTo(new[] { 41000001 }));
        Assert.That(individual, Is.Empty);

        (global, individual) = FishingManager.DropBoxes(low, bait, [lure, anySpot]);
        Assert.That(global, Is.EqualTo(new[] { 41000001, 41001001 }));
        Assert.That(individual, Is.EqualTo(new[] { 381000005, 381000004 }));

        (global, individual) = FishingManager.DropBoxes(top, bait, [lure]);
        Assert.That(global, Is.EqualTo(new[] { 41000001, 41001001 }));
        Assert.That(individual, Is.EqualTo(new[] { 381000005, 381000001 }));
    }
}
