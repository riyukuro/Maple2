using Maple2.File.IO;
using Maple2.File.Parser;
using Maple2.File.Parser.Xml.Map;
using Maple2.File.Parser.Tools;
using Maple2.File.Parser.Xml.Table;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.File.Ingest.Mapper;

public class MapMapper : TypeMapper<MapMetadata> {
    private readonly TableParser spawnParser;
    private readonly MapParser parser;
    // The played client's own field table, spawn tags and names (MS2_PLAYED_CLIENT_XML); null without one.
    private readonly MapParser? playedParser;
    private readonly TableParser? playedSpawnParser;
    // The environment the played client keeps of each of its field table rows; empty without one.
    private readonly Dictionary<int, System.Xml.XmlElement> playedEnvironments = [];
    private static readonly System.Xml.Serialization.XmlSerializer MapDataSerializer =
        new(typeof(MapData), new System.Xml.Serialization.XmlRootAttribute("environment") { Namespace = "" });
    private int marked, packLists, lists, changedLists, survivalMaps, noEp, packNoEp;

    // Maps the played client cannot load (MS2_PLAYED_CLIENT_XML); empty when the option is not set.
    private readonly HashSet<int> notInPlayedClient = [];
    // All map ids the played client's field table has a row for; a map without a row is lacking too.
    private readonly HashSet<int> playedClientRows = [];
    // The played client's skillUseDisable list of each row (PlayedClientMaps.SkillUseDisable); empty without one.
    private readonly Dictionary<int, int[]> playedDisableSkills = [];
    // The played client's useEPSkill of each row (PlayedClientMaps.UseEpSkill); empty without one.
    private readonly Dictionary<int, bool> playedUseEp = [];

    // Of two available environments of a field row, the one the client keeps (CFeatureManager::SelectFeatureElement).
    private static System.Xml.XmlElement SelectEnvironment(System.Xml.XmlElement a, System.Xml.XmlElement b) =>
        Utils.PlayedNpcStats.SelectFeatureElement(a, b, feature => M2dXmlGenerator.FeatureLocaleFilter.Features.GetValueOrDefault(feature), M2dXmlGenerator.FeatureLocaleFilter.Locale);

    public MapMapper(M2dReader xmlReader, string language, M2dReader? playedReader = null) {
        spawnParser = PackTables.NewParser(xmlReader, language);
        parser = new MapParser(xmlReader, language);

        Maple2.File.IO.Crypto.Common.PackFileEntry? entry = playedReader?.Files.FirstOrDefault(file => file.Name == PlayedClientMaps.FieldTable);
        if (playedReader != null && entry != null) {
            System.Xml.XmlDocument fieldTable = playedReader.GetXmlDocument(entry);
            notInPlayedClient = PlayedClientMaps.Missing(fieldTable);
            playedClientRows = PlayedClientMaps.Rows(fieldTable);
            playedDisableSkills = PlayedClientMaps.SkillUseDisable(fieldTable, Helpers.ClientListedRows.Available, SelectEnvironment);
            playedUseEp = PlayedClientMaps.UseEpSkill(fieldTable, Helpers.ClientListedRows.Available, SelectEnvironment);
            playedEnvironments = PlayedClientMaps.Environments(fieldTable, Helpers.ClientListedRows.Available, SelectEnvironment);
            playedParser = new MapParser(playedReader, language);
        }
        if (playedReader != null && playedReader.Files.Any(file => file.Name == PlayedSpawnTable)) {
            playedSpawnParser = PackTables.NewParser(playedReader, language);
        }
    }

    private const string PlayedSpawnTable = "table/mapspawntag.xml";

    protected override IEnumerable<MapMetadata> Map() {
        // The spawn tags and pet spawns of the played client's tables when its pack has them (it adds spawn points
        // the data folder's pack lacks, e.g. 2000018, and drops others); else the data folder's.
        TableParser spawnParser = playedSpawnParser ?? this.spawnParser;
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

        HashSet<int> packIds = [];
        foreach ((int id, string name, MapData data) in parser.Parse()) {
            packIds.Add(id);
            yield return Build(id, name, data, spawns);
        }
        // The maps only the played client has (e.g. 2000526 to 2000529, 2200001 and 2200002): the data folder's pack (a
        // development pack) lacks them. They are read from the played client's field table (table/fielddata.xml);
        // what only the server's map files hold (useTimeEvent, the drop box, the survival element) keeps the parser's
        // default. Their xblocks are in the data folder's Exported.m2d. Of a row's environments the one the client
        // keeps is read (CFeatureManager::SelectFeatureElement), not the parser's last one.
        int playedOnly = 0;
        if (playedParser != null) {
            Dictionary<int, string> names = playedParser.ParseMapNames();
            foreach ((int id, System.Xml.XmlElement environment) in playedEnvironments.OrderBy(entry => entry.Key)) {
                if (!packIds.Add(id)) {
                    continue;
                }
                string xml = Sanitizer.SanitizeBool(Sanitizer.RemoveEmpty(environment.OuterXml));
                using var reader = System.Xml.XmlReader.Create(new StringReader(xml));
                var data = (MapData) MapDataSerializer.Deserialize(reader)!;
                playedOnly++;
                yield return Build(id, names.GetValueOrDefault(id, string.Empty), data, spawns);
            }
        }
        Console.WriteLine($"maps with a survival element: {survivalMaps}");
        Console.WriteLine($"maps whose EP skills are not cast (useEPSkill 0): {noEp} (data pack: {packNoEp})");
        if (playedClientRows.Count > 0) {
            Console.WriteLine($"maps: {marked} marked as not in the played client (entry refused)");
            Console.WriteLine($"maps: {playedOnly} only in the played client's field table, read from it");
            Console.WriteLine($"maps with a skillUseDisable list: {lists} ({packLists} in the data pack, {changedLists} taken from the played client)");
        }
        if (playedSpawnParser != null) {
            Console.WriteLine($"map spawn tags from the played client's table: {spawns.Values.Sum(list => list.Count)} in {spawns.Count} maps");
        }
    }

    private MapMetadata Build(int id, string name, MapData data, Dictionary<int, IReadOnlyList<MapMetadataSpawn>> spawns) {
        bool lacking = PlayedClientMaps.Lacks(playedClientRows, notInPlayedClient, id);
        if (lacking) {
            marked++;
        }
        // The client's list where it has the field (none where its row lists none or it has no row); the data pack's
        // differs (84000006 to 84000046 add 5, 8 and 9, 61000023 to 61000034 add 4) and the server refused casts the
        // client sends. Without a played client the pack's list is kept.
        int[] packDisable = data.property.skillUseDisable ?? [];
        int[] disableSkills = playedDisableSkills.Count > 0 ? playedDisableSkills.GetValueOrDefault(id, []) : packDisable;
        packLists += packDisable.Length > 0 ? 1 : 0;
        lists += disableSkills.Length > 0 ? 1 : 0;
        changedLists += !disableSkills.SequenceEqual(packDisable) ? 1 : 0;
        // The client's useEPSkill where it has the field (1 where its row has none); the data pack's differs (0 in
        // 00000001 to 00000027, 00001001, 00009051 to 00009073, 51000002, 99999912 and others the client allows).
        // Without a played client the pack's flag is kept.
        bool useEp = playedUseEp.Count > 0 ? playedUseEp.GetValueOrDefault(id, true) : data.ui.useEPSkill;
        noEp += useEp ? 0 : 1;
        packNoEp += data.ui.useEPSkill ? 0 : 1;
        var metadata = new MapMetadata(
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
                NotInPlayedClient: lacking) {
                // The parser gives every map a survival object; only the 6 maps whose xml has the element
                // carry values, so an all-zero object is a map without one.
                Survival = !HasSurvival(data.survival) ? null : new MapMetadataSurvival(
                    UserDrop: data.survival.userDrop,
                    UserAdditionalDrop: data.survival.userAdditionalDrop,
                    PlusInven: data.survival.plusInven,
                    PlusInvenBasic: data.survival.plusInven_Basic,
                    PlusInvenZero: data.survival.plusInven_Zero),
            },
            Limit: new MapMetadataLimit(
                Capacity: data.property.capacity,
                MinLevel: data.property.enterMinLevel,
                MaxLevel: data.property.enterMaxLevel,
                RequireQuest: data.property.requireQuest,
                DisableSkills: disableSkills,
                Climb: data.property.checkClimb,
                Fly: data.property.checkFly,
                Move: data.property.limitMove,
                FallDamage: data.ui.fallDamage,
                Dash: useEp,
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
        survivalMaps += HasSurvival(data.survival) ? 1 : 0;
        return metadata;
    }

    public static bool HasSurvival(Maple2.File.Parser.Xml.Map.Survival? survival) {
        return survival != null && (survival.userDrop != 0 || survival.userAdditionalDrop != 0 || survival.plusInven != 0
            || survival.plusInven_Basic != 0 || survival.plusInven_Zero != 0);
    }
}
