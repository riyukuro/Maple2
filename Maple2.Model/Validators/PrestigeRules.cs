using Maple2.Model.Enum;
using Maple2.Model.Game;

namespace Maple2.Model.Validators;

public static class PrestigeRules {
    // Prestige 5 (mission reward): the client sends the claim only for a mission that is not awarded yet
    // and whose gained levels reached the mission's count.
    public static bool CanClaimMission(bool awarded, long gainedLevels, int requiredCount) {
        return !awarded && gainedLevels >= requiredCount;
    }

    // The missions are weekly: the client's text has them start again each week at the weekly reset
    // ($s_adventure_level_weekly_mission_desc, feature AdventureLevelMission). The levels counted and the claims go back
    // to none; the client takes the new list from a Prestige 7.
    public static void ResetMissions(IEnumerable<PrestigeMission> missions) {
        foreach (PrestigeMission mission in missions) {
            mission.GainedLevels = 0;
            mission.Awarded = false;
        }
    }

    // The step of a prestige ability at a prestige level, as the client counts it
    // (CAdventureLevelAbility::GetCount, adventureleveltablehelper.cpp:506). The client looks the ability's
    // effect up at this step as its level, and shows the ability as locked unless the character has that
    // effect at that level (UIAdventureLevelDialog, uiadventureleveldialog.cpp:370-378). 0 is no effect.
    public static short AbilityStep(int id, int requiredLevel, int interval, int maxCount, int prestigeLevel) {
        if (id == 0 || prestigeLevel < requiredLevel) {
            return 0;
        }
        if (interval == 0) {
            return 1;
        }
        return (short) Math.Min((ushort) ((prestigeLevel - requiredLevel + interval) / interval), (ushort) maxCount);
    }

    // An amount times a rate, the rate read as its decimal text (0.02f is 0.02, 0.2f is 0.2) and the result truncated.
    public static long Scale(long amount, decimal rate) {
        return (long) (amount * rate);
    }

    public static long Scale(long amount, float rate) => Scale(amount, (decimal) rate);

    // The prestige exp a gain adds: in full up to the day's first fullRateExp, the rest times factor. KMS2 splits a gain
    // that crosses it (79250 of 86000 + 15000 adds 6750 + 8250 * 0.02 = 6915).
    public static long Gain(long amount, long gainedToday, long fullRateExp, float factor) {
        long full = Math.Clamp(fullRateExp - gainedToday, 0, amount);
        return full + Scale(amount - full, factor);
    }

    // A kill's prestige exp: the kill row times AdventureExpFactorElite for an elite (class 3; KMS2 adds 2500 for 250)
    // or AdventureExpFactorBoss for a boss (class 4). The dungeon classes (6, 7) are taken as common.
    public static decimal KillFactor(int npcClass, int eliteFactor, int bossFactor) {
        return npcClass switch {
            3 => eliteFactor,
            4 => bossFactor,
            _ => 1m,
        };
    }

    // A mini game round's prestige exp is its row times the round's exp rate (KMS2: 75000 at rate 1, 15000 at 0.2,
    // 18750 at 0.25); every other gain adds its row as it is.
    public static bool ScalesWithExpRate(ExpType type) => type is ExpType.miniGame or ExpType.userMiniGame;

    // The prestige levels a stock of prestige exp pays for, up to the level limit.
    public static int LevelsGained(long exp, long expPerLevel, int level, int levelLimit) {
        if (expPerLevel <= 0 || level >= levelLimit) {
            return 0;
        }
        return (int) Math.Min(exp / expPerLevel, levelLimit - level);
    }
}
