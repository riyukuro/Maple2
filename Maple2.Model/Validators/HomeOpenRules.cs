namespace Maple2.Model.Validators;

// RequestCube 24 (home visit setting, MyPC::SendHouseSetting): a kind byte, then a text.
// Kind 0 opens the home to all, kind 1 asks visitors for the six digits in the text, kind 2 closes it to visitors.
public static class HomeOpenRules {
    public const byte Public = 0;
    public const byte Password = 1;
    public const byte Private = 2;

    public const int PasscodeLength = 6;

    // The stored passcode of a private home. It is not six digits, so no visitor's entry equals it and
    // no client can set it as a password.
    public const string PrivatePasscode = "private";

    public static bool IsPrivate(string? passcode) {
        return passcode == PrivatePasscode;
    }

    public static bool IsPasscode(string text) {
        return text.Length == PasscodeLength && text.All(c => c is >= '0' and <= '9');
    }

    // The passcode to store for a setting; false for a kind the client does not send or a password that is not six digits.
    public static bool TryGetPasscode(byte kind, string text, out string passcode) {
        switch (kind) {
            case Public:
                passcode = string.Empty;
                return true;
            case Password when IsPasscode(text):
                passcode = text;
                return true;
            case Private:
                passcode = PrivatePasscode;
                return true;
            default:
                passcode = string.Empty;
                return false;
        }
    }
}
