using Maple2.Model.Enum;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;

namespace Maple2.Server.Game.Util;

// The new-user and returning-user marks: a GameEvent NewUser or ReturnUser row names a user value type in value2
// (KR 10001, 10002, 10005, 10006; a season row 10003) that its new or returning players get. The client reads such a
// mark with event id 0 and reads nothing else of the rows: an attendance row that requires it is listed only while
// it reads true, one that requires its absence only while there is none (GameEventAttendGiftCli, client
// gameeventcli.cpp:437), and the stamp card of key 10001 or 10002 is the one shown while that mark reads true
// (CGameRule::GetStampEvent, gamerule.cpp:2175). So the server sets them, and decides who gets them:
// - a ReturnUser row: a returning character, as the mentoring season decides it (MentoringManager.Returning: its
//   last save before entry is before value1's date, or value1 days or more ago);
// - a NewUser row: a character created on or after value1's date.
// Chosen here (the rows do not say account or character): per character, as the mentoring return is; a character
// marked keeps the mark to the row's end, so entering again (with a recent save) does not take it away.
public static class UserMarks {
    public const string Set = "1";

    // The marks due at an entry, with the end of the row that gives each (the latest when several rows give one).
    // creationTime, lastSave and now are epoch seconds; lastSave is the character's last save before this entry.
    public static Dictionary<GameEventUserValueType, long> Due(IEnumerable<GameEvent> events, long creationTime, long lastSave, long now) {
        var due = new Dictionary<GameEventUserValueType, long>();
        foreach (GameEvent gameEvent in events) {
            int mark = gameEvent.Metadata.Data switch {
                ReturnUser row when row.RequiredUserValue > 0
                                    && MentoringManager.Returning(row.DateInactiveSince, row.DaysInactive, lastSave, now) => row.RequiredUserValue,
                NewUser row when row.UserValue > 0 && row.DateCreatedBy > DateTimeOffset.MinValue
                                 && creationTime >= row.DateCreatedBy.ToUnixTimeSeconds() => row.UserValue,
                _ => 0,
            };
            if (mark <= 0 || gameEvent.EndTime <= now) {
                continue;
            }
            var type = (GameEventUserValueType) mark;
            due[type] = Math.Max(due.GetValueOrDefault(type), gameEvent.EndTime);
        }
        return due;
    }
}
