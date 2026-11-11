namespace Maple2.Model.Enum;

// The client's GM level: one byte, sent for the own account in the Admin packet (0x67 mode 0) and for every
// character as the grade byte after the gender in the character block. The client's own value for a normal
// player is 1, and almost every GM feature tests "level >= 50"; a few debug keys test 51, 52 or 59 on one
// region build only. The level only switches client features on: every GM action is checked again on the
// server against the account's permissions.
public static class AdminLevel {
    public const byte Normal = 1;
    public const byte GameMaster = 50;
    public const byte Admin = 255;

    // Admin (every flag) -> 255. GameMaster (all of its flags, with or without finer ones) -> 50.
    // Everything else, also an account with single flags such as Warp or SpawnItem -> 1.
    public static byte Of(AdminPermissions permissions) {
        if (permissions == AdminPermissions.Admin) {
            return Admin;
        }
        return permissions.HasFlag(AdminPermissions.GameMaster) ? GameMaster : Normal;
    }

    public static bool IsGm(AdminPermissions permissions) {
        return Of(permissions) >= GameMaster;
    }
}
