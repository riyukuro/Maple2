namespace Maple2.Server.Game.Util;

public static class FieldEntryRules {
    // Fields to try, in order, when the field a character logs into cannot be made: the return field, then
    // the default return field. The failed field and repeats are left out.
    public static IEnumerable<int> Fallbacks(int failedMapId, int returnMapId, int defaultMapId) {
        if (returnMapId != 0 && returnMapId != failedMapId) {
            yield return returnMapId;
        }
        if (defaultMapId != failedMapId && defaultMapId != returnMapId) {
            yield return defaultMapId;
        }
    }

    // A field with enterAdminLevel above 0 can be picked as a taxi destination by a GM account only.
    public static bool AdminFieldAllowed(int enterAdminLevel, bool gm) {
        return enterAdminLevel <= 0 || gm;
    }
}
