using Maple2.Model.Enum;
using Maple2.Model.Game;

namespace Maple2.Server.Game.Util;

/// <summary>
/// Time counted for trophies and quests: play time (one count for each full minute logged in) and emotion time
/// (seconds in an emotion such as sitting, reported once five or more are due, as KMS2 sends 5 every 5 s).
/// </summary>
public static class TrophyTimeRules {
    public const long PlayTimeIntervalMs = 60_000;
    public const long EmotionStepMs = 5_000;

    /// <summary>
    /// The emotion skill a state sync holds the player in, or 0. The client's record carries the emotion skill id
    /// (90200041 sit on the ground, 90200042 sit on a chair) in its first optional int while the state is an emotion.
    /// </summary>
    public static int EmotionSkillId(StateSync sync) {
        if (sync.State is not (ActorState.Emotion or ActorState.EmotionIdle)) {
            return 0;
        }
        return sync.Flags.HasFlag(StateSync.Flag.Flag1) ? sync.Flag1Unknown : 0;
    }

    /// <summary>
    /// Adds elapsed time to an emotion's time and returns the whole seconds to count: 0 until five seconds are due,
    /// then every due second; the rest of a second is kept.
    /// </summary>
    public static int EmotionSeconds(ref long accumulatedMs, long elapsedMs) {
        if (elapsedMs > 0) {
            accumulatedMs += elapsedMs;
        }
        if (accumulatedMs < EmotionStepMs) {
            return 0;
        }
        int seconds = (int) (accumulatedMs / 1000);
        accumulatedMs -= seconds * 1000L;
        return seconds;
    }

    /// <summary>
    /// The full minutes passed since the last counted minute; the tick of the last counted minute moves on by them.
    /// </summary>
    public static int PlayMinutes(ref long lastTick, long tick) {
        if (tick - lastTick < PlayTimeIntervalMs) {
            return 0;
        }
        int minutes = (int) ((tick - lastTick) / PlayTimeIntervalMs);
        lastTick += minutes * PlayTimeIntervalMs;
        return minutes;
    }
}
