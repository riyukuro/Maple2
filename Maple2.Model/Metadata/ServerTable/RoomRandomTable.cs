namespace Maple2.Model.Metadata;

// table/Server/room_random.xml: the random rooms an AI createRandomRoom node opens a portal to.
public record RoomRandomTable(
    IReadOnlyDictionary<int, RandomRoomMetadata> RandomRooms,
    IReadOnlyDictionary<int, RandomRoomFieldMetadata> Rooms) : ServerTable;

// <randomroom id prob>: prob out of 10000 (id 1 has 10000), then a room by weight.
public record RandomRoomMetadata(
    int Id,
    int Prob,
    RandomRoomMetadata.Ref[] Refs) {
    public record Ref(int RoomId, int Weight);
}

// <room id fieldIDs roomdurationTick nifAssetName maxUserCount autoClosing>.
public record RandomRoomFieldMetadata(
    int Id,
    int[] FieldIds,
    int DurationTick,
    string AssetName,
    int MaxUserCount,
    bool AutoClosing);
