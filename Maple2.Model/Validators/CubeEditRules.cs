using Maple2.Model.Error;

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
}
