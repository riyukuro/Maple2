using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;

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
    // refused where the client would have asked: a home smaller than the package, or cubes in the home.
    public static StringCode? Refusal(bool confirmed, int homeArea, int homeHeight, int cubes, (byte Area, byte Height) size) {
        if (confirmed) {
            return null;
        }
        if (homeArea < size.Area || homeHeight < size.Height) {
            return StringCode.s_err_ugcmap_package_not_a_indoor_for_this_package_item;
        }
        return cubes != 0 ? StringCode.s_err_ugcmap_package_clear_indoor_first : null;
    }
}
