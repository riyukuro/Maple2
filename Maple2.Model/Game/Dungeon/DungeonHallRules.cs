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
