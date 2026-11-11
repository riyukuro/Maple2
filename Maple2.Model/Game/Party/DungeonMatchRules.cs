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

    // The match kind of a room's group type (clubcommon.cpp FUN_1402178b0, the kind Party 34 and the room dialog's
    // IsMatchSearching use): normal and raid 1, worldBossDungeon 2, event 3, lapenta 4; 0 for a room no search runs for.
    public static byte KindOf(DungeonGroupType groupType) => groupType switch {
        DungeonGroupType.normal or DungeonGroupType.raid => 1,
        DungeonGroupType.worldBossDungeon => 2,
        DungeonGroupType.@event => 3,
        DungeonGroupType.lapenta => 4,
        _ => 0,
    };

    // The searching bits of the register reply (0x1e) and Party Load: CPartyManager::IsMatchSearching(kind) tests
    // bit kind.
    public static byte SearchBits(byte kind) => kind is > 0 and < 8 ? (byte) (1 << kind) : (byte) 0;

    // The rooms of a Party 32 with picked rooms, or Party 33, that the search may use: a room of the table whose
    // group type has this match kind, with a level limit the lowest member meets.
    public static List<DungeonRoomMetadata> PickedRooms(IReadOnlyDictionary<int, DungeonRoomMetadata> table, IEnumerable<int> roomIds, byte kind, short level) {
        var result = new List<DungeonRoomMetadata>();
        foreach (int id in roomIds.Distinct()) {
            if (table.TryGetValue(id, out DungeonRoomMetadata? room) && KindOf(room.GroupType) == kind && kind != 0 && level >= room.Limit.MinLevel) {
                result.Add(room);
            }
        }
        return result;
    }

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
