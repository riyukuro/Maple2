using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;

namespace Maple2.Model.Validators;

// The burning levelup's rules, as BurningLevelupContext has them (commonfiles/gameeventcontext.cpp:2063-2089). The
// client never calls IsBurningLevel or GetBonusLevel: the server grants the levels.
public static class BurningLevelupRules {
    // The event's user values (GameEventUserValueType 2000-2004).
    public static readonly GameEventUserValueType[] ValueTypes = [
        GameEventUserValueType.BurningLevelupCharacter, GameEventUserValueType.BurningLevelupReached,
        GameEventUserValueType.BurningLevelupRewardTaken, GameEventUserValueType.BurningLevelupJumpingNotify,
        GameEventUserValueType.BurningLevelupJumpingDone,
    ];

    // IsBurningLevel: above the begin level and below the end level.
    public static bool IsBurningLevel(BurningLevelup data, int level) => level > data.BeginLevel && level < data.EndLevel;

    // GetBonusLevel: the levels added on reaching `level`. Every (Bonus + 1)th level from the begin level is earned;
    // reaching any other one adds the levels up to the next earned one, at most up to the end level. KR rows (begin 9,
    // bonus 2): level 10 adds 2 (to 12), level 13 adds 2 (to 15), and so on up to 70.
    public static int BonusLevel(BurningLevelup data, int level) {
        if (!IsBurningLevel(data, level)) {
            return 0;
        }
        int step = System.Math.Max(data.Bonus, 0) + 1;
        int remain = (level - data.BeginLevel) % step;
        if (remain == 0) {
            return 0;
        }
        int bonus = step - remain;
        return level + bonus > data.EndLevel ? data.EndLevel - level : bonus;
    }

    // A flag value (2001-2004) is set when it is a number other than 0.
    public static bool IsSet(string? value) => long.TryParse(value, out long number) && number != 0;

    // HasTarget: no target list takes every job; else the job code must be listed.
    public static bool HasTarget(BurningLevelup data, int jobCode) => data.Targets.Length == 0 || data.Targets.Contains(jobCode);

    // The values the character list reads: the account's 2000-2004 values of the running events, each moved to its
    // event's current end first (EventValueRules.FollowEventEnd), then those not run out at `now`.
    public static List<GameEventUserValue> LoginValues(IEnumerable<GameEventUserValue> stored, IReadOnlyCollection<GameEvent> running, long now) {
        var values = new List<GameEventUserValue>();
        foreach (GameEventUserValue value in stored) {
            GameEvent? gameEvent = running.FirstOrDefault(found => found.Id == value.EventId && found.Metadata.Data is BurningLevelup);
            if (gameEvent == null || !ValueTypes.Contains(value.Type)) {
                continue;
            }
            GameEventUserValue moved = EventValueRules.FollowEventEnd(value, gameEvent);
            if (moved.ExpirationTime >= now) {
                values.Add(moved);
            }
        }
        return values;
    }

    public enum Pick {

        Ok,
        NoEvent, // no running event takes the character (none runs, the account picked for each, or its job is no target)
        NotOwned, // the character is not of the account, or is deleted
        Level, // the character is at or past the end level
    }

    // The pick of EventReward 0x10 (UICharacterListDialog::SendEventReward, uicharacterlistdialog.cpp:2093): the
    // event is the one the client's popup offers (ShowBurningLevelupPopup, :2041), the first running one the account
    // has not picked a character for (2000), not completed (2001), whose jumping is not done (2004) and that takes
    // the character's job. Chosen here: the lowest event id comes first (the client keeps the events in its own
    // order; KR runs one at a time). The client has no refusal reply.
    // picked: the account's 2000 value of an event (0: none); flag: whether the account's value of a type of an event is set.
    public static (Pick Result, GameEvent? Event) CheckPick(IEnumerable<GameEvent> running, bool owned, int level, int jobCode,
                                                           System.Func<int, long> picked, System.Func<int, GameEventUserValueType, bool> flag) {
        if (!owned) {
            return (Pick.NotOwned, null);
        }
        GameEvent? found = running
            .Where(gameEvent => gameEvent.Metadata.Data is BurningLevelup)
            .OrderBy(gameEvent => gameEvent.Id)
            .FirstOrDefault(gameEvent => {
                var data = (BurningLevelup) gameEvent.Metadata.Data;
                return !flag(gameEvent.Id, GameEventUserValueType.BurningLevelupReached)
                       && !(data.Jumping != null && flag(gameEvent.Id, GameEventUserValueType.BurningLevelupJumpingDone))
                       && picked(gameEvent.Id) == 0
                       && HasTarget(data, jobCode);
            });
        if (found == null) {
            return (Pick.NoEvent, null);
        }
        return level >= ((BurningLevelup) found.Metadata.Data).EndLevel ? (Pick.Level, found) : (Pick.Ok, found);
    }
}
