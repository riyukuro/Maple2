using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class RegenClockTests {
    private const int Wait = 1000;     // constants.xml RecoveryHPWaitTick
    private const int Interval = 3000; // userStat hp_inv
    private const int Step = 50;       // one field update

    // Runs the player's health regen from 0 to end, hit at each tick in hits; returns the ticks that regenerated.
    private static long[] Run(long end, params long[] hits) => RunStep(end, Step, hits);

    private static long[] RunStep(long end, int step, params long[] hits) {
        var clock = new RegenClock();
        var regen = new List<long>();
        for (long tick = 0; tick <= end; tick += step) {
            if (hits.Contains(tick)) {
                clock.Hit(BasicAttribute.Health, tick, Wait);
            }
            if (clock.TryTick(BasicAttribute.Health, tick, Interval)) {
                regen.Add(tick);
            }
        }
        return regen.ToArray();
    }

    [Test]
    public void NoHits_TicksEveryInterval() {
        Assert.That(Run(10_000, []), Is.EqualTo(new long[] { 3000, 6000, 9000 }));
    }

    [Test]
    public void HitEvery3s_NoRegenUntil4sAfterTheLastHit() {
        // KMS2: a player hit at least every 4 s gets no regen; the first tick comes 4 s after the last hit.
        long[] hits = [1000, 4000, 7000, 10_000, 13_000, 16_000];
        long[] regen = Run(25_000, hits);
        Assert.That(regen.Where(t => t < 16_000 + Wait + Interval), Is.Empty);
        Assert.That(regen.First(), Is.EqualTo(16_000 + Wait + Interval));
        Assert.That(regen.Skip(1).First() - regen.First(), Is.EqualTo(Interval));
    }

    [Test]
    public void SkillHitOnThePlayer_DelaysHealthRegen() {
        // Actor.ApplyDamage ends a hit that took health with OnDamageReceived; the player's override restarts the wait.
        (FieldPlayer player, RegenClock clock) = PlayerAt(10_000);
        typeof(FieldPlayer).GetMethod("OnDamageReceived", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(player, [player, 59L]);
        Assert.That(clock.TryTick(BasicAttribute.Health, 10_000 + Wait + Interval - 1, Interval), Is.False);
        Assert.That(clock.TryTick(BasicAttribute.Health, 10_000 + Wait + Interval, Interval), Is.True);
    }

    // A player on a field at fieldTick with RecoveryHPWaitTick and RecoveryEPWaitTick = Wait; nothing else of it is set up.
    private static (FieldPlayer, RegenClock) PlayerAt(long fieldTick) {
        var field = (FieldManager) RuntimeHelpers.GetUninitializedObject(typeof(FieldManager));
        GC.SuppressFinalize(field);
        typeof(FieldManager).GetProperty(nameof(FieldManager.FieldTick))!.SetValue(field, fieldTick);
        var tables = (ServerTableMetadataStorage) RuntimeHelpers.GetUninitializedObject(typeof(ServerTableMetadataStorage));
        typeof(ServerTableMetadataStorage).GetField("constantsTable", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(tables, new Lazy<ConstantsTable>(() => new ConstantsTable(RecoveryHPWaitTick: Wait, RecoveryEPWaitTick: Wait)));
        var session = (GameSession) RuntimeHelpers.GetUninitializedObject(typeof(GameSession));
        GC.SuppressFinalize(session);
        typeof(GameSession).GetProperty(nameof(GameSession.ServerTableMetadata))!.SetValue(session, tables);
        var player = (FieldPlayer) RuntimeHelpers.GetUninitializedObject(typeof(FieldPlayer));
        GC.SuppressFinalize(player);
        typeof(FieldPlayer).GetField(nameof(FieldPlayer.Session))!.SetValue(player, session);
        typeof(Actor<Player>).GetField("<Field>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(player, field);
        var clock = new RegenClock();
        typeof(FieldPlayer).GetField("regenClock", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(player, clock);
        return (player, clock);
    }

    [Test]
    public void HitResetsAPendingTick() {
        // A hit at 2.9 s pushes the tick that was due at 3 s to 6.9 s.
        Assert.That(Run(7_000, 2900).First(), Is.EqualTo(2900 + Wait + Interval));
    }

    [Test]
    public void UpdatesOffTheGrid_TicksStayOnTheIntervalGrid() {
        // A field update every 70 ms never lands on a multiple of 3000 after the first; each tick is taken at the
        // first update at or after its due time and the due times stay 3000 apart.
        long[] regen = RunStep(30_100, 70);
        Assert.That(regen, Has.Length.EqualTo(10));
        for (int i = 0; i < regen.Length; i++) {
            long due = Interval * (i + 1);
            Assert.That(regen[i], Is.InRange(due, due + 69));
        }
    }

    [Test]
    public void ShortInterval_OneTickPerInterval() {
        // Spirit at interval 200 on a 50 ms update: 5 ticks a second. An interval under the floor ticks at the floor.
        var clock = new RegenClock();
        int at200 = 0, at80 = 0;
        for (long tick = 50; tick <= 10_000; tick += Step) {
            if (clock.TryTick(BasicAttribute.Spirit, tick, 200)) at200++;
            if (clock.TryTick(BasicAttribute.Stamina, tick, 80)) at80++;
        }
        Assert.That(at200, Is.EqualTo(50));
        Assert.That(at80, Is.EqualTo(10_000 / Constant.MinStatIntervalTick));
    }

    [Test]
    public void AfterAPause_TheGridStartsAgain() {
        // The stat was full from 3 s to 60 s (no TryTick): the tick at 60 s is taken once, the next one 3 s later.
        var clock = new RegenClock();
        Assert.That(clock.TryTick(BasicAttribute.Health, 3000, Interval), Is.True);
        Assert.That(clock.TryTick(BasicAttribute.Health, 60_050, Interval), Is.True);
        Assert.That(clock.TryTick(BasicAttribute.Health, 60_100, Interval), Is.False);
        Assert.That(clock.TryTick(BasicAttribute.Health, 63_000, Interval), Is.False);
        Assert.That(clock.TryTick(BasicAttribute.Health, 63_050, Interval), Is.True);
    }

    [Test]
    public void StaminaUse_FirstRefillOneIntervalLater() {
        // No wait tick on top: RecoveryEPWaitTick (set to Wait here) is not applied to a use.
        const int staminaInterval = 500; // userStat ep_inv
        (FieldPlayer player, RegenClock clock) = PlayerAt(10_000);
        player.DelayStaminaRegen();
        Assert.That(clock.TryTick(BasicAttribute.Stamina, 10_000 + staminaInterval - Step, staminaInterval), Is.False);
        Assert.That(clock.TryTick(BasicAttribute.Stamina, 10_000 + staminaInterval, staminaInterval), Is.True);
        Assert.That(clock.TryTick(BasicAttribute.Stamina, 10_000 + 2 * staminaInterval, staminaInterval), Is.True);
    }
}
