using System.Collections.Concurrent;
using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// A dot's casterRecoveryHpByDamage (KR 90100002: 0.3; 90002301: 0.01): each tick that takes the holder's health heals
// its caster that part of the health the tick took, rounded down, up to the caster's max health. A tick that takes nothing heals
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

    private static AdditionalEffectMetadataDot.DotDamage Dot(int amount, float recover, bool notKill = false) =>
        new(AttackType.None, Element.None, UseGrade: false, Rate: 0, HpValue: amount, SpValue: 0, EpValue: 0,
            DamageByTargetMaxHp: 0, RecoverHpByDamage: recover, IsConstDamage: true, NotKill: notKill);

    // The rate times the health taken, rounded down, never below 0.
    [TestCase(1000, 0.3f, 300)]
    [TestCase(1234, 0.01f, 12)]
    [TestCase(0, 0.3f, 0)]
    [TestCase(-400, 0.3f, 0)]
    [TestCase(1000, 0f, 0)]
    public void TheRecoveredHealthIsTheRateOfTheHealthTaken(long taken, float recover, long expected) {
        Assert.That(DotDamageRecord.Lifesteal(recover, taken), Is.EqualTo(expected));
    }

    // The player's own lifesteal dot (caster and holder): a tick of 1000 at 0.3 takes 1000 and gives back 300, so the
    // player ends 700 down, never 1300.
    [Test]
    public void ALifestealTickHealsItsCaster() {
        Tick(Dot(1000, 0.3f));
        Assert.That(Health.Current, Is.EqualTo(9300));
    }

    // Only the health taken counts: a tick of 1000 on 50 health takes 49, as a not-kill tick does or as a held
    // deathResistance buff (10200221, Raging Soul) keeps the holder at 1, so 0.3 gives back 14 (14.7 rounded down),
    // never the 300 of the tick's whole amount.
    [TestCase(true, false)]
    [TestCase(false, true)]
    public void OnlyTheHealthTakenCounts(bool notKill, bool deathResistance) {
        if (deathResistance) {
            Buff ragingSoul = DisabledBuffTests.Held(DisabledBuffTests.Effect(10200221, deathResistance: true), true);
            var buffs = (ConcurrentDictionary<int, List<Buff>>) typeof(BuffManager).GetField("buffs", Any)!.GetValue(player.Buffs)!;
            buffs[ragingSoul.Id] = [ragingSoul];
            typeof(BuffManager).GetMethod("InvalidateSums", Any)!.Invoke(player.Buffs, null);
        }
        Health.Add(50 - Health.Current);
        Tick(Dot(1000, 0.3f, notKill));
        Assert.That(Health.Current, Is.EqualTo(1 + 14));
    }

    // The heal stops at the caster's max health: 9950 less a tick of 10, plus 10 times 10, is 10000.
    [Test]
    public void TheHealStopsAtMaxHealth() {
        Health.Add(-50);
        Tick(Dot(10, 10f));
        Assert.That(Health.Current, Is.EqualTo(10000));
    }

    // A tick that takes no health heals nothing, and a dead caster is not healed.
    [Test]
    public void NoHealWithoutDamageOrForADeadCaster() {
        Health.Add(-5000);
        Tick(Dot(0, 0.3f));
        Assert.That(Health.Current, Is.EqualTo(5000));
        typeof(FieldPlayer).GetField("isDead", Any)!.SetValue(player, true);
        Assert.That(player.IsDead, Is.True);
        Tick(Dot(1000, 0.3f));
        Assert.That(Health.Current, Is.EqualTo(4000));
    }

    // A self-cast tick that takes the caster's last health heals nothing: at 500 health a tick of 1000 at 0.3 leaves 0,
    // not 150 (IsDead is set only at the caster's next update).
    [Test]
    public void AKillingSelfCastTickDoesNotHealItsCaster() {
        Health.Add(500 - Health.Current);
        Tick(Dot(1000, 0.3f));
        Assert.That(Health.Current, Is.EqualTo(0));
    }

    private void Tick(AdditionalEffectMetadataDot.DotDamage damage) {
        AdditionalEffectMetadata dot = DisabledBuffTests.Effect(1) with {
            Dot = new AdditionalEffectMetadataDot(damage, null),
        };
        Buff tick = DisabledBuffTests.Held(dot, true);
        typeof(Buff).GetField("<Caster>k__BackingField", Any)!.SetValue(tick, player);
        typeof(Buff).GetField("<Owner>k__BackingField", Any)!.SetValue(tick, player);
        typeof(Buff).GetMethod("ApplyDotDamage", Any)!.Invoke(tick, null);
    }
}
