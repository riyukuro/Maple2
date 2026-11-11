using Maple2.Model.Enum;

namespace Maple2.Model.Validators;

// The fee of an upload that Ugc mode 1 took and mode 3 has not finished yet: a voucher item or an
// amount of one currency.
public sealed record UgcPendingFee(long CharacterId, long UgcUid, MeretMarketCurrencyType Currency, long Amount, int VoucherItemId);

// One per session. Ugc mode 0 is what the client sends when its picture upload failed
// (CUgcManager::SendUGCMode0, one byte); what the official server did with it is not proven. Here the
// fee of the character's one paid, unfinished upload is given back once. A new mode 1 gives it back
// too, since the client has one upload at a time, so a retry is paid once in total.
public sealed class UgcFeeBook {
    private UgcPendingFee? pending;

    // Mode 1 took the fee. The caller gives back the one before (TakeBack) first.
    public void Paid(UgcPendingFee fee) {
        pending = fee;
    }

    // The fee to give back, or null: no upload is pending, it is finished, it was given back
    // already, or it is another character's.
    public UgcPendingFee? TakeBack(long characterId) {
        if (pending == null || pending.CharacterId != characterId) {
            return null;
        }
        UgcPendingFee fee = pending;
        pending = null;
        return fee;
    }

    // Mode 3 for the upload: true when it is the paid, pending one. Nothing is given back after it.
    public bool Finish(long characterId, long ugcUid) {
        if (pending == null || pending.CharacterId != characterId || pending.UgcUid != ugcUid) {
            return false;
        }
        pending = null;
        return true;
    }
}
