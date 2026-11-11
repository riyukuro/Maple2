// ReSharper disable InconsistentNaming, IdentifierTypo

using System.ComponentModel;

namespace Maple2.Model.Error;

// The codes of the client's mode 8 switches (uibeautyshopdialog.cpp:1588-1611,
// uibeautyshopstyledialog.cpp:1335-1362). The two slot texts share code 19: the shop window shows
// the first, the saved style window the second.
public enum BeautyError {
    [Description("Not enough currency.")]
    lack_currency = 2, // The client somehow determines which currency is lacking.
    [Description("This dye can't be used.")]
    s_beauty_msg_error_color = 12,
    [Description("You have reached the maximum number of hairstyle slots.")]
    s_beauty_msg_error_style_slot_extend_max = 19,
    [Description("")]
    none = 18,
    [Description("You have reached the maximum number of hairstyle slots.\nSaved hairstyles can be deleted by the hair designer.")]
    s_beauty_msg_error_style_slot_max = 19,
    [Description("Not enough merets.\nDo you want to buy merets?")]
    s_err_lack_merat_ask = 20,
    [Description("Incorrect character gender.")]
    s_beauty_msg_error_style_disable_gender = 23,
    [Description("This hairstyle is already applied.")]
    s_beauty_msg_error_style_apply_already = 24,
    [Description("During the beauty day event, discounted hair cannot be stored.")]
    s_beauty_msg_error_style_save_cant_not_beauty_day = 25,

    [Description("System Error. Code = {0}\nWill return to the game.")]
    s_beauty_msg_error_code = byte.MaxValue,
}
