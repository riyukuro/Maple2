using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.File.Ingest.Helpers;

// The mentoring season out of the box. The client opens mentoring (the Community menu's Mentoring entry, UI 0x4f4b)
// only while a ReturnUser event of a season (value3 > 0) runs (GameEventReturnUser::OnStart, UIMentoringDialog::
// IsAvailable), and every season row of the server pack has ended (KR 180104..180117, seasons 1 to 10, the last
// 2019-04-11 to 2019-06-20). When the data has no season row that runs or is still to come, the ingest adds the
// seasons that follow the last one: the same row (its returner rule, quests and rewards), each a season length
// (its start to its end) after the one before, season numbers going on after the last (KR 11, 12, ...). Chosen
// here: rows from the season that runs at the ingest to Horizon ahead, ids IdBase + season number; past ones are
// left out (the ingest drops ended rows), and a later ingest makes the same rows, as the dates follow from the data
// alone. None when the Mentoring feature is off (the operator's feature.xml at the ingest) or the data has no
// season row.
public static class DefaultMentoringSeason {
    public const string Feature = "Mentoring";
    public const int IdBase = 1_800_000_000;
    public static readonly TimeSpan Horizon = TimeSpan.FromDays(20 * 365);

    public static List<GameEventMetadata> Rows(IEnumerable<GameEventMetadata> events, DateTime now, bool featureOn, ICollection<int> takenIds) {
        var rows = new List<GameEventMetadata>();
        if (!featureOn) {
            return rows;
        }
        List<GameEventMetadata> seasons = events.Where(gameEvent => gameEvent is { Type: GameEventType.ReturnUser, Data: ReturnUser { SeasonId: > 0 } }).ToList();
        if (seasons.Count == 0 || seasons.Any(gameEvent => gameEvent.EndTime >= now)) {
            return rows;
        }
        GameEventMetadata last = seasons.OrderBy(gameEvent => gameEvent.StartTime).ThenBy(gameEvent => ((ReturnUser) gameEvent.Data).SeasonId).Last();
        var lastData = (ReturnUser) last.Data;
        TimeSpan length = last.EndTime - last.StartTime;
        if (length <= TimeSpan.Zero || last.StartTime == DateTime.MinValue) {
            Console.WriteLine($"Mentoring: no season runs and the last season row {last.Id} has no readable start or end; no season added");
            return rows;
        }
        // The first season that has not ended at `now`.
        long first = Math.Max(1, (long) Math.Floor((now - last.EndTime) / length) + 1);
        for (long k = first; ; k++) {
            DateTime start = last.EndTime + (k - 1) * length;
            if (start > now + Horizon) {
                break;
            }
            long season = lastData.SeasonId + k;
            if (IdBase + season > int.MaxValue) {
                break;
            }
            int id = (int) (IdBase + season);
            if (takenIds.Contains(id)) {
                continue;
            }
            rows.Add(last with {
                Id = id,
                StartTime = start,
                EndTime = start + length,
                Value3 = season.ToString(),
                Data = lastData with { SeasonId = (int) season },
            });
        }
        return rows;
    }
}
