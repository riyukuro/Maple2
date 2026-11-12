namespace Maple2.Model.Validators;

// PlayerHost mode 7: the i32 the billboard dialog turns into its install banner.
public enum BillboardInstallResult {
    Success = 0,
    Fail = 1,
    MaxPerAccount = 2,
    MaxPerField = 3,
}

public static class BillboardRules {
    // The install item's text is title'description'public flag; the item must name its interact object.
    public const int InstallTextParts = 3;

    public static BillboardInstallResult InstallResult(int textParts, bool itemParameters, bool placed) {
        return textParts >= InstallTextParts && itemParameters && placed
            ? BillboardInstallResult.Success
            : BillboardInstallResult.Fail;
    }

    // The item's scale parameter is the float the client sets as the billboard entity's scale; none or unreadable is 1.
    public static float Scale(string? parameter) {
        return float.TryParse(parameter, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float scale) && scale > 0f
            ? scale
            : 1f;
    }

    // A board's id for the window packet's last long: never 0 (the client takes 0 for "no board shown") and never
    // given twice while the server runs. Boards do not outlive the server, so the count starts again with it.
    private static long lastBoardUid;

    public static long NextBoardUid() {
        return System.Threading.Interlocked.Increment(ref lastBoardUid);
    }
}
