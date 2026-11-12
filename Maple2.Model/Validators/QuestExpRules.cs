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
}
