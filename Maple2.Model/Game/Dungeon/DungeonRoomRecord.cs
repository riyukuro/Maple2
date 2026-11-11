using System.Collections.Concurrent;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Model.Game.Dungeon;

public class DungeonRoomRecord {
    public ConcurrentDictionary<long, DungeonUserRecord> UserResults = [];
    public readonly DungeonRoomMetadata Metadata;
    public long StartTick { get; set; }
    public long EndTick { get; set; }
    public DungeonState State { get; set; } = DungeonState.None;
    // The trigger dungeon variables of this instance (set_dungeon_variable, dungeon_variable). One record is shared
    // by the lobby and every room field of the instance; an id never set reads 0.
    public readonly ConcurrentDictionary<int, int> Variables = new();

    public DungeonRoomRecord(DungeonRoomMetadata metadata) {
        Metadata = metadata;
    }
}
