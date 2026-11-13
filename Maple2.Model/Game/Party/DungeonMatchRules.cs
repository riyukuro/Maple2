using Maple2.Model.Enum;
using Maple2.Model.Error;
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

    // A party join (an invite accepted, a Party Finder join) is refused while the party is matched and its room is
    // being made, and once a dungeon room is set when the party holds the room's maxUserCount (rv44 MINOR 3).
    // roomMaxUsers 0: not known (nothing is tested).
    public static bool RefusesJoin(bool matching, bool dungeonSet, int members, int roomMaxUsers) =>
        matching || (dungeonSet && roomMaxUsers > 0 && members >= roomMaxUsers);

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

    // Why a room is refused for a search, as OnDungeonMatchError (uidungeonhelper.cpp:333) names it; ok when it may be
    // searched. A room missing from the table, of another match kind, or closed now (day, hours, open period: it is
    // not created on request) cannot be entered (4). The lowest member's level under the room's limit, or an enter
    // state of 10 or more (CPartyManager::CanEnterDungeonRoom, partymanager.cpp:561, the FieldEntrance limits) is not
    // meeting its requirements (3). A party larger than the room's maxUserCount (MaxUsers) is over it (2).
    public static DungeonMatchError RoomError(DungeonRoomMetadata? room, byte kind, short level, int members, bool closed, DungeonEnterLimit? limit) {
        if (room == null || kind == 0 || KindOf(room.GroupType) != kind || closed) {
            return DungeonMatchError.s_dungeonMatch_error_containCouldNotEnter;
        }
        if (level < room.Limit.MinLevel || (limit != null && (byte) limit.Value >= 10)) {
            return DungeonMatchError.s_dungeonMatch_error_lackRequire;
        }
        if (members > MaxUsers(DungeonMatchRoom.Of(room))) {
            return DungeonMatchError.s_dungeonMatch_error_overMaxRegisterUser;
        }
        return DungeonMatchError.ok;
    }

    // The error of a search whose rooms were checked with RoomError (the refused ones with their maxUserCount), and
    // the int it carries. Picked rooms (Party 32 with rooms, Party 33): a room over its user count gives 2 with the
    // smallest such count, else one picked room gives its own error and several give 4 ("one of the selected
    // dungeons cannot be entered"). Recommended rooms (Party 32 with none) drop the refused ones, as the client's
    // GetAvailableRooms does; with none left the search gives 2 with the largest count when the party is too large
    // for an otherwise open room, else 1 (notFoundDungeon, the client's own CanStartSearch message,
    // uidungeonsearchdialog.cpp:431).
    public static (DungeonMatchError Error, int Param) SearchError(bool picked, int pickedCount, int accepted,
                                                                   IReadOnlyCollection<(DungeonMatchError Error, int MaxUsers)> refused) {
        int[] over = refused.Where(entry => entry.Error == DungeonMatchError.s_dungeonMatch_error_overMaxRegisterUser)
            .Select(entry => entry.MaxUsers).ToArray();
        if (!picked) {
            if (accepted > 0) {
                return (DungeonMatchError.ok, 0);
            }
            return over.Length > 0
                ? (DungeonMatchError.s_dungeonMatch_error_overMaxRegisterUser, over.Max())
                : (DungeonMatchError.s_dungeonMatch_error_notFoundDungeon, 0);
        }
        if (refused.Count == 0) {
            return accepted > 0 ? (DungeonMatchError.ok, 0) : (DungeonMatchError.s_dungeonMatch_error_containCouldNotEnter, 0);
        }
        if (over.Length > 0) {
            return (DungeonMatchError.s_dungeonMatch_error_overMaxRegisterUser, over.Min());
        }
        return pickedCount == 1 ? (refused.First().Error, 0) : (DungeonMatchError.s_dungeonMatch_error_containCouldNotEnter, 0);
    }

    // A member is in a dungeon (insideDungeonUser, 5): the member's field is a dungeon room instance, so it has a
    // dungeon id. The map alone does not tell: rooms also use open field maps (02000xxx) as their fields.
    public static bool InsideDungeon(IEnumerable<int> memberDungeonIds) {
        return memberDungeonIds.Any(id => id != 0);
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
