using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// A buff's damage taken rates (DefensiveProperty papDamageR and mapDamageR) take that share of the physical or
// magical hits on its holder away: the effect tooltips read 0.99 as "damage taken from enemies -99 %" and 1 as "blocks
// physical damage"; a negative rate adds to the hit. Ingest dropped the property before, so nothing read them.
public class DamageTakenTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly DamagePropertyRecord Physical = new() {
        CanCrit = false, AttackType = AttackType.Physical, Rate = 1, SuperArmorBreak = true,
    };
    private static readonly DamagePropertyRecord Magic = new() {
        CanCrit = false, AttackType = AttackType.Magic, Rate = 1, SuperArmorBreak = true,
    };

    private static AdditionalEffectMetadata Effect(int id, float physical, float magical) {
        AdditionalEffectMetadata effect = DisabledBuffTests.Effect(id);
        return effect with { Status = effect.Status with { PhysicalDamageTakenRate = physical, MagicalDamageTakenRate = magical } };
    }

    [Test]
    public void TheRateTakesItsShareAway() {
        Assert.That(DamageCalculator.DamageTakenFactor(0), Is.EqualTo(1));
        Assert.That(DamageCalculator.DamageTakenFactor(0.99), Is.EqualTo(0.01).Within(1e-9));
        Assert.That(DamageCalculator.DamageTakenFactor(-0.5), Is.EqualTo(1.5).Within(1e-9));
        // 1 or more takes the whole hit.
        Assert.That(DamageCalculator.DamageTakenFactor(1), Is.EqualTo(0));
        Assert.That(DamageCalculator.DamageTakenFactor(2), Is.EqualTo(0));
    }

    [Test]
    public void TheRatesOfTheBuffsHeldAddUpByStacks() {
        var sums = new BuffSums([
            (Effect(10100151, 0.25f, 0.25f), 1),
            (Effect(50000306, -0.6f, 0), 1),
            (Effect(50000317, 0, 0.1f), 3),
            (Effect(50000313, 0.99f, 0.99f), 0),
        ]);
        Assert.That(sums.PhysicalDamageTaken, Is.EqualTo(-0.35f).Within(1e-6f));
        Assert.That(sums.MagicalDamageTaken, Is.EqualTo(0.55f).Within(1e-6f));

        // A buff whose begin condition fails counts for nothing.
        BuffSums some = BuffManager.SumsOf([DisabledBuffTests.Held(Effect(50000313, 0.99f, 0.99f), false), DisabledBuffTests.Held(Effect(10100151, 0.25f, 0.5f), true)]);
        Assert.That(some.PhysicalDamageTaken, Is.EqualTo(0.25f).Within(1e-6f));
        Assert.That(some.MagicalDamageTaken, Is.EqualTo(0.5f).Within(1e-6f));
    }

    [Test]
    public void AHitOnTheHolderIsCutByTheRateOfItsAttackType() {
        var test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        FieldPlayer player = test.Session.Player;
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(player, test.Session.Field);
        // Even weapon attack: no roll between min and max, so two hits compare exactly.
        Stats stats = player.Stats.Values;
        stats[BasicAttribute.MinWeaponAtk].AddTotal(1000 - stats[BasicAttribute.MinWeaponAtk].Total);
        stats[BasicAttribute.MaxWeaponAtk].AddTotal(1000 - stats[BasicAttribute.MaxWeaponAtk].Total);
        stats[BasicAttribute.PhysicalAtk].AddTotal(1000);
        stats[BasicAttribute.MagicalAtk].AddTotal(1000);
        long physical = DamageCalculator.CalculateDamage(player, player, Physical).Item2;
        long magic = DamageCalculator.CalculateDamage(player, player, Magic).Item2;
        Assert.That(physical, Is.GreaterThan(1000));

        // 50000316: physical damage taken -99 %; 50001405 at level 1: both -50 % (frozen, takes more).
        var buffs = (ConcurrentDictionary<int, List<Buff>>) typeof(BuffManager).GetField("buffs", Any)!.GetValue(player.Buffs)!;
        void Hold(AdditionalEffectMetadata effect) {
            Buff buff = DisabledBuffTests.Held(effect, true);
            typeof(Buff).GetField("<Caster>k__BackingField", Any)!.SetValue(buff, player);
            typeof(Buff).GetField("<Owner>k__BackingField", Any)!.SetValue(buff, player);
            buffs[effect.Id] = [buff];
            player.Buffs.EnabledChanged(buff);
        }
        Hold(Effect(50000316, 0.99f, 0));
        Assert.That(DamageCalculator.CalculateDamage(player, player, Physical).Item2, Is.EqualTo(physical * 0.01).Within(1));
        Assert.That(DamageCalculator.CalculateDamage(player, player, Magic).Item2, Is.EqualTo(magic).Within(1));

        buffs.Clear();
        Hold(Effect(50001405, -0.5f, -0.5f));
        Assert.That(DamageCalculator.CalculateDamage(player, player, Physical).Item2, Is.EqualTo(physical * 1.5).Within(1));
        Assert.That(DamageCalculator.CalculateDamage(player, player, Magic).Item2, Is.EqualTo(magic * 1.5).Within(1));

        // A full block leaves the least hit.
        buffs.Clear();
        Hold(Effect(60132001, 1, 0));
        Assert.That(DamageCalculator.CalculateDamage(player, player, Physical).Item2, Is.EqualTo(1));
    }
}
