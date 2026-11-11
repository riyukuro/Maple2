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
    // The most a match fills the room with (rule 1): the row's maxUserCount, also for an isChangeMaxUser room.
    // DungeonRoom::GetMaxUserCount is not used here: for such a room it is the party size held at 2 or more, the
    // size of a room already made, so a search would be full at 2.
    public static int MaxUsers(DungeonMatchRoom room) => room.MaxUsers;

    // A party whose search ended (stopped, cancelled, or its room could not be made) with one member left is the
    // party of one made for a solo searcher, or what is left of a party: it goes, so the player searches solo
    // again next time (rv40 MAJOR 1).
    public static bool DisbandsWhenSearchEnds(int members) => members <= 1;

    // A member leaves, is kicked, or the leader goes with no one online to take over: does the party go? A party
    // with no search goes as before, with fewer than 2 online or at most 2 members. A searching party is kept
    // while anyone else is online, down to one member, so a 2-member party's search goes on for the member left
    // (the resize rule; it goes when that search ends, DisbandsWhenSearchEnds) (rv40 MAJOR 4).
    public static bool Disbands(int members, int online, int otherOnline, bool searching) =>
        searching ? otherOnline == 0 : online < 2 || members <= 2;

    // This many more searchers fit in a group of this size: the joined group is at most the room's maximum.
    public static bool HasSpace(DungeonMatchRoom room, int users, int joining = 1) => users + joining <= MaxUsers(room);

    // The group has reached the room's maximum.
    public static bool IsFull(DungeonMatchRoom room, int users) => users > 0 && users >= MaxUsers(room);

    // The leader may stop the search and go with this many (rule 2): at least the minimum (1 when the row has
    // none), at most the maximum.
    public static bool CanStart(DungeonMatchRoom room, int users) =>
        users >= Math.Max(1, room.MinUsers) && users <= MaxUsers(room);

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
