using Maple2.Model.Enum;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class UgcUploadPaymentTests {
    [Test]
    public void Voucher_WithoutCoupon_IsRefused() {
        // The voucher byte is the client's: without a coupon nothing may be created, whatever the balance.
        UgcUploadPayment.Result result = UgcUploadPayment.Decide(useVoucher: true, hasVoucher: false, canPay: true);

        Assert.That(result, Is.EqualTo(UgcUploadPayment.Result.NoVoucher));
        Assert.That(UgcUploadPayment.IsPaid(result), Is.False);
    }

    [Test]
    public void Voucher_WithCoupon_TakesTheCoupon() {
        UgcUploadPayment.Result result = UgcUploadPayment.Decide(useVoucher: true, hasVoucher: true, canPay: false);

        Assert.That(result, Is.EqualTo(UgcUploadPayment.Result.Voucher));
        Assert.That(UgcUploadPayment.IsPaid(result), Is.True);
    }

    [Test]
    public void Price_NotEnough_IsRefused() {
        UgcUploadPayment.Result result = UgcUploadPayment.Decide(useVoucher: false, hasVoucher: true, canPay: false);

        Assert.That(result, Is.EqualTo(UgcUploadPayment.Result.Lack));
        Assert.That(UgcUploadPayment.IsPaid(result), Is.False);
    }

    [Test]
    public void Price_Enough_IsPaid() {
        UgcUploadPayment.Result result = UgcUploadPayment.Decide(useVoucher: false, hasVoucher: false, canPay: true);

        Assert.That(result, Is.EqualTo(UgcUploadPayment.Result.Pay));
    }

    // The design's priceType as ConvertPriceType (ugccubecategorytable.cpp:445) turns it into the client's currency:
    // 1 plain merets (3), 2 red merets only (9), 3 red merets when they cover the price, else plain merets (10),
    // anything else mesos (1).
    [TestCase((byte) 0, UgcUploadPayment.Currency.Meso)]
    [TestCase((byte) 1, UgcUploadPayment.Currency.Meret)]
    [TestCase((byte) 2, UgcUploadPayment.Currency.RedMeret)]
    [TestCase((byte) 3, UgcUploadPayment.Currency.RedElseMeret)]
    [TestCase((byte) 4, UgcUploadPayment.Currency.Meso)]
    [TestCase((byte) 99, UgcUploadPayment.Currency.Meso)]
    public void PriceType_AsTheClientConvertsIt(byte priceType, UgcUploadPayment.Currency expected) {
        Assert.That(UgcUploadPayment.Of((MeretMarketCurrencyType) priceType), Is.EqualTo(expected));
    }
}
