// ReSharper disable InconsistentNaming, IdentifierTypo

using System.ComponentModel;

namespace Maple2.Model.Error;

// The codes of the client's switch for Shop mode 15 (uishopdialog.cpp:1858). Code 11 shows the lack notice of
// the currency kind in the packet's last int (0: mesos); code 16 is the client's s_err_lack_achieve text.
public enum ShopError {
    [Description("Not enough supplies.")]
    s_err_lack_shopitem = 2,
    // Codes 4, 5 and 6 show one notice; KMS2 answers a refused sale with 5. The name is chosen here.
    [Description("This item cannot be sold.")]
    s_err_cannot_sell_item = 5,
    [Description("Item not found.")]
    s_err_invalid_item = 7,
    [Description("Cannot be sold.")]
    s_msg_cant_sell = 10,
    [Description("Not enough mesos.")]
    s_err_lack_meso = 11,
    [Description("Not enough merets.")]
    s_err_lack_merat = 12,
    [Description("Your inventory is full.")]
    s_err_inventory = 15,
    [Description("Not enough guild trophies.")]
    s_err_lack_guild_trophy = 16,
    [Description("Insufficient items. (Missing Item: $item:{0}$)")]
    s_err_lack_payment_item = 18,
    [Description("You can buy after being in the guild for 7 days.")]
    s_err_lack_guild_require_date = 20,
    [Description("The game will not run due to fatigue time.")]
    s_anti_addiction_cannot_receive = 23,
    [Description("You can't sell items to this shop.")]
    s_msg_cant_sell_to_only_sell_shop = 24,
    [Description("A restriction will be placed on Trade for 60 seconds after entering the game.")]
    s_system_property_protection_time = 25,
    [Description("Can only be purchased by guild leaders.")]
    s_guild_err_buy_no_master = 26,
    [Description("Not enough guild funds.")]
    s_guild_err_not_enough_guild_fund = 27,
    [Description("You cannot purchase this at this time.")]
    s_err_invalid_item_cannot_buy_by_period = 28,
    [Description("Can only be used during the event period.")]
    s_shop_no_star_point_event = 30,
    [Description("This item cannot be purchased in the country from which you are connecting.")]
    s_meratmarket_error_country_limit = 32,
    [Description("You cannot sell a pet while it is summoned. Please unsummon your pet and try again.")]
    s_err_cannot_sell_petitem_summon = 33,
}
