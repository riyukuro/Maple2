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
    // the default return field when the saved one is unknown or is a GM-only field for a plain account
    // (an account that lost its GM rights while its return field was one of those).
    public static int ReturnMap(int returnMapId, bool known, int enterAdminLevel, bool gm, int defaultMapId) {
        if (!known || !AdminFieldAllowed(enterAdminLevel, gm)) {
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

    // A dungeon arrival whose room is gone when the character gets there is entered as a plain arrival in the
    // return field: the client is already on its loading screen and an error there locks it out.
    public static bool ArrivesInReturnField(MigrationType type, bool roomRuns) {
        return type == MigrationType.Dungeon && !roomRuns;
    }

    // The name for log lines of a session; empty before the character is loaded (an arrival with a bad token).
    // A map entered with an owner id that has one room per owner instead of one room for everyone: the
    // map of a guild event, which each guild plays in its own room, and a guild house map, which is the
    // house of one guild. Homes have their own path and a given room id names the room itself.
    public static bool HasOwnerRoom(InstanceType type, long ownerId, int roomId) {
        return ownerId != 0 && roomId == 0 && type is InstanceType.GuildEvent or InstanceType.GuildHouse;
    }

    public static string LogName(string? characterName) {
        return characterName ?? string.Empty;
    }

    // A field with enterAdminLevel above 0 can be entered, or picked as a taxi destination, by a GM account only.
    public static bool AdminFieldAllowed(int enterAdminLevel, bool gm) {
        return enterAdminLevel <= 0 || gm;
    }
}
