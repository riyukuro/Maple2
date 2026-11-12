using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// The item option lines HpOnKill, SpiritOnKill and StaminaOnKill (killhprestore and its two siblings): the killing
// blow on an npc gives the killer that much back, capped at the maximum. Nothing read them before.
public class RestoreOnKillTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private TestGameSession test = null!;
    private FieldPlayer player => test.Session.Player;
    private Stats stats => player.Stats.Values;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        // The parts of a field player a test session leaves unset (as DeathResistanceTests does).
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(player, test.Session.Field);
        typeof(FieldPlayer).GetField("healthSync", Any)!.SetValue(player, new HealthSyncClock());
        foreach (BasicAttribute attribute in new[] { BasicAttribute.Health, BasicAttribute.Spirit, BasicAttribute.Stamina }) {
            stats[attribute].AddTotal(1000 - stats[attribute].Total);
            stats[attribute].Add(-stats[attribute].Current + 500);
        }
    }

    private long Current(BasicAttribute attribute) => stats[attribute].Current;

    [Test]
    public void TheKillerGetsItsLinesBack() {
        stats[SpecialAttribute.HpOnKill].AddTotal(8);
        stats[SpecialAttribute.SpiritOnKill].AddTotal(3);
        stats[SpecialAttribute.StaminaOnKill].AddTotal(5);
        player.RestoreOnKill();
        Assert.That(Current(BasicAttribute.Health), Is.EqualTo(508));
        Assert.That(Current(BasicAttribute.Spirit), Is.EqualTo(503));
        Assert.That(Current(BasicAttribute.Stamina), Is.EqualTo(505));

        // Each kill gives it again.
        player.RestoreOnKill();
        Assert.That(Current(BasicAttribute.Health), Is.EqualTo(516));
    }

    [Test]
    public void NoLineGivesNothingAndTheMaximumCaps() {
        player.RestoreOnKill();
        Assert.That(Current(BasicAttribute.Health), Is.EqualTo(500));
        Assert.That(Current(BasicAttribute.Spirit), Is.EqualTo(500));

        stats[BasicAttribute.Health].Add(495);
        stats[SpecialAttribute.HpOnKill].AddTotal(8);
        player.RestoreOnKill();
        Assert.That(Current(BasicAttribute.Health), Is.EqualTo(1000));
    }

    [Test]
    public void ADeadKillerGetsNothing() {
        stats[SpecialAttribute.HpOnKill].AddTotal(8);
        typeof(FieldPlayer).GetField("isDead", Any)!.SetValue(player, true);
        player.RestoreOnKill();
        Assert.That(Current(BasicAttribute.Health), Is.EqualTo(500));
    }
}
