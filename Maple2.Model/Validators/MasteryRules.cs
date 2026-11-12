using Maple2.Model.Enum;
using Maple2.Model.Error;

namespace Maple2.Model.Validators;

public static class MasteryRules {
    // The reason KMS2 names for the character exp of a gather or a craft: 3100 plus the recipe's mastery type
    // (3103 mining, 3104 gathering, 3105 breeding, 3106 farming, 3108 engraving are seen; 3107 is in the client's
    // list). 3109 and 3110 for alchemy and cooking follow the same rule and are not seen. Null for the other types.
    public static ExpMessageCode? ExpReason(MasteryType type) {
        return type is >= MasteryType.Mining and <= MasteryType.Cooking ? (ExpMessageCode) (3100 + (int) type) : null;
    }

    // The id of a mastery grade's reward as the client makes it (CMasteryTable::MakeID, TypeFromID, GradeFromID):
    // type * 1000 + grade. Every id has one type and one grade, so a claimed grade has one key.
    public static (int Type, int Grade) RewardStep(int id) {
        return id < 0 ? (-1, -1) : (id / 1000, id % 1000);
    }

    // A required quest of a recipe counts while it is in progress: CMasteryManager asks IsQuestActive (state 1)
    // of each, so a quest that is completed or not taken does not count (masterymanager.cpp).
    public static bool CountsAsRequiredQuest(QuestState state) {
        return state == QuestState.Started;
    }

    // The client checks a recipe in this order before it gathers: mastery, then quest (CInteractGatheringObject).
    // A recipe with required quests needs one of them in progress, not all. 0 means the object may be gathered.
    public static MasteryError GatherError(int mastery, int requiredMastery, int requiredQuestCount, bool hasAnyRequiredQuest) {
        if (requiredMastery != 0 && mastery < requiredMastery) {
            return MasteryError.s_mastery_error_lack_mastery;
        }
        if (requiredQuestCount > 0 && !hasAnyRequiredQuest) {
            return MasteryError.s_mastery_error_lack_quest;
        }
        return 0;
    }

    // The client checks a recipe in this order before it sends the craft: mastery, quest, meso (items come after).
    // Its craft dialog also greys out a recipe above the player's level (UIMasteryDialog::HasRequiredLevel, 0 is
    // no limit); that check comes first here, as in the dialog's state text. 0 means the recipe may be crafted.
    public static MasteryError CraftError(int level, int requiredLevel, int mastery, int requiredMastery, int requiredQuestCount, bool hasAnyRequiredQuest, long meso, long requiredMeso) {
        if (requiredLevel != 0 && level < requiredLevel) {
            return MasteryError.s_mastery_error_invalid_level;
        }
        MasteryError error = GatherError(mastery, requiredMastery, requiredQuestCount, hasAnyRequiredQuest);
        if (error != 0) {
            return error;
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
