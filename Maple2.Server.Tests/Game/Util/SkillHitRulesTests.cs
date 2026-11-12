using System.Numerics;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class SkillHitRulesTests {
    [Test]
    public void TargetNeedsADeclaredAttackOnce() {
        var budget = new SkillHitBudget();
        Assert.That(budget.TakeTarget(11, 0, 0), Is.False, "nothing declared");
        Assert.That(budget.TakePoint(0, 0, held: false, count: 3, tick: 0), Is.True);
        budget.Declare(11, 0);
        budget.Declare(12, 0);
        budget.Declare(13, 0);
        Assert.That(budget.TakeTarget(11, 1, 0), Is.False, "another attack point");
        Assert.That(budget.TakeTarget(11, 0, 0), Is.True);
        Assert.That(budget.TakeTarget(11, 0, 0), Is.False, "spent");
        Assert.That(budget.TakeTarget(12, 0, 0), Is.True);
        Assert.That(budget.TakeTarget(13, 0, 0), Is.True);
        Assert.That(budget.TakeTarget(14, 0, 0), Is.False);
    }

    [Test]
    public void BouncingArrowPaysOncePerBounce() {
        var budget = new SkillHitBudget();
        Assert.That(budget.TakePoint(0, 0, held: false, count: 1, tick: 0), Is.True);
        budget.Declare(5, 0);
        for (int i = 0; i < 9; i++) {
            Assert.That(budget.TakeTarget(5, 0, bounceCount: 8), Is.True, $"use {i}");
        }
        Assert.That(budget.TakeTarget(5, 0, bounceCount: 8), Is.False);
    }

    [Test]
    public void AttackPointIsSentOncePerCast() {
        var budget = new SkillHitBudget();
        Assert.That(budget.TakePoint(0, 1, held: false, count: 1, tick: 0), Is.False, "first iteration is 0");
        Assert.That(budget.TakePoint(0, 0, held: false, count: 1, tick: 0), Is.True);
        Assert.That(budget.TakePoint(0, 0, held: false, count: 1, tick: 0), Is.False);
        Assert.That(budget.TakePoint(0, 1, held: false, count: 1, tick: 0), Is.False, "not a held skill");
        Assert.That(budget.TakePoint(1, 0, held: false, count: 1, tick: 0), Is.True, "the next attack point");
        Assert.That(budget.TakePoint(2, 0, held: false, count: SkillHitRules.MaxPointAttacks, tick: 0), Is.True);
        Assert.That(budget.TakePoint(3, 0, held: false, count: SkillHitRules.MaxPointAttacks + 1, tick: 0), Is.False);
    }

    [Test]
    public void HeldSkillRepeatsItsAttackPointInOrder() {
        var budget = new SkillHitBudget();
        for (int i = 0; i < 12; i++) {
            Assert.That(budget.TakePoint(0, i, held: true, count: 1, tick: 0), Is.True, $"iteration {i}");
        }
        Assert.That(budget.TakePoint(0, 11, held: true, count: 1, tick: 0), Is.False, "repeated");
        Assert.That(budget.TakePoint(0, 13, held: true, count: 1, tick: 0), Is.False, "skipped");
        Assert.That(budget.TakePoint(0, 12, held: true, count: 1, tick: 0), Is.True);
    }

    [Test]
    public void CastStandsNearThePlayer() {
        var player = new Vector3(100, 100, 150);
        Assert.That(SkillHitRules.CastTooFar(new Vector3(100, 607, 150), player, sinceSyncMs: 50), Is.False, "507 away");
        Assert.That(SkillHitRules.CastTooFar(new Vector3(100, 701, 150), player, sinceSyncMs: 50), Is.True);
        // A player who stands still sends no sync; the position is then not held against the cast.
        Assert.That(SkillHitRules.CastTooFar(new Vector3(100, 701, 150), player, sinceSyncMs: 3000), Is.False);
    }

    [Test]
    public void PointCarriesTheCastPosition() {
        var cast = new Vector3(10, 20, 30);
        Assert.That(SkillHitRules.PointMoved(cast, cast), Is.False);
        Assert.That(SkillHitRules.PointMoved(new Vector3(10, 22, 30), cast), Is.True);
    }

    [Test]
    public void ImpactStaysInReachOfTheCast() {
        Assert.That(SkillHitRules.ImpactReach(0), Is.EqualTo(750f));
        Assert.That(SkillHitRules.ImpactReach(150), Is.EqualTo(900f));
        Assert.That(SkillHitRules.ImpactReach(800), Is.EqualTo(2400f));
        var cast = new Vector3(0, 0, 0);
        Assert.That(SkillHitRules.ImpactTooFar(new Vector3(730, 0, 0), cast, 250), Is.False);
        Assert.That(SkillHitRules.ImpactTooFar(new Vector3(0, 1001, 0), cast, 250), Is.True);
        Assert.That(SkillHitRules.ImpactTooFar(new Vector3(0, 2400, 0), cast, 800), Is.False);
        Assert.That(SkillHitRules.ImpactTooFar(new Vector3(0, 2401, 0), cast, 800), Is.True);
    }

    [Test]
    public void HeldCastKeepsPayingPastTheWindow() {
        var budget = new SkillHitBudget();
        budget.Open(10000);
        long tick = 10000;
        for (int i = 0; i < 20; i++) {
            tick += 550;
            Assert.That(budget.TakePoint(0, i, held: true, count: 1, tick: tick), Is.True, $"iteration {i}");
            budget.Declare(100 + i, 0);
            Assert.That(budget.TooLate(tick + 200), Is.False);
            Assert.That(budget.TakeTarget(100 + i, 0, bounceCount: 0), Is.True);
        }
        Assert.That(tick, Is.GreaterThan(10000 + 2 * SkillHitRules.HitWindowMs));
        Assert.That(budget.TooLate(tick + SkillHitRules.HitWindowMs), Is.False);
        Assert.That(budget.TakePoint(0, 20, held: true, count: 1, tick: tick + SkillHitRules.HitWindowMs + 1), Is.False, "the hold stopped");
    }

    [Test]
    public void OtherCastStopsAtTheWindow() {
        var budget = new SkillHitBudget();
        budget.Open(10000);
        Assert.That(budget.TakePoint(0, 0, held: false, count: 1, tick: 14000), Is.True);
        budget.Declare(100, 0);
        Assert.That(budget.TooLate(15000), Is.False);
        Assert.That(budget.TooLate(15100), Is.True, "the attack point packet does not restart the window");
        Assert.That(budget.TakePoint(1, 0, held: false, count: 1, tick: 15100), Is.False);
    }

    [Test]
    public void HitsEndFiveSecondsAfterTheCast() {
        Assert.That(SkillHitRules.TooLate(13550, 10000), Is.False);
        Assert.That(SkillHitRules.TooLate(15000, 10000), Is.False);
        Assert.That(SkillHitRules.TooLate(15001, 10000), Is.True);
    }

    [Test]
    public void PointDeclaresNoMoreThanTheAttackTargets() {
        Assert.That(SkillHitRules.MaxAttacks(3, false), Is.EqualTo(3));
        Assert.That(SkillHitRules.MaxAttacks(0, false), Is.EqualTo(0));
        Assert.That(SkillHitRules.MaxAttacks(0, true), Is.EqualTo(1));
        Assert.That(SkillHitRules.MaxAttacks(40, false), Is.EqualTo(SkillHitRules.MaxPointAttacks));

        var budget = new SkillHitBudget();
        Assert.That(budget.TakePoint(0, 0, held: false, count: 4, tick: 0, maxCount: 3), Is.False);
        Assert.That(budget.TakePoint(0, 0, held: false, count: 3, tick: 0, maxCount: 3), Is.True);
        Assert.That(new SkillHitBudget().TakePoint(0, 0, held: false, count: 0, tick: 0, maxCount: 0), Is.True);
    }

    [Test]
    public void HeldIntervalIsTheLoopAtTheMotionSpeed() {
        Assert.That(SkillHitRules.HeldInterval(0.5f, 1f), Is.EqualTo(430)); // 500 x 0.9 - 20
        Assert.That(SkillHitRules.HeldInterval(0.5f, 1.19f), Is.EqualTo(358));
        Assert.That(SkillHitRules.HeldInterval(0f, 1f), Is.EqualTo(0), "no loop");
        Assert.That(SkillHitRules.HeldInterval(0.01f, 1f), Is.EqualTo(0));
    }

    [Test]
    public void HeldIterationComesNoFasterThanItsLoop() {
        // On time: every 500 ms on a 430 ms interval.
        var budget = new SkillHitBudget();
        for (int i = 0; i < 20; i++) {
            Assert.That(budget.TakePoint(0, i, held: true, count: 1, tick: i * 500, intervalMs: 430), Is.True, $"iteration {i}");
        }

        // A late iteration and the next right behind it.
        budget = new SkillHitBudget();
        Assert.That(budget.TakePoint(0, 0, held: true, count: 1, tick: 0, intervalMs: 430), Is.True);
        Assert.That(budget.TakePoint(0, 1, held: true, count: 1, tick: 950, intervalMs: 430), Is.True);
        Assert.That(budget.TakePoint(0, 2, held: true, count: 1, tick: 1000, intervalMs: 430), Is.True);
        Assert.That(budget.TakePoint(0, 3, held: true, count: 1, tick: 1500, intervalMs: 430), Is.True);

        // Twice as fast: the time owed runs out.
        budget = new SkillHitBudget();
        int taken = 0;
        for (int i = 0; i < 20 && budget.TakePoint(0, i, held: true, count: 1, tick: i * 215, intervalMs: 430); i++) {
            taken++;
        }
        Assert.That(taken, Is.EqualTo(3)); // due at 430 x i, taken while 215 x i >= 430 x i - 500

        // A 250 ms loop (interval 205): a stall of 1 s, then the iterations it held up arrive together and the
        // hold goes on.
        budget = new SkillHitBudget();
        Assert.That(budget.TakePoint(0, 0, held: true, count: 1, tick: 0, intervalMs: 205), Is.True);
        Assert.That(budget.TakePoint(0, 1, held: true, count: 1, tick: 250, intervalMs: 205), Is.True);
        for (int i = 2; i <= 6; i++) {
            Assert.That(budget.TakePoint(0, i, held: true, count: 1, tick: 1500, intervalMs: 205), Is.True, $"burst {i}");
        }
        for (int i = 7; i < 20; i++) {
            Assert.That(budget.TakePoint(0, i, held: true, count: 1, tick: 1500 + (i - 6) * 250, intervalMs: 205), Is.True, $"after the burst {i}");
        }

        // An iteration that comes too early pays nothing; the ones after it are counted on.
        budget = new SkillHitBudget();
        Assert.That(budget.TakePoint(0, 0, held: true, count: 1, tick: 0, intervalMs: 430), Is.True);
        Assert.That(budget.TakePoint(0, 1, held: true, count: 1, tick: 10, intervalMs: 430), Is.True, "430 - 500");
        Assert.That(budget.TakePoint(0, 2, held: true, count: 1, tick: 20, intervalMs: 430), Is.False, "due at 860");
        Assert.That(budget.TakePoint(0, 3, held: true, count: 1, tick: 790, intervalMs: 430), Is.True, "due at 1290");
        Assert.That(budget.TakePoint(0, 5, held: true, count: 1, tick: 5000, intervalMs: 430), Is.False, "iteration 4 never came");

        // A shorter interval at a higher speed is counted from then on.
        budget = new SkillHitBudget();
        Assert.That(budget.TakePoint(0, 0, held: true, count: 1, tick: 0, intervalMs: 430), Is.True);
        Assert.That(budget.TakePoint(0, 1, held: true, count: 1, tick: 300, intervalMs: 280), Is.True);
        Assert.That(budget.TakePoint(0, 2, held: true, count: 1, tick: 600, intervalMs: 280), Is.True);

        // Not checked without a loop, and each attack point on its own.
        budget = new SkillHitBudget();
        for (int i = 0; i < 20; i++) {
            Assert.That(budget.TakePoint(0, i, held: true, count: 1, tick: i), Is.True);
        }
        budget = new SkillHitBudget();
        Assert.That(budget.TakePoint(0, 0, held: true, count: 1, tick: 0, intervalMs: 430), Is.True);
        Assert.That(budget.TakePoint(1, 0, held: true, count: 1, tick: 10, intervalMs: 430), Is.True);
    }

    [Test]
    public void FastMovesAreLoggedOnceInTenSeconds() {
        var window = new MoveWindow();
        Assert.That(window.CountFast(1000), Is.EqualTo(1));
        Assert.That(window.CountFast(1100), Is.EqualTo(0));
        Assert.That(window.CountFast(10999), Is.EqualTo(0));
        Assert.That(window.CountFast(11000), Is.EqualTo(3));
    }

    [Test]
    public void FastMoveIsMeasuredOverOneSecondOnTheGround() {
        var window = new MoveWindow();
        Assert.That(window.Add(0, new Vector3(0, 0, 0), 0), Is.Null);
        Assert.That(window.Add(500, new Vector3(900, 0, 0), 500), Is.Null);
        Assert.That(window.Add(1000, new Vector3(1800, 0, 0), 1000), Is.Null, "1800 in 1 s");
        Assert.That(window.Add(1400, new Vector3(2800, 0, 0), 1400), Is.EqualTo(new Vector3(900, 0, 0)), "1900 in 0.9 s");
        Assert.That(window.Add(1500, new Vector3(2900, 0, 0), 1500), Is.Null, "starts again");

        window = new MoveWindow();
        Assert.That(window.Add(0, new Vector3(0, 0, 5000), 0), Is.Null);
        Assert.That(window.Add(900, new Vector3(100, 0, 0), 900), Is.Null, "a fall");

        window = new MoveWindow();
        Assert.That(window.Add(0, new Vector3(0, 0, 0), 0), Is.Null);
        Assert.That(window.Add(1100, new Vector3(1900, 0, 0), 1100), Is.Null, "over more than 1 s");
        Assert.That(window.Add(1000, new Vector3(9000, 0, 0), 1200), Is.Null, "a tick that runs backwards starts again");
    }

    [Test]
    public void ServerMoveIsNotMeasured() {
        var window = new MoveWindow();
        Assert.That(window.Add(0, new Vector3(0, 0, 0), 0), Is.Null);
        window.ServerMoved(100);
        Assert.That(window.Add(200, new Vector3(5000, 0, 0), 200), Is.Null);
        Assert.That(window.Add(300, new Vector3(0, 0, 0), 2099), Is.Null, "still the old place");
        Assert.That(window.Add(400, new Vector3(5000, 0, 0), 2100), Is.Null, "first position after the grace");
        Assert.That(window.Add(500, new Vector3(7000, 0, 0), 2200), Is.EqualTo(new Vector3(5000, 0, 0)));
    }
}
