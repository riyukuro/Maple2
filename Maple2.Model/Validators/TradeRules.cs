using Maple2.Model.Enum;

namespace Maple2.Model.Validators;

// Trade 0x0049 mode 8 (add item): i64 item uid, i32 amount, i32 trade slot.
// The client asks its own "may be traded" check before it sends the packet: no binding, and
// either the trade flag or the limited-trade flag with trades left. The server asks the same.
public static class TradeRules {
    public static bool IsOfferAmount(int amount) {
        return amount > 0;
    }

    public static bool IsTradeable(TransferFlag flag, int remainTrades, bool bound) {
        if (bound) {
            return false;
        }
        if (flag.HasFlag(TransferFlag.LimitTrade) && remainTrades > 0) {
            return true;
        }
        return flag.HasFlag(TransferFlag.Trade);
    }
}
