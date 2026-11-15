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

    // The ended events of the ids, moved to the ingest day; ids already in the table (running, or replayed by
    // MS2_REPLAY_ATTEND_GIFT) are left out. The second list names the ids that are neither ended nor kept.
    public static (List<GameEventMetadata> Replayed, List<int> Missing) Pick(IReadOnlyCollection<int> ids, IEnumerable<GameEventMetadata> ended,
                                                                           Func<int, bool> kept, DateTime now) {
        Dictionary<int, GameEventMetadata> byId = ended.GroupBy(e => e.Id).ToDictionary(group => group.Key, group => group.Last());
        var replayed = new List<GameEventMetadata>();
        var missing = new List<int>();
        foreach (int id in ids) {
            if (kept(id)) {
                continue;
            }
            if (byId.TryGetValue(id, out GameEventMetadata? gameEvent)) {
                replayed.Add(AttendGiftReplay.Redate(gameEvent, now));
            } else {
                missing.Add(id);
            }
        }
        return (replayed, missing);
    }
}
