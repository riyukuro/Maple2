using Maple2.Model.Enum;

namespace Maple2.Model.Game;

// The intimacy a quest's weddingExp adds to the marriage's exp log.
public static class MarriageExpRules {
    // The log names whose mission it was: s_wedding_dialog_exp_11 (the groom's) and _12 (the bride's) are two lines, so
    // each partner who completes a mission adds their own entry, and both completing the same quest add it twice.
    // The groom is the first stored partner, as for a filed divorce (DivorceRules.FiledStatus).
    public static MarriageExpType WeddingMission(long characterId, long firstId) {
        return characterId == firstId ? MarriageExpType.WeddingMissionGroom : MarriageExpType.WeddingMissionBride;
    }

    // Only a marriage counts it, also while a divorce is filed (still married until it is complete). A single,
    // an engaged or a divorced player gets nothing.
    public static bool CountsWeddingExp(MaritalStatus status) {
        return status is MaritalStatus.Married or MaritalStatus.DivorceFiledByGroom or MaritalStatus.DivorceFiledByBride;
    }

    // The type as seen with the two partners in the other order (DivorceRules.Swapped): a character is sent its
    // marriage with the partner first, so for the first stored partner the groom's and the bride's lines change places.
    public static MarriageExpType Swapped(MarriageExpType type) {
        return type switch {
            MarriageExpType.WeddingMissionGroom => MarriageExpType.WeddingMissionBride,
            MarriageExpType.WeddingMissionBride => MarriageExpType.WeddingMissionGroom,
            _ => type,
        };
    }
}
