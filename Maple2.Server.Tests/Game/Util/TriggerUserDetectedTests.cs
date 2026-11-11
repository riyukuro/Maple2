using Maple2.Server.Game.Trigger;

namespace Maple2.Server.Tests.Game.Util;

// user_detected: a dead player lying in the box is not detected, so !user_detected on an arena box ends the round
// when the player dies. Made-up players.
public class TriggerUserDetectedTests {
    private record Player(bool Dead, bool InBox);

    private static bool Detected(params Player[] players) =>
        TriggerContext.Detected(players, player => player.Dead, player => player.InBox);

    [Test]
    public void LivingPlayerInBox_IsDetected() {
        Assert.That(Detected(new Player(false, true)), Is.True);
    }

    [Test]
    public void DeadPlayerInBox_IsNotDetected() {
        Assert.That(Detected(new Player(true, true)), Is.False);
    }

    [Test]
    public void OneLivingPlayerOfTwo_IsDetected() {
        Assert.That(Detected(new Player(true, true), new Player(false, true)), Is.True);
    }

    [Test]
    public void LivingPlayerOutsideTheBox_IsNotDetected() {
        Assert.That(Detected(new Player(false, false)), Is.False);
    }
}
