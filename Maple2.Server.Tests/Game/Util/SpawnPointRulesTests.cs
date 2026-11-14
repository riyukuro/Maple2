using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// A plain spawn point's NpcCount caps its npcs up at once; each list entry's count still caps its npc. The list is
// 02020051's: 23001008/09/10 at 5/3/2 with NpcCount 5.
public class SpawnPointRulesTests {
    private static SpawnPointNPC Point(int npcCount, params (int Id, int Count)[] list) =>
        new("p", 1, Vector3.Zero, Vector3.Zero, true, true, 0, list.Select(e => new SpawnPointNPCListEntry(e.Id, e.Count)).ToList(), 10, null, npcCount);

    private static readonly (int, int)[] List = [(23001008, 5), (23001009, 3), (23001010, 2)];

    [TestCase(5, ExpectedResult = 5)]
    [TestCase(10, ExpectedResult = 10)]
    [TestCase(3, ExpectedResult = 3)]
    [TestCase(15, ExpectedResult = 10)] // above the list total: the list
    [TestCase(0, ExpectedResult = 10)] // a row ingested before NpcCount
    public int PlainCap_IsNpcCountUpToTheListTotal(int npcCount) => SpawnPointRules.PlainCap(Point(npcCount, List));

    [Test]
    public void PickNpcs_FillsUpToNpcCountWithinEachEntrysCount() {
        SpawnPointNPC point = Point(5, List);
        for (int seed = 0; seed < 50; seed++) {
            List<int> picked = SpawnPointRules.PickNpcs(point, [], _ => true, new Random(seed));
            Assert.That(picked, Has.Count.EqualTo(5));
            Assert.That(picked.Count(id => id == 23001009), Is.LessThanOrEqualTo(3));
            Assert.That(picked.Count(id => id == 23001010), Is.LessThanOrEqualTo(2));
        }
    }

    [Test]
    public void PickNpcs_RegenOnlyRefillsTheOpenSlots() {
        SpawnPointNPC point = Point(5, List);
        // Four up, two of them 23001010 (its whole count): one slot open, never another 23001010.
        int[] alive = [23001008, 23001008, 23001010, 23001010];
        for (int seed = 0; seed < 50; seed++) {
            List<int> picked = SpawnPointRules.PickNpcs(point, alive, _ => true, new Random(seed));
            Assert.That(picked, Has.Count.EqualTo(1));
            Assert.That(picked[0], Is.Not.EqualTo(23001010));
        }
        Assert.That(SpawnPointRules.PickNpcs(point, [.. alive, 23001009], _ => true, new Random(0)), Is.Empty);
    }

    [Test]
    public void PickNpcs_AForeignNpcOnTheSpawnIdHoldsNoSlot() {
        // Summons, pets and QuestMask partner points all sit at spawn id 0, and plain points can share an id: an
        // alive npc that is not on this point's list must not take one of its slots.
        SpawnPointNPC partner = Point(1, (11003146, 1));
        Assert.That(SpawnPointRules.PickNpcs(partner, [11000001], _ => true, new Random(0)), Is.EqualTo(new[] { 11003146 }));
        Assert.That(SpawnPointRules.PickNpcs(partner, [11000001, 11003146], _ => true, new Random(0)), Is.Empty);

        SpawnPointNPC point = Point(5, List);
        int[] alive = [23001008, 23001008, 23001010, 99999999, 99999999, 99999999];
        Assert.That(SpawnPointRules.PickNpcs(point, alive, _ => true, new Random(0)), Has.Count.EqualTo(2));
    }

    [Test]
    public void PickNpcs_ACountAtOrAboveTheListSpawnsTheListInOrder() {
        Assert.That(SpawnPointRules.PickNpcs(Point(3, (21000059, 1)), [], _ => true, new Random(0)), Is.EqualTo(new[] { 21000059 }));
        Assert.That(SpawnPointRules.PickNpcs(Point(0, (1, 2), (2, 1)), [], _ => true, new Random(0)), Is.EqualTo(new[] { 1, 1, 2 }));
    }

    [Test]
    public void PickNpcs_AnUnknownNpcTakesNoSlot() {
        SpawnPointNPC point = Point(2, (1, 2), (2, 2));
        List<int> picked = SpawnPointRules.PickNpcs(point, [], id => id != 1, new Random(0));
        Assert.That(picked, Is.EqualTo(new[] { 2, 2 }));
    }
}
