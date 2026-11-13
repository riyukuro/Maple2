using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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

namespace Maple2.Server.Tests.Game;

// constDamageByTargetHP / constDamageByTargetSP (SkillMetadataDamage.ConstDamageByTargetHp / Sp): a hit takes the
// target's health, and a player's spirit, down to the attack's value and never kills. KR: 1 on the Zakum rage attack
// 50100116 (both) and the Pink Bean curses 99999737 and 99999814 (health).
public class ConstDamageByTargetTests {
    private TestGameSession test = null!;
    private FieldPlayer player => test.Session.Player;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        typeof(Actor<Player>).GetField("<Field>k__BackingField", any)!.SetValue(player, test.Session.Field);
        typeof(Actor<Player>).GetField("DamageDealers", any)!.SetValue(player, new ConcurrentDictionary<int, DamageRecordTarget>());
        typeof(FieldPlayer).GetField("healthSync", any)!.SetValue(player, new HealthSyncClock());
        typeof(FieldPlayer).GetField("regenClock", any)!.SetValue(player, new RegenClock());
        Set(BasicAttribute.Health, 1000, 1000);
        Set(BasicAttribute.Spirit, 100, 100);
    }

    private void Set(BasicAttribute attribute, long current, long total) {
        Stat stat = player.Stats.Values[attribute];
        stat.AddTotal(total - stat.Total);
        stat.Add(current - stat.Current);
    }

    private void Hold(AdditionalEffectMetadata metadata) {
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var buffs = (ConcurrentDictionary<int, List<Buff>>) typeof(BuffManager).GetField("buffs", any)!.GetValue(player.Buffs)!;
        buffs[metadata.Id] = [DisabledBuffTests.Held(metadata, true)];
        typeof(BuffManager).GetMethod("InvalidateSums", any)!.Invoke(player.Buffs, null);
    }

    private long Current(BasicAttribute attribute) => player.Stats.Values[attribute].Current;

    // A hit of the given count whose health and spirit go down to hp and sp; the record's amounts.
    private long[] Hit(long hp, long sp, int count = 1) {
        var property = (SkillMetadataProperty) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataProperty));
        var skill = new SkillMetadata(50100116, 1, null, property, null!, null!);
        var attack = (SkillMetadataAttack) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataAttack)) with {
            CompulsionTypes = [],
            Damage = new SkillMetadataDamage(Count: count, Rate: 1, HitSpeed: 0, HitDelay: 0, IsConstDamage: false, Value: 0,
                DamageByTargetMaxHp: 0, SuperArmorBreak: false, Push: null, ConstDamageByTargetHp: hp, ConstDamageByTargetSp: sp),
        };
        var record = new DamageRecord(skill, attack) { SkillId = 50100116 };
        player.ApplyDamage(player, record, attack);
        return record.Targets[player.ObjectId].Damage.Select(hit => hit.Amount).ToArray();
    }

    [Test]
    public void DownTo_TakesTheCurrentDownToTheValue() {
        Assert.Multiple(() => {
            Assert.That(DamageCalculator.DownTo(1000, 1), Is.EqualTo(999));
            Assert.That(DamageCalculator.DownTo(1, 1), Is.Zero, "already there");
            Assert.That(DamageCalculator.DownTo(0, 1), Is.Zero, "below it");
            Assert.That(DamageCalculator.DownTo(1000, 0), Is.Zero, "not given");
            Assert.That(DamageCalculator.DownTo(1000, -5), Is.Zero);
        });
    }

    [Test]
    public void TheHitTakesHealthAndSpiritDownToTheValue() {
        Assert.That(Hit(hp: 1, sp: 1), Is.EqualTo(new long[] { 999 }), "the real health lost");
        Assert.Multiple(() => {
            Assert.That(Current(BasicAttribute.Health), Is.EqualTo(1));
            Assert.That(Current(BasicAttribute.Spirit), Is.EqualTo(1));
        });
        Assert.That(Hit(hp: 1, sp: 1), Is.EqualTo(new long[] { 0 }), "a target at the value takes a hit of 0 and does not die");
        Assert.That(Current(BasicAttribute.Health), Is.EqualTo(1));
    }

    [Test]
    public void LaterHitsOfTheSameAttackTakeNothingMore() {
        Assert.That(Hit(hp: 1, sp: 0, count: 3), Is.EqualTo(new long[] { 999, 0, 0 }));
        Assert.Multiple(() => {
            Assert.That(Current(BasicAttribute.Health), Is.EqualTo(1));
            Assert.That(Current(BasicAttribute.Spirit), Is.EqualTo(100), "no spirit value");
        });
    }

    [Test]
    public void SpiritBelowTheValueIsLeft() {
        Set(BasicAttribute.Spirit, 0, 100);
        Hit(hp: 1, sp: 1);
        Assert.That(Current(BasicAttribute.Spirit), Is.Zero);
    }

    [Test]
    public void AnInvincibleTargetTakesNothing() {
        Hold(DisabledBuffTests.Effect(6, invincible: true));
        Assert.That(Hit(hp: 1, sp: 1), Is.EqualTo(new long[] { 0 }));
        Assert.Multiple(() => {
            Assert.That(Current(BasicAttribute.Health), Is.EqualTo(1000));
            Assert.That(Current(BasicAttribute.Spirit), Is.EqualTo(100), "no hit landed");
        });
    }

    [Test]
    public void ARefusedHitTakesNothing() {
        // 62100016: only skill 90003005 and dot 120030007 hit its holder (attackPossible).
        Hold(DisabledBuffTests.Effect(62100016,
            property: DisabledBuffTests.Property with { AttackPossible = new AttackPossibleMetadata([90003005], [120030007], 0) }));
        Assert.That(Hit(hp: 1, sp: 1), Is.EqualTo(new long[] { 0 }));
        Assert.Multiple(() => {
            Assert.That(Current(BasicAttribute.Health), Is.EqualTo(1000));
            Assert.That(Current(BasicAttribute.Spirit), Is.EqualTo(100), "no hit landed");
        });
    }

    [Test]
    public void TheHealthChangeOfAConstHitCannotKill() {
        // The hit's amount is taken from the health at the hit; the change goes in with noDie, so health lowered
        // in between (here to 1 before a change of -999) is left at 1, not 0.
        Assert.That(NpcTargetRules.HealthChange(1, -999, noDie: true), Is.Zero);
        Assert.That(NpcTargetRules.HealthChange(500, -999, noDie: true), Is.EqualTo(-499));
        Hit(hp: 1, sp: 0);
        Set(BasicAttribute.Health, 300, 1000);
        Assert.That(Hit(hp: 1, sp: 0), Is.EqualTo(new long[] { 299 }), "a later attack goes from the health it finds");
        Assert.That(Current(BasicAttribute.Health), Is.EqualTo(1));
    }
}
