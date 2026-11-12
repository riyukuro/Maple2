using Maple2.Model.Metadata;

namespace Maple2.Model.Validators;

// The stamp card (GameEventStampEvent). Its dialog (client uistampeventdialog.cpp) shows a stamp as done when the
// stamp's quest is rewarded, and reads the claims from user values: 1200 is 1 once the complete reward is
// claimed, 1201 the counts of the claimed reward rows as "4,7", 1202 the same for the special rows. It sends
// EventReward 0 + int count + byte special to claim a reward row and EventReward 1 for the complete reward.
public static class StampEventRules {
    // Several live cards: the one without a key (the client shows it whatever the player's values), else the first.
    // Of several cards without a key the last in the order sent: the client keeps one card per key, and a later
    // card of the same key replaces the earlier one (gamerule.cpp:2175).
    public static T? PickEvent<T>(IEnumerable<T> events, Func<T, StampEvent?> data) where T : class {
        T? first = null;
        T? keyless = null;
        foreach (T gameEvent in events) {
            StampEvent? stampEvent = data(gameEvent);
            if (stampEvent == null) {
                continue;
            }
            if (stampEvent.Key == 0) {
                keyless = gameEvent;
            }
            first ??= gameEvent;
        }
        return keyless ?? first;
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

    // The quests a new run of the card deletes: its own stamps' quests that are stamp event missions and that no
    // other card has as a stamp. Any other quest id in a card's data is left alone, and so is a quest two cards
    // share, which the other card still shows as done.
    public static int[] RunQuests(StampEvent card, IEnumerable<StampEvent> otherCards, Func<int, bool> isStampQuest) {
        HashSet<int> shared = Quests(otherCards);
        return card.Stamps.Select(stamp => stamp.QuestId).Where(questId => !shared.Contains(questId) && isStampQuest(questId)).Distinct().ToArray();
    }

    // The quests of the cards: the server starts them and completes them when their conditions are met.
    public static HashSet<int> Quests(IEnumerable<StampEvent> stampEvents) {
        return stampEvents.SelectMany(stampEvent => stampEvent.Stamps).Select(stamp => stamp.QuestId).ToHashSet();
    }
}
