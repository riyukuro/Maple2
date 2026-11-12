using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// deathResistanceHP (10200221, the Berserker's Raging Soul, 8 s): while the buff is on and enabled, hits and dot
// ticks take its holder no lower than 1 health.
public class DeathResistanceTests {
    private static readonly AdditionalEffectMetadata RagingSoul = DisabledBuffTests.Effect(10200221, deathResistance: true);

    [Test]
    public void AnEnabledBuffWithDeathResistanceHoldsAtOneHealth() {
        Assert.That(BuffManager.SumsOf([DisabledBuffTests.Held(RagingSoul, true)]).DeathResistance, Is.True);
        Assert.That(BuffManager.SumsOf([DisabledBuffTests.Held(RagingSoul, false)]).DeathResistance, Is.False);
        Assert.That(BuffManager.SumsOf([DisabledBuffTests.Held(DisabledBuffTests.Effect(1), true)]).DeathResistance, Is.False);

        // 500 health, a hit of 2000: held, the holder keeps 1; without the buff it falls to -1500 (dead).
        Assert.That(NpcTargetRules.HealthChange(500, -2000, true), Is.EqualTo(-499));
        Assert.That(NpcTargetRules.HealthChange(500, -2000, false), Is.EqualTo(-2000));
        // At 1 health a hit takes nothing; a heal still goes in whole.
        Assert.That(NpcTargetRules.HealthChange(1, -300, true), Is.EqualTo(0));
        Assert.That(NpcTargetRules.HealthChange(1, 300, true), Is.EqualTo(300));
    }

    // The wiring: a player's own BuffManager holding the buff, a skill hit (Actor.ApplyDamage) and a dot tick
    // (Buff.ApplyDotDamage) of 5000 on 1000 health.
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private TestGameSession test = null!;
    private FieldPlayer player => test.Session.Player;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(player, test.Session.Field);
        typeof(Actor<Player>).GetField("DamageDealers", Any)!.SetValue(player, new ConcurrentDictionary<int, DamageRecordTarget>());
        typeof(FieldPlayer).GetField("healthSync", Any)!.SetValue(player, new HealthSyncClock());
        typeof(FieldPlayer).GetField("regenClock", Any)!.SetValue(player, new RegenClock());
        Stat health = player.Stats.Values[BasicAttribute.Health];
        health.AddTotal(1000 - health.Total);
        health.Add(1000 - health.Current);
    }

    private void Hold(Buff buff) {
        var buffs = (ConcurrentDictionary<int, List<Buff>>) typeof(BuffManager).GetField("buffs", Any)!.GetValue(player.Buffs)!;
        buffs[buff.Id] = [buff];
        typeof(BuffManager).GetMethod("InvalidateSums", Any)!.Invoke(player.Buffs, null);
    }

    private void Hit(long amount) {
        var property = (SkillMetadataProperty) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataProperty));
        var skill = new SkillMetadata(1, 1, null, property, null!, null!);
        var attack = (SkillMetadataAttack) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataAttack)) with {
            CompulsionTypes = [],
            Damage = new SkillMetadataDamage(Count: 1, Rate: 0, HitSpeed: 0, HitDelay: 0, IsConstDamage: true, Value: amount,
                DamageByTargetMaxHp: 0, SuperArmorBreak: false, Push: null),
        };
        player.ApplyDamage(player, new DamageRecord(skill, attack), attack);
    }

    private void DotTick(int amount) {
        AdditionalEffectMetadata poison = DisabledBuffTests.Effect(1) with {
            Dot = new AdditionalEffectMetadataDot(new AdditionalEffectMetadataDot.DotDamage(AttackType.None, Element.None, UseGrade: false,
                Rate: 0, HpValue: amount, SpValue: 0, EpValue: 0, DamageByTargetMaxHp: 0, RecoverHpByDamage: 0, IsConstDamage: true, NotKill: false), null),
        };
        Buff tick = DisabledBuffTests.Held(poison, true);
        typeof(Buff).GetField("<Caster>k__BackingField", Any)!.SetValue(tick, player);
        typeof(Buff).GetField("<Owner>k__BackingField", Any)!.SetValue(tick, player);
        typeof(Buff).GetMethod("ApplyDotDamage", Any)!.Invoke(tick, null);
    }

    [TestCase(true, 1)]
    [TestCase(false, 0)]
    public void AHitTakesTheHolderToOneHealth(bool held, long expected) {
        if (held) {
            Hold(DisabledBuffTests.Held(RagingSoul, true));
        }
        Hit(5000);
        Assert.That(player.Stats.Values[BasicAttribute.Health].Current, Is.EqualTo(expected));
    }

    [TestCase(true, 1)]
    [TestCase(false, 0)]
    public void ADotTickTakesTheHolderToOneHealth(bool held, long expected) {
        if (held) {
            Hold(DisabledBuffTests.Held(RagingSoul, true));
        }
        DotTick(5000);
        Assert.That(player.Stats.Values[BasicAttribute.Health].Current, Is.EqualTo(expected));
    }

    [Test]
    public void ADisabledBuffDoesNotHold() {
        Hold(DisabledBuffTests.Held(RagingSoul, false));
        Hit(5000);
        Assert.That(player.Stats.Values[BasicAttribute.Health].Current, Is.EqualTo(0));
    }
}
