using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// A special attribute's total is the plain sum of its lines, as the client sums them, so a negative line (a boss
// shield's weak element, a debuff's damage rate) counts in full whatever order the lines are added in. A basic stat
// still stops at 0.
public class SignedSpecialStatTests {
    [Test]
    public void ASpecialTotalGoesBelowZeroInAnyOrder() {
        var first = new Stats(new Dictionary<BasicAttribute, long> { { BasicAttribute.Health, 1000 } }, JobCode.Newbie);
        first[SpecialAttribute.ReduceFireDamage].AddTotal(-5000);
        first[SpecialAttribute.ReduceFireDamage].AddTotal(3000);
        var second = new Stats(new Dictionary<BasicAttribute, long> { { BasicAttribute.Health, 1000 } }, JobCode.Newbie);
        second[SpecialAttribute.ReduceFireDamage].AddTotal(3000);
        second[SpecialAttribute.ReduceFireDamage].AddTotal(-5000);
        Assert.That(first[SpecialAttribute.ReduceFireDamage].Total, Is.EqualTo(-2000));
        Assert.That(second[SpecialAttribute.ReduceFireDamage].Total, Is.EqualTo(-2000));
        Assert.That(first[SpecialAttribute.ReduceFireDamage].Multiplier(), Is.EqualTo(-2).Within(1e-9));

        // A basic stat stops at 0.
        first[BasicAttribute.PhysicalRes].AddTotal(-first[BasicAttribute.PhysicalRes].Total - 50);
        Assert.That(first[BasicAttribute.PhysicalRes].Total, Is.EqualTo(0));
    }

    [Test]
    public void ARebuildWithANegativeSpecialTotalKeepsIt() {
        var stats = new Stats(new Dictionary<BasicAttribute, long> { { BasicAttribute.Health, 1000 } }, JobCode.Newbie);
        stats[SpecialAttribute.OffensivePhysicalDamage].AddTotal(-1000);
        stats.Reset(new Dictionary<BasicAttribute, long> { { BasicAttribute.Health, 1000 } });
        stats[SpecialAttribute.OffensivePhysicalDamage].AddTotal(-1000);
        Assert.DoesNotThrow(() => stats.EndReset(false));
        Assert.That(stats[SpecialAttribute.OffensivePhysicalDamage].Total, Is.EqualTo(-1000));
        Assert.That(stats[SpecialAttribute.OffensivePhysicalDamage].Current, Is.EqualTo(-1000));

        // A rebuild clears it like any other line.
        stats.Reset(new Dictionary<BasicAttribute, long> { { BasicAttribute.Health, 1000 } });
        stats.EndReset(false);
        Assert.That(stats[SpecialAttribute.OffensivePhysicalDamage].Total, Is.EqualTo(0));
    }

    [Test]
    public void ADamageRateOfMinusOneOrBelowLeavesNothing() {
        Assert.That(DamageCalculator.DamageRateFactor(0.021), Is.EqualTo(1.021).Within(1e-9));
        Assert.That(DamageCalculator.DamageRateFactor(-0.08), Is.EqualTo(0.92).Within(1e-9));
        Assert.That(DamageCalculator.DamageRateFactor(-1), Is.EqualTo(0));
        Assert.That(DamageCalculator.DamageRateFactor(-2), Is.EqualTo(0));
    }

    [Test]
    public void AShieldsWeakElementTakesSixTimesTheHit() {
        var test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        FieldPlayer player = test.Session.Player;
        Stats stats = player.Stats.Values;
        stats[BasicAttribute.MinWeaponAtk].AddTotal(1000 - stats[BasicAttribute.MinWeaponAtk].Total);
        stats[BasicAttribute.MaxWeaponAtk].AddTotal(1000 - stats[BasicAttribute.MaxWeaponAtk].Total);
        stats[BasicAttribute.PhysicalAtk].AddTotal(1000);

        DamagePropertyRecord Hit(Element element) => new() {
            CanCrit = false, Element = element, RangeType = RangeType.Melee, AttackType = AttackType.Physical,
            Rate = 1, SuperArmorBreak = true,
        };
        (_, long fire) = DamageCalculator.CalculateDamage(player, player, Hit(Element.Fire));
        (_, long ice) = DamageCalculator.CalculateDamage(player, player, Hit(Element.Ice));
        Assert.That(fire, Is.GreaterThan(1000));

        // 50006000: every element 0.99 but fire, -5.
        stats[SpecialAttribute.ReduceFireDamage].AddTotal(-5000);
        stats[SpecialAttribute.ReduceIceDamage].AddTotal(990);
        (_, long weak) = DamageCalculator.CalculateDamage(player, player, Hit(Element.Fire));
        (_, long guarded) = DamageCalculator.CalculateDamage(player, player, Hit(Element.Ice));
        Assert.That(weak, Is.EqualTo(fire * 6).Within(6));
        Assert.That(guarded, Is.EqualTo(ice * 0.01).Within(1));

        // A -1 damage rate on the caster leaves the least hit.
        stats[SpecialAttribute.OffensivePhysicalDamage].AddTotal(-1000);
        (_, long none) = DamageCalculator.CalculateDamage(player, player, Hit(Element.Fire));
        Assert.That(none, Is.EqualTo(1));
    }
}
