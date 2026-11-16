using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// The branches of DamageCalculator.CalculateDamage that no hit took before: the element bonuses past fire and ice,
// the attack type All, and True Evasion. A player hits itself with an even weapon attack, so the weapon roll is the
// same each time and two hits compare exactly.
public class DamageBranchTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private TestGameSession test = null!;
    private FieldPlayer Player => test.Session.Player;
    private Stats Stats => Player.Stats.Values;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(Player, test.Session.Field);
        Stats[BasicAttribute.MinWeaponAtk].AddTotal(1000 - Stats[BasicAttribute.MinWeaponAtk].Total);
        Stats[BasicAttribute.MaxWeaponAtk].AddTotal(1000 - Stats[BasicAttribute.MaxWeaponAtk].Total);
        Stats[BasicAttribute.PhysicalAtk].AddTotal(1000);
        Stats[BasicAttribute.MagicalAtk].AddTotal(1000);
    }

    private static DamagePropertyRecord Hit(AttackType type = AttackType.Physical, Element element = Element.None, bool superArmorBreak = true) => new() {
        CanCrit = false, AttackType = type, Element = element, Rate = 1, SuperArmorBreak = superArmorBreak,
    };

    private long Damage(DamagePropertyRecord hit) {
        (DamageType type, long amount) = DamageCalculator.CalculateDamage(Player, Player, hit);
        Assert.That(type, Is.EqualTo(DamageType.Normal));
        return amount;
    }

    // Each element's damage rate (special attributes 27 to 32, in thousandths) adds to the hit's damage bonus of 1;
    // 1000 doubles a hit of that element and leaves the hits of every other element alone.
    [TestCase(Element.Fire, SpecialAttribute.FireDamage, Element.Ice)]
    [TestCase(Element.Ice, SpecialAttribute.IceDamage, Element.Fire)]
    [TestCase(Element.Electric, SpecialAttribute.ElectricDamage, Element.Holy)]
    [TestCase(Element.Holy, SpecialAttribute.HolyDamage, Element.Dark)]
    [TestCase(Element.Dark, SpecialAttribute.DarkDamage, Element.Poison)]
    [TestCase(Element.Poison, SpecialAttribute.PoisonDamage, Element.Electric)]
    public void AnElementsDamageRateAddsToItsOwnHits(Element element, SpecialAttribute rate, Element other) {
        long plain = Damage(Hit(element: element));
        long otherPlain = Damage(Hit(element: other));
        Assert.That(plain, Is.GreaterThan(1000));

        Stats[rate].AddTotal(1000);
        Assert.That(Damage(Hit(element: element)), Is.EqualTo(plain * 2).Within(2));
        Assert.That(Damage(Hit(element: other)), Is.EqualTo(otherPlain));
    }

    // The physical and magical piercing rates (parpen, marpen; special attributes 26, 27, in thousandths) take that
    // part of 1500 off the target's resistance of their own type: resistance 600 keeps 0.6 of a hit, piercing 200 takes
    // 300 off and keeps 0.8, piercing 1000 takes all of it off and keeps the whole hit (never more). The other type's
    // hits stay as they were.
    [TestCase(AttackType.Physical, SpecialAttribute.PhysicalPiercing, AttackType.Magic)]
    [TestCase(AttackType.Magic, SpecialAttribute.MagicalPiercing, AttackType.Physical)]
    public void PiercingTakesItsPartOffTheResistanceOfItsType(AttackType type, SpecialAttribute piercing, AttackType other) {
        Stats[BasicAttribute.PhysicalRes].AddTotal(600 - Stats[BasicAttribute.PhysicalRes].Total);
        Stats[BasicAttribute.MagicalRes].AddTotal(600 - Stats[BasicAttribute.MagicalRes].Total);
        long resisted = Damage(Hit(type));
        long otherResisted = Damage(Hit(other));

        Stats[piercing].AddTotal(200);
        Assert.That(Damage(Hit(type)), Is.EqualTo(resisted * 0.8 / 0.6).Within(2));
        Assert.That(Damage(Hit(other)), Is.EqualTo(otherResisted));

        Stats[piercing].AddTotal(800);
        Assert.That(Damage(Hit(type)), Is.EqualTo(resisted / 0.6).Within(2));
        Stats[piercing].AddTotal(1000);
        Assert.That(Damage(Hit(type)), Is.EqualTo(resisted / 0.6).Within(2));
    }

    // Attack type All (both): half the higher of the two attacks, against the resistance and with the damage rate of
    // that attack's own type. Physical resistance 500 makes the two types' hits differ.
    [TestCase(2000, 1000, AttackType.Physical)]
    [TestCase(1000, 2000, AttackType.Magic)]
    [TestCase(1500, 1500, AttackType.Physical)]
    public void AttackTypeAllTakesHalfOfTheHigherAttack(long physicalAtk, long magicalAtk, AttackType higher) {
        Stats[BasicAttribute.PhysicalAtk].AddTotal(physicalAtk - Stats[BasicAttribute.PhysicalAtk].Total);
        Stats[BasicAttribute.MagicalAtk].AddTotal(magicalAtk - Stats[BasicAttribute.MagicalAtk].Total);
        Stats[BasicAttribute.PhysicalRes].AddTotal(500);
        Stats[SpecialAttribute.OffensivePhysicalDamage].AddTotal(100);
        Stats[SpecialAttribute.OffensiveMagicalDamage].AddTotal(300);
        long physical = Damage(Hit(AttackType.Physical));
        long magic = Damage(Hit(AttackType.Magic));
        Assume.That(physical, Is.Not.EqualTo(magic).Within(4));

        long both = Damage(Hit(AttackType.All));
        Assert.That(both, Is.EqualTo((higher == AttackType.Physical ? physical : magic) / 2.0).Within(1));
    }

    // True Evasion (special ability 204, in thousandths): 1000 evades every hit the accuracy roll is made for, as a
    // Miss of 0, but not a super armor break, and not the hits of the field's own actor.
    [Test]
    public void TrueEvasionMissesAllButASuperArmorBreak() {
        Stats[SpecialAttribute.TrueEvasion].AddTotal(1000);
        for (int i = 0; i < 20; i++) {
            Assert.That(DamageCalculator.CalculateDamage(Player, Player, Hit(superArmorBreak: false)), Is.EqualTo((DamageType.Miss, 0L)));
        }
        Assert.That(Damage(Hit(superArmorBreak: true)), Is.GreaterThan(1000));

        FieldManager field = test.Session.Field!;
        var actor = new FieldActor(field, 99, field.Metadata, null!);
        Assert.That(DamageCalculator.HitRoll(actor, Player, Hit(superArmorBreak: false)), Is.Null);
    }
}
