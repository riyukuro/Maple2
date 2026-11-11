using Maple2.Tools;

namespace Maple2.Server.Game.Util;

/// <summary>
/// The room_expire trigger action. It is used only in the wedding rooms: the photo studios 84000003-84000005_wd
/// (after the 300 s timer 4000 or the exitstudio user value, guide summary 28400138 shows for 5 s, then the room
/// expires) and the minigame rooms 84000006_wd and 84000007_wd. The room ends by
/// sending every player back to their return map, as move_user with map 0 does.
/// Returning a player removes them from the field, so the player list is copied before the first return.
/// Each of these maps has itself as its enterReturnId, so entering the room saved the room as the newest return map
/// and a plain return would load the room again: the room's own entries are dropped first, and when none is left
/// the default return map is used.
/// </summary>
public static class RoomExpiry {
    public static void DropOwnReturn(LimitedStack<int> returnMaps, int roomMapId, int defaultMapId) {
        while (!returnMaps.IsEmpty && returnMaps.Peek() == roomMapId) {
            returnMaps.Pop();
        }
        if (returnMaps.IsEmpty) {
            returnMaps.Push(defaultMapId);
        }
    }

    public static int ReturnAll<T>(IEnumerable<T> players, Action<T> returnField) {
        T[] snapshot = players.ToArray();
        foreach (T player in snapshot) {
            returnField(player);
        }
        return snapshot.Length;
    }
}
