using Maple2.Model.Error;
using Maple2.Model.Metadata;

namespace Maple2.Model.Game.Dungeon;

// The hall buttons of the dungeon search window (UIDungeonSearchDialog::OnLoadComplete,
// uidungeonsearchdialog.cpp:565-570): RoomDungeon 4 (chaos raid), 12 (reverse raid), 13 (colosseum) and 14 (lapenta),
// each a bare mode byte. The client has no hall field id of its own; the server constants name them
// (ChaosDungeonHallFieldID and the others, channelScale fields of the instance field table).
public static class DungeonHallRules {
    public const byte ChaosRaid = 4;
    public const byte ReverseRaid = 12;
    public const byte Colosseum = 13;
    public const byte Lapenta = 14;

    // The hall field of a lobby mode; 0 when the mode has none or the constant is not set.
    public static int HallFieldId(byte mode, ConstantsTable constants) => mode switch {
        ChaosRaid => constants.ChaosDungeonHallFieldID,
        ReverseRaid => constants.ReverseRaidDungeonHallFieldID,
        Colosseum => constants.ColosseumDungeonHallFieldID,
        Lapenta => constants.LapentaDungeonHallFieldID,
        _ => 0,
    };

    // Where the hall's exit leads. The halls are channelScale fields without saveField, so entering one pushes no
    // return map (FieldEntryRules.SavesReturnMap) and the top of the return maps stays the open field's own return map.
    // The exits of 63000047, 63000063 and 83000001 have no target field (TargetFieldSN 0), so they take the player
    // back to that return map (GameSession.ReturnField); 63000054's lead to 2000207 and 2000409. A relog inside the hall
    // lands on the saved map, which an instanced move leaves as it was. Chosen here, as the PvP match move does: when
    // the button is pressed on the return map itself, that spot is the return position, so the exit leads back to it
    // and not to the map's spawn point. Pressed elsewhere (another hall, a field whose return map is a town), the
    // position is not kept: it belongs to another map.
    public static bool KeepsReturnPosition(int fieldMapId, int returnMapId) => fieldMapId != 0 && fieldMapId == returnMapId;

    // Why the move is refused (DungeonRecordManagerCli::OnError, dungeonrecordcli.cpp:374-382), none when it is made.
    // Inside a dungeon room (a field with a dungeon id, DungeonMatchRules.InsideDungeon): CantEnterInDungeon (16).
    // Already on the hall's map: AlreadyChaosHall (13), AlreadyLapentaHall (14), AlreadyColosseumHall (15). The client
    // has no such code for the reverse raid hall; Chosen here: it gets 13, whose text names no hall.
    public static DungeonRoomError? Error(byte mode, int hallFieldId, int mapId, int dungeonId) {
        if (DungeonMatchRules.InsideDungeon([dungeonId])) {
            return DungeonRoomError.s_room_dungeon_CantEnterInDungeon;
        }
        if (mapId != hallFieldId) {
            return null;
        }
        return mode switch {
            Lapenta => DungeonRoomError.s_room_dungeon_AlreadyLapentaHall,
            Colosseum => DungeonRoomError.s_room_dungeon_AlreadyColosseumHall,
            _ => DungeonRoomError.s_room_dungeon_AlreadyChaosHall,
        };
    }
}
