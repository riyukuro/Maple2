using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Util;

// The tree watering event (TreeWatering): the player waters the event's tree with the water pot held
// (TreeWateringAction, actionkeyobject.cpp; state 0xa0) up to the water limit a day, and on an emotion day, once the
// day's waterings are done, cheers it (state 0xa1) up to the emotion limit. At the end of the cast the client sends
// 0x00AA with 0 (water) or 1 (emotion) and shows the server's 0x0118 reply (SceneStateGame::OnTreeWatering).
// The client checks the tree is within 400 of the player by its name tag; the server's map entities carry no name
// tag, so here only the field is checked. Chosen here: the pot is held, not used up (the client only asks that it
// is held), and the counts end at midnight like the other daily event counts. The client keeps one event; here the
// lowest id.
public static class TreeWateringEvent {
    public enum Action : byte {
        Water = 0,
        Emotion = 1,
    }

    // The results the client shows (OnTreeWatering): mode 2 for water, mode 3 for emotion.
    public const byte Rewarded = 0;
    public const byte NoWaterPot = 1;
    public const byte WaterLimit = 2;
    public const byte EmotionLimit = 3;

    public static GameEvent? Find(IEnumerable<GameEvent> events) {
        return events.Where(gameEvent => gameEvent.Metadata.Data is TreeWatering).MinBy(gameEvent => gameEvent.Id);
    }

    // GetStep and IsInEmotionDay compare strictly (begin < now < end).
    public static bool InEmotionDay(TreeWatering tree, DateTime now) {
        return tree.EmotionDays.Any(day => day.Begin < now && now < day.End);
    }

    public static byte Water(TreeWatering tree, bool holdsPot, int waterCount) {
        if (!holdsPot) {
            return NoWaterPot;
        }
        return waterCount < tree.WaterLimit ? Rewarded : WaterLimit;
    }

    // Null: the client would not have let the player cheer (not an emotion day, or the day's waterings not done),
    // so nothing is sent.
    public static byte? Emotion(TreeWatering tree, DateTime now, int waterCount, int emotionCount) {
        if (!InEmotionDay(tree, now) || waterCount < tree.WaterLimit) {
            return null;
        }
        return emotionCount < tree.EmotionLimit ? Rewarded : EmotionLimit;
    }

    public static GameEventUserValueType CountType(Action action) {
        return action == Action.Water ? GameEventUserValueType.TreeWateringCount : GameEventUserValueType.TreeWateringEmotionCount;
    }

    public static int Count(GameSession session, GameEvent gameEvent, Action action) {
        return session.GameEvent.Get(CountType(action), gameEvent.Id, DateTime.Now.Date.AddDays(1).ToEpochSeconds()).Int();
    }

    public static void AddCount(GameSession session, GameEvent gameEvent, Action action) {
        session.GameEvent.Set(gameEvent.Id, CountType(action), Count(session, gameEvent, action) + 1);
    }
}
