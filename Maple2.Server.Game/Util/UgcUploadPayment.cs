using Maple2.Model.Enum;

namespace Maple2.Server.Game.Util;

// How a workshop item upload is paid. The voucher byte comes from the client, so every outcome that does
// not take a coupon or the full price ends the upload.
public static class UgcUploadPayment {
    public enum Result {
        Voucher, // take one FreeDesignCoupon
        Pay, // take the create price in the design's currency
        NoVoucher, // the client asked for a coupon it does not hold
        Lack, // not enough of the design's currency
        UnknownCurrency,
    }

    public static bool IsPaid(Result result) {
        return result is Result.Voucher or Result.Pay;
    }

    public static Result Decide(bool useVoucher, bool hasVoucher, MeretMarketCurrencyType currency, bool canPay) {
        if (useVoucher) {
            return hasVoucher ? Result.Voucher : Result.NoVoucher;
        }

        switch (currency) {
            case MeretMarketCurrencyType.Meso:
            case MeretMarketCurrencyType.Meret:
            case MeretMarketCurrencyType.RedMeret:
                return canPay ? Result.Pay : Result.Lack;
            default:
                return Result.UnknownCurrency;
        }
    }
}
