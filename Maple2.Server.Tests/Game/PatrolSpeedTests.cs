using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// Air patrols fly at patrolSpeed x 100 u/s whatever the npc's own speeds.
public class PatrolSpeedTests {
    [Test]
    public void AirSpeedIsPatrolSpeedTimesHundred() {
        Assert.That(PatrolRules.AirSpeed(6), Is.EqualTo(600f));
        Assert.That(PatrolRules.AirSpeed(8), Is.EqualTo(800f));
    }

    [Test]
    public void MultiplierDividesBySequenceBase() {
        // The dock boat: run 400, walk 240, only Run_A.
        Assert.That(PatrolRules.AirMultiplier(6, "Run_A", 240, 400) * 400, Is.EqualTo(600f).Within(0.001f));
        Assert.That(PatrolRules.AirMultiplier(6, "Walk_A", 240, 400) * 240, Is.EqualTo(600f).Within(0.001f));
    }

    [Test]
    public void NoBaseSpeedKeepsOldMultiplier() {
        Assert.That(PatrolRules.AirMultiplier(6, "Run_A", 0, 0), Is.EqualTo(3f));
    }
}
