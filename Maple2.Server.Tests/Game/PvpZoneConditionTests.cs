using System.Collections.Generic;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// applyPvPZoneType: 1 holds outside PvP zones (maps of type Pvp), 2 only in them, 0 anywhere. 10100151 (damage rate
// 0.25, type 1) and 10100152 (0.125, type 2) are one passive's PvE and PvP halves.
public class PvpZoneConditionTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [TestCase(PvpZoneApply.Everywhere, MapType.None, true)]
    [TestCase(PvpZoneApply.Everywhere, MapType.Pvp, true)]
    [TestCase(PvpZoneApply.OutsidePvp, MapType.None, true)]
    [TestCase(PvpZoneApply.OutsidePvp, MapType.Dungeon, true)]
    [TestCase(PvpZoneApply.OutsidePvp, MapType.Pvp, false)]
    [TestCase(PvpZoneApply.InsidePvp, MapType.None, false)]
    [TestCase(PvpZoneApply.InsidePvp, MapType.SurvivalSolo, false)]
    [TestCase(PvpZoneApply.InsidePvp, MapType.Pvp, true)]
    public void PvpZoneHolds_ByTheMapType(PvpZoneApply apply, MapType type, bool holds) {
        Assert.That(SkillUtils.PvpZoneHolds(apply, type), Is.EqualTo(holds));
    }

    private static FieldPlayer Enter(MapType type) {
        var test = new TestGameSession();
        test.Start();
        MapMetadata map = TestGameSession.Map(2000001);
        test.EnterMap(map with { Property = map.Property with { Type = type } });
        FieldPlayer player = test.Session.Player;
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(player, test.Session.Field);
        return player;
    }

    // An effect condition with only the zone type (battle mounts allowed: a test session has no ride).
    private static BeginCondition Condition(PvpZoneApply apply) {
        return new BeginCondition(0, 0, Gender.All, [], 1, 0, 0, 0, false, false, false, true, false, true, [],
            new Dictionary<BasicAttribute, long>(), [], [], [], [], null, null, null, null, PvpZone: apply);
    }

    // On a field map only the PvE half holds, in an arena only the PvP half; before, both held everywhere.
    [Test]
    public void Condition_PairSplitsByTheMap() {
        FieldPlayer field = Enter(MapType.None);
        FieldPlayer arena = Enter(MapType.Pvp);
        Assert.Multiple(() => {
            Assert.That(Condition(PvpZoneApply.OutsidePvp).Check(field, field, field), Is.True);
            Assert.That(Condition(PvpZoneApply.InsidePvp).Check(field, field, field), Is.False);
            Assert.That(Condition(PvpZoneApply.OutsidePvp).Check(arena, arena, arena), Is.False);
            Assert.That(Condition(PvpZoneApply.InsidePvp).Check(arena, arena, arena), Is.True);
            Assert.That(Condition(PvpZoneApply.Everywhere).Check(arena, arena, arena), Is.True);
        });
    }
}
