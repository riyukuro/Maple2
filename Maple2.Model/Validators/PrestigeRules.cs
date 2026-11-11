namespace Maple2.Model.Validators;

public static class PrestigeRules {
    // Prestige 5 (mission reward): the client sends the claim only for a mission that is not awarded yet
    // and whose gained levels reached the mission's count.
    public static bool CanClaimMission(bool awarded, long gainedLevels, int requiredCount) {
        return !awarded && gainedLevels >= requiredCount;
    }
}
