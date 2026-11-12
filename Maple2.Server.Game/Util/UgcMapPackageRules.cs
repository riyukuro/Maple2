using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;

namespace Maple2.Server.Game.Util;

// The interior packages (item function UGCMapPackage, the 12 furnishing sets 59300001-59300012 in KR): the parameter
// names an exported UGC map (Xml ExportedUGCMap/<name>.xml, the client's GetUGCMapPackageData). The client's
// UIInteriorBuildDialog::OnUseItem (uiinteriorbuilddialog.cpp:188) checks the item in this order and sends the item
// use with "false" when all pass; a home smaller than the package (area below indoorSizeType [0] or height below
// [2]) or a home that is not empty asks first (indoorSizeDifferent, indoorEmpty), and a yes sends "true".
public static class UgcMapPackageRules {
    // The package's indoor size (area, height) from its indoorSizeType "area x area x height"; null when it has none.
    public static (byte Area, byte Height)? Size(ExportedUgcMapMetadata package) =>
        package.IndoorSize is { Length: >= 3 } size ? (size[0], size[2]) : null;

    // The home's base cell, the cell offsets are taken from (CubeMapInformationService FUN_1410c3670: the highest x
    // and y less the area plus 1, the lowest z; the indoor cells run from 1 - area to 0 and the floor is z 0).
    public static Vector3B Origin(int area) => new(1 - area, 1 - area, 0);

    // The text of the item use: "false" from the plain use, "true" after a yes. Null for any other text.
    public static bool? Confirmed(string text) => text switch {
        "true" => true,
        "false" => false,
        _ => null,
    };

    // What refuses the install of a valid package in the owner's home; null when it goes ahead. A plain use is
    // refused where the client would have asked: a home smaller than the package or a package without a size
    // (indoorSizeDifferent, uiinteriorbuilddialog.cpp:239), or cubes in the home.
    public static StringCode? Refusal(bool confirmed, int homeArea, int homeHeight, int cubes, (byte Area, byte Height)? size) {
        if (confirmed) {
            return null;
        }
        if (size is not { } known || homeArea < known.Area || homeHeight < known.Height) {
            return StringCode.s_err_ugcmap_package_not_a_indoor_for_this_package_item;
        }
        return cubes != 0 ? StringCode.s_err_ugcmap_package_clear_indoor_first : null;
    }

    // A furnishing storage stack: the item, its template url, whether it is a maid's item, its amount and stack size.
    public record struct Stack(int ItemId, string? Url, bool Maid, int Amount, int SlotMax);

    // Whether the furnishing storage takes every incoming cube, one at a time, as FurnishingManager.AddStorage adds
    // them: onto the first stack of the same item and design (MaidRules.CanJoin; a maid item only onto an empty stack), which must
    // have room; with no such stack into a new slot. Each incoming cube counts as staying in storage.
    public static bool StorageTakes(IEnumerable<Stack> stored, int openSlots, IEnumerable<Stack> incoming) {
        List<Stack> stacks = stored.ToList();
        foreach (Stack add in incoming) {
            int index = stacks.FindIndex(stack => stack.ItemId == add.ItemId && MaidRules.DesignKey(stack.Url) == MaidRules.DesignKey(add.Url) && (!add.Maid || stack.Amount == 0));
            if (index < 0) {
                if (openSlots <= 0) {
                    return false;
                }
                openSlots--;
                stacks.Add(add with { Amount = 1 });
                continue;
            }
            Stack join = stacks[index];
            if (join.Amount + 1 > join.SlotMax) {
                return false;
            }
            stacks[index] = join with { Amount = join.Amount + 1 };
        }
        return true;
    }
}
