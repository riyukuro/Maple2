namespace Maple2.Model.Validators;

public static class PrestigeRules {
    // Prestige 5 (mission reward): the client sends the claim only for a mission that is not awarded yet
    // and whose gained levels reached the mission's count.
    public static bool CanClaimMission(bool awarded, long gainedLevels, int requiredCount) {
        return !awarded && gainedLevels >= requiredCount;
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

    // The prestige levels a stock of prestige exp pays for, up to the level limit.
    public static int LevelsGained(long exp, long expPerLevel, int level, int levelLimit) {
        if (expPerLevel <= 0 || level >= levelLimit) {
            return 0;
        }
        return (int) Math.Min(exp / expPerLevel, levelLimit - level);
    }
}
