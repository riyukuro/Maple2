using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Manager;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// deathResistanceHP (10200221, the Berserker's Raging Soul, 8 s): while the buff is on and enabled, hits and dot
// ticks take its holder no lower than 1 health.
public class DeathResistanceTests {
    private static readonly AdditionalEffectMetadata RagingSoul = DisabledBuffTests.Effect(10200221, deathResistance: true);

    [Test]
    public void AnEnabledBuffWithDeathResistanceHoldsAtOneHealth() {
        Assert.That(BuffManager.SumsOf([DisabledBuffTests.Held(RagingSoul, true)]).DeathResistance, Is.True);
        Assert.That(BuffManager.SumsOf([DisabledBuffTests.Held(RagingSoul, false)]).DeathResistance, Is.False);
        Assert.That(BuffManager.SumsOf([DisabledBuffTests.Held(DisabledBuffTests.Effect(1), true)]).DeathResistance, Is.False);

        // 500 health, a hit of 2000: held, the holder keeps 1; without the buff it falls to -1500 (dead).
        Assert.That(NpcTargetRules.HealthChange(500, -2000, true), Is.EqualTo(-499));
        Assert.That(NpcTargetRules.HealthChange(500, -2000, false), Is.EqualTo(-2000));
        // At 1 health a hit takes nothing; a heal still goes in whole.
        Assert.That(NpcTargetRules.HealthChange(1, -300, true), Is.EqualTo(0));
        Assert.That(NpcTargetRules.HealthChange(1, 300, true), Is.EqualTo(300));
    }
}
