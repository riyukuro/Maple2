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
    // members and for a character at a PC bang (UIInventoryDialog's bank button: PC::FUN_141658f30, PC bang grade
    // above 0, or PC::IsVip). The home dialog is the panel of the player's own home, so kind 1 needs that home, like
    // the doctor.
    public static bool IsBankKindAllowed(byte kind, bool premium, bool pcBang, bool inOwnHome) {
        return kind switch {
            BankFromHome => inOwnHome,
            BankFromInventory => premium || pcBang,
            _ => false,
        };
    }

    // Premium runs while its end time is still ahead: the same strict test the client and the rest of the server use.
    public static bool IsPremium(long premiumEndSeconds, long nowSeconds) {
        return premiumEndSeconds > nowSeconds;
    }

    // home_bank (70400061, 74000003 "Use the safe inside your home once"): only the home dialog's call counts,
    // not the premium bank button in the inventory.
    public static bool CountsAsHomeBank(byte kind) {
        return kind == BankFromHome;
    }

    // The client offers the doctor only in the player's own home.
    public static bool IsDoctorAllowed(bool inHome, long homeOwnerAccountId, long accountId) {
        return inHome && homeOwnerAccountId == accountId;
    }
}
