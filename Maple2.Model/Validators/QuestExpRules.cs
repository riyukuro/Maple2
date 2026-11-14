using Maple2.Model.Enum;

namespace Maple2.Model.Validators;

// The exp a quest's complete reward gives, as the client shows it in the reward line (FUN_140762500,
// questmasktable.cpp:891): the larger of the fixed exp and the common exp curve of the reward's relativeExp at the
// player's level, then, for a quest with a rank, Lua calcQuestRewardExpFactorRateByRank.
public static class QuestExpRules {
    // The quest's rank when it has none (the client skips the Lua factor for it).
    public const int NoRank = 5;

    // One level of a common exp curve: the base exp times the type's factor, rounded, and at least 1
    // (Unk_ExpCurve::Scale, exptable.cpp:155). A factor of 0 (epicQuest in KR) makes every level 1.
    public static long CurveValue(long baseExp, double factor) {
        return Math.Max((long) (baseExp * factor + 0.5), 1);
    }

    // calcQuestRewardExpFactorRateByRank(type, rank, rewardExpExtra, alliance) of the client's script: a table only for
    // field quests (0.32 at every rank) and alliance quests (S to D: 1.4/1.3/1.2/1.1/1.0, for alliances 1 to 5
    // 5/4/1.2/0.426/0.32), picked by the rank clamped to the table. Any other type has no table, the script fails and
    // the client reads 0 (no change). The extra is not read.
    public static float RankFactor(QuestType type, int rank, int alliance) {
        float[] rates;
        switch (type) {
            case QuestType.FieldQuest:
                rates = [0.32f, 0.32f, 0.32f, 0.32f, 0.32f];
                break;
            case QuestType.AllianceQuest:
                rates = alliance <= 5 ? [5f, 4f, 1.2f, 0.426f, 0.32f] : [1.4f, 1.3f, 1.2f, 1.1f, 1.0f];
                break;
            default:
                return 0f;
        }
        int index = Math.Max(Math.Min(rank + 1, rates.Length), 1);
        return rates[index - 1];
    }

    // The lowest standard level of an epic quest whose exp KMS2 names a quest's. Below it (standard levels 1 to 7
    // are seen, 10 and up on the other side) the exp is named a mission's; 10 is chosen here for 8 and 9.
    public const int QuestReasonLevel = 10;

    // The reason KMS2 names for a completed quest's exp: 1053 (mission) for an exploration mission and for an
    // epic quest of a standard level below 10, 1010 (quest) for every other quest seen (epic from level 10,
    // world, field and alliance quests).
    public static ExpType Reason(QuestType type, int standardLevel) {
        return type switch {
            QuestType.FieldMission => ExpType.mission,
            QuestType.EpicQuest when standardLevel < QuestReasonLevel => ExpType.mission,
            _ => ExpType.quest,
        };
    }

    // The prestige row a completed quest's exp adds: Exp_EpicQuest for an epic quest, Exp_QuestETC for any other.
    public static ExpType PrestigeType(QuestType type) {
        return type == QuestType.EpicQuest ? ExpType.epicQuest : ExpType.questEtc;
    }

    // The reward's exp: relativeExp is the curve value at the player's level (0 without a relativeExp).
    public static long Exp(int fixedExp, long relativeExp, QuestType type, int rank, int alliance) {
        long exp = Math.Max(fixedExp, relativeExp);
        if (rank != NoRank) {
            float rate = RankFactor(type, rank, alliance);
            if (Math.Abs(rate) > 1e-6f) {
                exp = (long) (exp * (double) rate + 0.000001); // ScaleValue
            }
        }
        return exp;
    }

    // The player's quest exp rate (SpecialAttribute.QuestExp, seg_questrewardrate: guild buff 110000113 +30% to +38%,
    // buff 100000009 +10%; a fraction) adds its share of the exp for a quest of standardLevel QuestRewardSAIgnoreLevel
    // (10 in KR) and up: CQuest::ShowRewards (questmanager_1.cpp:333-344) skips the rate below that level and adds
    // exp * (1 + rate) - exp, in float, to the exp.
    public static long StatBonus(long exp, double rate, int standardLevel, int ignoreLevel) {
        if (exp <= 0 || rate == 0 || standardLevel < ignoreLevel) {
            return exp;
        }
        long extra = (long) ((float) exp * (1f + (float) rate) - (float) exp);
        return Math.Max(0, exp + extra);
    }
}
