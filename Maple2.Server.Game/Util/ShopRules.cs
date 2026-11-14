using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;

namespace Maple2.Server.Game.Util;

public static class ShopRules {
    // The client refuses to send a purchase of less than one item, so such a request is not a player's.
    // The total is checked in 64 bits: a product that does not fit an int would wrap to a small or negative price.
    public static bool TryTotalPrice(int unitPrice, int quantity, out int total) {
        total = 0;
        if (quantity <= 0 || unitPrice < 0) {
            return false;
        }

        long price = (long) unitPrice * quantity;
        if (price > int.MaxValue) {
            return false;
        }

        total = (int) price;
        return true;
    }

    // The condition an NPC shop purchase counts for, by the currency paid; each counts the price. The client names
    // currency 3 honor and 4 karma (the take messages, uiworldshareinfoeventlistener.cpp:264), which the shop pays as
    // ValorToken and Treva; trophies 23200012 and 23200014 spend Valor Token and Treva.
    public static ConditionType? BuyCondition(ShopCurrencyType currency) {
        return currency switch {
            ShopCurrencyType.Meso => ConditionType.shop_buy,
            ShopCurrencyType.Treva => ConditionType.shop_buy_karma_token,
            ShopCurrencyType.ValorToken => ConditionType.shop_buy_honor_token,
            _ => null,
        };
    }

    // The restock time of a shop that never restocks (reset type 99, Unlimited, or a type the server does not know): the
    // first second of the year 3000. The time is stored as a database DateTime, which long.MaxValue does not fit: the
    // first opening of such a shop threw and the shop did not open (the KR event shops 226, 260, 266, 270, 276 and 280
    // have reset type 99 and no fixed time). The client reads no time for type 99 (UIShopDialog::UpdateSerial).
    public const long NoRestockTime = 32503680000;

    // The client offers the instant restock (Shop 9, UIShopDialog::ResetList) only through the reset button, which it
    // shows with the shop's reset on, a reset type other than 99 (Unlimited) and the hide flag (DisableInstantRestock)
    // off (UIShopDialog, uishopdialog.cpp:912-920 and :2391-2394).
    public static bool CanInstantRestock(bool enableReset, ResetType resetType, bool disableInstantRestock) {
        return enableReset && resetType != ResetType.Unlimited && !disableInstantRestock;
    }

    // The star point event runs while a StarShop event names an npc the client knows (IsStarPointEventActive,
    // meratmarketbasketinfo.cpp:2574). Without it the client refuses, with s_shop_no_star_point_event, every buy, buy
    // back and sale in a star point shop (frame 2, UIShopDialog::HasStarEvent, uishopdialog.cpp:2690, asked by BuyProduct
    // :2425 and CanSellItem :2088) and a product paid in star points in any shop (CNpcShopProductCli::CheckStarPointEvent,
    // npcshopproductbase.cpp:504).
    public static bool StarEventActive(IEnumerable<int> starShopNpcIds, Func<int, bool> npcExists) {
        return starShopNpcIds.Any(npcId => npcId != 0 && npcExists(npcId));
    }

    public static bool RestockDue(long fixedRestockTime, long restockTime, long now) {
        return fixedRestockTime == 0 && restockTime < now;
    }

    // What the client's shop dialog refuses to sell (UIShopDialog::CanSellItem, client/ms2/uishopdialog.cpp:2076):
    // an item whose data forbids the shop sale, a locked item (locked, or unlocking and the unlock time not yet
    // reached) and an item with a gemstone in any socket.
    public static bool CanSell(bool shopSell, bool isLocked, long unlockTime, long now, bool hasGem) {
        return shopSell && !isLocked && now >= unlockTime && !hasGem;
    }

    // The client does not send a sale of less than one item. The shop pays the unit price for each item sold.
    public static bool TrySellTotal(long unitPrice, int quantity, int owned, out long total) {
        total = 0;
        if (quantity <= 0 || quantity > owned || unitPrice < 0) {
            return false;
        }

        total = unitPrice * quantity;
        return true;
    }

    // The client's fame grade of an alliance (FUN_140931250, unit_14092f420_accountsessionstate.cpp:952): the grade
    // of the alliance's fame points by the fame table. For no alliance (0) it is the best grade of alliances 1 to 9
    // (GetGeneralFameGrade, :958).
    public static int FameGrade(FameTable table, IDictionary<AllianceType, int> famePoints, AllianceType alliance) {
        if (alliance != AllianceType.AnyFaction) {
            return FameLogRules.Grade(table, alliance, famePoints.TryGetValue(alliance, out int points) ? points : 0);
        }
        int best = 0;
        for (int i = 1; i < 10; i++) {
            best = Math.Max(best, FameGrade(table, famePoints, (AllianceType) i));
        }
        return best;
    }

    // The client's fame product check (FUN_141b04af0, client/ms2/uishopdialog.cpp:3612): a product with a grade of
    // 0 needs nothing, else the alliance's fame grade must be at least the product's. The client shows
    // s_err_lack_fame_alliance itself, so no shop code carries the refusal.
    public static bool CheckFameProduct(ShopItemMetadata.QuestAlliance requirement, FameTable table, IDictionary<AllianceType, int> famePoints) {
        return requirement.Grade == 0 || FameGrade(table, famePoints, requirement.Type) >= requirement.Grade;
    }

    public const long GuildTrophyJoinSeconds = 604800;

    // The client's guild product checks (client/ms2/uishopdialog.cpp:3562-3711). False refuses the buy; error is the
    // shop code to answer with, or null where the client shows a notice no shop code carries (the npc level).
    // - Guild trophy products: a member for 7 days (20, s_err_lack_guild_require_date), then enough trophies (16).
    // - Guild npc products: the guild's npc of that type at the product's level (s_err_lack_guild_npc_level).
    // - Guild fund products (currency 7): the guild master only (26, s_guild_err_buy_no_master).
    public static bool CheckGuildProduct(ShopItemMetadata.Requirement requirement, ShopCurrencyType currency, long? joinTime,
                                         long now, int trophies, int npcLevel, bool isMaster, out ShopError? error) {
        error = null;
        if (requirement.GuildTrophy > 0) {
            if (joinTime == null) {
                return false;
            }
            if (joinTime.Value + GuildTrophyJoinSeconds > now) {
                error = ShopError.s_err_lack_guild_require_date;
                return false;
            }
            if (requirement.GuildTrophy > trophies) {
                error = ShopError.s_err_lack_guild_trophy;
                return false;
            }
        }
        if (requirement.GuildNpc.Type != GuildNpcType.Unknown) {
            if (joinTime == null || requirement.GuildNpc.Level > npcLevel) {
                return false;
            }
        }
        if (currency == ShopCurrencyType.GuildFund && (joinTime == null || !isMaster)) {
            error = ShopError.s_guild_err_buy_no_master;
            return false;
        }
        return true;
    }
}
