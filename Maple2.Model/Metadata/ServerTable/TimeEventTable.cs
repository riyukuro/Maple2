namespace Maple2.Model.Metadata;

public record TimeEventTable(
    IReadOnlyDictionary<int, GlobalPortalMetadata> GlobalPortal,
    IReadOnlyDictionary<int, WorldBossMetadata> WorldBoss,
    // The field war events (timeEventData type 7) and the wars they pick from (the client's table/fieldwardata.xml),
    // both after the feature filter. Null in a table ingested before them.
    IReadOnlyDictionary<int, FieldWarEventMetadata>? FieldWarEvent = null,
    IReadOnlyDictionary<int, FieldWarMetadata>? FieldWar = null) : ServerTable;

// A field war event: at each run one war of its group opens for LifeTime.
public record FieldWarEventMetadata(
    int Id,
    int Probability,
    DateTime StartTime,
    DateTime EndTime,
    TimeSpan CycleTime,
    TimeSpan RandomTime,
    TimeSpan LifeTime,
    int GroupId);

// A row of table/fieldwardata.xml: the war's instance map, its reward and the map the client names it by (its
// announce shows the name and the image of UiMapId).
public record FieldWarMetadata(
    int Id,
    int GroupId,
    int PickWeight,
    int MapId,
    int RewardId,
    int UiMapId);

public record WorldBossMetadata(
    int Id,
    int Probability,
    DateTime StartTime,
    DateTime EndTime,
    TimeSpan CycleTime,
    TimeSpan RandomTime,
    TimeSpan LifeTime,
    int[] TargetMapIds,
    int[] SpawnPointIds,
    int[] NpcIds,
    int Tag,
    bool Unique,
    bool IndividualChannelSpawn,
    float VariableCountByChannel,
    bool ScreenNotice,
    bool ChatNotice);

public record GlobalPortalMetadata(
    int Id,
    int Probability,
    DateTime StartTime,
    DateTime EndTime,
    TimeSpan CycleTime,
    TimeSpan RandomTime,
    TimeSpan LifeTime,
    string PopupMessage,
    string SoundId,
    GlobalPortalMetadata.Field[] Entries) {
    public record Field(
        string Name,
        int MapId,
        int PortalId);
}
