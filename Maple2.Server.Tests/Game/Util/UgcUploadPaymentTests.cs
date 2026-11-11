using Maple2.Model.Enum;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class UgcUploadPaymentTests {
    [Test]
    public void Voucher_WithoutCoupon_IsRefused() {
        // The voucher byte is the client's: without a coupon nothing may be created, whatever the balance.
        UgcUploadPayment.Result result = UgcUploadPayment.Decide(useVoucher: true, hasVoucher: false, MeretMarketCurrencyType.Meso, canPay: true);

        Assert.That(result, Is.EqualTo(UgcUploadPayment.Result.NoVoucher));
        Assert.That(UgcUploadPayment.IsPaid(result), Is.False);
    }

    [Test]
    public void Voucher_WithCoupon_TakesTheCoupon() {
        UgcUploadPayment.Result result = UgcUploadPayment.Decide(useVoucher: true, hasVoucher: true, MeretMarketCurrencyType.Meret, canPay: false);

        Assert.That(result, Is.EqualTo(UgcUploadPayment.Result.Voucher));
        Assert.That(UgcUploadPayment.IsPaid(result), Is.True);
    }

    [TestCase(MeretMarketCurrencyType.Meso)]
    [TestCase(MeretMarketCurrencyType.Meret)]
    [TestCase(MeretMarketCurrencyType.RedMeret)]
    public void Price_NotEnough_IsRefused(MeretMarketCurrencyType currency) {
        UgcUploadPayment.Result result = UgcUploadPayment.Decide(useVoucher: false, hasVoucher: true, currency, canPay: false);

        Assert.That(result, Is.EqualTo(UgcUploadPayment.Result.Lack));
        Assert.That(UgcUploadPayment.IsPaid(result), Is.False);
    }

    [TestCase(MeretMarketCurrencyType.Meso)]
    [TestCase(MeretMarketCurrencyType.Meret)]
    [TestCase(MeretMarketCurrencyType.RedMeret)]
    public void Price_Enough_IsPaid(MeretMarketCurrencyType currency) {
        UgcUploadPayment.Result result = UgcUploadPayment.Decide(useVoucher: false, hasVoucher: false, currency, canPay: true);

        Assert.That(result, Is.EqualTo(UgcUploadPayment.Result.Pay));
    }

    [Test]
    public void UnknownCurrency_IsRefused() {
        UgcUploadPayment.Result result = UgcUploadPayment.Decide(useVoucher: false, hasVoucher: false, (MeretMarketCurrencyType) 99, canPay: true);

        Assert.That(UgcUploadPayment.IsPaid(result), Is.False);
    }
}
