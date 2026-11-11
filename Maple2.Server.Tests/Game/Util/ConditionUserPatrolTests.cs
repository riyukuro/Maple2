using System.Collections.Generic;
using System.Linq;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// 02020310_bf field_5: patrol_condition_user MS2PatrolData_01..04 with 73000005..73000008, one per route buff.
public class ConditionUserPatrolTests {
    [Test]
    public void OnlyTheRouteBuffMoves() {
        var routes = new Dictionary<string, int> { ["a"] = 73000005, ["b"] = 73000006, ["c"] = 73000008 };
        string[] moved = routes.Where(p => ConditionUserPatrol.Moves(73000006, id => id == p.Value)).Select(p => p.Key).ToArray();
        Assert.That(moved, Is.EqualTo(new[] { "b" }));
    }

    [Test]
    public void NoBuffStays() {
        Assert.That(ConditionUserPatrol.Moves(73000005, _ => false), Is.False);
    }

    [Test]
    public void NoEffectIdMovesEveryone() {
        Assert.Multiple(() => {
            Assert.That(ConditionUserPatrol.Moves(0, _ => false), Is.True);
            Assert.That(ConditionUserPatrol.Moves(-1, _ => false), Is.True);
        });
    }
}
