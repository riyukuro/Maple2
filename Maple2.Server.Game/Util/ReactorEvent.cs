using Maple2.Database.Extensions;
using Maple2.Model.Enum;
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

    // Stored at once, not at logout: the reward may sit in a stored mail already, and a lost session must not
    // give it again. False when the database refused it.
    public static bool AddReward(GameSession session, GameEvent gameEvent) {
        session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.ReactorRewardCount, RewardCount(session, gameEvent) + 1);
        return session.GameEvent.Store(gameEvent.Id, GameEventUserValueType.ReactorRewardCount);
    }
}
