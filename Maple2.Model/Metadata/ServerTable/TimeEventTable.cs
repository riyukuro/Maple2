namespace Maple2.Model.Metadata;

public record TimeEventTable(
    IReadOnlyDictionary<int, GlobalPortalMetadata> GlobalPortal,
    IReadOnlyDictionary<int, WorldBossMetadata> WorldBoss,
    // The field war events (timeEventData type 7) and the wars they pick from (the client's table/fieldwardata.xml),
    // both after the feature filter. Null in a table ingested before them.
    IReadOnlyDictionary<int, FieldWarEventMetadata>? FieldWarEvent = null,
    IReadOnlyDictionary<int, FieldWarMetadata>? FieldWar = null,
    // The field interact objects (timeEventData type 4) of the rows the feature and locale keep. Null in a table
    // ingested before them.
    IReadOnlyDictionary<int, FieldInteractEventMetadata>? FieldInteractEvent = null,
    // The field trigger events (timeEventData type 6) of the rows the feature and locale keep. Null in a table
    // ingested before them.
    IReadOnlyDictionary<int, FieldTriggerEventMetadata>? FieldTriggerEvent = null) : ServerTable;

// A timeEventData type 6 row (the parser's KritiasChestPuzzle): every CycleTime (late by up to RandomTime) the
// fields of MapIds give their trigger TriggerId the user value Key = Value, and the run's LifeTime in seconds as
// its user value TimeEventLifeTime (the scripts wait on it, then close the event).
public record FieldTriggerEventMetadata(
    int Id,
    int Probability,
    DateTime StartTime,
    DateTime EndTime,
    TimeSpan CycleTime,
    TimeSpan RandomTime,
    TimeSpan LifeTime,
    int[] MapIds,
    int TriggerId,
    string Key,
    int Value);

// A timeEventData type 4 row: interact objects a field puts on some of its region spawn points, a new set every
// CycleTime (late by up to RandomTime), each for LifeTime. The points are SpawnPointIds and the points of the map's
// mapspawntag regions with one of SpawnTags; InteractCount of them hold an object, or VariableCountByChannel of
// them when it is 0.
public record FieldInteractEventMetadata(
    int Id,
    int Probability,
    DateTime StartTime,
    DateTime EndTime,
    TimeSpan CycleTime,
    TimeSpan RandomTime,
    TimeSpan LifeTime,
    int[] MapIds,
    int[] SpawnPointIds,
    string[] SpawnTags,
    int InteractCount,
    float VariableCountByChannel,
    int InteractId,
    string Model,
    string Asset,
    string Normal,
    string Reactable,
    // The hours of the day the row runs in (eventDurationStartHour:StartMin to EndHour:EndMin, both 0: all day).
    int DurationStartHour,
    int DurationStartMin,
    int DurationEndHour,
    int DurationEndMin);

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
