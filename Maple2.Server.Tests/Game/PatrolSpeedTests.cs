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

    // An npc without the sequence's speed (Anos 11003313: no walkSpeed or runSpeed) moves at the patrol's own speed;
    // one with it, or a patrol without a speed, keeps the multiplier.
    [Test]
    public void SpeedlessNpcTakesThePatrolSpeed() {
        Assert.That(PatrolRules.SpeedlessBase(2, "Walk_A", 0, 0, air: false), Is.EqualTo(200f));
        Assert.That(PatrolRules.SpeedlessBase(220, "Run_A", 0, 0, air: false), Is.EqualTo(220f));
        Assert.That(PatrolRules.SpeedlessBase(10, "Run_A", 120, 0, air: true), Is.EqualTo(1000f));
        Assert.That(PatrolRules.SpeedlessBase(2, "Walk_A", 120, 0, air: false), Is.EqualTo(0f));
        Assert.That(PatrolRules.SpeedlessBase(0, "Walk_A", 0, 0, air: false), Is.EqualTo(0f));
    }

    // 52000167_qd's patrols 403-406 carry 220-280: already u/s.
    [Test]
    public void LargeGroundSpeedIsUnitsPerSecond() {
        Assert.That(PatrolRules.GroundMultiplier(220, "Run_A", 240, 400) * 400, Is.EqualTo(220f).Within(0.001f));
        Assert.That(PatrolRules.GroundMultiplier(99, "Run_A", 240, 400) * 400, Is.EqualTo(9900f).Within(0.001f));
    }

    // 02000255_bf MS2PatrolData4 holds Idle_A 3000 ms at a waypoint; 52000045_qd's 15 is not read as ms (one play).
    [TestCase(3000, 3000f)]
    [TestCase(100, 100f)]
    [TestCase(99, -1f)]
    [TestCase(15, -1f)]
    [TestCase(0, -1f)]
    public void ArriveAnimationTimeIsMillisecondsFromHundred(int time, float duration) {
        Assert.That(PatrolRules.ArriveDuration(time), Is.EqualTo(duration));
    }

    // 52000064_qd MS2PatrolData_1103 names "Run_A " (trailing space) as its approach animation.
    [Test]
    public void SequenceNameIsTrimmed() {
        Assert.That(PatrolRules.SequenceName("Run_A "), Is.EqualTo("Run_A"));
        Assert.That(PatrolRules.SequenceName(null), Is.EqualTo(string.Empty));
    }
}
