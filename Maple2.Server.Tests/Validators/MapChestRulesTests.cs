using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Validators;

public class MapChestRulesTests {
    [TestCase(1999, MapChestRules.Kind.None)]
    [TestCase(2000, MapChestRules.Kind.Wood)]
    [TestCase(2999, MapChestRules.Kind.Wood)]
    [TestCase(3000, MapChestRules.Kind.Gold)]
    [TestCase(3999, MapChestRules.Kind.Gold)]
    [TestCase(4000, MapChestRules.Kind.None)]
    [TestCase(101, MapChestRules.Kind.None)]
    [TestCase(94000, MapChestRules.Kind.None)]
    public void SpawnKind(int id, MapChestRules.Kind kind) {
        Assert.That(MapChestRules.SpawnKind(id), Is.EqualTo(kind));
    }

    [TestCase(0, 0)]
    [TestCase(1, 1)]
    [TestCase(3, 1)]
    [TestCase(4, 2)]
    [TestCase(5, 2)]
    [TestCase(6, 2)]
    [TestCase(7, 3)]
    [TestCase(15, 5)]
    [TestCase(17, 6)]
    public void WoodCount(int points, int count) {
        Assert.That(MapChestRules.WoodCount(points), Is.EqualTo(count));
    }

    [Test]
    public void PickWoodTakesDistinctPoints() {
        int[] points = [2001, 2002, 2003, 2004, 2005, 2006, 2007];
        var seen = new HashSet<int>();
        var random = new Random(7);
        for (int i = 0; i < 200; i++) {
            List<int> picked = MapChestRules.PickWood(points, random);
            Assert.That(picked, Has.Count.EqualTo(3));
            Assert.That(picked.Distinct().Count(), Is.EqualTo(3));
            Assert.That(picked, Is.SubsetOf(points));
            seen.UnionWith(picked);
        }
        // Every point gets its turn.
        Assert.That(seen, Is.EquivalentTo(points));
        Assert.That(MapChestRules.PickWood([], random), Is.Empty);
    }

    [TestCase("The Golden Chests of $map:02000004$", true, 2000004)]
    [TestCase("$map:52000037$ chests", true, 52000037)]
    [TestCase("Gilded Globetrotter", false, 0)]
    [TestCase("", false, 0)]
    [TestCase(null, false, 0)]
    public void TrophyMap(string? name, bool found, int mapId) {
        Assert.That(MapChestRules.TryTrophyMap(name, out int id), Is.EqualTo(found));
        Assert.That(id, Is.EqualTo(mapId));
    }

    private static ConditionMetadata Condition(ConditionType type, ConditionMetadata.Parameters? codes) {
        return new ConditionMetadata(type, 1, codes, null);
    }

    [Test]
    public void TrophyChestIdsOfRange() {
        var range = new ConditionMetadata.Parameters(Range: new ConditionMetadata.Range<int>(14000008, 14000014));
        Assert.That(MapChestRules.TrophyChestIds(Condition(ConditionType.interact_object, range)),
            Is.EqualTo(new[] { 14000008, 14000009, 14000010, 14000011, 14000012, 14000013, 14000014 }));
        // Another condition, no codes or a backward range: none.
        Assert.That(MapChestRules.TrophyChestIds(Condition(ConditionType.map, range)), Is.Empty);
        Assert.That(MapChestRules.TrophyChestIds(Condition(ConditionType.interact_object, null)), Is.Empty);
        var backward = new ConditionMetadata.Parameters(Range: new ConditionMetadata.Range<int>(5, 4));
        Assert.That(MapChestRules.TrophyChestIds(Condition(ConditionType.interact_object, backward)), Is.Empty);
    }

    [Test]
    public void TrophyChestIdsOfListStartWithLaterRun() {
        // The map KMS2 shows: points 3000-3002 hold 14000598-600, points 3003-3006 hold 14000268-271.
        var list = new ConditionMetadata.Parameters(Integers: [14000268, 14000269, 14000270, 14000271, 14000598, 14000599, 14000600]);
        int[] ids = MapChestRules.TrophyChestIds(Condition(ConditionType.interact_object, list));
        Assert.That(ids, Is.EqualTo(new[] { 14000598, 14000599, 14000600, 14000268, 14000269, 14000270, 14000271 }));

        Dictionary<int, int> chests = MapChestRules.GoldChests([3006, 3000, 3003, 2001, 3001, 3002, 3004, 3005, 101], ids);
        Assert.That(chests, Has.Count.EqualTo(7));
        Assert.That(chests[3000], Is.EqualTo(14000598));
        Assert.That(chests[3002], Is.EqualTo(14000600));
        Assert.That(chests[3003], Is.EqualTo(14000268));
        Assert.That(chests[3006], Is.EqualTo(14000271));

        var one = new ConditionMetadata.Parameters(Integers: [7, 5, 6]);
        Assert.That(MapChestRules.TrophyChestIds(Condition(ConditionType.interact_object, one)), Is.EqualTo(new[] { 5, 6, 7 }));
    }

    [Test]
    public void GoldenChestCountsOnce() {
        Assert.That(MapChestRules.Counts(MapChestRules.Kind.Gold, firstOpen: true), Is.True);
        Assert.That(MapChestRules.Counts(MapChestRules.Kind.Gold, firstOpen: false), Is.False);
        Assert.That(MapChestRules.Counts(MapChestRules.Kind.Wood, firstOpen: false), Is.True);
        Assert.That(MapChestRules.Counts(MapChestRules.Kind.None, firstOpen: false), Is.True);

        // Seven opens of one golden chest over seven sets count once.
        var opened = new HashSet<int>();
        int counted = 0;
        for (int set = 0; set < 7; set++) {
            if (MapChestRules.Counts(MapChestRules.Kind.Gold, opened.Add(14000008))) {
                counted++;
            }
        }
        Assert.That(counted, Is.EqualTo(1));
    }

    [Test]
    public void OpenedChestHidesWhateverItsResetTime() {
        Assert.That(MapChestRules.StateAfterWait(MapChestRules.Kind.Wood, 0, 0), Is.EqualTo(InteractState.Hidden));
        Assert.That(MapChestRules.StateAfterWait(MapChestRules.Kind.Gold, 0, 1200000), Is.EqualTo(InteractState.Hidden));
        Assert.That(MapChestRules.StateAfterWait(MapChestRules.Kind.Gold, 5, 1200000), Is.EqualTo(InteractState.Hidden));
        // Any other object: as before.
        Assert.That(MapChestRules.StateAfterWait(MapChestRules.Kind.None, 3, 0), Is.Null);
        Assert.That(MapChestRules.StateAfterWait(MapChestRules.Kind.None, 0, 0), Is.Null);
        Assert.That(MapChestRules.StateAfterWait(MapChestRules.Kind.None, 3, 900000), Is.EqualTo(InteractState.Reactable));
        Assert.That(MapChestRules.StateAfterWait(MapChestRules.Kind.None, 0, 900000), Is.EqualTo(InteractState.Hidden));
    }

    [Test]
    public void OpenedChestGetsItsStateAfterTheAdd() {
        Assert.That(MapChestRules.NeedsStateAfterAdd(MapChestRules.Kind.Wood, InteractState.Normal), Is.True);
        Assert.That(MapChestRules.NeedsStateAfterAdd(MapChestRules.Kind.Gold, InteractState.Reactable), Is.False);
        Assert.That(MapChestRules.NeedsStateAfterAdd(MapChestRules.Kind.None, InteractState.Normal), Is.False);
    }

    [Test]
    public void EntityNameCutsToTheBlock() {
        // KMS2: the chest on the point at (1230, -2220, 1345) is EventCreate_586248, block (8, -14, 8).
        Assert.That(MapChestRules.EntityName(1230, -2220, 1345), Is.EqualTo("EventCreate_586248"));
        Assert.That(MapChestRules.EntityName(4490, 2390, 4505), Is.EqualTo("EventCreate_1969949"));
        Assert.That(MapChestRules.EntityName(-460, 4790, 2550), Is.EqualTo("EventCreate_1122301"));
        Assert.That(MapChestRules.EntityName(-2400, 3040, 1650), Is.EqualTo("EventCreate_726256"));
    }

    [Test]
    public void GoldPointsOfMapsNumberedFrom90000() {
        Assert.That(MapChestRules.SpawnKind(93000), Is.EqualTo(MapChestRules.Kind.Gold));
        Assert.That(MapChestRules.SpawnKind(92000), Is.EqualTo(MapChestRules.Kind.None));
        Assert.That(MapChestRules.SpawnKind(9000), Is.EqualTo(MapChestRules.Kind.None));
        Dictionary<int, int> chests = MapChestRules.GoldChests([92000, 93001, 93000, 94000], [14000426, 14000427]);
        Assert.That(chests, Is.EquivalentTo(new Dictionary<int, int> { [93000] = 14000426, [93001] = 14000427 }));
    }

    [Test]
    public void GoldChestsFollowPointOrder() {
        // Point ids with a gap: the n-th point holds the n-th id (KMS2: points 3000 and 3002 hold 14000041 and 42).
        Dictionary<int, int> chests = MapChestRules.GoldChests([3002, 3000, 2000], [14000041, 14000042]);
        Assert.That(chests, Is.EquivalentTo(new Dictionary<int, int> { [3000] = 14000041, [3002] = 14000042 }));
        // More points than ids: the points past the end hold none. No ids: none.
        Assert.That(MapChestRules.GoldChests([3000, 3001, 3002], [9]), Is.EquivalentTo(new Dictionary<int, int> { [3000] = 9 }));
        Assert.That(MapChestRules.GoldChests([3000, 3001], []), Is.Empty);
    }

    [TestCase(MapChestRules.Kind.Wood, false, ExpType.normalChest, 1091)]
    [TestCase(MapChestRules.Kind.Wood, true, ExpType.normalChest, 1091)]
    [TestCase(MapChestRules.Kind.Gold, true, ExpType.rareChestFirst, 1093)]
    [TestCase(MapChestRules.Kind.Gold, false, ExpType.rareChest, 1092)]
    public void ExpAndReason(MapChestRules.Kind kind, bool firstOpen, ExpType type, int reason) {
        Assert.That(MapChestRules.Exp(kind, firstOpen), Is.EqualTo(type));
        Assert.That((int) type.Message(), Is.EqualTo(reason));
        // The chest exp is taken at the field's level.
        Assert.That(CommonExpRules.Level(type), Is.EqualTo(CommonExpRules.LevelSource.Field));
    }

    [Test]
    public void NoChestNoExp() {
        Assert.That(MapChestRules.Exp(MapChestRules.Kind.None, true), Is.EqualTo(ExpType.none));
    }
}
