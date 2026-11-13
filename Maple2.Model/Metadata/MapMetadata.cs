using Maple2.Model.Common;
using Maple2.Model.Enum;

namespace Maple2.Model.Metadata;

public record UgcMapGroup(
    int Number,
    int ApartmentNumber,
    int Type,
    UgcMapGroup.Cost ContractCost,
    UgcMapGroup.Cost ExtensionCost,
    UgcMapGroup.Limits Limit) {

    public record Cost(
        int Amount,
        int ItemId,
        int Days);

    public record Limits(
        int Height,
        int Area,
        int Maid,
        int Trigger,
        int InstallNpc,
        int InstallBuilding);
}

#region map
public record MapMetadata(
    int Id,
    string? Name,
    string XBlock,
    MapMetadataProperty Property,
    MapMetadataLimit Limit,
    MapMetadataDrop Drop,
    IReadOnlyList<MapMetadataSpawn> Spawns,
    MapMetadataCashCall CashCall,
    MapEntranceBuff[] EntranceBuffs) : ISearchResult;

public record MapMetadataSurvival(
    int UserDrop,
    int UserAdditionalDrop,
    int PlusInven,
    int PlusInvenBasic,
    int PlusInvenZero);

public record MapMetadataProperty(
    Continent Continent,
    MapRegion Region,
    int Category,
    MapType Type,
    bool BigCity,
    int ExploreType,
    int TutorialType,
    int RevivalReturnId,
    int EnterReturnId,
    AutoReviveType AutoRevivalType,
    int AutoRevivalTime,
    bool InfiniteMeretRevival,
    bool NoRevivalHere,
    bool ReviveFullHp,
    bool UseTimeEvent,
    bool HomeReturnable,
    bool DeathPenalty,
    bool OnlyDarkTomb,
    bool PkMode,
    bool CanFly,
    bool CanClimb,
    float IndoorType,
    // Field property "minimapPing": the client sends and draws minimap pings only where it is set
    // (UIMinimapDialog::IsPingAllowed); false when absent, as in the client. The JSON store omits false values,
    // so the default must be false. Data ingested before this field reads as false until re-ingested.
    bool MinimapPing = false,
    // Set at ingest when MS2_PLAYED_CLIENT_XML names the pack of the client that is played and that pack's
    // field table has only an empty row for this map (no xblock name) or no row at all: that client cannot load the map and
    // hangs when it is sent there. The server then refuses every way in. False when the option is not set;
    // the JSON store omits false values, so the default must be false.
    bool NotInPlayedClient = false) {
    // The map xml's survival element (82000000-82000003, 82000006, 82000012). Null where the map has none, and in a
    // row ingested before this field existed. The client reads none of it: it is server data (SurvivalItemRules).
    public MapMetadataSurvival? Survival { get; init; }
}

public record MapMetadataDrop(
    int Level,
    int DropRank,
    int[] GlobalDropBoxId);

public record MapMetadataLimit(
    int Capacity,
    short MinLevel,
    short MaxLevel,
    int RequireQuest,
    int[] DisableSkills,
    bool Climb,
    bool Fly,
    bool Move,
    bool FallDamage,
    bool Dash,
    bool Ride,
    bool Pet,
    int EnterAdminLevel = 0); // enterAdminLevel: above 0, the client lets only a debug-mode (GM) client pick the field

public record MapMetadataSpawn(
    int Id,
    int MinDifficulty,
    int MaxDifficulty,
    int Population,
    int Cooldown,
    string[] Tags,
    int PetPopulation,
    int PetSpawnRate,
    IReadOnlyDictionary<int, int> PetIds); // NpcId => PetId

public record MapMetadataCashCall(
    bool TaxiDeparture,
    bool TaxiDestination,
    bool Medic,
    bool Market,
    bool Recall);

public record MapEntranceBuff(int Id, short Level);
#endregion

#region ugcmap
public record UgcMapMetadata(
    int Id,
    IReadOnlyDictionary<int, UgcMapGroup> Plots);

public record ExportedUgcMapMetadata(
    string Id,
    Vector3B BaseCubePosition,
    byte[] IndoorSize,
    List<ExportedUgcMapMetadata.Cube> Cubes) {

    public record Cube(
        int ItemId,
        Vector3B OffsetPosition,
        int Rotation,
        byte WallDirection
    );
}
#endregion
