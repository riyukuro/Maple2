namespace Maple2.Model.Validators;

public static class InstrumentRules {
    // The client's score writer refuses a score that plays for less than two seconds.
    public const int MinScoreLengthMs = 2000;

    // The time a played score counts for: from the start to the stop, and no longer than the score itself
    // (a written score's length, a ready-made score's playTime; 0 when the score gives none).
    public static long PlayedMs(long elapsedMs, int scoreLengthMs) {
        if (elapsedMs <= 0) {
            return 0;
        }
        return scoreLengthMs > 0 ? Math.Min(elapsedMs, scoreLengthMs) : elapsedMs;
    }

    // The mastery of a played score: its masteryValue per second, up to its masteryValueMax.
    public static int Mastery(long playedMs, int masteryValue, int masteryValueMax) {
        return (int) Math.Clamp(playedMs * masteryValue / 1000, 0, Math.Max(masteryValueMax, 0));
    }

    // The play exp counts half seconds after the first; a stop before that gives none (never a negative amount).
    public static float ExpTicks(long playedMs) {
        return playedMs <= 500 ? 0f : (playedMs - 500) / 500f;
    }

    // PlayInstrument 8 (compose): the checks the client's score writer makes before it sends.
    // The mml is its tabs joined by ':'; the client allows the item's note limit plus one character per tab.
    public static bool IsComposable(string title, Func<string, bool> isBanned, string mml, int noteLengthMax, int lengthMs) {
        if (string.IsNullOrWhiteSpace(title) || isBanned(title)) {
            return false;
        }
        if (mml.Length < 1) {
            return false;
        }

        int tabs = 1;
        foreach (char c in mml) {
            if (c == ':') {
                tabs++;
            }
        }
        return mml.Length <= noteLengthMax + tabs && lengthMs >= MinScoreLengthMs;
    }
}
