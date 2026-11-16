using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// A home edit's cube buy (FurnishingManager.PurchaseCube) takes the row's price in its currency; too little answers
// the currency's error and takes nothing, a row the shop does not sell is not bought, a free one costs nothing, and
// RefundCube gives back what was taken. KR rows: 50100000 30 mesos, 50100181 10 merets, 50100369 10 merets not for
// sale, 50400158 free.
public class FurnishingPurchaseTests {
    private static readonly FurnishingShopTable.Entry MesoRow = new(50100000, true, FurnishingCurrencyType.Meso, 30);
    private static readonly FurnishingShopTable.Entry MeretRow = new(50100181, true, FurnishingCurrencyType.Meret, 10);
    private static readonly FurnishingShopTable.Entry NotSold = new(50100369, false, FurnishingCurrencyType.Meret, 10);
    private static readonly FurnishingShopTable.Entry Free = new(50400158, true, FurnishingCurrencyType.Meso, 0);

    private static GameSession Start(long meso, long meret) {
        var test = new TestGameSession();
        GameSession session = test.Start();
        session.Currency.Meso = meso;
        session.Currency.Meret = meret;
        return session;
    }

    [TestCase(100, true, 70, UgcMapError.s_ugcmap_ok)]
    [TestCase(29, false, 29, UgcMapError.s_err_ugcmap_not_enough_meso_balance)]
    public void Mesos(long meso, bool bought, long left, UgcMapError expected) {
        GameSession session = Start(meso, 1000);

        Assert.That(session.Item.Furnishing.CanPay(MesoRow), Is.EqualTo(expected));
        Assert.That(session.Item.Furnishing.PurchaseCube(MesoRow, out UgcMapError error), Is.EqualTo(bought));
        Assert.That(error, Is.EqualTo(expected));
        Assert.That(session.Currency.Meso, Is.EqualTo(left));
        Assert.That(session.Currency.Meret, Is.EqualTo(1000));
    }

    [TestCase(50, true, 40, UgcMapError.s_ugcmap_ok)]
    [TestCase(9, false, 9, UgcMapError.s_err_ugcmap_not_enough_merat_balance)]
    public void Merets(long meret, bool bought, long left, UgcMapError expected) {
        GameSession session = Start(1000, meret);

        Assert.That(session.Item.Furnishing.CanPay(MeretRow), Is.EqualTo(expected));
        Assert.That(session.Item.Furnishing.PurchaseCube(MeretRow, out UgcMapError error), Is.EqualTo(bought));
        Assert.That(error, Is.EqualTo(expected));
        Assert.That(session.Currency.Meret, Is.EqualTo(left));
        Assert.That(session.Currency.Meso, Is.EqualTo(1000));
    }

    [Test]
    public void NotForSale_IsNotBoughtAndTakesNothing() {
        GameSession session = Start(1000, 1000);

        Assert.That(session.Item.Furnishing.PurchaseCube(NotSold, out UgcMapError error), Is.False);
        Assert.That(error, Is.EqualTo(UgcMapError.s_ugcmap_ok));
        session.Item.Furnishing.RefundCube(NotSold);
        Assert.That(session.Currency.Meret, Is.EqualTo(1000));
    }

    [Test]
    public void Free_CostsNothing() {
        GameSession session = Start(0, 0);

        Assert.That(session.Item.Furnishing.CanPay(Free), Is.EqualTo(UgcMapError.s_ugcmap_ok));
        Assert.That(session.Item.Furnishing.PurchaseCube(Free, out _), Is.True);
        session.Item.Furnishing.RefundCube(Free);
        Assert.That(session.Currency.Meso, Is.EqualTo(0));
    }

    // A buy whose cube could not be added is given back in full.
    [Test]
    public void Refund_GivesThePriceBack() {
        GameSession session = Start(100, 50);
        Assert.That(session.Item.Furnishing.PurchaseCube(MesoRow, out _), Is.True);
        Assert.That(session.Item.Furnishing.PurchaseCube(MeretRow, out _), Is.True);

        session.Item.Furnishing.RefundCube(MesoRow);
        session.Item.Furnishing.RefundCube(MeretRow);

        Assert.That(session.Currency.Meso, Is.EqualTo(100));
        Assert.That(session.Currency.Meret, Is.EqualTo(50));
    }
}
