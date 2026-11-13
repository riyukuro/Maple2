using Maple2.Model.Enum;

namespace Maple2.Server.Game.Util;

// The meret wallet a cost is checked against and taken from, as the client's currency checks read it
// (HasEnoughCurrency, GetCurrencyAmount, uicommon.cpp:972/1020).
public static class MeretCost {
    public enum Wallet {
        Meret, // the plain merets (client currency 3)
        Red, // the red merets only (client currency 9)
        RedElseMeret, // the red merets when they cover the price, else the plain merets (client currency 10)
    }

    // The shop and beauty cost types go through the client's shop map (FUN_1403baee0, gameentitycommon.cpp:269;
    // beauty: CanPayFee, unit_141347b10.cpp:616): Meret is currency 3, EventMeret 10 and GameMeret 9.
    public static Wallet? OfShop(ShopCurrencyType type) {
        return type switch {
            ShopCurrencyType.Meret => Wallet.Meret,
            ShopCurrencyType.EventMeret => Wallet.RedElseMeret,
            ShopCurrencyType.GameMeret => Wallet.Red,
            _ => null,
        };
    }

    // The merets the client counts for the price: GetCurrencyAmount gives the red merets for currency 10 when
    // they cover the price, else the plain ones.
    public static long Owned(Wallet wallet, long meret, long red, long price) {
        return wallet switch {
            Wallet.Meret => meret,
            Wallet.Red => red,
            _ => red >= price ? red : meret,
        };
    }
}
