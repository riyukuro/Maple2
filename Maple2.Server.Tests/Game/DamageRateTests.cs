using Maple2.Model.Enum;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// A buff's physical or magical damage rate (OffensiveProperty papDamageR and mapDamageR, and the invoke bonuses on
// them) raises the holder's hits by that rate: the effect tooltips read 0.021 as "damage dealt to enemies +2.1 %".
// The hit was multiplied by the rate itself before, so a +8 % buff left 8 % of the hit.
public class DamageRateTests {
    private static readonly DamagePropertyRecord Physical = new() {
        CanCrit = false, AttackType = AttackType.Physical, Rate = 1, SuperArmorBreak = true,
    };
    private static readonly DamagePropertyRecord Magic = new() {
        CanCrit = false, AttackType = AttackType.Magic, Rate = 1, SuperArmorBreak = true,
    };
    private static readonly DamagePropertyRecord Either = new() {
        CanCrit = false, AttackType = AttackType.All, Rate = 1, SuperArmorBreak = true,
    };

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
        stats[BasicAttribute.MagicalAtk].AddTotal(1000);
        return player;
    }

    private static long Hit(FieldPlayer player, DamagePropertyRecord property) => DamageCalculator.CalculateDamage(player, player, property).Item2;

    [Test]
    public void ARateRaisesTheHitsOfItsAttackType() {
        FieldPlayer player = Player();
        Stats stats = player.Stats.Values;
        long physical = Hit(player, Physical);
        long magic = Hit(player, Magic);
        Assert.That(physical, Is.GreaterThan(1000));
        Assert.That(magic, Is.GreaterThan(1000));

        // +8 % physical (thousandths, as a buff's special rate is kept): physical hits rise, magic hits stay.
        stats[SpecialAttribute.OffensivePhysicalDamage].AddTotal(80);
        Assert.That(Hit(player, Physical), Is.EqualTo(physical * 1.08).Within(1));
        Assert.That(Hit(player, Magic), Is.EqualTo(magic).Within(1));

        // +12 % magical.
        stats[SpecialAttribute.OffensiveMagicalDamage].AddTotal(120);
        Assert.That(Hit(player, Magic), Is.EqualTo(magic * 1.12).Within(1));
    }

    [Test]
    public void TheFactorIsOnePlusTheRate() {
        Assert.That(DamageCalculator.DamageRateFactor(0), Is.EqualTo(1));
        Assert.That(DamageCalculator.DamageRateFactor(0.08), Is.EqualTo(1.08).Within(1e-9));
    }

    [Test]
    public void AHitOfEitherTypeTakesTheRateOfTheHigherAttack() {
        FieldPlayer player = Player();
        Stats stats = player.Stats.Values;
        long either = Hit(player, Either);
        // Physical attack is not below magical, so the physical rate counts.
        stats[SpecialAttribute.OffensiveMagicalDamage].AddTotal(500);
        Assert.That(Hit(player, Either), Is.EqualTo(either).Within(1));
        stats[SpecialAttribute.OffensivePhysicalDamage].AddTotal(100);
        Assert.That(Hit(player, Either), Is.EqualTo(either * 1.1).Within(1));
    }
}
