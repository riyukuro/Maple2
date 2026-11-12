namespace Maple2.Model.Validators;

// What the client asks before it starts a bank or black market cash call (CCashCallManager @0x140b03d70 and
// @0x140b03980, cashcallmanager.cpp). Its other checks (the window already open, the casting or riding states) are
// the client's own.
public static class CashCallRules {
    public static bool CanOpenBank(bool dead) => !dead;

    // IsCashCallMarketAllowed: the field's cashCall market flag (cashCallMarketProhibit off).
    public static bool CanCallBlackMarket(bool dead, bool marketAllowed) => !dead && marketAllowed;
}
