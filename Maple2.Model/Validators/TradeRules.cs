using Maple2.Model.Enum;
using Maple2.Model.Game;

namespace Maple2.Model.Validators;

// Trade 0x0049 mode 8 (add item): i64 item uid, i32 amount, i32 trade slot.
// The client asks its own "may be traded" check before it sends the packet: no binding, and
// either the trade flag or the limited-trade flag with trades left. The server asks the same.
public static class TradeRules {
    public static bool IsOfferAmount(int amount) {
        return amount > 0;
    }

    // Black-market-only items go out with the trade bit, as KMS2 sends them, and the client's own checks read only
    // that bit (CItemTransfer FUN_140635590 for the trade slot, CanDestroy for the drop; neither asks the item's
    // TransferType). So the server keeps the hand trade and the field drop to what the item's data allows: below
    // its trade rarity, or with trades left. Every other transfer type is decided by the flag.
    public static bool IsHandTradeable(TransferType type, int rarity, int tradeMaxRarity, int remainTrades) {
        if (type != TransferType.BlackMarketOnly) {
            return true;
        }
        return rarity < tradeMaxRarity || remainTrades > 0;
    }

    public static bool IsHandTradeable(Item item) {
        return IsHandTradeable(item.Metadata.Limit.TransferType, item.Rarity, item.Metadata.Limit.TradeMaxRarity,
            item.Transfer?.RemainTrades ?? 0);
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

    // A gem in one of the item's sockets the trade and the black market let go with it (CItemSocket::IsGemTradeable,
    // FUN_140622790 / FUN_1406227f0): not bound, not locked, and of the one transfer type the client's own rule
    // (CItemTransfer::ComputeRule) gives the trade bit, Tradable.
    public static bool IsGemTradeable(bool bound, bool usable, TransferType type) {
        return !bound && usable && type == TransferType.Tradable;
    }

    // Every gem in the sockets passes IsGemTradeable; empty slots and gems with no data are skipped (the client's
    // loop does the same). gemType gives a gem item's transfer type, null when it has no data.
    public static bool AreGemsTradeable(ItemSocket? socket, Func<int, TransferType?> gemType, long now) {
        if (socket == null) {
            return true;
        }
        foreach (ItemGemstone? gem in socket.Sockets) {
            if (gem == null || gem.ItemId == 0) {
                continue;
            }
            TransferType? type = gemType(gem.ItemId);
            if (type == null) {
                continue;
            }
            bool bound = gem.Binding != null && gem.Binding.CharacterId != 0;
            if (!IsGemTradeable(bound, ItemLockRules.IsUsable(gem.IsLocked, gem.UnlockTime, now), type.Value)) {
                return false;
            }
        }
        return true;
    }
}
