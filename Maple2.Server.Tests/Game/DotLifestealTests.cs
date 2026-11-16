using System.Collections.Concurrent;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// A dot's casterRecoveryHpByDamage (KR 90100002: 0.3; 90002301: 0.01): each tick that takes the holder's health heals
// its caster that part of the damage, rounded down, up to the caster's max health. A tick that takes nothing heals
// nothing, and a dead caster is not healed.
public class DotLifestealTests {
    private const System.Reflection.BindingFlags Any = DotDamageCreditTests.Any;
    private TestGameSession test = null!;
    private FieldPlayer player => test.Session.Player;
    private Stat Health => player.Stats.Values[BasicAttribute.Health];

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(player, test.Session.Field);
        typeof(Actor<Player>).GetField("DamageDealers", Any)!.SetValue(player, new ConcurrentDictionary<int, DamageRecordTarget>());
        typeof(FieldPlayer).GetField("healthSync", Any)!.SetValue(player, new HealthSyncClock());
        typeof(FieldPlayer).GetField("regenClock", Any)!.SetValue(player, new RegenClock());
        Health.AddTotal(10000 - Health.Total);
        Health.Add(10000 - Health.Current);
    }

    private static AdditionalEffectMetadataDot.DotDamage Dot(int amount, float recover) =>
        new(AttackType.None, Element.None, UseGrade: false, Rate: 0, HpValue: amount, SpValue: 0, EpValue: 0,
            DamageByTargetMaxHp: 0, RecoverHpByDamage: recover, IsConstDamage: true, NotKill: false);

    // The record: the rate times the damage, rounded down, never below 0.
    [TestCase(1000, 0.3f, 300)]
    [TestCase(1234, 0.01f, 12)]
    [TestCase(0, 0.3f, 0)]
    [TestCase(-400, 0.3f, 0)]
    [TestCase(1000, 0f, 0)]
    public void TheRecoveredHealthIsTheRateOfTheDamage(int amount, float recover, int expected) {
        var record = new DotDamageRecord(player, player, Dot(amount, recover));
        Assert.That(record.RecoverHp, Is.EqualTo(expected));
    }

    // The player's lifesteal dot on another holder: a tick of 1000 at 0.3 heals the player 300, never takes 300.
    [Test]
    public void ALifestealTickHealsItsCaster() {
        Health.Add(-5000);
        using FieldActor holder = Holder();
        Tick(player, holder, 1000, 0.3f);
        Assert.That(Health.Current, Is.EqualTo(5300));
    }

    // The heal stops at the caster's max health.
    [Test]
    public void TheHealStopsAtMaxHealth() {
        Health.Add(-50);
        using FieldActor holder = Holder();
        Tick(player, holder, 1000, 0.3f);
        Assert.That(Health.Current, Is.EqualTo(10000));
    }

    // A tick that takes no health heals nothing, and a dead caster is not healed.
    [Test]
    public void NoHealWithoutDamageOrForADeadCaster() {
        Health.Add(-5000);
        using FieldActor holder = Holder();
        Tick(player, holder, 0, 0.3f);
        Assert.That(Health.Current, Is.EqualTo(5000));
        typeof(FieldPlayer).GetField("isDead", Any)!.SetValue(player, true);
        Assert.That(player.IsDead, Is.True);
        Tick(player, holder, 1000, 0.3f);
        Assert.That(Health.Current, Is.EqualTo(5000));
    }

    private FieldActor Holder() {
        var holder = new FieldActor(test.Session.Field!, 99, test.Session.Field!.Metadata, null!);
        Stat health = holder.Stats.Values[BasicAttribute.Health];
        health.AddTotal(100000 - health.Total);
        health.Add(100000 - health.Current);
        return holder;
    }

    private static void Tick(IActor caster, IActor holder, int amount, float recover) {
        AdditionalEffectMetadata dot = DisabledBuffTests.Effect(1) with {
            Dot = new AdditionalEffectMetadataDot(Dot(amount, recover), null),
        };
        Buff tick = DisabledBuffTests.Held(dot, true);
        typeof(Buff).GetField("<Caster>k__BackingField", Any)!.SetValue(tick, caster);
        typeof(Buff).GetField("<Owner>k__BackingField", Any)!.SetValue(tick, holder);
        typeof(Buff).GetMethod("ApplyDotDamage", Any)!.Invoke(tick, null);
    }
}
