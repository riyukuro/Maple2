using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// The attackPossible wiring: a player's own BuffManager holding a limit, a skill hit (Actor.ApplyDamage) and a dot tick
// (DotDamageRecord, whose amount is negative for damage) of 300 on 1000 health. The player hits itself, so the caster's held effects are its own.
public class AttackRefusedTests {
    // 62100016: skill 90003005 and dot 120030007 break the shield.
    private static readonly AdditionalEffectMetadata Shield = DisabledBuffTests.Effect(62100016,
        property: DisabledBuffTests.Property with { AttackPossible = new AttackPossibleMetadata([90003005], [120030007], 0) });
    // 50004543: only holders of 90000874 hit it.
    private static readonly AdditionalEffectMetadata ByEffect = DisabledBuffTests.Effect(50004543,
        property: DisabledBuffTests.Property with { AttackPossible = new AttackPossibleMetadata([], [], 90000874) });

    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private TestGameSession test = null!;
    private FieldPlayer player => test.Session.Player;
    private long Health => player.Stats.Values[BasicAttribute.Health].Current;

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

    private void Hold(params Buff[] held) {
        var buffs = (ConcurrentDictionary<int, List<Buff>>) typeof(BuffManager).GetField("buffs", Any)!.GetValue(player.Buffs)!;
        foreach (Buff buff in held) {
            buffs[buff.Metadata.Id] = [buff];
        }
        typeof(BuffManager).GetMethod("InvalidateSums", Any)!.Invoke(player.Buffs, null);
    }

    private IReadOnlyList<(DamageType Type, long Amount)> Hit(int skillId) {
        var property = (SkillMetadataProperty) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataProperty));
        var skill = new SkillMetadata(skillId, 1, null, property, null!, null!);
        var attack = (SkillMetadataAttack) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataAttack)) with {
            CompulsionTypes = [],
            Damage = new SkillMetadataDamage(Count: 1, Rate: 0, HitSpeed: 0, HitDelay: 0, IsConstDamage: true, Value: 300,
                DamageByTargetMaxHp: 0, SuperArmorBreak: false, Push: null),
        };
        var record = new DamageRecord(skill, attack) { SkillId = skillId };
        player.ApplyDamage(player, record, attack);
        return record.Targets[player.ObjectId].Damage;
    }

    private DotDamageRecord DotTick(int effectId) {
        var dot = new AdditionalEffectMetadataDot.DotDamage(AttackType.None, Element.None, UseGrade: false, Rate: 0, HpValue: 300,
            SpValue: 0, EpValue: 0, DamageByTargetMaxHp: 0, RecoverHpByDamage: 0, IsConstDamage: true, NotKill: false);
        return new DotDamageRecord(player, player, dot, effectId: effectId);
    }

    [Test]
    public void ARefusedHitIsInvincibleAndTakesNothing() {
        Hold(DisabledBuffTests.Held(Shield, true));
        Assert.That(Hit(10000001).ToArray(), Is.EqualTo(new[] { (DamageType.Invincible, 0L) }));
        Assert.That(Health, Is.EqualTo(1000));
    }

    [Test]
    public void TheListedSkillHits() {
        Hold(DisabledBuffTests.Held(Shield, true));
        Assert.That(Hit(90003005).ToArray(), Is.EqualTo(new[] { (DamageType.Normal, 300L) }));
        Assert.That(Health, Is.EqualTo(700));
    }

    [Test]
    public void ADisabledLimitRefusesNothing() {
        Hold(DisabledBuffTests.Held(Shield, false));
        Assert.That(Hit(10000001).ToArray(), Is.EqualTo(new[] { (DamageType.Normal, 300L) }));
        Assert.That(Health, Is.EqualTo(700));
    }

    [Test]
    public void ARefusedDotTickIsZeroAndTheListedDotHits() {
        Hold(DisabledBuffTests.Held(Shield, true));
        DotDamageRecord refused = DotTick(120030008);
        Assert.That((refused.HpAmount, refused.Invincible), Is.EqualTo((0, true)));
        DotDamageRecord listed = DotTick(120030007);
        Assert.That((listed.HpAmount, listed.Invincible), Is.EqualTo((-300, false)));
    }

    // The caster's named effect counts only while it is enabled.
    [TestCase(true, 700)]
    [TestCase(false, 1000)]
    public void TheCastersNamedEffectCountsWhenEnabled(bool enabled, long expected) {
        Hold(DisabledBuffTests.Held(ByEffect, true), DisabledBuffTests.Held(DisabledBuffTests.Effect(90000874), enabled));
        Hit(10000001);
        Assert.That(Health, Is.EqualTo(expected));
    }

    // A holder of no limit pays nothing: the lookup is not built.
    [Test]
    public void NoLimitsRefuseNothing() {
        Assert.That(player.Buffs.AttackRefused(player, 10000001), Is.False);
        Assert.That(BuffManager.SumsOf([]).HasAttackLimits, Is.False);
    }
}
