using Maple2.Model.Enum;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// The PvP Damage and PvP Defense lines are flat points (the client shows them as whole numbers, no percent), and no
// source names how points turn into damage, so a hit between players is left as it is.
public class PvpDamageTests {
    private static FieldPlayer Player() {
        var test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        FieldPlayer player = test.Session.Player;
        // Even weapon attack: no roll between min and max, so two hits compare exactly.
        Stats stats = player.Stats.Values;
        stats[BasicAttribute.MinWeaponAtk].AddTotal(1000 - stats[BasicAttribute.MinWeaponAtk].Total);
        stats[BasicAttribute.MaxWeaponAtk].AddTotal(1000 - stats[BasicAttribute.MaxWeaponAtk].Total);
        stats[BasicAttribute.PhysicalAtk].AddTotal(1000);
        return player;
    }

    [Test]
    public void ThePvpLinesLeaveAHitBetweenPlayersAsItIs() {
        FieldPlayer caster = Player();
        FieldPlayer target = Player();
        // No crit and a hit that cannot miss.
        var hit = new DamagePropertyRecord {
            CanCrit = false, Element = Element.None, RangeType = RangeType.None, AttackType = AttackType.Physical,
            Rate = 1, SuperArmorBreak = true,
        };
        (_, long full) = DamageCalculator.CalculateDamage(caster, target, hit);
        Assert.That(full, Is.GreaterThan(100));

        // The largest lines in the KR data: 84 PvP Damage, 189 PvP Defense.
        caster.Stats.Values[SpecialAttribute.PvpDamage].AddTotal(84);
        target.Stats.Values[SpecialAttribute.ReducePvpDamage].AddTotal(189);
        (_, long lined) = DamageCalculator.CalculateDamage(caster, target, hit);
        Assert.That(lined, Is.EqualTo(full));
    }
}
