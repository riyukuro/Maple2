namespace Maple2.Model.Validators;

// The adventurer protection (feature AdventureProtect, KR level 69). While the feature is on, the client refuses a
// meso market buy (BlackMarketPageMesoSearch::vfunc_4, uiblackmarketpagemeso.cpp:180) and a cash shop bill
// (uicashshopdialog.cpp:1137) with s_adventure_protect_err (string 0xd56) unless the state byte that opcode 0x131 sets
// (CSceneStateGame::FUN_14173f7c0) is 7; nothing else in the client sets or reads that byte. The conditions are the
// constants AdventureProtectRequireLevel, AdventureProtectRequireQuest and AdventureProtectCharCreateTime (hours since
// the character was made). Chosen here: 7 as three bits, 1 the level, 2 the quest, 4 the character's age (the client
// tests only for 7, and no client code reads the constants); a constant of 0 (no row) counts as met.
public static class AdventureProtectRules {
    public const byte LevelMet = 1;
    public const byte QuestMet = 2;
    public const byte AgeMet = 4;
    public const byte Cleared = LevelMet | QuestMet | AgeMet;

    public static byte State(int level, bool questDone, long creationTime, long now, int requireLevel, int requireQuest, int requireHours) {
        byte state = 0;
        if (level >= requireLevel) {
            state |= LevelMet;
        }
        if (requireQuest <= 0 || questDone) {
            state |= QuestMet;
        }
        if (AgeRemaining(creationTime, now, requireHours) == 0) {
            state |= AgeMet;
        }
        return state;
    }

    // Seconds until the character is old enough; 0 once it is.
    public static long AgeRemaining(long creationTime, long now, int requireHours) {
        if (requireHours <= 0) {
            return 0;
        }
        return Math.Max(0, creationTime + requireHours * 3600L - now);
    }
}
