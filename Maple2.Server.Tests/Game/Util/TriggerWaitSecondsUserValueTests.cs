using Maple2.Server.Game.Trigger;

namespace Maple2.Server.Tests.Game.Util;

// wait_seconds_user_value waits the user value's seconds from the state's start, like wait_tick. Made-up values.
public class TriggerWaitSecondsUserValueTests {
    [Test]
    public void WaitsTheUserValueSeconds() {
        Assert.That(TriggerContext.WaitSecondsElapsed(10_000, 39_999, 30), Is.False);
        Assert.That(TriggerContext.WaitSecondsElapsed(10_000, 40_000, 30), Is.False);
        Assert.That(TriggerContext.WaitSecondsElapsed(10_000, 40_001, 30), Is.True);
        // An unset key reads 0: the wait ends on the next tick.
        Assert.That(TriggerContext.WaitSecondsElapsed(10_000, 10_001, 0), Is.True);
    }
}
