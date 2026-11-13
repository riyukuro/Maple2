using Maple2.Model.Error;

namespace Maple2.Model.Validators;

// The answers to the two interior design rewards, ResponseCube 0x28 and 0x29. The client reads one byte, the error,
// and shows it with ShowBuildNotice when it is not 0 (scenestategame.cpp:10635 and 10645). A reward that is given
// is answered with 0x27, DesignRankReward, instead.
public static class InteriorRewardRules {
    // RequestCube 0x28, the daily check-in (UIUGCHousingPointDialog::OnRewardItem, uiugchousingpointdialog.cpp:359):
    // a home already checked in today gets 84 ("No more bonuses can be collected today."), a reward that cannot be
    // made gets 85 ("Bonuses cannot be received at the moment.").
    public static UgcMapError CheckInError(bool checkedInToday, bool hasReward) {
        if (checkedInToday) {
            return UgcMapError.s_ugcmap_cant_take_interior_gift_more;
        }
        return hasReward ? UgcMapError.s_ugcmap_ok : UgcMapError.s_ugcmap_take_interior_gift_fail_from_db;
    }

    // RequestCube 0x29, a housing grade reward (the mastery dialog's housing tab, uimasterydialog.cpp:517). The client
    // offers a grade only when it is reached and not claimed (uimasterydialog.cpp:958: "active" when the decoration
    // level is at least the grade, "inactive" once claimed, else "lock"). A claimed grade gets 86 ("You have already
    // received the bonus."), a reward that cannot be made 85. A grade not reached yet is never offered, so it gets
    // no reward and no answer (null).
    public static UgcMapError? GradeError(IReadOnlyCollection<int> claimed, long decorationLevel, int grade, bool hasReward) {
        if (claimed.Contains(grade)) {
            return UgcMapError.s_ugcmap_already_taken_interior_grade_gift;
        }
        if (grade < 1 || grade > decorationLevel) {
            return null;
        }
        return hasReward ? UgcMapError.s_ugcmap_ok : UgcMapError.s_ugcmap_take_interior_gift_fail_from_db;
    }
}
