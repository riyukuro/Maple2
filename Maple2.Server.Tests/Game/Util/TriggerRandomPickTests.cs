using System;
using System.Linq;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// spawn_npc_range random_pick_count: N different ids of the range, or the whole range without it.
public class TriggerRandomPickTests {
    // The vip dungeon round: range_ids="1101-1164" random_pick_count="3".
    [Test]
    public void PicksThreeOfTheRange() {
        int[] range = TriggerFunctionMapping.ParseIntArray("1101-1164");
        int[] picked = TriggerRandomPick.Pick(range, 3, new Random(7));
        Assert.Multiple(() => {
            Assert.That(range, Has.Length.EqualTo(64));
            Assert.That(picked, Has.Length.EqualTo(3));
            Assert.That(picked, Is.Unique);
            Assert.That(picked, Is.SubsetOf(range));
        });
    }

    // The parsed array is kept by the action and used again on the next spawn.
    [Test]
    public void TheActionsIdsStayInOrder() {
        int[] range = TriggerFunctionMapping.ParseIntArray("1901-1909");
        TriggerRandomPick.Pick(range, 3, new Random(1));
        Assert.That(range, Is.EqualTo(Enumerable.Range(1901, 9).ToArray()));
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(9)]
    [TestCase(12)]
    public void NoPickOrAPickOfAllSpawnsTheWholeRange(int count) {
        int[] range = TriggerFunctionMapping.ParseIntArray("1901-1909");
        Assert.That(TriggerRandomPick.Pick(range, count, new Random(1)), Is.EqualTo(range));
    }

    // Over many spawns every id of the range is picked, not only the first ones.
    [Test]
    public void EveryIdCanBePicked() {
        int[] range = TriggerFunctionMapping.ParseIntArray("2001-2004");
        var random = new Random(3);
        int[] seen = Enumerable.Range(0, 200).Select(_ => TriggerRandomPick.Pick(range, 1, random).Single()).Distinct().Order().ToArray();
        Assert.That(seen, Is.EqualTo(range));
    }
}
