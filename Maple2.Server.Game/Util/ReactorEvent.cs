using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;
using Serilog;

namespace Maple2.Server.Game.Util;

// The game event side of a reactor (CheckReactorGameEvent): the reactor's event must run, list the player's
// field, and the player must be under the event's reward count for the day.
public static class ReactorEvent {
    private static readonly ILogger Logger = Log.Logger.ForContext(typeof(ReactorEvent));

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

    // The reward items, the day's count one more and the reward state's condition items taken (take: bag stacks and
    // amounts, as ReactorRules.Plan), stored in one transaction (EventClaim): a crash cannot keep the items and lose
    // the count, so the day's limit holds after a restart, and a store that fails takes nothing (the items were taken
    // from memory before the claim, and lost with it). The take reaches the bag at the commit, before the reward.
    // False when nothing was stored.
    public static bool Claim(GameSession session, GameEvent gameEvent, IReadOnlyList<Item> items, IReadOnlyList<(long Uid, int Amount)> take) {
        int count = RewardCount(session, gameEvent);
        return EventClaim.Give(session, items, [EventClaim.Of(gameEvent.Id, GameEventUserValueType.ReactorRewardCount, count + 1)], db => {
            foreach ((long uid, int amount) in take) {
                if (!session.Item.Inventory.SaveConsumed(db, uid, amount)) {
                    return false;
                }
            }
            // Registered before EventClaim's own commit action, so the take leaves the bag before the reward comes.
            db.AfterCommit(() => {
                foreach ((long uid, int amount) in take) {
                    // Stored as taken; a stack that left the bag between its save and the commit is logged as the
                    // other SaveConsumed uses. A throw is caught as in EventClaim's action: out of the commit, it
                    // would skip that action (the count not set in memory, written back as it was by the next save)
                    // and answer "not stored" for a claim that was.
                    try {
                        if (!session.Item.Inventory.ConsumeStored(uid, amount)) {
                            Logger.Error("Reactor event {EventId}: stored take of {Amount} of item {ItemUid} not taken from {CharacterId}", gameEvent.Id, amount, uid, session.CharacterId);
                        }
                    } catch (Exception ex) {
                        Logger.Error(ex, "Reactor event {EventId}: stored take of {Amount} of item {ItemUid} not taken from {CharacterId}", gameEvent.Id, amount, uid, session.CharacterId);
                    }
                }
            });
            return true;
        }) != null;
    }
}
