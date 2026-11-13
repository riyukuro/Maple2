using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// clearCooldownFromPVPZone: entering a map of type Pvp (arenas and battlefields) ends the cooldown of each flagged
// skill or effect; other cooldowns run on, and no cooldown ends on other maps. 10000001 and 10100001 are flagged
// job skills in the data.
public class PvpCooldownClearTests {
    private static readonly HashSet<int> Flagged = [10000001, 10100001];
    private static readonly int[] Cooldowns = [10000001, 10000002, 10100001];

    [Test]
    public void Pvp_EndsTheFlaggedCooldowns() {
        Assert.That(SkillCastRules.PvpZoneClears(MapType.Pvp, Cooldowns, Flagged.Contains), Is.EqualTo(new[] { 10000001, 10100001 }));
    }

    [TestCase(MapType.None)]
    [TestCase(MapType.Dungeon)]
    [TestCase(MapType.SurvivalSolo)]
    public void OtherMaps_EndNone(MapType type) {
        Assert.That(SkillCastRules.PvpZoneClears(type, Cooldowns, Flagged.Contains), Is.Empty);
    }

    [Test]
    public void Pvp_KeepsTheUnflagged() {
        Assert.That(SkillCastRules.PvpZoneClears(MapType.Pvp, [10000002, 20000001], Flagged.Contains), Is.Empty);
    }
}
