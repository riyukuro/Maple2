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
    }

    public enum Currency {
        Meso,
        Meret, // the plain merets (client currency 3)
        RedMeret, // the red merets only (client currency 9)
        RedElseMeret, // the red merets when they cover the price, else the plain merets (client currency 10)
    }

    // The design's priceType as the client converts it (ConvertPriceType, ugccubecategorytable.cpp:445) and checks
    // it before the upload (FUN_1418ff110, uiugcitemeditdialog.cpp:1139): 1 is currency 3, 2 is 9, 3 is 10 and any
    // other value is mesos (currency 1).
    public static Currency Of(MeretMarketCurrencyType priceType) {
        return (byte) priceType switch {
            1 => Currency.Meret,
            2 => Currency.RedMeret,
            3 => Currency.RedElseMeret,
            _ => Currency.Meso,
        };
    }

    public static bool IsPaid(Result result) {
        return result is Result.Voucher or Result.Pay;
    }

    public static Result Decide(bool useVoucher, bool hasVoucher, bool canPay) {
        if (useVoucher) {
            return hasVoucher ? Result.Voucher : Result.NoVoucher;
        }
        return canPay ? Result.Pay : Result.Lack;
    }
}
