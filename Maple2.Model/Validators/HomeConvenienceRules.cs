namespace Maple2.Model.Validators;

// The home bank and home doctor calls: RequestHomeBank (u8 kind) and RequestHomeDoctor (no body).
public static class HomeConvenienceRules {
    public const byte BankFromHome = 1;
    public const byte BankFromInventory = 2;

    // The call times are unix seconds; the cooltime constants are milliseconds, the client divides them by 1000.
    // A call is free again once the whole seconds since the last one reach the cooltime.
    public static bool IsReady(long lastCallSeconds, int cooltimeMs, long nowSeconds) {
        if (lastCallSeconds <= 0) {
            return true;
        }
        return nowSeconds - lastCallSeconds >= cooltimeMs / 1000;
    }

    // Kind 1 is the home dialog's button, kind 2 the inventory's bank button, which the client enables for premium
    // members and for a character whose PC cafe level is above 0. The server has no PC cafe: it sends that level as 0
    // in the field add record and never sends the packet that sets it, so premium is the only way in.
    public static bool IsBankKindAllowed(byte kind, bool premium) {
        return kind switch {
            BankFromHome => true,
            BankFromInventory => premium,
            _ => false,
        };
    }

    // The client offers the doctor only in the player's own home.
    public static bool IsDoctorAllowed(bool inHome, long homeOwnerAccountId, long accountId) {
        return inHome && homeOwnerAccountId == accountId;
    }
}
