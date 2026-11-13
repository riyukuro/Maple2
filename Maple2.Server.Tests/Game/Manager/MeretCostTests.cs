using Maple2.Model.Enum;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Manager;

// The meret wallets a cost is checked against and taken from, as the client picks them (FUN_1403baee0 for shop and
// beauty costs; HasEnoughCurrency and GetCurrencyAmount, uicommon.cpp:972/1020).
public class MeretCostTests {
    [TestCase(ShopCurrencyType.Meret, MeretCost.Wallet.Meret)]
    [TestCase(ShopCurrencyType.EventMeret, MeretCost.Wallet.RedElseMeret)]
    [TestCase(ShopCurrencyType.GameMeret, MeretCost.Wallet.Red)]
    public void OfShop_MapsAsTheClient(ShopCurrencyType type, MeretCost.Wallet wallet) {
        Assert.That(MeretCost.OfShop(type), Is.EqualTo(wallet));
    }

    [TestCase(ShopCurrencyType.Meso)]
    [TestCase(ShopCurrencyType.Treva)]
    public void OfShop_OtherCurrenciesAreNoMeretCost(ShopCurrencyType type) {
        Assert.That(MeretCost.OfShop(type), Is.Null);
    }

    [TestCase(MeretCost.Wallet.Meret, 100, 50, 40, 100)]
    [TestCase(MeretCost.Wallet.Red, 100, 50, 40, 50)]
    [TestCase(MeretCost.Wallet.RedElseMeret, 100, 50, 40, 50)]
    [TestCase(MeretCost.Wallet.RedElseMeret, 100, 30, 40, 100)]
    public void Owned_IsTheWalletThatWouldPay(MeretCost.Wallet wallet, long meret, long red, long price, long owned) {
        Assert.That(MeretCost.Owned(wallet, meret, red, price), Is.EqualTo(owned));
    }

    [TestCase(MeretCost.Wallet.RedElseMeret, 100, 50, true, 100, 10, true)]
    [TestCase(MeretCost.Wallet.RedElseMeret, 100, 30, true, 60, 30, false)]
    [TestCase(MeretCost.Wallet.RedElseMeret, 20, 30, false, 20, 30, false)]
    [TestCase(MeretCost.Wallet.Red, 100, 50, true, 100, 10, true)]
    [TestCase(MeretCost.Wallet.Red, 100, 30, false, 100, 30, true)]
    [TestCase(MeretCost.Wallet.Meret, 100, 50, true, 60, 50, false)]
    [TestCase(MeretCost.Wallet.Meret, 30, 50, false, 30, 50, false)]
    public void TryTakeMerets_TakesOneWalletOrNothing(MeretCost.Wallet wallet, long meret, long red, bool taken,
                                                     long meretAfter, long redAfter, bool paidRed) {
        var test = new TestGameSession();
        GameSession session = test.Start();
        session.Currency.Meret = meret;
        session.Currency.GameMeret = red;

        bool result = session.Currency.TryTakeMerets(wallet, 40, out bool wasRed);

        Assert.Multiple(() => {
            Assert.That(result, Is.EqualTo(taken));
            Assert.That(session.Currency.Meret, Is.EqualTo(meretAfter));
            Assert.That(session.Currency.GameMeret, Is.EqualTo(redAfter));
            if (taken) {
                Assert.That(wasRed, Is.EqualTo(paidRed));
            }
        });
    }
}
