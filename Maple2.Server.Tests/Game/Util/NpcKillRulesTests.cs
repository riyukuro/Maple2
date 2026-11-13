using System;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class NpcKillRulesTests {
    [Test]
    public void LastHitTime_IsHoursAndMinutes() {
        Assert.That(NpcKillRules.LastHitTime(new DateTime(2026, 11, 12, 23, 59, 41)), Is.EqualTo("23:59"));
        Assert.That(NpcKillRules.LastHitTime(new DateTime(2026, 11, 12, 7, 5, 0)), Is.EqualTo("07:05"));
    }

    [TestCase(0, 500_000, null)]
    [TestCase(10_000, 5_000, null)]
    [TestCase(10_000, 490_999, 480L)]
    [TestCase(10_000, 491_000, 481L)]
    public void TimeAttackSeconds_CountsFromTheRunStart(long startTick, long now, long? expected) {
        Assert.That(NpcKillRules.TimeAttackSeconds(startTick, now), Is.EqualTo(expected));
    }
}
