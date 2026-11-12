using System.Numerics;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class SkillHitRulesTests {
    [Test]
    public void TargetNeedsADeclaredAttackOnce() {
        var budget = new SkillHitBudget();
        Assert.That(budget.TakeTarget(11, 0, 0), Is.False, "nothing declared");
        Assert.That(budget.TakePoint(0, 0, held: false, count: 3), Is.True);
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
        Assert.That(budget.TakePoint(0, 0, held: false, count: 1), Is.True);
        budget.Declare(5, 0);
        for (int i = 0; i < 9; i++) {
            Assert.That(budget.TakeTarget(5, 0, bounceCount: 8), Is.True, $"use {i}");
        }
        Assert.That(budget.TakeTarget(5, 0, bounceCount: 8), Is.False);
    }

    [Test]
    public void AttackPointIsSentOncePerCast() {
        var budget = new SkillHitBudget();
        Assert.That(budget.TakePoint(0, 1, held: false, count: 1), Is.False, "first iteration is 0");
        Assert.That(budget.TakePoint(0, 0, held: false, count: 1), Is.True);
        Assert.That(budget.TakePoint(0, 0, held: false, count: 1), Is.False);
        Assert.That(budget.TakePoint(0, 1, held: false, count: 1), Is.False, "not a held skill");
        Assert.That(budget.TakePoint(1, 0, held: false, count: 1), Is.True, "the next attack point");
        Assert.That(budget.TakePoint(2, 0, held: false, count: SkillHitRules.MaxPointAttacks), Is.True);
        Assert.That(budget.TakePoint(3, 0, held: false, count: SkillHitRules.MaxPointAttacks + 1), Is.False);
    }

    [Test]
    public void HeldSkillRepeatsItsAttackPointInOrder() {
        var budget = new SkillHitBudget();
        for (int i = 0; i < 12; i++) {
            Assert.That(budget.TakePoint(0, i, held: true, count: 1), Is.True, $"iteration {i}");
        }
        Assert.That(budget.TakePoint(0, 11, held: true, count: 1), Is.False, "repeated");
        Assert.That(budget.TakePoint(0, 13, held: true, count: 1), Is.False, "skipped");
        Assert.That(budget.TakePoint(0, 12, held: true, count: 1), Is.True);
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
    public void HitsEndFiveSecondsAfterTheCast() {
        Assert.That(SkillHitRules.TooLate(13550, 10000), Is.False);
        Assert.That(SkillHitRules.TooLate(15000, 10000), Is.False);
        Assert.That(SkillHitRules.TooLate(15001, 10000), Is.True);
    }
}
