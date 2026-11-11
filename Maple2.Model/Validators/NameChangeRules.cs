using Maple2.Model.Enum;

namespace Maple2.Model.Validators;

// The name change item in game. The game scene reads no CharacterCreate packet, so a refused name is
// answered with what it can show: the already-taken flag of the name check answer, or a message box with
// one of the client's name change texts.
public static class NameChangeRules {
    public enum Refusal {
        None,
        Invalid, // blank, a character the region does not allow, too short or too long
        Banned,
        Taken,
    }

    // width: the client's name width for the region, negative for a character it does not allow.
    public static Refusal Check(bool blank, int width, int min, int max, bool banned, bool taken) {
        if (blank || width < min || width > max) {
            return Refusal.Invalid;
        }
        if (banned) {
            return Refusal.Banned;
        }
        return taken ? Refusal.Taken : Refusal.None;
    }

    // The text of the message box: the client's own texts for a refused rename, each with the name.
    // The system text has a second argument, the error code.
    public static (StringCode Code, string[] Args) Text(Refusal refusal, string name) {
        return refusal switch {
            Refusal.Banned => (StringCode.s_change_charname_err_bad_words, [name]),
            Refusal.Taken => (StringCode.s_change_charname_err_already_taken, [name]),
            _ => (StringCode.s_change_charname_err_system, [name, "0"]),
        };
    }
}
