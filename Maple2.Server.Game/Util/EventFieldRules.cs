namespace Maple2.Server.Game.Util;

// The world map's time event portal (client: UIWorldMapDialog "OnPortal" and MoveByPortal,
// uiworldmapdialog.cpp:604 and :1510). The client itself refuses a dead player, a player in battle, the field
// the player is in and a field outside the continent group, and then sends the field id.
public static class EventFieldRules {
    public enum Result {
        Ignore, // the client does not send this; nothing is answered
        NoEvent, // answer MoveEventField 0
        Move,
    }

    // eventFields: the target fields of the time events that are running now.
    public static Result Decide(bool hasField, bool isDead, int currentFieldId, int targetFieldId, IEnumerable<int> eventFields) {
        if (!hasField || isDead || targetFieldId <= 0 || targetFieldId == currentFieldId) {
            return Result.Ignore;
        }

        return eventFields.Contains(targetFieldId) ? Result.Move : Result.NoEvent;
    }
}
