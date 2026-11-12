using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Metadata;

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
