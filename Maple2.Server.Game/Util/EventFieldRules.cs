namespace Maple2.Server.Game.Util;

// The world map's time event portal (client: UIWorldMapDialog "OnPortal" and MoveByPortal,
// uiworldmapdialog.cpp:604 and :1510). The client itself refuses a dead player, a player in battle, the field
// the player is in and a field outside the continent group, and then sends the field id.
public static class EventFieldRules {
    public enum Result {
        Ignore, // the client does not send this; nothing is answered
        NoEvent, // answer MoveEventField 0
        OtherContinent, // answer MoveEventField 1
        Move,
    }

    // eventFields: the target fields of the time events that are running now.
    public static Result Decide(bool hasField, bool isDead, int currentFieldId, int targetFieldId, IEnumerable<int> eventFields) {
        if (!hasField || isDead || targetFieldId <= 0 || targetFieldId == currentFieldId) {
            return Result.Ignore;
        }

        return eventFields.Contains(targetFieldId) ? Result.Move : Result.NoEvent;
    }

    // Client: Constants::AreSameGroup (unit_1402ab440_constantsblocks.cpp:303) on the continent codes of the two
    // fields (UIWorldMapDialog::FUN_141b60b40, uiworldmapdialog.cpp:2348): the same continent, or both in the
    // WarpOpenContinent list of the constants.
    public static bool SameGroup(int fromContinent, int toContinent, IEnumerable<int> openContinents) {
        if (fromContinent == toContinent) {
            return true;
        }

        return openContinents.Contains(fromContinent) && openContinents.Contains(toContinent);
    }

    // How long an answer of the World server about the running events is kept for one session, so that a
    // client that repeats the request does not make one World call each time.
    public static readonly TimeSpan CacheTime = TimeSpan.FromSeconds(5);

    public static bool IsFresh(DateTime askedAt, DateTime now) {
        return now >= askedAt && now - askedAt < CacheTime;
    }
}
