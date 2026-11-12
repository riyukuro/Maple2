using Maple2.Model.Enum;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// A hit's damage is cut by the target's reduction of its range type (Melee Reduction, Ranged Damage Reduction) and
// of its element (Fire ... Poison Damage Reduction), the item option and buff lines that did nothing before.
public class DamageReductionTests {
    [Test]
    public void EachRangeTypeAndElementHasItsReduction() {
        Assert.That(DamageCalculator.RangeReductionOf(RangeType.Melee), Is.EqualTo(SpecialAttribute.ReduceMeleeDamage));
        Assert.That(DamageCalculator.RangeReductionOf(RangeType.Range), Is.EqualTo(SpecialAttribute.ReduceRangedDamage));
        Assert.That(DamageCalculator.RangeReductionOf(RangeType.None), Is.Null);
        Assert.That(DamageCalculator.ElementReductionOf(Element.Fire), Is.EqualTo(SpecialAttribute.ReduceFireDamage));
        Assert.That(DamageCalculator.ElementReductionOf(Element.Ice), Is.EqualTo(SpecialAttribute.ReduceIceDamage));
        Assert.That(DamageCalculator.ElementReductionOf(Element.Electric), Is.EqualTo(SpecialAttribute.ReduceElectricDamage));
        Assert.That(DamageCalculator.ElementReductionOf(Element.Holy), Is.EqualTo(SpecialAttribute.ReduceHolyDamage));
        Assert.That(DamageCalculator.ElementReductionOf(Element.Dark), Is.EqualTo(SpecialAttribute.ReduceDarkDamage));
        Assert.That(DamageCalculator.ElementReductionOf(Element.Poison), Is.EqualTo(SpecialAttribute.ReducePoisonDamage));
        Assert.That(DamageCalculator.ElementReductionOf(Element.Physical), Is.Null);
        Assert.That(DamageCalculator.ElementReductionOf(Element.None), Is.Null);
    }

    [Test]
    public void ReductionsTakeTheirPartOfWhatIsLeft() {
        Assert.That(DamageCalculator.ReductionFactor(0, 0), Is.EqualTo(1));
        Assert.That(DamageCalculator.ReductionFactor(0.022, 0), Is.EqualTo(0.978).Within(1e-9));
        Assert.That(DamageCalculator.ReductionFactor(0.2, 0.5), Is.EqualTo(0.4).Within(1e-9));
        // 100 % or more takes the whole hit; a negative rate adds to it (a boss shield's weak element, -5: 6 times).
        Assert.That(DamageCalculator.ReductionFactor(1.5, 0), Is.EqualTo(0));
        Assert.That(DamageCalculator.ReductionFactor(-0.3, 0), Is.EqualTo(1.3).Within(1e-9));
        Assert.That(DamageCalculator.ReductionFactor(0, -5), Is.EqualTo(6).Within(1e-9));
    }

    [Test]
    public void AHitOnAHolderOfAReductionIsCut() {
        var test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        FieldPlayer player = test.Session.Player;
        // Even weapon attack: no roll between min and max, so two hits compare exactly.
        Stats stats = player.Stats.Values;
        stats[BasicAttribute.MinWeaponAtk].AddTotal(1000 - stats[BasicAttribute.MinWeaponAtk].Total);
        stats[BasicAttribute.MaxWeaponAtk].AddTotal(1000 - stats[BasicAttribute.MaxWeaponAtk].Total);
        stats[BasicAttribute.PhysicalAtk].AddTotal(1000);

        // No crit and a hit that cannot miss.
        var fireMelee = new DamagePropertyRecord {
            CanCrit = false, Element = Element.Fire, RangeType = RangeType.Melee, AttackType = AttackType.Physical,
            Rate = 1, SuperArmorBreak = true,
        };
        (DamageType type, long full) = DamageCalculator.CalculateDamage(player, player, fireMelee);
        Assert.That(type, Is.EqualTo(DamageType.Normal));
        Assert.That(full, Is.GreaterThan(100));

        // 20 % melee and 50 % fire reduction (thousandths, as an item's special rate is kept).
        stats[SpecialAttribute.ReduceMeleeDamage].AddTotal(200);
        stats[SpecialAttribute.ReduceFireDamage].AddTotal(500);
        (_, long cut) = DamageCalculator.CalculateDamage(player, player, fireMelee);
        Assert.That(cut, Is.EqualTo(full * 0.4).Within(1));

        // A ranged ice hit meets neither reduction.
        var iceRanged = new DamagePropertyRecord {
            CanCrit = false, Element = Element.Ice, RangeType = RangeType.Range, AttackType = AttackType.Physical,
            Rate = 1, SuperArmorBreak = true,
        };
        (_, long other) = DamageCalculator.CalculateDamage(player, player, iceRanged);
        Assert.That(other, Is.EqualTo(full).Within(1));
    }
}
