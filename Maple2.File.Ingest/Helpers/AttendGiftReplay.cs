using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.File.Ingest.Helpers;

// Every attendance gift event (AttendGift) in the server pack has ended, and the ingest drops ended events, so
// the client never shows the attendance calendar. MS2_REPLAY_ATTEND_GIFT=1 keeps one ended AttendGift event and
// moves it to start on the ingest day with its original length. Unset, the ingest is unchanged.
public static class AttendGiftReplay {
    public const string Option = "MS2_REPLAY_ATTEND_GIFT";

    // Unset, empty, 0 or false: off. 1 or true: on. Anything else stops the ingest, so a typo is not silently off.
    public static bool Enabled(string? value) {
        return value?.Trim().ToLowerInvariant() switch {
            null or "" or "0" or "false" => false,
            "1" or "true" => true,
            _ => throw new ArgumentException($"{Option} must be 1/true or 0/false, not '{value}'"),
        };
    }

    // The ended AttendGift event to replay: the latest end; on the same end the longer event (more days to
    // attend), then the higher id. Events without a readable start or end time (DateTime.MinValue) or that end
    // before they start are not taken, nor (with clientRank) an event with a reward the client cannot show.
    public static GameEventMetadata? Pick(IEnumerable<GameEventMetadata> events, DateTime now, Func<int, int>? clientRank = null) {
        return Candidates(events, now)
            .Where(e => clientRank == null || ClientHasRewards(e, clientRank))
            .FirstOrDefault();
    }

    // The ended AttendGift events in pick order, whatever their rewards.
    public static IEnumerable<GameEventMetadata> Candidates(IEnumerable<GameEventMetadata> events, DateTime now) {
        return events
            .Where(e => e.Type == GameEventType.AttendGift && e.StartTime != DateTime.MinValue
                        && e.EndTime != DateTime.MinValue && e.StartTime <= e.EndTime && e.EndTime < now)
            .OrderByDescending(e => e.EndTime)
            .ThenByDescending(e => e.EndTime - e.StartTime)
            .ThenByDescending(e => e.Id);
    }

    // The client reads an AttendGift reward as an ItemGift, whose rank is the item data's rank, not the one sent
    // (ItemGift::vfunc_o18_1, commonfiles/itemgift.cpp:32, GetItemRank of the item id), and it asserts when that
    // is 0: the item is not in its item data or its property element has no rank. clientRank gives that rank for
    // an item id. KR AttendGift rows list items the KR item data does not have (99000056's four), so such an
    // event cannot be shown.
    public static bool ClientHasRewards(GameEventMetadata gameEvent, Func<int, int> clientRank) {
        return gameEvent.Data is not AttendGift attendGift || attendGift.Items.All(item => clientRank(item.ItemId) > 0);
    }

    // The event moved to start on the given day at its original time of day, with its original length. The times
    // keep no time zone, like the other events' (the server compares them with its local clock).
    public static GameEventMetadata Redate(GameEventMetadata gameEvent, DateTime day) {
        DateTime start = DateTime.SpecifyKind(day.Date, DateTimeKind.Unspecified) + gameEvent.StartTime.TimeOfDay;
        TimeSpan length = gameEvent.EndTime - gameEvent.StartTime;
        return gameEvent with { StartTime = start, EndTime = start + length };
    }
}
