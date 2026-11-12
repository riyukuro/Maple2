using System;
using System.Collections.Generic;
using System.Numerics;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class CastTimingTests {
    // inputcombo 0.6 s at speed 1: the next cast of another skill is taken from 500 ms on.
    private const long Required = 500;

    private static SkillCastRules.LastMotion Motion(long serverTick, int castTick) {
        return new SkillCastRules.LastMotion(10, 0.6f, 0.6f, 1f, 100, serverTick, castTick);
    }

    // Sends casts (client tick, arrival) through the motion check of one session; returns how many are refused.
    // A refused cast is not kept as the last one, as in the handler with enforce set.
    private static int Refused(IEnumerable<(int Tick, long Arrival)> casts, LagBudget lag, ClockRate? clock = null) {
        SkillCastRules.LastMotion? last = null;
        int refused = 0;
        foreach ((int tick, long arrival) in casts) {
            clock?.Add(tick, arrival);
            long need = last is { } l ? SkillCastRules.MotionShortfall(l, 11, false, 100, tick, arrival) : 0;
            if (SkillCastRules.TimingRefusal(need, clock?.IsFast(arrival) ?? false, lag, arrival) != null) {
                refused++;
                continue;
            }
            last = Motion(arrival, tick);
        }
        return refused;
    }

    // An honest client casts every 500 ms; the link holds everything for holdMs once, then delivers it at once.
    private static List<(int, long)> Spike(long holdMs) {
        var casts = new List<(int, long)>();
        for (int t = 0; t <= 20_000; t += 500) {
            bool held = t >= 5000 && t < 5000 + holdMs;
            casts.Add((t, held ? 5000 + holdMs : t));
        }
        return casts;
    }

    [Test]
    public void SettingIsEnforceOnlyWhenNamed() {
        Assert.That(CastCheckSetting.Parse(null), Is.EqualTo(CastCheckMode.LogOnly));
        Assert.That(CastCheckSetting.Parse(""), Is.EqualTo(CastCheckMode.LogOnly));
        Assert.That(CastCheckSetting.Parse("log"), Is.EqualTo(CastCheckMode.LogOnly));
        Assert.That(CastCheckSetting.Parse("1"), Is.EqualTo(CastCheckMode.LogOnly));
        Assert.That(CastCheckSetting.Parse("enforce"), Is.EqualTo(CastCheckMode.Enforce));
        Assert.That(CastCheckSetting.Parse(" Enforce "), Is.EqualTo(CastCheckMode.Enforce));
    }

    [Test]
    public void LagBudgetHoldsTwoSecondsAndRefillsOneInTen() {
        var lag = new LagBudget();
        Assert.That(lag.Level(1000), Is.EqualTo(2000));
        Assert.That(lag.TryDraw(1500, 1000), Is.True);
        Assert.That(lag.TryDraw(600, 1000), Is.False, "500 left");
        Assert.That(lag.Level(1000), Is.EqualTo(500), "a refused draw takes nothing");
        Assert.That(lag.TryDraw(600, 1999), Is.False);
        Assert.That(lag.TryDraw(600, 2000), Is.True, "100 ms back in 1 s");
        Assert.That(lag.Level(2000), Is.EqualTo(0));
        Assert.That(lag.Level(12_000), Is.EqualTo(1000));
        Assert.That(lag.Level(60_000), Is.EqualTo(2000), "never more than full");
        // Short steps add up.
        var slow = new LagBudget();
        Assert.That(slow.TryDraw(2000, 0), Is.True);
        for (long now = 3; now <= 1000; now += 3) {
            slow.Level(now);
        }
        Assert.That(slow.Level(1000), Is.EqualTo(100));
    }

    [Test]
    public void ShortfallIsMeasuredBetweenArrivals() {
        // On time by the arrivals: taken whatever the ticks say (a time sync that came late moves the ticks back).
        Assert.That(SkillCastRules.ArrivalShortfall(50_300, 50_000, 100_600, 100_000, Required), Is.EqualTo(0));
        Assert.That(SkillCastRules.ArrivalShortfall(58_000, 50_000, 100_500, 100_000, Required), Is.EqualTo(0));
        // Arrivals closer than the time needed: the client's ticks must be far enough apart.
        Assert.That(SkillCastRules.ArrivalShortfall(50_499, 50_000, 100_100, 100_000, Required), Is.EqualTo(SkillCastRules.ClientRefused));
        Assert.That(SkillCastRules.ArrivalShortfall(50_500, 50_000, 100_100, 100_000, Required), Is.EqualTo(400));
        // The client's tick does not lengthen the time.
        Assert.That(SkillCastRules.ArrivalShortfall(58_000, 50_000, 100_000, 100_000, Required), Is.EqualTo(500));
        // A tick that runs backwards draws nothing; on time by the arrivals it is taken.
        Assert.That(SkillCastRules.ArrivalShortfall(40_000, 50_000, 100_100, 100_000, Required), Is.EqualTo(SkillCastRules.ClientRefused));
        Assert.That(SkillCastRules.ArrivalShortfall(40_000, 50_000, 100_500, 100_000, Required), Is.EqualTo(0));
        // The 32-bit tick wraps.
        Assert.That(SkillCastRules.ArrivalShortfall(int.MinValue + 250, int.MaxValue - 250, 100_100, 100_000, Required), Is.EqualTo(400));
        Assert.That(SkillCastRules.ArrivalShortfall(int.MinValue + 200, int.MaxValue - 200, 100_100, 100_000, Required), Is.EqualTo(SkillCastRules.ClientRefused));
    }

    [Test]
    public void TimingDrawsFromTheBudgetOnce() {
        var lag = new LagBudget();
        Assert.That(SkillCastRules.TimingRefusal(0, false, lag, 0), Is.Null);
        Assert.That(SkillCastRules.TimingRefusal(1500, false, lag, 0), Is.Null);
        Assert.That(lag.Level(0), Is.EqualTo(500));
        Assert.That(SkillCastRules.TimingRefusal(501, false, lag, 0), Is.Not.Null);
        Assert.That(SkillCastRules.TimingRefusal(SkillCastRules.ClientRefused, false, lag, 0), Is.Not.Null);
        Assert.That(SkillCastRules.TimingRefusal(100, true, lag, 0), Is.Not.Null, "a fast clock draws nothing");
        Assert.That(lag.Level(0), Is.EqualTo(500));
    }

    [Test]
    public void OneSpikeIsTakenUpToTheBudget() {
        Assert.That(Refused(Spike(0), new LagBudget()), Is.EqualTo(0));
        Assert.That(Refused(Spike(500), new LagBudget()), Is.EqualTo(0));
        Assert.That(Refused(Spike(1000), new LagBudget()), Is.EqualTo(0));
        Assert.That(Refused(Spike(2000), new LagBudget()), Is.EqualTo(0));
        // 2 s held: four casts arrive with the one after them, four of the five 500 ms early: the whole budget.
        // 2.5 s and 3 s held: one and two casts more than the budget covers.
        Assert.That(Refused(Spike(2500), new LagBudget()), Is.EqualTo(1));
        Assert.That(Refused(Spike(3000), new LagBudget()), Is.EqualTo(2));
        // Two spikes of 2 s, 15 s apart: the second finds 1.5 s back.
        var lag = new LagBudget();
        Assert.That(Refused(Spike(2000), lag), Is.EqualTo(0));
        Assert.That(lag.Level(20_000), Is.LessThan(2000));
    }

    [Test]
    public void CastsFasterThanTheClientsOwnTicksAreRefused() {
        // Every 300 ms by its own ticks, arriving as sent: by the arrivals and by the ticks too early.
        var casts = new List<(int, long)>();
        for (int t = 0; t < 3000; t += 300) {
            casts.Add((t, t));
        }
        // 0 taken, 300 refused, 600 taken, 900 refused, ...
        Assert.That(Refused(casts, new LagBudget()), Is.EqualTo(5));
    }

    [Test]
    public void InflatedTicksGetTheBudgetOnce() {
        // Back to back, 50 ms apart, each tick 500 ms on: 450 ms from the budget each time.
        var casts = new List<(int, long)>();
        for (int i = 0; i < 200; i++) {
            casts.Add((i * 500, i * 50L));
        }
        var lag = new LagBudget();
        int refused = Refused(casts, lag);
        // 10 s: 20 casts are due by the arrivals, the budget and its refill give 3 s more (6 casts).
        Assert.That(200 - refused, Is.InRange(20, 27));
        // With the clock check the ticks are found fast once the first 5 s are over, and the arrivals alone count
        // from then on.
        var clock = new ClockRate(ClockRate.CastBoundMs);
        Assert.That(200 - Refused(casts, new LagBudget(), clock), Is.InRange(20, 27));
        Assert.That(clock.IsFast(10_000), Is.True);
    }

    [Test]
    public void ClockRateFindsTicksThatRunFast() {
        // An honest clock with packets up to 300 ms late is never fast.
        var clock = new ClockRate(ClockRate.CastBoundMs);
        var random = new Random(7);
        for (int t = 0; t < 600_000; t += 700) {
            Assert.That(clock.Add(t, t + random.Next(300)), Is.False);
        }
        // One packet 5 s late, the next on time again.
        Assert.That(clock.Add(600_700, 605_700), Is.False);
        Assert.That(clock.Add(606_000, 606_000), Is.False);
        // A time sync that came 3 s late: the ticks run 3 s behind for a while, then catch up.
        Assert.That(clock.Add(604_000, 607_000), Is.False);
        Assert.That(clock.Add(608_000, 608_000), Is.False);
        Assert.That(clock.IsFast(608_000), Is.False);

        // 2 % fast: inside the bound of 1 s in 30 s.
        clock = new ClockRate(ClockRate.CastBoundMs);
        for (int t = 0; t < 600_000; t += 500) {
            Assert.That(clock.Add(t + t / 50, t), Is.False);
        }
        // 5 % fast: found 20 s after the first 5 s, and named once while it lasts.
        clock = new ClockRate(ClockRate.CastBoundMs);
        int named = 0;
        long first = 0;
        for (int t = 0; t < 600_000; t += 500) {
            if (clock.Add(t + t / 20, t)) {
                named++;
                first = first == 0 ? t : first;
            }
        }
        Assert.That(first, Is.EqualTo(25_000));
        Assert.That(named, Is.EqualTo(1));
        Assert.That(clock.IsFast(600_000), Is.True);
        Assert.That(clock.IsFast(660_000), Is.False, "over a window after the last fast tick");
        // One jump ahead is fast for one window.
        clock = new ClockRate(ClockRate.CastBoundMs);
        clock.Add(0, 0);
        Assert.That(clock.Add(3000, 1000), Is.False, "the first 5 s are not measured");
        Assert.That(clock.Add(8000, 6000), Is.False);
        Assert.That(clock.Add(11_000, 7000), Is.True);
        Assert.That(clock.Add(12_000, 8000), Is.False);
        Assert.That(clock.IsFast(36_999), Is.True);
        Assert.That(clock.IsFast(37_000), Is.False);
    }

    [Test]
    public void ClockRateCountsATickThatJumpsBack() {
        var clock = new ClockRate(ClockRate.CastBoundMs, ClockRate.CastBackMs);
        clock.Start(0, 0);
        Assert.That(clock.Add(6000, 6000), Is.False);
        // A time sync answer held up for 2 s moves the ticks back by that much.
        Assert.That(clock.Add(4500, 6500), Is.False);
        Assert.That(clock.IsFast(6500), Is.False);
        // Further back than that: named once, and nothing is drawn for a window.
        Assert.That(clock.Add(2000, 7000), Is.True);
        Assert.That(clock.Add(-1000, 7500), Is.False);
        Assert.That(clock.IsFast(37_499), Is.True);
        Assert.That(clock.IsFast(37_500), Is.False);
        var lag = new LagBudget();
        Assert.That(SkillCastRules.TimingRefusal(SkillCastRules.ArrivalShortfall(-2000, -1000, 7600, 7500, Required), clock.IsFast(7600), lag, 7600), Is.Not.Null);
        Assert.That(lag.Level(7600), Is.EqualTo(LagBudget.CapacityMs), "a backward tick draws nothing");
        // The position syncs' measure has no such rule.
        clock = new ClockRate(ClockRate.SyncBoundMs);
        clock.Add(0, 0);
        clock.Add(9000, 6000);
        Assert.That(clock.Add(1000, 6500), Is.False);
    }

    [Test]
    public void ClockRateIsMeasuredFromTheSessionStart() {
        // The first cast comes 20 s into the session and 3 s late (a stall); the next ones come on time.
        var clock = new ClockRate(ClockRate.CastBoundMs, ClockRate.CastBackMs);
        clock.Start(1000, 0);
        Assert.That(clock.Add(21_000, 23_000), Is.False);
        for (int t = 21_500; t < 120_000; t += 500) {
            Assert.That(clock.Add(t, t - 1000), Is.False);
        }
        Assert.That(clock.IsFast(120_000), Is.False);
        // Measured from the first cast instead, the ticks after it look 3 s fast.
        clock = new ClockRate(ClockRate.CastBoundMs, ClockRate.CastBackMs);
        clock.Add(21_000, 23_000);
        Assert.That(clock.Add(29_500, 28_500), Is.True);
        // A clock that is fast from the start is found once the first 5 s of the session are over.
        clock = new ClockRate(ClockRate.CastBoundMs, ClockRate.CastBackMs);
        clock.Start(1000, 0);
        Assert.That(clock.Add(1000 + 20_000 + 4000, 20_000), Is.True);
    }

    [Test]
    public void LogGateHoldsALineToOneInTenSeconds() {
        var gate = new LogGate();
        Assert.That(gate.Count(-5000), Is.EqualTo(1));
        Assert.That(gate.Count(-4000), Is.EqualTo(0));
        Assert.That(gate.Count(4999), Is.EqualTo(0));
        Assert.That(gate.Count(5000), Is.EqualTo(3));
        Assert.That(gate.Count(15_000), Is.EqualTo(1));
    }

    [Test]
    public void HeldIterationDrawsFromTheBudget() {
        // Loop 500 ms. The first iteration was held up 1.5 s, the next ones come on time: 1 s under the allowance.
        var lag = new LagBudget();
        var budget = new SkillHitBudget();
        budget.Open(0);
        Assert.That(budget.TakePoint(0, 0, held: true, count: 1, tick: 1500, intervalMs: 500, lag: lag), Is.True);
        Assert.That(budget.TakePoint(0, 1, held: true, count: 1, tick: 500, intervalMs: 500, lag: lag), Is.True);
        Assert.That(lag.Level(500), Is.EqualTo(1000));
        Assert.That(budget.TakePoint(0, 2, held: true, count: 1, tick: 1000, intervalMs: 500, lag: lag), Is.True, "counted on from the drawn one");
        Assert.That(lag.Level(1000), Is.EqualTo(1050));
        // Iterations sent at once: the budget runs out and they pay nothing.
        int paid = 0;
        for (int i = 3; i < 20; i++) {
            paid += budget.TakePoint(0, i, held: true, count: 1, tick: 1000, intervalMs: 500, lag: lag) ? 1 : 0;
        }
        Assert.That(paid, Is.EqualTo(2), "500 ms from the budget each, as long as it holds that");
        Assert.That(budget.Unenforced, Is.Null);
    }

    [Test]
    public void LogOnlyTakesWhatTheTimingChecksRefuse() {
        var budget = new SkillHitBudget();
        budget.Open(0);
        Assert.That(budget.TakePoint(0, 0, held: true, count: 5, tick: 0, maxCount: 3, intervalMs: 500), Is.False);
        Assert.That(budget.TakePoint(0, 0, held: true, count: 5, tick: 0, maxCount: 3, intervalMs: 500, enforce: false), Is.True);
        Assert.That(budget.Unenforced, Does.Contain("5 attacks"));
        Assert.That(budget.TakePoint(0, 1, held: true, count: 1, tick: 500, maxCount: 3, intervalMs: 500, enforce: false), Is.True);
        Assert.That(budget.Unenforced, Is.Null);
        Assert.That(budget.TakePoint(0, 2, held: true, count: 1, tick: 501, maxCount: 3, intervalMs: 2000, enforce: false), Is.True);
        Assert.That(budget.Unenforced, Is.Null, "inside the allowance");
        Assert.That(budget.TakePoint(0, 3, held: true, count: 1, tick: 502, maxCount: 3, intervalMs: 2000, enforce: false), Is.True);
        Assert.That(budget.Unenforced, Does.Contain("early"));
        // More than 20 attacks, and the packet order, are refused in either mode.
        Assert.That(budget.TakePoint(1, 0, held: true, count: SkillHitRules.MaxPointAttacks + 1, tick: 600, maxCount: 3, enforce: false), Is.False);
        Assert.That(budget.TakePoint(1, 0, held: true, count: 255, tick: 600, maxCount: 40, enforce: false), Is.False);
        Assert.That(budget.TakePoint(1, 0, held: true, count: SkillHitRules.MaxPointAttacks, tick: 600, maxCount: 3, enforce: false), Is.True);
        Assert.That(budget.TakePoint(0, 7, held: true, count: 1, tick: 9000, maxCount: 3, intervalMs: 500, enforce: false), Is.False);
    }

    [Test]
    public void MoveWindowIsNotShortenedByFastTicks() {
        // 3000 units a second, ticks at twice the server's time: 1500 a second by the ticks.
        var window = new MoveWindow();
        int found = 0;
        for (int i = 0; i <= 100; i++) {
            found += window.Add(i * 200, new Vector3(i * 300, 0, 0), i * 100L) != null ? 1 : 0;
        }
        Assert.That(found, Is.EqualTo(0), "by the ticks alone");

        window = new MoveWindow();
        var lag = new LagBudget();
        for (int i = 0; i <= 100; i++) {
            found += window.Add(i * 200, new Vector3(i * 300, 0, 0), i * 100L, lag) != null ? 1 : 0;
        }
        Assert.That(found, Is.GreaterThanOrEqualTo(8), "found about once a second once the budget is gone");
        Assert.That(lag.Level(10_000), Is.LessThan(600), "drawn again as it refills");

        // The same with the clock found fast: nothing is drawn.
        window = new MoveWindow();
        lag = new LagBudget();
        found = 0;
        for (int i = 0; i <= 100; i++) {
            found += window.Add(i * 200, new Vector3(i * 300, 0, 0), i * 100L, lag, clockFast: true) != null ? 1 : 0;
        }
        Assert.That(found, Is.GreaterThanOrEqualTo(9));
        Assert.That(lag.Level(10_000), Is.EqualTo(2000));
    }

    [Test]
    public void MoveWindowTakesPositionsHeldUpTogether() {
        // Running at 1500 a second, a position every 100 ms; 3 s of them are held up and arrive at once.
        var window = new MoveWindow();
        var lag = new LagBudget();
        for (int t = 0; t <= 20_000; t += 100) {
            bool held = t >= 5000 && t < 8000;
            Assert.That(window.Add(t, new Vector3(t * 1.5f, 0, 0), held ? 8000 : t, lag), Is.Null, $"at {t}");
        }
        Assert.That(lag.Level(8100), Is.InRange(1000, 1200), "one draw of the window's length for the burst");
    }
}
