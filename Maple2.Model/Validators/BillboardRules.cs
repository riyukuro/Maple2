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
    public const int TitleLengthMax = 15;
    public const int DescriptionLengthMax = 100;

    // The dialog sends a title of 1-15 and a description of 1-100 characters, its quotes turned into backquotes, so
    // the text has exactly three parts (UIItemBillBoardDialog::FUN_140c33840, uiitembillboarddialog.cpp:535-546).
    public static bool TextAllowed(string[] parts) {
        return parts.Length == InstallTextParts
            && parts[0].Length is >= 1 and <= TitleLengthMax
            && parts[1].Length is >= 1 and <= DescriptionLengthMax;
    }

    public static BillboardInstallResult InstallResult(bool text, bool itemParameters, bool placed) {
        return text && itemParameters && placed
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
