using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// A dot tick's amounts (DotDamageRecord, negative for damage) from the KR effect rows' dot damage: a constant health
// value, a spirit or stamina value, the stack count and the not-kill cap.
public class DotDamageRowTests {
    private static FieldPlayer Player(int maxHealth, int health) {
        var test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        FieldPlayer player = test.Session.Player;
        Stats stats = player.Stats.Values;
        stats[BasicAttribute.Health].AddTotal(maxHealth - stats[BasicAttribute.Health].Total);
        stats[BasicAttribute.Health].Add(health - stats[BasicAttribute.Health].Current);
        return player;
    }

    private static AdditionalEffectMetadataDot.DotDamage Dot(AttackType type = AttackType.None, bool useGrade = false,
            int hpValue = 0, int spValue = 0, int epValue = 0, float damageByTargetMaxHp = 0, bool isConstDamage = false,
            bool notKill = false) {
        return new AdditionalEffectMetadataDot.DotDamage(type, Element.None, useGrade, Rate: 0, hpValue, spValue, epValue,
            damageByTargetMaxHp, RecoverHpByDamage: 0, isConstDamage, notKill);
    }

    // 70000011 (KR: HpValue 10, IsConstDamage): 10 a tick, times the stack count.
    [Test]
    public void ConstHpValue_HitsForTheValueTimesTheStacks() {
        FieldPlayer caster = Player(5000, 5000);
        FieldPlayer target = Player(10000, 10000);
        AdditionalEffectMetadataDot.DotDamage dot = Dot(hpValue: 10, isConstDamage: true);

        Assert.That(new DotDamageRecord(caster, target, dot).HpAmount, Is.EqualTo(-10));
        Assert.That(new DotDamageRecord(caster, target, dot, stacks: 3).HpAmount, Is.EqualTo(-30));
        Assert.That(new DotDamageRecord(caster, target, dot).SpAmount, Is.EqualTo(0));
    }

    // 10700042 (KR: SpValue 10) takes 10 spirit a tick and no health; 10400031 (KR: EpValue 120) 120 stamina.
    [Test]
    public void SpAndEpValue_TakeSpiritAndStamina() {
        FieldPlayer caster = Player(5000, 5000);
        FieldPlayer target = Player(10000, 10000);

        var sp = new DotDamageRecord(caster, target, Dot(spValue: 10));
        Assert.That(sp.SpAmount, Is.EqualTo(-10));
        Assert.That(sp.HpAmount, Is.EqualTo(0));
        Assert.That(new DotDamageRecord(caster, target, Dot(spValue: 10), stacks: 2).SpAmount, Is.EqualTo(-20));

        var ep = new DotDamageRecord(caster, target, Dot(epValue: 120));
        Assert.That(ep.EpAmount, Is.EqualTo(-120));
        Assert.That(ep.HpAmount, Is.EqualTo(0));
    }

    // 90051039 (KR: Type 3, NotKill, UseGrade, DamageByTargetMaxHp 0.1): a tenth of 10000 on a target with 300 left
    // stops at 1 health.
    [Test]
    public void NotKill_LeavesOneHealth() {
        FieldPlayer caster = Player(5000, 5000);
        FieldPlayer target = Player(10000, 300);

        var record = new DotDamageRecord(caster, target, Dot(AttackType.All, useGrade: true, damageByTargetMaxHp: 0.1f, notKill: true));
        Assert.That(record.HpAmount, Is.EqualTo(-299));
    }

    // 70000114 (KR: Type 1, DamageByTargetMaxHp 0.2), Snowfield's Intense Cold, which the Glacial Canyon (52000082)
    // trigger adds each second to the players in box 701. Its caster is the field's own actor, which has no stats: no
    // tick is rolled, each is a Normal fifth of the max health (an accuracy of 0 used to make each tick a Miss).
    [Test]
    public void FieldActorDot_IsNotRolled() {
        FieldPlayer target = Player(10000, 10000);
        var field = new FieldActor(target.Session.Field!, 1, target.Session.Field!.Metadata, null!);
        Assert.That(field.Stats.Values[BasicAttribute.Accuracy].Total, Is.EqualTo(0));
        AdditionalEffectMetadataDot.DotDamage dot = Dot(AttackType.Physical, damageByTargetMaxHp: 0.2f);
        for (int i = 0; i < 20; i++) {
            var record = new DotDamageRecord(field, target, dot);
            Assert.That(record.Type, Is.EqualTo(DamageType.Normal));
            // The share alone: a typed dot without a rate has no rolled part, so not the hit's least damage of 1 on top.
            Assert.That(record.HpAmount, Is.EqualTo(-2000));
        }
    }

    // 50004001 (KR: Type 1, DamageByTargetMaxHp 1.1, no rate), as a player's: no Miss on a target that evades every
    // roll, and the share alone (the rolled part of a dot without a rate was a Miss of 0 beside the share, or a 1).
    [Test]
    public void TypedDotWithoutRate_IsNotRolled() {
        FieldPlayer caster = Player(5000, 5000);
        FieldPlayer target = Player(10000, 10000);
        target.Stats.Values[BasicAttribute.Evasion].AddTotal(1_000_000);
        AdditionalEffectMetadataDot.DotDamage dot = Dot(AttackType.Physical, useGrade: true, damageByTargetMaxHp: 1.1f);
        for (int i = 0; i < 20; i++) {
            var record = new DotDamageRecord(caster, target, dot);
            Assert.That(record.Type, Is.EqualTo(DamageType.Normal));
            Assert.That(record.HpAmount, Is.EqualTo(-11000));
        }
    }

    // A hit of the field's own actor is not rolled for evasion, but the target's block and evasion compulsions hold:
    // the KR rows 10100081 (BlockChance 1 on every skill) block it, 10700193 (EvasionChanceOverride 1) make it a Miss.
    [TestCase(10100081, BuffCompulsionEventType.BlockChance, DamageType.Block)]
    [TestCase(10700193, BuffCompulsionEventType.EvasionChanceOverride, DamageType.Miss)]
    public void FieldActorHit_KeepsTheTargetsCompulsions(int effectId, BuffCompulsionEventType type, DamageType expected) {
        FieldPlayer target = Player(10000, 10000);
        var field = new FieldActor(target.Session.Field!, 1, target.Session.Field!.Metadata, null!);
        var property = new DamagePropertyRecord { Rate = 1, AttackType = AttackType.Physical };
        Assert.That(DamageCalculator.HitRoll(field, target, property), Is.Null);

        AdditionalEffectMetadata effect = DisabledBuffTests.Effect(effectId, compulsion: new AdditionalEffectMetadataStatus.CompulsionEvent(type, 1f, []));
        var held = (ConcurrentDictionary<int, List<Buff>>) typeof(BuffManager).GetField("buffs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target.Buffs)!;
        held[effectId] = [DisabledBuffTests.Held(effect, true)];
        Assert.That(DamageCalculator.HitRoll(field, target, property), Is.EqualTo(expected));
    }
}
