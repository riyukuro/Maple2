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
}
