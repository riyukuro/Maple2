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

    // 52000066's mine cart (run 400, walk 240) on MS2PatrolData_201 (speed 7): 700 u/s, so it covers the 5,175 units
    // of track and reaches box 9500 (y up to -515, track end y -1125) before train03 destroys it 10 s after the start.
    [Test]
    public void GroundPatrolMovesAtPatrolSpeedTimesHundred() {
        float speed = PatrolRules.GroundMultiplier(7, "Run_A", 240, 400) * 400;
        Assert.That(speed, Is.EqualTo(700f).Within(0.001f));
        Assert.That((4050 - -515) / speed, Is.LessThan(10f));
        Assert.That(PatrolRules.GroundMultiplier(2, "Walk_A", 120, 320) * 120, Is.EqualTo(200f).Within(0.001f));
    }

    [Test]
    public void GroundPatrolWithoutSpeedKeepsSequenceSpeed() {
        Assert.That(PatrolRules.GroundMultiplier(0, "Run_A", 240, 400), Is.EqualTo(1f));
        Assert.That(PatrolRules.GroundMultiplier(7, "Run_A", 0, 0), Is.EqualTo(1f));
    }

    // 52000167_qd's patrols 403-406 carry 220-280: already u/s.
    [Test]
    public void LargeGroundSpeedIsUnitsPerSecond() {
        Assert.That(PatrolRules.GroundMultiplier(220, "Run_A", 240, 400) * 400, Is.EqualTo(220f).Within(0.001f));
        Assert.That(PatrolRules.GroundMultiplier(99, "Run_A", 240, 400) * 400, Is.EqualTo(9900f).Within(0.001f));
    }
}
