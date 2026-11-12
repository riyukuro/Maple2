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
// rows at each end of a game of the arcade that cleared a round, up to that count a day, and lists them in the result
// dialog. Neither the client nor the event row gives a rule for a game that cleared no round (a Three Two One game
// lost from its first round still ends at its init_score 10000); it gets only arcadereward's round 0 row, so a
// player cannot take the day's items by losing at once.
public static class ArcadeEvent {
    public const int MinRoundsForReward = 1;

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

    // The event's reward rows when the game cleared a round (in Three Two One, won one) and the player is under its
    // count for the day, else none.
    public static RewardItem[] RewardsDue(ArcadeOpen arcadeOpen, int countToday, int roundsCleared) {
        return roundsCleared >= MinRoundsForReward && countToday < arcadeOpen.RewardCountPerDay ? arcadeOpen.Rewards : [];
    }

    public static int RewardCount(GameSession session, GameEvent gameEvent) {
        return session.GameEvent.Get(GameEventUserValueType.ArcadeOpenRewardCount, gameEvent.Id, DateTime.Now.Date.AddDays(1).ToEpochSeconds()).Int();
    }

    // Stored at once, not at logout: the reward may sit in a stored mail already, and a lost session must not
    // give it again. False when the database refused it.
    public static bool AddReward(GameSession session, GameEvent gameEvent) {
        session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.ArcadeOpenRewardCount, RewardCount(session, gameEvent) + 1);
        return session.GameEvent.Store(gameEvent.Id, GameEventUserValueType.ArcadeOpenRewardCount);
    }
}
