using Maple2.Model.Metadata;

namespace Maple2.File.Ingest.Helpers;

// MS2_REPLAY_EVENTS: ended game events kept by id, each moved to start on the ingest day with its original length
// (AttendGiftReplay.Redate). The ingest drops ended events, and every KR row of some event types has ended: the epic
// quest restart (EpicRestart, 320001 and 320002, both 2017) among them. An operator names the rows to run, e.g.
// MS2_REPLAY_EVENTS=320002. Unset or empty, the ingest is unchanged. A running event, or one the ingest dropped for
// another reason (no readable data, no map for an EpicRestart), is not changed; the ingest prints a line for it.
public static class EventReplay {
    public const string Option = "MS2_REPLAY_EVENTS";

    // The ids of the option: numbers separated by commas or spaces. Anything else stops the ingest, so a typo is not
    // silently ignored.
    public static int[] Ids(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return [];
        }
        var ids = new List<int>();
        foreach (string text in value.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            if (!int.TryParse(text, out int id) || id <= 0) {
                throw new ArgumentException($"{Option} must list event ids separated by commas, not '{value}'");
            }
            if (!ids.Contains(id)) {
                ids.Add(id);
            }
        }
        return ids.ToArray();
    }

    // What happened to each listed id.
    public enum Outcome { Replayed, Companion, Kept, Missing, NoData, NoRewards, NoQuests, NoPlayItem }

    public readonly record struct Result(int Id, Outcome Outcome, GameEventMetadata? Original = null, GameEventMetadata? Moved = null, int With = 0);

    // The ended events of the ids, moved to the ingest day, with the tests MS2_REPLAY_ATTEND_GIFT makes before it
    // replays an event (AttendGiftReplay): an event whose type needs its data and has none, whose reward items the
    // client's item data lacks (clientRank 0: the client asserts on such an item at login), whose quests the
    // client's quest table lacks, or whose play item no ended row gives (with items the client has) is not
    // replayed. The play item's rows (AttendGiftReplay.Companions) whose items the client has move with the event.
    // An id already in the table (running, or replayed by MS2_REPLAY_ATTEND_GIFT) is left as it is.
    public static List<Result> Pick(IReadOnlyCollection<int> ids, IReadOnlyCollection<GameEventMetadata> ended, Func<int, bool> kept, DateTime now,
                                    Func<int, int> clientRank, Func<int, bool> clientQuest) {
        Dictionary<int, GameEventMetadata> byId = ended.GroupBy(e => e.Id).ToDictionary(group => group.Key, group => group.Last());
        var results = new List<Result>();
        var added = new HashSet<int>();
        bool Kept(int id) => kept(id) || added.Contains(id);
        foreach (int id in ids) {
            if (Kept(id)) {
                results.Add(new Result(id, Outcome.Kept));
                continue;
            }
            if (!byId.TryGetValue(id, out GameEventMetadata? gameEvent)) {
                results.Add(new Result(id, Outcome.Missing));
                continue;
            }
            GameEventMetadata[] companions = AttendGiftReplay.Companions(gameEvent, ended)
                .Where(companion => AttendGiftReplay.ClientHasRewards(companion, clientRank)).ToArray();
            Outcome? refused = AttendGiftReplay.NeedData.Contains(gameEvent.Type) && gameEvent.Data == null ? Outcome.NoData
                : !AttendGiftReplay.ClientHasRewards(gameEvent, clientRank) ? Outcome.NoRewards
                : !AttendGiftReplay.ClientHasQuests(gameEvent, clientQuest) ? Outcome.NoQuests
                : AttendGiftReplay.PlayItem(gameEvent.Data) != 0 && companions.Length == 0 ? Outcome.NoPlayItem
                : null;
            if (refused is Outcome outcome) {
                results.Add(new Result(id, outcome, gameEvent));
                continue;
            }
            GameEventMetadata moved = AttendGiftReplay.Redate(gameEvent, now);
            results.Add(new Result(id, Outcome.Replayed, gameEvent, moved));
            added.Add(id);
            foreach (GameEventMetadata companion in companions) {
                if (Kept(companion.Id)) {
                    continue;
                }
                results.Add(new Result(companion.Id, Outcome.Companion, companion, AttendGiftReplay.Shift(companion, moved.StartTime - gameEvent.StartTime), id));
                added.Add(companion.Id);
            }
        }
        return results;
    }

    // The ingest's line for a result.
    public static string Line(Result result) {
        GameEventMetadata? o = result.Original;
        GameEventMetadata? m = result.Moved;
        return result.Outcome switch {
            Outcome.Replayed => $"{Option}: {o!.Type} {result.Id} ({o.StartTime:yyyy-MM-dd HH:mm} to {o.EndTime:yyyy-MM-dd HH:mm}) replayed {m!.StartTime:yyyy-MM-dd HH:mm} to {m.EndTime:yyyy-MM-dd HH:mm}",
            Outcome.Companion => $"{Option}: {o!.Type} {result.Id} replayed with {result.With}, {m!.StartTime:yyyy-MM-dd HH:mm} to {m.EndTime:yyyy-MM-dd HH:mm}",
            Outcome.Kept => $"{Option}: {result.Id} is already in the table (running or replayed), left as it is",
            Outcome.Missing => $"{Option}: {result.Id} is no ended event the ingest read, not replayed",
            Outcome.NoData => $"{Option}: {o!.Type} {result.Id} not replayed, its data was not read",
            Outcome.NoRewards => $"{Option}: {o!.Type} {result.Id} not replayed, a reward item has no rank in the client's item data",
            Outcome.NoQuests => $"{Option}: {o!.Type} {result.Id} not replayed, a quest is not in the client's quest table",
            Outcome.NoPlayItem => $"{Option}: {o!.Type} {result.Id} not replayed, no ended row gives its play item",
            _ => $"{Option}: {result.Id}",
        };
    }
}
