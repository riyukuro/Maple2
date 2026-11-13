using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Core.Constants;
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

    // The UiText packets sent since the last call, as (object id, type, value).
    private List<(int Id, int Type, int Value)> UiTexts() => test.TakeSent()
        .Where(packet => TestGameSession.Op(packet) == SendOp.UiText)
        .Select(packet => {
            Assert.That(packet.Length, Is.EqualTo(2 + 12));
            return (BitConverter.ToInt32(packet, 2), BitConverter.ToInt32(packet, 6), BitConverter.ToInt32(packet, 10));
        }).ToList();

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
    public void EachRestoreShowsItsRecoveryNumberToTheKiller() {
        stats[SpecialAttribute.HpOnKill].AddTotal(8);
        stats[SpecialAttribute.SpiritOnKill].AddTotal(3);
        stats[SpecialAttribute.StaminaOnKill].AddTotal(5);
        stats[BasicAttribute.Health].Add(495);
        UiTexts();
        player.RestoreOnKill();
        // Health 4 (only 5 left below the maximum), Spirit 7, Stamina 10: the amounts actually restored.
        Assert.That(UiTexts(), Is.EqualTo(new List<(int, int, int)> {
            (player.ObjectId, 4, 5), (player.ObjectId, 7, 3), (player.ObjectId, 10, 5),
        }));

        // At the maximum nothing is restored and no number is shown.
        stats[BasicAttribute.Spirit].Add(1000);
        stats[BasicAttribute.Stamina].Add(1000);
        player.RestoreOnKill();
        Assert.That(UiTexts(), Is.Empty);
    }

    [Test]
    public void NoLineShowsNoNumber() {
        UiTexts();
        player.RestoreOnKill();
        Assert.That(UiTexts(), Is.Empty);
    }

    [Test]
    public void ADeadKillerGetsNothing() {
        stats[SpecialAttribute.HpOnKill].AddTotal(8);
        typeof(FieldPlayer).GetField("isDead", Any)!.SetValue(player, true);
        player.RestoreOnKill();
        Assert.That(Current(BasicAttribute.Health), Is.EqualTo(500));
    }
}
