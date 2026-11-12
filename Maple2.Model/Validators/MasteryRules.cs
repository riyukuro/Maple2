using Maple2.Model.Enum;
using Maple2.Model.Error;

namespace Maple2.Model.Validators;

public static class MasteryRules {
    // The client checks a recipe in this order before it sends the craft: mastery, quest, meso (items come after).
    // A recipe with required quests needs one of them, not all. 0 means the recipe may be crafted.
    public static MasteryError CraftError(int mastery, int requiredMastery, int requiredQuestCount, bool hasAnyRequiredQuest, long meso, long requiredMeso) {
        if (requiredMastery != 0 && mastery < requiredMastery) {
            return MasteryError.s_mastery_error_lack_mastery;
        }
        if (requiredQuestCount > 0 && !hasAnyRequiredQuest) {
            return MasteryError.s_mastery_error_lack_quest;
        }
        if (requiredMeso != 0 && meso < requiredMeso) {
            return MasteryError.s_mastery_error_lack_meso;
        }
        return 0;
    }

    // The mastery a gathered or farmed recipe gives, as the client's mastery table works it out
    // (CMasteryTable::Factor, Grade, MaxGrade and IsProductionType, masterytable.cpp).
    // IsProductionType: mining, gathering, breeding and farming. Every other type gets the recipe's
    // rewardMastery whole.
    public static bool IsProduction(MasteryType type) {
        return type is MasteryType.Mining or MasteryType.Gathering or MasteryType.Breeding or MasteryType.Farming;
    }

    // Grade: the grade rows of the type in grade order; the first row whose value is above the mastery
    // gives its grade minus one, and a mastery no row is above gives the last row's grade (MaxGrade).
    // No rows: 0.
    public static int Grade(IEnumerable<(int Grade, int Value)> rows, int mastery) {
        int max = 0;
        foreach ((int grade, int value) in rows.OrderBy(row => row.Grade)) {
            if ((uint) value > (uint) mastery) {
                return grade - 1;
            }
            max = grade;
        }
        return max;
    }

    // Factor: for a production type, rewardMastery times the factor (in 1/10000) of the row for
    // grade(current mastery) - grade(the recipe's requireMastery), rounded half up; nothing when the
    // difference is negative or has no row.
    public static int Reward(MasteryType type, int rewardMastery, int requireMastery, int mastery,
                             IEnumerable<(int Grade, int Value)> rows, Func<int, int?> factorOf) {
        if (!IsProduction(type)) {
            return rewardMastery;
        }

        List<(int Grade, int Value)> list = rows.ToList();
        int difference = Grade(list, mastery) - Grade(list, requireMastery);
        if (difference < 0) {
            return 0;
        }
        int? factor = factorOf(difference);
        if (factor == null) {
            return 0;
        }

        float reward = (uint) (rewardMastery * factor.Value) / 10000.0f;
        return (int) (uint) (reward + 0.5f);
    }
}
