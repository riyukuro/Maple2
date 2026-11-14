using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// A dot tick that takes health is damage its caster dealt (Buff.DealDotDamage -> RecordDotDamage), as a skill hit's
// is: a monster a dot kills gives its caster the kill (exp, loot, last hit), and the rank target counts it.
public class DotDamageCreditTests {
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
        health.AddTotal(10000 - health.Total);
        health.Add(10000 - health.Current);
    }

    private void DotTick(int amount) {
        AdditionalEffectMetadata dot = DisabledBuffTests.Effect(1) with {
            Dot = new AdditionalEffectMetadataDot(new AdditionalEffectMetadataDot.DotDamage(AttackType.None, Element.None, UseGrade: false,
                Rate: 0, HpValue: amount, SpValue: 0, EpValue: 0, DamageByTargetMaxHp: 0, RecoverHpByDamage: 0, IsConstDamage: true, NotKill: false), null),
        };
        Buff tick = DisabledBuffTests.Held(dot, true);
        typeof(Buff).GetField("<Caster>k__BackingField", Any)!.SetValue(tick, player);
        typeof(Buff).GetField("<Owner>k__BackingField", Any)!.SetValue(tick, player);
        typeof(Buff).GetMethod("ApplyDotDamage", Any)!.Invoke(tick, null);
    }

    [Test]
    public void ADamagingTickIsCreditedToItsCaster() {
        DotTick(300);
        DotTick(200);
        Assert.That(player.Stats.Values[BasicAttribute.Health].Current, Is.EqualTo(9500));
        Assert.That(player.DamageByDealer.ToArray(), Is.EqualTo(new[] { (player.ObjectId, 500L) }));
    }

    // A tick that takes nothing (a value of 0, or below it: the record floors it at 0) credits nothing.
    [TestCase(0)]
    [TestCase(-400)]
    public void ATickWithoutDamageIsNotCredited(int amount) {
        DotTick(amount);
        Assert.That(player.Stats.Values[BasicAttribute.Health].Current, Is.EqualTo(10000));
        Assert.That(player.DamageByDealer, Is.Empty);
    }
}
