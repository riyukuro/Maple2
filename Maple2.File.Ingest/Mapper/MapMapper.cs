using Maple2.File.IO;
using Maple2.File.Parser;
using Maple2.File.Parser.Xml.Map;
using Maple2.File.Parser.Xml.Table;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.File.Ingest.Mapper;

public class MapMapper : TypeMapper<MapMetadata> {
    private readonly TableParser spawnParser;
    private readonly MapParser parser;

    // Maps the played client cannot load (MS2_PLAYED_CLIENT_XML); empty when the option is not set.
    private readonly HashSet<int> notInPlayedClient = [];
    // All map ids the played client's field table has a row for; a map without a row is lacking too.
    private readonly HashSet<int> playedClientRows = [];

    public MapMapper(M2dReader xmlReader, string language, M2dReader? playedReader = null) {
        spawnParser = PackTables.NewParser(xmlReader, language);
        parser = new MapParser(xmlReader, language);

        Maple2.File.IO.Crypto.Common.PackFileEntry? entry = playedReader?.Files.FirstOrDefault(file => file.Name == PlayedClientMaps.FieldTable);
        if (playedReader != null && entry != null) {
            System.Xml.XmlDocument fieldTable = playedReader.GetXmlDocument(entry);
            notInPlayedClient = PlayedClientMaps.Missing(fieldTable);
            playedClientRows = PlayedClientMaps.Rows(fieldTable);
        }
    }

    protected override IEnumerable<MapMetadata> Map() {
        var pets = new Dictionary<int, Dictionary<int, int>>();
        foreach ((int mapId, IEnumerable<PetSpawnInfo> infos) in spawnParser.ParsePetSpawnInfo()) {
            pets[mapId] = infos.ToDictionary(info => info.npcID, info => info.petID);
        }

        var spawns = new Dictionary<int, IReadOnlyList<MapMetadataSpawn>>();
        foreach ((int mapId, IEnumerable<MapSpawnTag.Region> regions) in spawnParser.ParseMapSpawnTag()) {
            spawns[mapId] = regions.Select(region => new MapMetadataSpawn(
                Id: region.spawnPointID,
                MinDifficulty: region.difficultyMin,
                MaxDifficulty: region.difficulty,
                Population: region.population,
                Cooldown: region.coolTime,
                Tags: region.tag,
                PetPopulation: region.petPopulation,
                PetSpawnRate: region.petSpawnProbability,
                PetIds: pets.GetValueOrDefault(mapId, new Dictionary<int, int>()))
            ).ToList();
        }

        int marked = 0;
        foreach ((int id, string name, MapData data) in parser.Parse()) {
            bool lacking = PlayedClientMaps.Lacks(playedClientRows, notInPlayedClient, id);
            if (lacking) {
                marked++;
            }
            yield return new MapMetadata(
                Id: id,
                Name: name,
                XBlock: data.xblock.name.ToLower(),
                Property: new MapMetadataProperty(
                    Continent: (Continent) data.property.continentCode,
                    Region: (MapRegion) data.property.regionCode,
                    Category: data.property.mapCategoryCode,
                    Type: (MapType) data.property.mapType,
                    BigCity: data.property.bigCity,
                    ExploreType: data.property.exploreType,
                    TutorialType: data.property.tutorialType,
                    RevivalReturnId: data.property.revivalreturnid,
                    EnterReturnId: data.property.enterreturnid,
                    AutoRevivalType: (AutoReviveType) data.property.autoRevivalType,
                    AutoRevivalTime: data.property.autoRevivalTime,
                    InfiniteMeretRevival: data.property.infinityMeratRevival,
                    NoRevivalHere: data.property.doNotRevivalHere,
                    ReviveFullHp: data.property.recoveryFullHP,
                    UseTimeEvent: data.property.useTimeEvent,
                    HomeReturnable: data.property.homeReturnable,
                    DeathPenalty: data.property.deathPenalty,
                    OnlyDarkTomb: data.property.onlyDarkTomb,
                    PkMode: data.property.pkMode,
                    CanFly: data.property.checkFly,
                    CanClimb: data.property.checkClimb,
                    IndoorType: data.indoor.type,
                    MinimapPing: data.property.minimapPing,
                    NotInPlayedClient: lacking),
                Limit: new MapMetadataLimit(
                    Capacity: data.property.capacity,
                    MinLevel: data.property.enterMinLevel,
                    MaxLevel: data.property.enterMaxLevel,
                    RequireQuest: data.property.requireQuest,
                    DisableSkills: data.property.skillUseDisable,
                    Climb: data.property.checkClimb,
                    Fly: data.property.checkFly,
                    Move: data.property.limitMove,
                    FallDamage: data.ui.fallDamage,
                    Dash: data.ui.useEPSkill,
                    Ride: data.ui.useRidee,
                    Pet: data.ui.usePet,
                    EnterAdminLevel: data.property.enterAdminLevel),
                Drop: new MapMetadataDrop(
                    Level: data.drop.maplevel,
                    DropRank: data.drop.droprank,
                    GlobalDropBoxId: data.drop.globalDropBoxID),
                Spawns: spawns.GetValueOrDefault(id, new List<MapMetadataSpawn>()),
                CashCall: new MapMetadataCashCall(
                    TaxiDeparture: !data.cashCall.cashTaxiNotDeparture,
                    TaxiDestination: !data.cashCall.cashTaxiNotDestination,
                    Medic: !data.cashCall.cashCallMedicProhibit,
                    Market: !data.cashCall.cashCallMarketProhibit,
                    Recall: !data.cashCall.RecallOtherUserProhibit),
                // TODO: There are also EntranceBuffs for Survival
                EntranceBuffs: data.property.enteranceBuffIDs.Zip(data.property.enteranceBuffLevels,
                        (skillId, level) => new MapEntranceBuff(skillId, level)).ToArray());
        }
        if (playedClientRows.Count > 0) {
            Console.WriteLine($"maps: {marked} marked as not in the played client (entry refused)");
        }
    }
}
