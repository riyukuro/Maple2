using Maple2.Model.Metadata;

namespace Maple2.Model.Validators;

// The stamp card (GameEventStampEvent). Its dialog (client uistampeventdialog.cpp) shows a stamp as done when the
// stamp's quest is rewarded, and reads the claims from user values: 1200 is 1 once the complete reward is
// claimed, 1201 the counts of the claimed reward rows as "4,7", 1202 the same for the special rows. It sends
// EventReward 0 + int count + byte special to claim a reward row and EventReward 1 for the complete reward.
public static class StampEventRules {
    // Several live cards: the one without a key (the client shows it whatever the player's values), else the first.
    public static T? PickEvent<T>(IEnumerable<T> events, Func<T, StampEvent?> data) where T : class {
        T? first = null;
        foreach (T gameEvent in events) {
            StampEvent? stampEvent = data(gameEvent);
            if (stampEvent == null) {
                continue;
            }
            if (stampEvent.Key == 0) {
                return gameEvent;
            }
            first ??= gameEvent;
        }
        return first;
    }

    // The stamps of the kind (special or not) whose quest is completed.
    public static int DoneStamps(StampEvent stampEvent, bool special, Func<int, bool> questCompleted) {
        return stampEvent.Stamps.Count(stamp => stamp.Special == special && questCompleted(stamp.QuestId));
    }

    // EventReward 0: the reward row of that count and kind, when as many stamps of the kind are done and the row
    // is not claimed yet (the dialog's row state 1). Null otherwise.
    public static StampEvent.Reward? CheckReward(StampEvent stampEvent, int count, bool special, string? claimed, Func<int, bool> questCompleted) {
        if (count <= 0 || LongTermAttendRules.ClaimedDays(claimed).Contains(count)) {
            return null;
        }
        StampEvent.Reward? reward = stampEvent.Rewards.FirstOrDefault(row => row.Count == count && row.Special == special);
        if (reward == null || DoneStamps(stampEvent, special, questCompleted) < count) {
            return null;
        }
        return reward;
    }

    // EventReward 1: the complete reward, when every stamp is done and it is not claimed yet.
    public static bool CanClaimComplete(StampEvent stampEvent, string? claimed, Func<int, bool> questCompleted) {
        return !CompleteClaimed(claimed) && stampEvent.Stamps.Length > 0 && stampEvent.Stamps.All(stamp => questCompleted(stamp.QuestId));
    }

    public static bool CompleteClaimed(string? claimed) {
        return int.TryParse(claimed, out int value) && value != 0;
    }

    // The quests are the account's, the user values a character's: the claims of the account's characters are
    // put together, so that a reward is given once for the account. Counts keep their first order.
    public static string MergeClaimed(IEnumerable<string?> claimed) {
        var counts = new List<int>();
        foreach (string? text in claimed) {
            foreach (int count in LongTermAttendRules.ClaimedDays(text)) {
                if (!counts.Contains(count)) {
                    counts.Add(count);
                }
            }
        }
        return string.Join(',', counts);
    }

    // The quests of the cards: the server starts them and completes them when their conditions are met.
    public static HashSet<int> Quests(IEnumerable<StampEvent> stampEvents) {
        return stampEvents.SelectMany(stampEvent => stampEvent.Stamps).Select(stamp => stamp.QuestId).ToHashSet();
    }
}
