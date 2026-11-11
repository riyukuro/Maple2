namespace Maple2.Model.Metadata;

// dungeonrounddata.xml: each DungeonRoundGroup (a room's roundID) lists its rounds in order; round N is entry N-1.
public record DungeonRoundDataTable(IReadOnlyDictionary<int, DungeonRoundMetadata[]> Entries) : Table;

public record DungeonRoundMetadata(int RewardId, int GearScore);
