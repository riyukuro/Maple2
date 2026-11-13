using System.Diagnostics;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Xml;
using M2dXmlGenerator;
using Maple2.File.IO;
using Maple2.File.IO.Crypto.Common;
using Maple2.File.Ingest.Utils;
using Maple2.File.Parser;
using Maple2.File.Parser.Xml.Common;
using Maple2.File.Parser.Xml.Npc;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;

namespace Maple2.File.Ingest.Mapper;

public class NpcMapper : TypeMapper<NpcMetadata> {
    private readonly NpcParser parser;
    private readonly M2dReader xmlReader;
    private readonly M2dReader? playedReader;

    public static Dictionary<int, NpcMetadata> NpcMetadataById { get; } = new();

    public NpcMapper(M2dReader xmlReader, string language, M2dReader? playedReader = null) {
        parser = new NpcParser(xmlReader, language);
        this.xmlReader = xmlReader;
        this.playedReader = playedReader;
    }

    // The file parser does not read aiInfo's summonNpcIDs, so it is read from the npc xml here. In the data the
    // list is the same in every environment of a file (3 of 531 files differ only in the AI path).
    private Dictionary<int, int[]> SummonNpcIds() {
        var result = new Dictionary<int, int[]>();
        foreach (PackFileEntry file in xmlReader.Files) {
            if (!file.Name.StartsWith("npc/", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if (!int.TryParse(Path.GetFileNameWithoutExtension(file.Name), out int id) || result.ContainsKey(id)) {
                continue;
            }
            int[]? ids = ParseSummonNpcIds(xmlReader.GetString(file));
            if (ids != null) {
                result[id] = ids;
            }
        }
        return result;
    }

    public static int[]? ParseSummonNpcIds(string xml) {
        Match match = Regex.Match(xml, "<aiInfo\\b[^>]*\\bsummonNpcIDs=\"([^\"]*)\"");
        if (!match.Success) {
            return null;
        }
        int[] ids = match.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(value => int.TryParse(value, out int npcId) ? npcId : 0)
            .ToArray();
        return ids.Length == 0 ? null : ids;
    }

    protected override IEnumerable<NpcMetadata> Map() {
        Dictionary<int, int[]> summonNpcIds = SummonNpcIds();
        // The NPCs the played client has NPC data for (PlayedClientNpcs); null without a played client or when its
        // pack has no NPC data, and then no NPC is marked.
        HashSet<int>? known = null;
        // The played client's stat block of each of its NPCs (PlayedNpcStats); empty without a played client.
        Dictionary<int, Stat> playedStats = new();
        if (playedReader != null) {
            List<XmlDocument> npcFiles = playedReader.Files.Where(file => PlayedClientNpcs.IsNpcFile(file.Name))
                .Select(file => playedReader.GetXmlDocument(file)).ToList();
            known = PlayedClientNpcs.Ids(npcFiles, EnvironmentAvailable);
            if (known.Count == 0) {
                known = null;
            }
            playedStats = PlayedNpcStats.Read(npcFiles, EnvironmentAvailable, feature => FeatureLocaleFilter.Features.GetValueOrDefault(feature), FeatureLocaleFilter.Locale);
        }
        int notInPlayed = 0, inPlayed = 0;
        foreach ((int id, string name, NpcData data, List<EffectDummy> _) in parser.Parse()) {
            bool notInPlayedClient = known != null && !known.Contains(id);
            if (known != null) {
                if (notInPlayedClient) notInPlayed++;
                else inPlayed++;
            }
            Debug.Assert(data.collision.shape == "box" || string.IsNullOrWhiteSpace(data.collision.shape));

            var metadata = new NpcMetadata(Id: id,
                Name: name,
                AiPath: data.aiInfo.path,
                Model: new NpcMetadataModel(
                    Name: data.model.kfm,
                    Scale: data.model.scale,
                    AniSpeed: data.model.anispeed
                ),
                Distance: new NpcMetadataDistance(
                    Avoid: data.distance.avoid,
                    Sight: data.distance.sight,
                    SightHeightUp: data.distance.sightHeightUP,
                    SightHeightDown: data.distance.sightHeightDown,
                    LastSightRadius: data.distance.customLastSightRadius == 0 ? Constant.NpcLastSightRadius : data.distance.customLastSightRadius,
                    LastSightHeightUp: data.distance.customLastSightHeightUp == 0 ? Constant.NpcLastSightHeightUp : data.distance.customLastSightHeightUp,
                    LastSightHeightDown: data.distance.customLastSightHeightDown == 0 ? Constant.NpcLastSightHeightDown : data.distance.customLastSightHeightDown
                ),
                Skill: new NpcMetadataSkill(
                    Entries: data.skill.ids.Select((skillId, i) =>
                        new NpcMetadataSkill.Entry(skillId, data.skill.levels[i], i < data.skill.probs.Length ? data.skill.probs[i] : 0)).ToArray(),
                    Cooldown: data.skill.coolDown
                ),
                Stat: new NpcMetadataStat(Stats: MapStats(data.stat, playedStats.GetValueOrDefault(id)),
                    ScaleStatRate: [
                        data.stat.scaleStatRate_1,
                        data.stat.scaleStatRate_2,
                        data.stat.scaleStatRate_3,
                        data.stat.scaleStatRate_4,
                    ],
                    ScaleBaseTap: [
                        data.stat.scaleBaseTap_1,
                        data.stat.scaleBaseTap_2,
                        data.stat.scaleBaseTap_3,
                        data.stat.scaleBaseTap_4,
                    ],
                    ScaleBaseDef: [
                        data.stat.scaleBaseDef_1,
                        data.stat.scaleBaseDef_2,
                        data.stat.scaleBaseDef_3,
                        data.stat.scaleBaseDef_4,
                    ],
                    ScaleBaseSpaRate: [
                        data.stat.scaleBaseSpaRate_1,
                        data.stat.scaleBaseSpaRate_2,
                        data.stat.scaleBaseSpaRate_3,
                        data.stat.scaleBaseSpaRate_4,
                    ]),
                Basic: new NpcMetadataBasic(Friendly: data.basic.friendly,
                    AttackGroup: data.basic.npcAttackGroup,
                    DefenseGroup: data.basic.npcDefenseGroup,
                    Kind: data.basic.kind,
                    ShopId: data.basic.shopId,
                    HitImmune: data.basic.hitImmune,
                    AbnormalImmune: data.basic.abnormalImmune,
                    Level: data.basic.level,
                    Class: data.basic.@class,
                    RotationDisabled: data.basic.rotationDisabled,
                    MaxSpawnCount: data.basic.maxSpawnCount,
                    GroupSpawnCount: data.basic.groupSpawnCount,
                    RareDegree: data.basic.rareDegree,
                    MainTags: data.basic.mainTags,
                    SubTags: data.basic.subTags,
                    Difficulty: data.basic.difficulty,
                    CustomExp: data.exp.customExp,
                    PropertyTags: data.basic.propertyTags,
                    SummonNpcIds: summonNpcIds.GetValueOrDefault(id),
                    StringId: string.IsNullOrEmpty(data.basic.stringID) ? null : data.basic.stringID,
                    NotInPlayedClient: notInPlayedClient,
                    RaceStrings: data.basic.raceString),
                Property: new NpcMetadataProperty(
                    Buffs: data.additionalEffect.codes.Select((buffId, i) => new NpcMetadataBuff(buffId, data.additionalEffect.levels[i])).ToArray(),
                    Capsule: new NpcMetadataCapsule(data.capsule.radius, data.capsule.height),
                    Collision: data.collision.shape == "box" ? new NpcMetadataCollision(
                        Dimensions: new Vector3(data.collision.width, data.collision.depth, data.collision.height),
                        Offset: new Vector3(data.collision.widthOffset, data.collision.depthOffset, data.collision.heightOffset)
                    ) : null),
                DropInfo: new NpcMetadataDropInfo(
                    DropDistanceBase: data.dropiteminfo.dropDistanceBase,
                    DropDistanceRandom: data.dropiteminfo.dropDistanceRandom,
                    GlobalDropBoxIds: data.dropiteminfo.globalDropBoxId,
                    DeadGlobalDropBoxIds: data.dropiteminfo.globalDeadDropBoxId,
                    IndividualDropBoxIds: data.dropiteminfo.individualDropBoxId,
                    GlobalHitDropBoxIds: data.dropiteminfo.globalHitDropBoxId,
                    IndividualHitDropBoxIds: data.dropiteminfo.individualHitDropBoxId),
                Action: new NpcMetadataAction(
                    RotateSpeed: data.speed.rotation,
                    WalkSpeed: data.speed.walk,
                    RunSpeed: data.speed.run,
                    Actions: data.normal.action.Zip(data.normal.prob, (action, prob) => new NpcAction(action, prob)).ToArray(),
                    MoveArea: data.normal.movearea,
                    MaidExpired: data.normal.maidExpired),
                Dead: new NpcMetadataDead(
                    Time: data.dead.time,
                    Revival: data.dead.revival,
                    Count: data.dead.count,
                    LifeTime: data.dead.lifeTime,
                    ExtendRoomTime: data.dead.extendRoomTime),
                Corpse: data.corpse.hitAble == 1 ? new NpcMetadataCorpse(
                    Width: data.corpse.width,
                    Height: data.corpse.height,
                    Depth: data.corpse.depth,
                    Added: data.corpse.added,
                    OffsetNametag: data.corpse.offsetNametag,
                    CorpseEffect: data.corpse.corpseEffect,
                    HitAble: true,
                    Rotation: data.corpse.rotation) : null,
                LookAtTarget: new NpcMetadataLookAtTarget(
                    data.lookattarget.targetdummy,
                    data.lookattarget.lookAtMyPCWhenTalking == 1,
                    data.lookattarget.useTalkMotion == 1),
                Interact: NpcInteractRules.Parse(data.interact?.interactFunction, data.interact?.interactCastingTime ?? 0,
                    data.interact?.interactCoolTime ?? 0)
            );

            NpcMetadataById[id] = metadata;

            yield return metadata;
        }
        if (known != null) {
            Console.WriteLine($"npcs: {notInPlayed} not in the played client's NPC data (the server does not spawn them), {inPlayed} in it");
        }
        if (playedStats.Count > 0) {
            Console.WriteLine($"npcs: {playedStats.Count} stat blocks taken from the played client's NPC data (no hidden stat added)");
        }
    }

    private static bool EnvironmentAvailable(XmlElement node) =>
        FeatureLocaleFilter.FeatureEnabled(node.GetAttribute("feature")) && FeatureLocaleFilter.HasLocale(node.GetAttribute("locale"));

    // The NPC's stats. With the played client's stat block (played) they are that block as it is: the client adds no
    // hidden stat to it, and its data has the adds of its own region baked in (KR npcdata holds base + the
    // HiddenStatAdd01/02 values of the NA pack, and no HiddenStatAdd03/04 one), so adding the ingested pack's adds
    // would stack another region's tuning on top. Without it (no played client given, or an NPC the played client
    // does not have) they are the ingested pack's base plus the hidden adds of the features on for the locale.
    public static IReadOnlyDictionary<BasicAttribute, long> MapStats(Stat stat, StatValue? played = null) {
        if (played != null) {
            return played.ToDictionary();
        }
        Dictionary<BasicAttribute, long> stats = stat.ToDictionary();

        if (FeatureLocaleFilter.FeatureEnabled("HiddenStatAdd01")) {
            stats[BasicAttribute.Health] = stats.GetValueOrDefault(BasicAttribute.Health) + stat.hiddenhpadd;
            stats[BasicAttribute.Defense] = stats.GetValueOrDefault(BasicAttribute.Defense) + stat.hiddennddadd;
        }
        if (FeatureLocaleFilter.FeatureEnabled("HiddenStatAdd02")) {
            stats[BasicAttribute.PhysicalAtk] = stats.GetValueOrDefault(BasicAttribute.PhysicalAtk) + stat.hiddenwapadd;
            stats[BasicAttribute.MagicalAtk] = stats.GetValueOrDefault(BasicAttribute.MagicalAtk) + stat.hiddenwapadd;
        }
        if (FeatureLocaleFilter.FeatureEnabled("HiddenStatAdd03")) {
            stats[BasicAttribute.Health] = stats.GetValueOrDefault(BasicAttribute.Health) + stat.hiddenhpadd03;
            stats[BasicAttribute.Defense] = stats.GetValueOrDefault(BasicAttribute.Defense) + stat.hiddennddadd03;
            stats[BasicAttribute.PhysicalAtk] = stats.GetValueOrDefault(BasicAttribute.PhysicalAtk) + stat.hiddenwapadd03;
            stats[BasicAttribute.MagicalAtk] = stats.GetValueOrDefault(BasicAttribute.MagicalAtk) + stat.hiddenwapadd03;
        }
        if (FeatureLocaleFilter.FeatureEnabled("HiddenStatAdd04")) {
            stats[BasicAttribute.Health] = stats.GetValueOrDefault(BasicAttribute.Health) + stat.hiddenhpadd04;
            stats[BasicAttribute.Defense] = stats.GetValueOrDefault(BasicAttribute.Defense) + stat.hiddennddadd04;
            stats[BasicAttribute.PhysicalAtk] = stats.GetValueOrDefault(BasicAttribute.PhysicalAtk) + stat.hiddenwapadd04;
            stats[BasicAttribute.MagicalAtk] = stats.GetValueOrDefault(BasicAttribute.MagicalAtk) + stat.hiddenwapadd04;
        }

        return stats;
    }
}
