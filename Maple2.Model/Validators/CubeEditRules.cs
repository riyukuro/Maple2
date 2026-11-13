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

    private static bool IsHouse(ItemMetadata item, FunctionCubeMetadata? _) => item.Property.Type == 6 && item.Property.SubType == 4;
}
