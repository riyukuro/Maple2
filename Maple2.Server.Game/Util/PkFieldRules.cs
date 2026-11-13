namespace Maple2.Server.Game.Util;

// The open PvP of a field whose field data has pkMode (MapMetadataProperty.PkMode). Under KR that is 03000059 (Outlaw's
// Overlook), the four Mushtopia maps and test maps: the 19 shadow-world maps of continent 202 have pkMode 1 in the
// field data's default environment, and their KR environment sets it to 0.
public static class PkFieldRules {
    // The client's rule on a pk field (PC::vfunc_122, pc.cpp:3112): another player is hostile unless it is the player
    // itself, both are in the same party (party id 0 is no party), or either one holds an enabled buff with pvpOnOff.
    public static bool Hostile(bool self, int casterParty, int targetParty, bool casterPvpOff, bool targetPvpOff) {
        if (self) {
            return false;
        }
        if (casterParty != 0 && casterParty == targetParty) {
            return false;
        }
        return !casterPvpOff && !targetPvpOff;
    }
}
