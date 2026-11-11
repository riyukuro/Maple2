namespace Maple2.Model.Validators;

public static class InstrumentRules {
    // The client's score writer refuses a score that plays for less than two seconds.
    public const int MinScoreLengthMs = 2000;

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
