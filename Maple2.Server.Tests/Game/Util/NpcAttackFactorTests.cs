using System;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Formulas;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class NpcAttackFactorTests {
    // The npc-caster part of the damage formula: factor x Rate x pap x res / defense (made-up numbers).
    private static double Hit(bool npcCaster, double rate, double pap, double defense, double par) {
        return DamageCalculator.AttackDamageFactor(npcCaster) * rate * pap * Damage.CalculateResistance(par, 0) / defense;
    }

    [Test]
    public void NpcCasterUsesItsOwnFactor() {
        Assert.That(DamageCalculator.AttackDamageFactor(true), Is.EqualTo(Constant.NpcAttackDamageFactor));
        Assert.That(DamageCalculator.AttackDamageFactor(false), Is.EqualTo(Constant.AttackDamageFactor));
        Assert.That(Constant.NpcAttackDamageFactor, Is.EqualTo(2.85));
    }

    [Test]
    public void NpcHitIsSpreadOverItsTopFourPercent() {
        Assert.That(DamageCalculator.NpcAttackRoll(0), Is.EqualTo(1));
        Assert.That(DamageCalculator.NpcAttackRoll(0.5), Is.EqualTo(0.98).Within(1e-9));
        Assert.That(DamageCalculator.NpcAttackRoll(1), Is.EqualTo(0.96).Within(1e-9));
        // A full hit of 452 never falls below 433.
        Assert.That(Math.Floor(452 * DamageCalculator.NpcAttackRoll(0.999999)), Is.EqualTo(433));
    }

    [Test]
    public void NpcHitScalesWithRatePapResistanceAndDefense() {
        Assert.That(Hit(true, 1.0, 1000, 100, 0), Is.EqualTo(28.5).Within(1e-9));
        Assert.That(Hit(false, 1.0, 1000, 100, 0), Is.EqualTo(40).Within(1e-9));
        Assert.That(Hit(true, 0.5, 1000, 100, 0), Is.EqualTo(14.25).Within(1e-9));
        Assert.That(Hit(true, 1.0, 1000, 200, 0), Is.EqualTo(14.25).Within(1e-9));
        Assert.That(Hit(true, 1.0, 1000, 100, 300), Is.EqualTo(22.8).Within(1e-9));
    }
}
