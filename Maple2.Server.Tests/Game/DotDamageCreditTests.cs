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
    internal const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
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

    // A caster other than the holder.
    private FieldActor Other() => new(test.Session.Field!, 99, test.Session.Field!.Metadata, null!);

    internal static void DotTick(IActor caster, IActor owner, int amount, int spirit = 0, int stamina = 0) {
        AdditionalEffectMetadata dot = DisabledBuffTests.Effect(1) with {
            Dot = new AdditionalEffectMetadataDot(new AdditionalEffectMetadataDot.DotDamage(AttackType.None, Element.None, UseGrade: false,
                Rate: 0, HpValue: amount, SpValue: spirit, EpValue: stamina, DamageByTargetMaxHp: 0, RecoverHpByDamage: 0, IsConstDamage: true, NotKill: false), null),
        };
        Buff tick = DisabledBuffTests.Held(dot, true);
        typeof(Buff).GetField("<Caster>k__BackingField", Any)!.SetValue(tick, caster);
        typeof(Buff).GetField("<Owner>k__BackingField", Any)!.SetValue(tick, owner);
        typeof(Buff).GetMethod("ApplyDotDamage", Any)!.Invoke(tick, null);
    }

    [Test]
    public void ADamagingTickIsCreditedToItsCaster() {
        using FieldActor other = Other();
        DotTick(other, player, 300);
        DotTick(other, player, 200);
        Assert.That(player.Stats.Values[BasicAttribute.Health].Current, Is.EqualTo(9500));
        Assert.That(player.DamageByDealer.ToArray(), Is.EqualTo(new[] { (other.ObjectId, 500L) }));
    }

    // A dot the holder cast on itself takes its health but is no one's damage.
    [Test]
    public void ASelfCastTickIsNotCredited() {
        DotTick(player, player, 300);
        Assert.That(player.Stats.Values[BasicAttribute.Health].Current, Is.EqualTo(9700));
        Assert.That(player.DamageByDealer, Is.Empty);
    }

    // A tick that takes nothing (a value of 0, or below it: the record floors it at 0) credits nothing.
    [TestCase(0)]
    [TestCase(-400)]
    public void ATickWithoutDamageIsNotCredited(int amount) {
        using FieldActor other = Other();
        DotTick(other, player, amount);
        Assert.That(player.Stats.Values[BasicAttribute.Health].Current, Is.EqualTo(10000));
        Assert.That(player.DamageByDealer, Is.Empty);
    }

    // A tick of 10700042 (KR: SpValue 10) takes 10 spirit, one of 10400031 (EpValue 120) 120 stamina; neither takes
    // health, and neither is credited as damage.
    [TestCase(10, 0, 490, 500)]
    [TestCase(0, 120, 500, 380)]
    public void ASpiritOrStaminaTickTakesOnlyThat(int spirit, int stamina, long spiritLeft, long staminaLeft) {
        using FieldActor other = Other();
        foreach (BasicAttribute attribute in new[] { BasicAttribute.Spirit, BasicAttribute.Stamina }) {
            Stat stat = player.Stats.Values[attribute];
            (stat.Total, stat.Current) = (500, 500);
        }

        DotTick(other, player, 0, spirit, stamina);

        Assert.That(player.Stats.Values[BasicAttribute.Spirit].Current, Is.EqualTo(spiritLeft));
        Assert.That(player.Stats.Values[BasicAttribute.Stamina].Current, Is.EqualTo(staminaLeft));
        Assert.That(player.Stats.Values[BasicAttribute.Health].Current, Is.EqualTo(10000));
        Assert.That(player.DamageByDealer, Is.Empty);
    }
}
