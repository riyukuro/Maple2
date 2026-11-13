using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Metadata;

namespace Maple2.Model.Validators;

// What KMS2 answers a cube edit on a cell that is taken (ResponseCube 10 and 15 with an error).
public static class CubeEditRules {
    // A place on a taken cell is refused with s_ugcmap_create_on_non_empty_area (2); a replace by the item
    // that is on the cell with s_ugcmap_cant_replace_same_cube (53). existingItemId is null for a free cell.
    public static UgcMapError Taken(bool isReplace, int? existingItemId, int itemId) {
        if (existingItemId == null) {
            return UgcMapError.s_ugcmap_ok;
        }
        if (!isReplace) {
            return UgcMapError.s_ugcmap_create_on_non_empty_area;
        }
        return existingItemId == itemId ? UgcMapError.s_ugcmap_cant_replace_same_cube : UgcMapError.s_ugcmap_ok;
    }

    // A place outside the home's build space (the client checks none of it, so the error is the server's choice): a
    // cell outside the planar area, X and Y in (-area, 0] and Z at 0 or above, gets s_ugcmap_area_limit (44); a cell
    // above the build height, Z over height, s_ugcmap_height_limit (43). Both were 44 before.
    public static UgcMapError HomeBounds(int x, int y, int z, int area, int height) {
        if (x > 0 || y > 0 || z < 0 || x <= -area || y <= -area) {
            return UgcMapError.s_ugcmap_area_limit;
        }
        return z > height ? UgcMapError.s_ugcmap_height_limit : UgcMapError.s_ugcmap_ok;
    }

    // An outdoor plot's height: a cell more than the plot's limit height above its ground tile gets
    // s_ugcmap_height_limit (43); it got s_ugcmap_area_limit (44).
    public static UgcMapError PlotHeight(float aboveGround, int limitHeight) =>
        aboveGround > limitHeight ? UgcMapError.s_ugcmap_height_limit : UgcMapError.s_ugcmap_ok;

    // A cube the builder may not place yet: s_ugcmap_cube_lock (88), which the client shows before it sends the place
    // (Unk_PCCubeCraft::ConfirmPlacement, cubecraft_2.cpp:954; MyPC::FUN_1415302e0, mypc.cpp:4446). The client locks a
    // cube whose trophy grade (housing trophyID, trophyLevel) is not cleared: its own in its home or on a plot, the home
    // owner's (ResponseCube 0x37) for a build helper. A cube of a meret price is never locked: the client's housing
    // priceCount is the shop_UGCAll/shop_Maid ugcHousingMoneyType (ItemHousing::LoadXml, itemdatastruct.cpp:1465,
    // CPriceTable::LoadHousing), the FurnishingShopTable money type here, 1 for an item without a row. A trophyLevel
    // of 0 (IsGradeCleared(id, 0): any grade cleared) is the server's grade 0 too; no KR housing row has one (all 334
    // trophy rows give level 1 to 9). The interior level checks behind feature HousingLevelhurdle (0xdb) are left out: feature.xml gives it
    // level 100 for KR and NA, above the Live level, so the client never runs them. cleared is the trophy owner's.
    public static UgcMapError CubeLock(ItemMetadataHousing? housing, FurnishingCurrencyType? currency, Func<int, int, bool> cleared) {
        if (housing == null || currency == FurnishingCurrencyType.Meret) {
            return UgcMapError.s_ugcmap_ok;
        }
        if (housing.TrophyId > 0 && !cleared(housing.TrophyId, housing.TrophyLevel)) {
            return UgcMapError.s_ugcmap_cube_lock;
        }
        return UgcMapError.s_ugcmap_ok;
    }

    // The plot group's count limits on a place (client CubeMapInformationService FUN_1410d27c0,
    // cubemapinformationservice_3.cpp:4603). The client counts a group's cubes of each kind and allows one more only
    // while the limit is above 0 and above the count:
    // - a house (item type 6, subtype 4: CItemData::FUN_14053d940) counts against installableBuildingCount (45);
    // - a trigger tool (FunctionUI TriggerTool) against triggerCount (75);
    // - an installed npc (ControlType InstallNPC, IsInstallNpcControlType) against installNpcCount (99).
    // The maid count needs an item of type 6 and subtype 7 (CItemData::GetMaidId); no KMS2 item is one (the maids are
    // subtype 5), so maidCount never refuses. others are the plot's other cubes, without one a replace removes.
    public static UgcMapError GroupLimit(UgcMapGroup.Limits limit, ItemMetadata item, FunctionCubeMetadata? function,
                                         IEnumerable<(ItemMetadata Item, FunctionCubeMetadata? Function)> others) {
        (Func<ItemMetadata, FunctionCubeMetadata?, bool> kind, int max, UgcMapError error)[] limits = [
            (IsHouse, limit.InstallBuilding, UgcMapError.s_ugcmap_building_count),
            ((_, f) => f is { TriggerTool: true }, limit.Trigger, UgcMapError.s_ugcmap_trigger_count),
            ((_, f) => f?.ControlType == InteractCubeControlType.InstallNPC, limit.InstallNpc, UgcMapError.s_ugcmap_installnpc_count),
        ];
        foreach ((Func<ItemMetadata, FunctionCubeMetadata?, bool> kind, int max, UgcMapError error) in limits) {
            if (!kind(item, function)) {
                continue;
            }
            int count = others.Count(other => kind(other.Item, other.Function));
            if (max <= 0 || count >= max) {
                return error;
            }
        }
        return UgcMapError.s_ugcmap_ok;
    }

    // A template cube (an item with a ucc mesh, CItemData::HasCubeTextureMesh) placed without a template look, or with
    // a look of id 0 (CubeItemInfo::FUN_1408b3860), is refused with s_err_ugcmap_cant_build_empty_ugc (98)
    // (Unk_PCCubeCraft::ConfirmPlacement, cubecraft_2.cpp:964-972).
    public static UgcMapError EmptyTemplate(string mesh, long? templateId) =>
        !string.IsNullOrEmpty(mesh) && templateId is null or 0 ? UgcMapError.s_err_ugcmap_cant_build_empty_ugc : UgcMapError.s_ugcmap_ok;

    private static bool IsHouse(ItemMetadata item, FunctionCubeMetadata? _) => item.Property.Type == 6 && item.Property.SubType == 4;
}
