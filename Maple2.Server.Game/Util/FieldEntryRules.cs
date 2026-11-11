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

    // The field a character goes back to (leaving a dungeon, a home, a mini-game): its saved return field, or
    // the default return field when the saved one is unknown or is a GM-only field for a plain account
    // (an account that lost its GM rights while its return field was one of those).
    public static int ReturnMap(int returnMapId, bool known, int enterAdminLevel, bool gm, int defaultMapId) {
        if (!known || !AdminFieldAllowed(enterAdminLevel, gm)) {
            return defaultMapId;
        }
        return returnMapId;
    }

    // A field with enterAdminLevel above 0 can be entered, or picked as a taxi destination, by a GM account only.
    public static bool AdminFieldAllowed(int enterAdminLevel, bool gm) {
        return enterAdminLevel <= 0 || gm;
    }
}
