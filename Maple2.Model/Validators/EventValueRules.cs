using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;

namespace Maple2.Model.Validators;

// The game event user values that last as long as their event (moved from GameEventManager: the login reads them too).
public static class EventValueRules {
    public static readonly GameEventUserValueType[] StampValueTypes = [
        GameEventUserValueType.StampCompleteRewardClaimed,
        GameEventUserValueType.StampRewardsClaimed,
        GameEventUserValueType.StampSpecialRewardsClaimed,
    ];

    // A long-term attendance value, a stamp card's claims and an item collection's claim last as long as their event. When the event's end has
    // moved since the value was stored (a replayed event is moved again by a later ingest), the value follows it:
    // stored at the first end it would run out while the event is still live, and every day or reward row claimed
    // so far could be claimed again. An event that is live again after its end is a new run, whose old values the
    // world server drops at its start (GameStorage BeginEventRun), so none is left here to follow.
    public static GameEventUserValue FollowEventEnd(GameEventUserValue value, GameEvent gameEvent) {
        bool follows = gameEvent.Metadata.Type switch {
            GameEventType.LongTermAttendGift => value.Type is GameEventUserValueType.LongTermAttendClaimedDays or GameEventUserValueType.LongTermAttendPoints
                or GameEventUserValueType.LongTermAttendDays or GameEventUserValueType.LongTermAttendPointTime,
            GameEventType.StampEvent => StampValueTypes.Contains(value.Type),
            GameEventType.CollectItemGroup => value.Type is GameEventUserValueType.CollectItemGroup,
            GameEventType.Snowman => value.Type is GameEventUserValueType.AccumCompleteCount or GameEventUserValueType.AccumCompleteRewardReceived,
            GameEventType.BurningLevelup => BurningLevelupRules.ValueTypes.Contains(value.Type),
            GameEventType.UserCondition => value.Type is GameEventUserValueType.UserConditionDone or GameEventUserValueType.UserConditionNotice,
            _ => false,
        };
        if (!follows || value.ExpirationTime == gameEvent.EndTime) {
            return value;
        }
        var moved = new GameEventUserValue(value.Type, gameEvent.EndTime, value.EventId);
        moved.SetValue(value.Value);
        return moved;
    }
}
