using Maple2.Model.Metadata;

namespace Maple2.Model.Validators;

// The time run (GameEventTimeRunEvent). Its dialog (client uitimeruneventdialog.cpp) counts the distance run as
// the sum of the distances of the rewarded quests (GetEarnedDistance) and reads two user values: 1500 is the final
// reward state (the final claim is sent only while it is 0) and 1501 the key of the last step reward taken. A step
// row is offered only when its key is the next key after 1501 (TimeRunEventContext::GetNextRewardKey: the first
// key above it) and the distance reaches it; the final reward only when the distance reaches the event's total.
public static class TimeRunEventRules {
    public static int EarnedDistance(TimeRunEvent timeRun, Func<int, bool> questRewarded) {
        return timeRun.Quests.Where(quest => questRewarded(quest.Id)).Sum(quest => quest.Distance);
    }

    public static int TotalDistance(TimeRunEvent timeRun) {
        return timeRun.Quests.Sum(quest => quest.Distance);
    }

    // The first step key above the one taken, 0 when none is left.
    public static int NextRewardKey(TimeRunEvent timeRun, int taken) {
        int next = 0;
        foreach (int key in timeRun.StepRewards.Keys) {
            if (key > taken && (next == 0 || key < next)) {
                next = key;
            }
        }
        return next;
    }

    public static bool CanClaimStep(TimeRunEvent timeRun, int key, int taken, Func<int, bool> questRewarded) {
        return key > 0 && key == NextRewardKey(timeRun, taken) && key <= EarnedDistance(timeRun, questRewarded);
    }

    public static bool CanClaimFinal(TimeRunEvent timeRun, int received, Func<int, bool> questRewarded) {
        return received == 0 && EarnedDistance(timeRun, questRewarded) >= TotalDistance(timeRun);
    }
}
