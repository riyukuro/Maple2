namespace Maple2.Model.Validators;

// What the client asks before it starts a bank, black market or medic cash call (CCashCallManager @0x140b03d70,
// @0x140b03980 and @0x140b03590, cashcallmanager.cpp). Its other checks (the window already open, the casting or riding states) are
// the client's own.
public static class CashCallRules {
    public static bool CanOpenBank(bool dead) => !dead;

    // IsCashCallMarketAllowed: the field's cashCall market flag (cashCallMarketProhibit off).
    public static bool CanCallBlackMarket(bool dead, bool marketAllowed) => !dead && marketAllowed;

    // CCashCallManager @0x140b03590: alive, a death penalty count above 0, and IsCashCallMedicAllowed (the field's
    // cashCall medic flag, cashCallMedicProhibit off).
    public static bool CanCallMedic(bool dead, bool penalty, bool medicAllowed) => !dead && penalty && medicAllowed;
}
