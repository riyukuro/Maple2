// ReSharper disable InconsistentNaming, IdentifierTypo

using System.ComponentModel;

namespace Maple2.Model.Error;

public enum FishingError : short {
    none = 0,
    [Description("You can only fish near swimmable water.")]
    s_fishing_error_notexist_water = 1,
    [Description("The fishing pole is not valid.")]
    s_fishing_error_invalid_item = 2,
    [Description("Your fishing mastery is too low to fish here.")]
    s_fishing_error_lack_mastery = 3,
    // The client's switch for Fishing mode 2 (fishingmanager.cpp:642): 4 is the no-fish text, 5 the text for a
    // block that cannot be fished.
    [Description("You cannot fish here.")]
    s_fishing_error_notexist_fish = 4,
    s_fishing_error_invalid_cube = 5,
    [Description("Your fishing mastery is too low to use this fishing pole.")]
    s_fishing_error_fishingrod_mastery = 6,
    [Description("You cannot fish because your Gear tab or Misc tab is full.")]
    s_fishing_error_inventory_full = 7,
    [Description("You cannot fish here.")]
    s_fishing_error_ugcmap = 8,
    [Description("System error")]
    s_fishing_error_system_error = 9,
}
