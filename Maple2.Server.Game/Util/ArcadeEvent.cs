using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Util;

// The event arcades (arcadegame.xml feature Event, m_bIsEventArcade): the lobby lists one only while an ArcadeOpen
// game event names it (UIArcadeMainDialog, uiarcademaindialog.cpp:287, CGameEventValueArcadeOpen::Get), so the
// server lets a player in only then. The client keeps the first event of an arcade (CGameEventValueArcadeOpen::Add
// refuses a second); here the lowest event id.
// Chosen here: the client never receives the event's reward rows or its rewardCountPerDay, so the server gives the
// rows at each end of a game of the arcade, up to that count a day, and lists them in the result dialog.
public static class ArcadeEvent {
    public static GameEvent? Open(IEnumerable<GameEvent> events, int arcadeId) {
        return events
            .Where(gameEvent => gameEvent.Metadata.Data is ArcadeOpen arcadeOpen && arcadeOpen.ArcadeId == arcadeId)
            .MinBy(gameEvent => gameEvent.Id);
    }

    // Whether the lobby's enter of the game may go on: an arcade that is not an event arcade always, an event
    // arcade only while an event opens it.
    public static bool CanEnter(ArcadeGameTable.Entry game, IEnumerable<GameEvent> events) {
        return !game.IsEventArcade || Open(events, game.Id) != null;
    }

    // The event's reward rows when the player is under its count for the day, else none.
    public static RewardItem[] RewardsDue(ArcadeOpen arcadeOpen, int countToday) {
        return countToday < arcadeOpen.RewardCountPerDay ? arcadeOpen.Rewards : [];
    }

    public static int RewardCount(GameSession session, GameEvent gameEvent) {
        return session.GameEvent.Get(GameEventUserValueType.ArcadeOpenRewardCount, gameEvent.Id, DateTime.Now.Date.AddDays(1).ToEpochSeconds()).Int();
    }

    public static void AddReward(GameSession session, GameEvent gameEvent) {
        session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.ArcadeOpenRewardCount, RewardCount(session, gameEvent) + 1);
    }
}
