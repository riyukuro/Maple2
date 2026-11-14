using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

// The field trigger events of timeEventData type 6 (FORK.md "Field trigger events"). The client does not read these
// rows; what the server does with them follows the scripts they feed. The TimeEvent_triggerValue scripts of the
// Kritias fields wait on the row's key (EventStart or MelodyOn = 1), open their puzzle or chests, then wait
// wait_seconds_user_value TimeEventLifeTime ("the time event gives the keep time") before they close it.
public static class FieldTriggerEventRules {
    public const string LifeTimeKey = "TimeEventLifeTime";

    // The run started on the row's grid that is still alive at now; null when none is.
    public static DateTime? CurrentRun(FieldTriggerEventMetadata row, DateTime now) =>
        FieldInteractEventRules.CurrentRun(row.StartTime, row.EndTime, row.CycleTime, row.LifeTime, now);

    // The next run start after now, late by a part of randomTime (randomPart, 0 to 1); null when none is left.
    public static DateTime? NextRun(FieldTriggerEventMetadata row, DateTime now, double randomPart) {
        if (FieldInteractEventRules.NextRun(row.StartTime, row.EndTime, row.CycleTime, now) is not { } next) {
            return null;
        }
        return row.RandomTime > TimeSpan.Zero ? next + row.RandomTime * randomPart : next;
    }

    // The seconds left of a run that ends at ends (TimeEventLifeTime), rounded up; 0 when it is over.
    public static int SecondsLeft(DateTime now, DateTime ends) =>
        ends <= now ? 0 : (int) Math.Ceiling((ends - now).TotalSeconds);

    // The user values a run gives the row's trigger, the life time first: the script reads it once the key moves it.
    public static void Apply(FieldTriggerEventMetadata row, TriggerUserValues values, int secondsLeft) {
        values.Set(row.TriggerId, LifeTimeKey, secondsLeft);
        values.Set(row.TriggerId, row.Key, row.Value);
    }
}
