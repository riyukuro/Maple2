using Maple2.File.Parser.Enum;
using Maple2.Model.Enum;

namespace Maple2.File.Ingest.Helpers;

// The wedding reward table (table/weddingreward.xml) by its row names. The parser numbers the types marriedLife 1,
// attendance 2, coupleMessage 3, marriageRegister 4, weddingExpItem 5, logoutPanelty 6, which are not the exp log's
// types, so a row is keyed by the server type of the same name; a row without one is left out.
public static class WeddingRewardMapping {
    public static MarriageExpType? Type(WeddingRewardType type) {
        return type switch {
            WeddingRewardType.marriedLife => MarriageExpType.marriedLife,
            WeddingRewardType.attendance => MarriageExpType.attendance,
            WeddingRewardType.coupleMessage => MarriageExpType.coupleMessage,
            WeddingRewardType.marriageRegister => MarriageExpType.marriageRegister,
            WeddingRewardType.weddingExpItem => MarriageExpType.weddingExpItem,
            WeddingRewardType.logoutPanelty => MarriageExpType.logoutPanelty,
            _ => null,
        };
    }

    public static MarriageExpLimit Limit(WeddingRewardLimit limit) {
        return limit switch {
            WeddingRewardLimit.day => MarriageExpLimit.day,
            WeddingRewardLimit.week => MarriageExpLimit.week,
            WeddingRewardLimit.month => MarriageExpLimit.month,
            _ => MarriageExpLimit.none,
        };
    }
}
