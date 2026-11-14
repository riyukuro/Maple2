using Maple2.Model.Enum;

namespace Maple2.Model.Validators;

// RequestCube 42 and 43, the restriction rows of the home options dialog. A row sends 42 (row, on) and then
// 43 (row, setting) (UIHomeOptionDialog::SetPermissionRow). Each client keeps the rows of the home it stands in
// (CubeMapInformationService, filled from the home info at field entry and from ResponseCube 0x2a/0x2b) and
// enforces them itself: an enabled row stops everyone at setting 0, everyone but the owner at 1, and
// everyone but the owner and the owner's party at 2 (CubeMapInformationService::FUN_1410c3cd0).
public static class HomePermissionRules {
    public const int Count = 9;

    // The home options are the owner's, changed in a field of its own home. A visitor's dialog would
    // otherwise change the visitor's home and send it to the players of the home it visits.
    public static bool MayChange(long homeOwnerId, long accountId) {
        return HomeEditRules.OwnsPlot(homeOwnerId, accountId);
    }

    // 42: an enabled row starts at setting 0. False for a row the client does not have.
    public static bool Enable(IDictionary<HomePermission, HomePermissionSetting> permissions, HomePermission permission, bool enabled) {
        if ((int) permission >= Count) {
            return false;
        }
        if (enabled) {
            permissions[permission] = HomePermissionSetting.None;
        } else {
            permissions.Remove(permission);
        }
        return true;
    }

    // 43: the setting of an enabled row; a disabled row stays at 0. Null for a row or setting the
    // client does not have.
    public static HomePermissionSetting? Set(IDictionary<HomePermission, HomePermissionSetting> permissions, HomePermission permission, HomePermissionSetting setting) {
        if ((int) permission >= Count || setting > HomePermissionSetting.Party) {
            return null;
        }
        if (!permissions.ContainsKey(permission)) {
            return HomePermissionSetting.None;
        }
        permissions[permission] = setting;
        return setting;
    }

    // Whether the home's row stops a player (CubeMapInformationService::FUN_1410c3cd0): a disabled row stops no
    // one; an enabled one stops everyone at setting 0, everyone but the owner at 1, and everyone but the owner and
    // a party member of the owner at 2. The client checks it before it sends the act; the server checks it again.
    public static bool Blocks(IDictionary<HomePermission, HomePermissionSetting> permissions, HomePermission permission,
                              long homeOwnerId, long accountId, Func<bool> ownerInParty) {
        if (!permissions.TryGetValue(permission, out HomePermissionSetting setting)) {
            return false;
        }
        return setting switch {
            HomePermissionSetting.Self => homeOwnerId != accountId,
            HomePermissionSetting.Party => homeOwnerId != accountId && !ownerInParty(),
            _ => true,
        };
    }
}
