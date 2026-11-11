using Maple2.Model.Enum;
using Maple2.Model.Error;

namespace Maple2.Server.Game.Util;

public static class FieldEntryRules {
    // Fields to try, in order, when the field a character logs into cannot be made: the return field, then
    // the default return field. The failed field and repeats are left out.
    public static IEnumerable<int> Fallbacks(int failedMapId, int returnMapId, int defaultMapId) {
        if (returnMapId != 0 && returnMapId != failedMapId) {
            yield return returnMapId;
        }
        if (defaultMapId != failedMapId && defaultMapId != returnMapId) {
            yield return defaultMapId;
        }
    }

    // The field a character goes back to (leaving a dungeon, a home, a mini-game): its saved return field, or
    // the default return field when the saved one is unknown, is a GM-only field for a plain account
    // (an account that lost its GM rights while its return field was one of those), or is a map the played
    // client does not have.
    public static int ReturnMap(int returnMapId, bool known, int enterAdminLevel, bool gm, int defaultMapId, bool notInPlayedClient = false) {
        if (!known || notInPlayedClient || !AdminFieldAllowed(enterAdminLevel, gm)) {
            return defaultMapId;
        }
        return returnMapId;
    }

    // The answer to "Enter Dungeon" for a room that is gone (closed after everyone left it): error 0x1D, which
    // the client shows as its "dungeon does not exist" box (GetFieldMoveErrorMessage) from OnGameToGame without
    // leaving the field. A running room is ok.
    public static MigrationError DungeonRoomError(bool roomRuns) {
        return roomRuns ? MigrationError.ok : MigrationError.s_move_err_dungeon_not_exist_2;
    }

    // "Enter Dungeon" failed for a room that is gone. The party's dungeon is cleared only when the party still
    // names that room: a member whose session holds an older room id must not wipe a dungeon the leader made since.
    public static bool ClearsPartyDungeon(int failedRoomId, int partyRoomId) {
        return failedRoomId != 0 && failedRoomId == partyRoomId;
    }

    // Whether entering a field saves its enterReturnId as the return field: an open field (instance type none) or
    // a saveField instance does, a dungeon lobby or room never does. A room may sit on an open map (Zakum's room
    // 23040003 is field 02000426): its field carries the map's own enterReturnId, and saving that made leaving the
    // room (or Exit on its result box) land in that map as an open field instead of the field the player came from.
    public static bool SavesReturnMap(InstanceType type, bool saveField, bool dungeonField, int enterReturnId) {
        return !dungeonField && enterReturnId != 0 && (type == InstanceType.none || saveField);
    }

    // A running dungeon room on this channel: its lobby room id, whether the character has a record in it (it entered
    // the room's lobby), the lobby's map and the room's fields by map.
    public readonly record struct RunningRoom<T>(int RoomId, bool Member, int LobbyMapId, T Lobby, IReadOnlyDictionary<int, T> Fields);

    // The field a character logging in on its saved map goes back into: the lobby or room field of that map in a
    // running room the character entered, the newest room first. null when none: the map is entered as before.
    // Without this a relog in a room (the client closed in Zakum's field 02000426) made a new open field of the map,
    // with no room clock (RoomTimer), no RoomStageDungeon and no mission list.
    public static T? RejoinedField<T>(IEnumerable<RunningRoom<T>> rooms, int mapId) where T : class {
        foreach (RunningRoom<T> room in rooms.Where(room => room.Member).OrderByDescending(room => room.RoomId)) {
            if (room.LobbyMapId == mapId) {
                return room.Lobby;
            }
            if (room.Fields.TryGetValue(mapId, out T? field)) {
                return field;
            }
        }
        return null;
    }

    // A dungeon arrival whose room is gone when the character gets there is entered as a plain arrival in the
    // return field: the client is already on its loading screen and an error there locks it out.
    public static bool ArrivesInReturnField(MigrationType type, bool roomRuns) {
        return type == MigrationType.Dungeon && !roomRuns;
    }

    // The field a character arrives in when the one it was saved on (or sent to from another channel) is a
    // map the played client does not have: the first of the return field and the default return field that
    // the client has. The client is on its loading screen and hangs on a map it cannot load, so it must not
    // be sent there. 0 when neither can be used; the arrival then fails.
    public static int ArrivalMap(int mapId, bool notInPlayedClient, int returnMapId, int defaultMapId, Func<int, bool> usable) {
        if (!notInPlayedClient) {
            return mapId;
        }
        foreach (int fallbackId in Fallbacks(mapId, returnMapId, defaultMapId)) {
            if (usable(fallbackId)) {
                return fallbackId;
            }
        }
        return 0;
    }

    // The chat line for a refused entry to a map the played client does not have.
    public static string NotInPlayedClientNotice(int mapId, string? name) {
        return string.IsNullOrEmpty(name)
            ? $"Map {mapId} is not in this client version. Entry refused."
            : $"Map {name} ({mapId}) is not in this client version. Entry refused.";
    }

    // The exp a field pays on entry: only on the first visit, and only when the map has an explore type
    // (1 pays the common map exp, any other positive type the hidden map exp). Null when it pays none.
    public static ExpType? ExploreExp(bool firstVisit, int exploreType) {
        if (!firstVisit || exploreType <= 0) {
            return null;
        }
        return exploreType == 1 ? ExpType.mapCommon : ExpType.mapHidden;
    }

    // The name for log lines of a session; empty before the character is loaded (an arrival with a bad token).
    // A map entered with an owner id that has one room per owner instead of one room for everyone: the
    // map of a guild event, which each guild plays in its own room, and a guild house map, which is the
    // house of one guild, and a wedding hall map, where the owner is the marriage whose ceremony it holds. Homes
    // have their own path and a given room id names the room itself.
    public static bool HasOwnerRoom(InstanceType type, long ownerId, int roomId) {
        return ownerId != 0 && roomId == 0 && type is InstanceType.GuildEvent or InstanceType.GuildHouse or InstanceType.WeddingHall;
    }

    public static string LogName(string? characterName) {
        return characterName ?? string.Empty;
    }

    // A field with enterAdminLevel above 0 can be entered, or picked as a taxi destination, by a GM account only.
    public static bool AdminFieldAllowed(int enterAdminLevel, bool gm) {
        return enterAdminLevel <= 0 || gm;
    }
}
