namespace Maple2.Model.Validators;

// PlayInstrument 17, the compose result. After it sends a compose (8), the client's score writer locks its movie; a
// composed score (8) or one of these results unlocks it (UIWriteMusicDialog::OnMakeScoreResult). Any other value
// leaves the writer locked.
public enum ComposeResult : byte {
    // Unlocks the writer: the player can fix the score and send it again.
    Retry = 8,
    // Unlocks the writer and ends the writing (its mode 0): the score item cannot be written.
    Close = 10,
    // Unlocks the writer and shows text 0x8b7, the text the client gives a banned word in its search inputs.
    BannedWord = 0x10,
}

public static class InstrumentRules {
    // The client's score writer refuses a score that plays for less than two seconds.
    public const int MinScoreLengthMs = 2000;

    // The client plays no expired instrument: its play, improvise and ensemble requests check the item first
    // (CItem::FUN_140528980, the s_play_instrument_invalid_instrument box). Four KR instruments expire (34000003,
    // 34000038 and 34000051 among them), and an expired one stays in the bag until a sort takes it out.
    public static bool IsPlayable(long expiryTime, long now) => expiryTime <= 0 || now < expiryTime;

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

    // The result a refused compose sends, or null when the score can be composed. The item must be a blank score
    // with uses left (Close); the title is checked against the server's ban list (BannedWord), which can hold a
    // word the client's filter passed; then the client's own checks (Retry).
    public static ComposeResult? ComposeRefusal(bool scoreUsable, bool composed, string title, Func<string, bool> isBanned,
                                                string mml, int noteLengthMax, int lengthMs) {
        if (!scoreUsable || composed) {
            return ComposeResult.Close;
        }
        if (!string.IsNullOrWhiteSpace(title) && isBanned(title)) {
            return ComposeResult.BannedWord;
        }
        return IsComposable(title, isBanned, mml, noteLengthMax, lengthMs) ? null : ComposeResult.Retry;
    }
}
