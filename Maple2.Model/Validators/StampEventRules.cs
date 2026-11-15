using Maple2.Model.Metadata;

namespace Maple2.Model.Validators;

// The stamp card (GameEventStampEvent). Its dialog (client uistampeventdialog.cpp) shows a stamp as done when the
// stamp's quest is rewarded, and reads the claims from user values: 1200 is 1 once the complete reward is
// claimed, 1201 the counts of the claimed reward rows as "4,7", 1202 the same for the special rows. It sends
// EventReward 0 + int count + byte special to claim a reward row and EventReward 1 for the complete reward.
public static class StampEventRules {
    // The card the client shows (CGameRule::GetStampEvent, gamerule.cpp:2175). It keeps one card per key, a later
    // card of a key replacing the earlier (the order sent), then takes the card of key 10001 while that mark (the
    // user value of that type with event id 0, UserMarks) reads true, else the card of key 10002 while that one
    // does, else the card without a key, else a card whose mark reads true. None otherwise: the client shows no
    // card then, so nothing is claimed on one. `marked` reads a mark as the client does (true, false when absent).
    public static T? PickEvent<T>(IEnumerable<T> events, Func<T, StampEvent?> data, Func<int, bool> marked) where T : class {
        var byKey = new Dictionary<int, T>();
        var keys = new List<int>();
        foreach (T gameEvent in events) {
            StampEvent? stampEvent = data(gameEvent);
            if (stampEvent == null) {
                continue;
            }
            if (!byKey.ContainsKey(stampEvent.Key)) {
                keys.Add(stampEvent.Key);
            }
            byKey[stampEvent.Key] = gameEvent;
        }
        foreach (int key in new[] { 10001, 10002 }) {
            if (byKey.TryGetValue(key, out T? card) && marked(key)) {
                return card;
            }
        }
        if (byKey.TryGetValue(0, out T? keyless)) {
            return keyless;
        }
        int found = keys.FirstOrDefault(key => marked(key), -1);
        return found < 0 ? null : byKey[found];
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
