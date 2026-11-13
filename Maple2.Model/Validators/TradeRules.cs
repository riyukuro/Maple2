using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;

namespace Maple2.Model.Validators;

// Trade 0x0049 mode 8 (add item): i64 item uid, i32 amount, i32 trade slot.
// The client asks its own "may be traded" check before it sends the packet: no binding, and
// either the trade flag or the limited-trade flag with trades left. The server asks the same.
public static class TradeRules {
    // Trade 0 (a request, CTradeManagerEx::SendTradeMode0 from the player menu): the client tests only its own
    // trade window and its GM action block before it sends, and the one asked tests only its "refuse trades"
    // option. Who may be asked is the server's to say, in the answers the client has a text for
    // (OnTradeRequestFailed): 6 "not possible with this target" for oneself and a block either way,
    // 1 "too far away" past the constants' TradeDistance (500, 0 for no limit).
    public static TradeError? CheckRequest(long requesterId, long targetId, bool blockedByRequester, bool blockedByTarget,
                                           Vector3 requester, Vector3 target, int maxDistance) {
        if (requesterId == targetId || blockedByRequester || blockedByTarget) {
            return TradeError.s_trade_error_decline;
        }
        if (maxDistance > 0 && Vector3.Distance(requester, target) > maxDistance) {
            return TradeError.s_trade_error_distance;
        }
        return null;
    }

    // Trade 0x0049 mode 10 (the meso offer): 0 takes the offer back; any other amount lies in the constants'
    // TradeMinMeso..TradeMaxMeso (FUN_14087d200). The client puts 0 in the input and shows
    // s_trade_error_invalid_meso_range for an amount outside it (UITradeDialogEx::ValidateMeso).
    public static bool IsMesoOffer(long amount, long min, long max) {
        return amount == 0 || (amount >= min && amount <= max);
    }

    // The fee taken from the mesos a trader receives, as the client shows it under the other player's offer
    // (UITradeDialogEx::UpdateFee, FUN_14087d150): mesos * TradeFeePercent / 100 in whole numbers, then times
    // (1 - rate) in float and cut to a whole number, where rate is the receiver's own reduce_meso_trade_fee
    // (special attribute 0x40, total / 1000). Never below 0. Chosen here: never above the mesos either (the client
    // does not cap it), so a reduction far below 0 cannot take more than the offer and leave the receiver's change
    // negative after the items have moved.
    public static long Fee(long mesos, int percent, float rate) {
        if (mesos <= 0) {
            return 0;
        }
        long fee = mesos * percent / 100;
        fee = (long) (float) (fee * (1.0f - rate));
        return Math.Clamp(fee, 0, mesos);
    }

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
