using System.Collections.Generic;

namespace Maple2.Model.Validators;

// The global portal (timed event popup). The client's dialog reads exactly three button captions after the popup
// text and the sound, whatever the number of fields in the time event row, and sends back the button it was given.
public static class GlobalPortalRules {
    public const int CaptionCount = 3;

    // The three captions of the announce: the row's field names, cut at three, empty where the row has fewer.
    public static string[] Captions(IReadOnlyList<string> names) {
        string[] captions = new string[CaptionCount];
        for (int i = 0; i < CaptionCount; i++) {
            captions[i] = i < names.Count ? names[i] ?? string.Empty : string.Empty;
        }
        return captions;
    }

    // The constant GlobalPortalMinLevel (KR 0, the default row 10): the dialog does not show the announce to a player
    // below it (UIGlobalPortalDialog::vfunc_72, uiglobalportaldialog.cpp:149) and has the notice of its mode 4 for an
    // enter below it. 0 is no limit.
    public static bool LevelAllowed(int level, int minLevel) => minLevel <= 0 || level >= minLevel;

    // The button index of an enter request names a field of the row.
    public static bool EntryValid(int index, int entryCount) => index >= 0 && index < entryCount && index < CaptionCount;
}
