using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Util;

// The game event side of a reactor (CheckReactorGameEvent): the reactor's event must run, list the player's
// field, and the player must be under the event's reward count for the day.
public static class ReactorEvent {
    // gameEvent is null for a reactor that names no event.
    public static bool Check(GameSession session, ReactorTable.Entry metadata, out GameEvent? gameEvent) {
        gameEvent = null;
        if (metadata.GameEventId == 0) {
            return true;
        }
        if (session.Field == null) {
            return false;
        }

        gameEvent = session.FindEvent(metadata.GameEventId);
        if (gameEvent?.Metadata.Data is not Reactor reactor) {
            return false;
        }
        if (!reactor.MapIds.Contains(session.Field.MapId)) {
            return false;
        }
        return RewardCount(session, gameEvent) < reactor.DailyCount;
    }

    public static int RewardCount(GameSession session, GameEvent gameEvent) {
        return session.GameEvent.Get(GameEventUserValueType.ReactorRewardCount, gameEvent.Id, DateTime.Now.Date.AddDays(1).ToEpochSeconds()).Int();
    }

    // The reward items and the day's count one more, stored in one transaction (EventClaim): a crash cannot keep
    // the items and lose the count, so the day's limit holds after a restart. False when nothing was stored.
    public static bool Claim(GameSession session, GameEvent gameEvent, IReadOnlyList<Item> items) {
        int count = RewardCount(session, gameEvent);
        return EventClaim.Give(session, items, [EventClaim.Of(gameEvent.Id, GameEventUserValueType.ReactorRewardCount, count + 1)]) != null;
    }
}
