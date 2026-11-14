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

    // The row a cast of the skill's main type is checked against (CSkillSystem, skillslot.cpp): 5 (potion) the
    // potion row; 7 (glide) the air mount row in a home; 13 (climb) the climb row; and a skill of any main type that
    // needs learning (SkillData::FUN_1407e9f30: not 1, 3, 4, 5, 7, 10, 11, 13, 14, 15, 17, 18 or 19) the skill row.
    public static HomePermission? SkillRow(int mainType) {
        return mainType switch {
            5 => HomePermission.Potion,
            7 => HomePermission.AirMount,
            13 => HomePermission.Climb,
            1 or 3 or 4 or 10 or 11 or 14 or 15 or 17 or 18 or 19 => null,
            _ => HomePermission.Skill,
        };
    }

    // A glide is let through for a GM (account level 50 or more) with the constants' glide skill (skillslot.cpp,
    // the DAT_143ab2914 test); the server lets a GM's glide through whatever its item.
    public static bool SkillRowApplies(HomePermission row, byte adminLevel) {
        return row != HomePermission.AirMount || adminLevel < 50;
    }
}
