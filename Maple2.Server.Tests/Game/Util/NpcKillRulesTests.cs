using System;
using Maple2.Model.Metadata;
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

    // 21210003 counts npc_race "animal": the code comes from raceString, not from the npc's mainTags.
    [Test]
    public void RaceCodes_AreTheRaceStrings() {
        var basic = new NpcMetadataBasic(0, 0, 0, 0, 0, 0, 0, 1, 0, false, 0, 0, 0, 0, ["\uace0\uc591\uc774"], [], 0,
            RaceStrings: ["animal"]);
        Assert.That(NpcKillRules.RaceCodes(basic), Is.EqualTo(new[] { "animal" }));
    }

    [Test]
    public void RaceCodes_EmptyForAnOldRow() {
        var basic = new NpcMetadataBasic(0, 0, 0, 0, 0, 0, 0, 1, 0, false, 0, 0, 0, 0, ["x"], [], 0);
        Assert.That(NpcKillRules.RaceCodes(basic), Is.Empty);
    }
}
