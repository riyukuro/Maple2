using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Model.Game;

// A dungeon room as the matchmaker sees it: its id and the user counts of its dungeonroom row.
public readonly record struct DungeonMatchRoom(int Id, int MinUsers, int MaxUsers, bool ChangeMaxUsers) {
    public static DungeonMatchRoom Of(DungeonRoomMetadata room) =>
        new(room.Id, room.Limit.MinUserCount, room.Limit.MaxUserCount, room.Limit.ChangeMaxUsers);
}

// The user's rules for Party 32/33 (11-11): a match fills up to the room's maximum user count; the leader can
// stop early with the members found so far, at least the room's minimum.
public static class DungeonMatchRules {
    // DungeonRoom::GetMaxUserCount (dungeonroom.cpp): the maxUserCount of the row, or for an isChangeMaxUser
    // room the user count clamped into [2, maxUserCount].
    public static int MaxUsers(DungeonMatchRoom room, int users) {
        if (!room.ChangeMaxUsers) {
            return room.MaxUsers;
        }
        if (users < 2) {
            return 2;
        }
        return users > room.MaxUsers ? room.MaxUsers : users;
    }

    // One more searcher fits in a group of this size.
    public static bool HasSpace(DungeonMatchRoom room, int users) => users + 1 <= MaxUsers(room, users + 1);

    // The group has reached the room's maximum.
    public static bool IsFull(DungeonMatchRoom room, int users) => users > 0 && users >= MaxUsers(room, users);

    // The leader may stop the search and go with this many (rule 2): at least the minimum (1 when the row has
    // none), at most the maximum.
    public static bool CanStart(DungeonMatchRoom room, int users) =>
        users >= Math.Max(1, room.MinUsers) && users <= MaxUsers(room, users);

    // DungeonRoomTable::GetMatchRooms (dungeonroom.cpp), the rooms of an empty Party 32 room set: the rooms of group
    // types 1 and 2 ordered by gear score, player level limit and id (the row's sortOrder is not ingested), taken from
    // the highest at or under (gear score, level) downwards, skipping a room whose level limit is above the level,
    // at most pickCount of them (0: no limit). The client then drops the rooms CanEnterDungeonRoom refuses and the
    // hidden ones (isHidden is not ingested).
    public static List<DungeonRoomMetadata> MatchRooms(IEnumerable<DungeonRoomMetadata> rooms, short level, int gearScore, int pickCount) {
        List<DungeonRoomMetadata> ordered = rooms
            .Where(room => room.GroupType is DungeonGroupType.normal or DungeonGroupType.raid)
            .OrderByDescending(room => room.Limit.GearScore)
            .ThenByDescending(room => room.Limit.MinLevel)
            .ThenByDescending(room => room.Id)
            .ToList();

        var result = new List<DungeonRoomMetadata>();
        bool started = false;
        foreach (DungeonRoomMetadata room in ordered) {
            if (!started) {
                bool above = room.Limit.GearScore > gearScore
                             || (room.Limit.GearScore == gearScore && room.Limit.MinLevel > level);
                if (above) {
                    continue;
                }
                started = true;
            }
            if (level < room.Limit.MinLevel) {
                continue;
            }
            result.Add(room);
            if (pickCount != 0 && result.Count >= pickCount) {
                break;
            }
        }
        return result;
    }
}
