using Maple2.Model.Metadata;

namespace Maple2.Model.Validators;

// The field war (KR features Fieldwar01/02): a time event of type 7 opens one war of its group for LifeTime. The
// client shows the announce (FieldWar mode 0: the war id and a uid) as a side popup by Neirin, and its button sends
// SidePopup 1 with no id. The war's map (02020061-02020065, instancefield type FieldWar, at most 10 players) runs
// its trigger: main waits at its user count state for the user value FieldGameStart, 1 starts the war, 2 sends the
// players out ("room closed") and ends it.
//
// Chosen here (the server's side is not in the client): the announce goes to the players that meet the constants
// FieldWarRequireLevel and FieldWarRequireAchieveID, in the fields the global portal announce goes to; a room takes
// players for FieldWarInstanceEnterableDurationSeconds from its first one, then FieldGameStart is 1 when it holds
// FieldWarRequirePlayerCount players, else 2. A join after that window, or into a full room, opens a new room.
public static class FieldWarRules {
    public const int TimeEventType = 7;
    public const string StartKey = "FieldGameStart";
    public const int Start = 1;
    public const int Close = 2;

    // The war of the group a run opens: each row by its pickWeight. The roll is in [0, TotalWeight).
    public static FieldWarMetadata? Pick(IEnumerable<FieldWarMetadata> wars, int groupId, int roll) {
        foreach (FieldWarMetadata war in Group(wars, groupId)) {
            if (roll < war.PickWeight) {
                return war;
            }
            roll -= war.PickWeight;
        }
        return null;
    }

    public static int TotalWeight(IEnumerable<FieldWarMetadata> wars, int groupId) {
        return Group(wars, groupId).Sum(war => war.PickWeight);
    }

    private static IEnumerable<FieldWarMetadata> Group(IEnumerable<FieldWarMetadata> wars, int groupId) {
        return wars.Where(war => war.GroupId == groupId && war.PickWeight > 0).OrderBy(war => war.Id);
    }

    // A constant of 0 asks for nothing.
    public static bool Eligible(int level, int requireLevel, int requireAchievementId, Func<int, bool> hasAchievement) {
        return level >= requireLevel && (requireAchievementId <= 0 || hasAchievement(requireAchievementId));
    }

    public static int StartValue(int players, int requirePlayers) {
        return players >= requirePlayers ? Start : Close;
    }

    // Whether a join opens a new room: none yet, the room's enter window is over, or it is full.
    public static bool NeedsNewRoom(int roomId, long openedTick, long nowTick, int enterSeconds, int joined, int maxCount) {
        return roomId == 0 || nowTick - openedTick >= enterSeconds * 1000L || (maxCount > 0 && joined >= maxCount);
    }
}
