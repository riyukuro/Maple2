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
}
