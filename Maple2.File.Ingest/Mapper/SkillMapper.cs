using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using M2dXmlGenerator;
using Maple2.File.IO;
using Maple2.File.Ingest.Helpers;
using Maple2.File.Ingest.Utils;
using Maple2.File.Parser;
using Maple2.File.Parser.Xml.Skill;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.File.Ingest.Mapper;

public class SkillMapper : TypeMapper<StoredSkillMetadata> {
    private readonly SkillParser parser;
    private readonly SkillMoveTypes moveTypes;
    private readonly SkillAttackDirections directions;

    // The played client's own skill data (PlayedClientSkills); empty without a played client.
    private readonly Dictionary<int, PlayedClientSkills.Entry> played = [];
    // The played client's skill-hit effects by skill and level (PlayedClientActionAdditionals), and the effect ids and
    // levels of the data pack, which the ones added must have; empty without a played client.
    private readonly Dictionary<(int SkillId, short Level), PlayedClientActionAdditionals.LevelUses> clientEffects = [];
    private readonly HashSet<(int Id, short Level)> packEffects = [];
    // The played client's attackMethodType of each attack of its levels (PlayedClientSkills.ReadAttackMethods); empty
    // without a played client.
    private readonly Dictionary<(int Skill, short Level, int Motion, int Attack), (int Method, string Point)> clientAttackMethods = [];
    private int attackMethodPointMismatches;
    // The played client's splashActiveSkillID lists (PlayedClientSkills.ReadSplashActive); empty without a played client.
    private readonly Dictionary<(int Skill, short Level, int Motion, int Attack), (int[] Ids, string Point)> clientSplashActive = [];
    private int splashActivePointMismatches;
    // The played client's level cooldowns in milliseconds (PlayedClientSkills.ReadLevelCooldowns); empty without a
    // played client.
    private readonly Dictionary<(int Skill, short Level), int> clientCooldowns = [];
    private int clientCooldownsTaken;
    // The played client's level spirit and stamina costs (PlayedClientSkills.ReadLevelCosts); empty without a played
    // client.
    private readonly Dictionary<(int Skill, short Level), (int Sp, int Ep)> clientCosts = [];
    private int clientCostsTaken;
    // The played client's attack damage rates (PlayedClientSkills.ReadAttackDamageRates); empty without a played
    // client.
    private readonly Dictionary<(int Skill, short Level, int Motion, int Attack), (float Rate, string Point)> clientDamageRates = [];
    private int clientDamageRatesTaken;
    private int damageRatePointMismatches;
    // The played client's attack range shapes and target counts (PlayedClientSkills.ReadAttackRanges); empty without
    // a played client.
    private readonly Dictionary<(int Skill, short Level, int Motion, int Attack), PlayedClientSkills.AttackRange> clientRanges = [];
    private int clientRangesTaken;
    private int clientTargetCountsTaken;
    private int clientTargetCountsZero;
    private int rangePointMismatches;
    // The played client's motion sequence speeds, move distances and splash life ticks
    // (PlayedClientSkills.ReadMotionTimings); empty without a played client.
    private readonly Dictionary<(int Skill, short Level, int Motion), PlayedClientSkills.MotionTiming> clientMotions = [];
    private int clientMotionsTaken;
    private int motionPointMismatches;
    // The data pack attacks put in the played client's order (MotionAttacks).
    private int clientAttacksMoved, clientAttacksBuilt, packAttacksDropped, clientSplashesAdded, clientSplashValues, clientMotionListsTaken;
    // The data pack's link rows the played client lacks, left out (DropLinksClientLacks): skill, level, motion and
    // attack (-1 for the level's own rows) and the row's skill ids.
    private readonly List<(int Skill, short Level, int Motion, int Attack, string Ids)> linksDropped = [];
    // The played client's attack pushes (PlayedClientSkills.ReadAttackPushes); empty without a played client.
    private readonly Dictionary<(int Skill, short Level, int Motion, int Attack), (PlayedClientSkills.AttackPush Push, string Point)> clientPushes = [];
    private int clientPushesTaken;
    // The played client's attack arrows (PlayedClientSkills.ReadAttackArrows); empty without a played client.
    private readonly Dictionary<(int Skill, short Level, int Motion, int Attack), (PlayedClientSkills.AttackArrow Arrow, string Point)> clientArrows = [];
    private int clientArrowsTaken;
    private int arrowPointMismatches;
    private int pushPointMismatches;
    // The played client's skills and levels as its SkillData::Load takes them (PlayedClientSkills.SelectSkills), to add
    // the levels and skills the data pack lacks (PlayedClientSkills.BuildLevel); empty without a played client.
    private readonly Dictionary<int, (System.Xml.XmlElement Skill, List<(short Value, System.Xml.XmlElement Level)> Levels)> playedSkills = [];

    // The data pack and the played client, for the skills a player's client can cast (PlayerSkillSources).
    private readonly M2dReader xmlReader;
    private readonly M2dReader? playedReader;
    // The skills each data pack skill's cast turns into (combo output, change skill, default skill, jump), filled by MapSkills.
    private readonly Dictionary<int, HashSet<int>> castEdges = [];

    // Whether the effect ingest takes over an effect only the played client has (AdditionalEffectMapper.LeftOut), so a
    // use of it is kept: one whose <SpecialAbility> it reads counts too (before: "SpecialAbility" stayed unread).
    public static bool PlayedOnlyTakenOver(int id, PlayedClientAdditionalEffects.Entry entry, ClientSpecialAbility? abilities, FinalStatusRows? finals) {
        return entry.Rows.Count > 0 && AdditionalEffectMapper.LeftOut(id, entry, abilities, finals, out _).Count == 0;
    }

    public SkillMapper(M2dReader xmlReader, string language, M2dReader? playedReader = null) {
        parser = new SkillParser(xmlReader, language);
        this.xmlReader = xmlReader;
        this.playedReader = playedReader;
        if (playedReader != null) {
            played = PlayedClientSkills.Read(playedReader.Files.Where(file => PlayedClientSkills.IsSkillFile(file.Name))
                .Select(file => playedReader.GetXmlDocument(file)));
            Console.WriteLine($"Skills in the played client's skill data: {played.Count} ({played.Values.Count(entry => PlayedClientSkills.UnrideMainType(entry.MainType))} of a main type that leaves the ride, {played.Values.Count(entry => entry.RechargeMaxCount > 0)} with recharge uses, {played.Values.Count(entry => entry.Weapons.Length > 0)} with hand weapons, {played.Values.Count(entry => entry.SplashMaxCount > 0)} with a region limit)");
            clientEffects = PlayedClientActionAdditionals.Read(playedReader.Files.Where(file => PlayedClientSkills.IsSkillFile(file.Name))
                .Select(file => playedReader.GetXmlDocument(file)), out int unsupported, ClientListedRows.Available, SelectLevel);
            foreach ((int effectId, IList<Maple2.File.Parser.Xml.AdditionalEffect.AdditionalEffectData> datas) in new AdditionalEffectParser(xmlReader).Parse()) {
                foreach (Maple2.File.Parser.Xml.AdditionalEffect.AdditionalEffectData data in datas) {
                    packEffects.Add((effectId, data.BasicProperty.level));
                }
            }
            // And the levels of the played client's effects the pack lacks that the effect ingest takes over
            // (AdditionalEffectMapper.PlayedOnly): Focused Fire 10600181's charge 10600280.
            var packIds = packEffects.Select(effect => effect.Id).ToHashSet();
            var abilities = new ClientSpecialAbility(playedReader, xmlReader);
            var finals = new FinalStatusRows(xmlReader);
            foreach ((int effectId, PlayedClientAdditionalEffects.Entry entry) in PlayedClientAdditionalEffects.Read(playedReader.Files
                         .Where(file => PlayedClientAdditionalEffects.IsAdditionalFile(file.Name)).Select(file => playedReader.GetXmlDocument(file)), ClientListedRows.Available)) {
                if (packIds.Contains(effectId) || !PlayedOnlyTakenOver(effectId, entry, abilities, finals)) {
                    continue;
                }
                foreach (short level in entry.Rows.Keys) {
                    packEffects.Add((effectId, level));
                }
            }
            Console.WriteLine($"Skill levels with effects in the played client's skill data: {clientEffects.Count} ({unsupported} effect rows left out for a condition the ingest does not read)");
            clientAttackMethods = PlayedClientSkills.ReadAttackMethods(playedReader.Files.Where(file => PlayedClientSkills.IsSkillFile(file.Name))
                .Select(file => playedReader.GetXmlDocument(file)), ClientListedRows.Available, SelectLevel);
            Console.WriteLine($"Skill attacks in the played client's skill data: {clientAttackMethods.Count} ({clientAttackMethods.Values.Count(entry => entry.Method == 1)} of attack method type 1)");
            clientSplashActive = PlayedClientSkills.ReadSplashActive(playedReader.Files.Where(file => PlayedClientSkills.IsSkillFile(file.Name))
                .Select(file => playedReader.GetXmlDocument(file)), ClientListedRows.Available, SelectLevel);
            Console.WriteLine($"Skill attacks with splash skills to fire in the played client's skill data: {clientSplashActive.Count}");
            clientCooldowns = PlayedClientSkills.ReadLevelCooldowns(playedReader.Files.Where(file => PlayedClientSkills.IsSkillFile(file.Name))
                .Select(file => playedReader.GetXmlDocument(file)), ClientListedRows.Available, SelectLevel);
            Console.WriteLine($"Skill levels with a cooldown in the played client's skill data: {clientCooldowns.Count}");
            clientCosts = PlayedClientSkills.ReadLevelCosts(playedReader.Files.Where(file => PlayedClientSkills.IsSkillFile(file.Name))
                .Select(file => playedReader.GetXmlDocument(file)), ClientListedRows.Available, SelectLevel);
            Console.WriteLine($"Skill levels in the played client's skill data (spirit and stamina cost): {clientCosts.Count}");
            clientDamageRates = PlayedClientSkills.ReadAttackDamageRates(playedReader.Files.Where(file => PlayedClientSkills.IsSkillFile(file.Name))
                .Select(file => playedReader.GetXmlDocument(file)), ClientListedRows.Available, SelectLevel);
            Console.WriteLine($"Skill attacks in the played client's skill data (damage rate): {clientDamageRates.Count}");
            clientRanges = PlayedClientSkills.ReadAttackRanges(playedReader.Files.Where(file => PlayedClientSkills.IsSkillFile(file.Name))
                .Select(file => playedReader.GetXmlDocument(file)), ClientListedRows.Available, SelectLevel);
            Console.WriteLine($"Skill attacks in the played client's skill data (range and target count): {clientRanges.Count}");
            clientMotions = PlayedClientSkills.ReadMotionTimings(playedReader.Files.Where(file => PlayedClientSkills.IsSkillFile(file.Name))
                .Select(file => playedReader.GetXmlDocument(file)), ClientListedRows.Available, SelectLevel);
            Console.WriteLine($"Skill motions in the played client's skill data: {clientMotions.Count}");
            clientPushes = PlayedClientSkills.ReadAttackPushes(playedReader.Files.Where(file => PlayedClientSkills.IsSkillFile(file.Name))
                .Select(file => playedReader.GetXmlDocument(file)), ClientListedRows.Available, SelectLevel);
            Console.WriteLine($"Skill attacks in the played client's skill data (push): {clientPushes.Count}");
            clientArrows = PlayedClientSkills.ReadAttackArrows(playedReader.Files.Where(file => PlayedClientSkills.IsSkillFile(file.Name))
                .Select(file => playedReader.GetXmlDocument(file)), ClientListedRows.Available, SelectLevel);
            Console.WriteLine($"Skill attacks in the played client's skill data (arrow): {clientArrows.Count}");
            playedSkills = PlayedClientSkills.SelectSkills(playedReader.Files.Where(file => PlayedClientSkills.IsSkillFile(file.Name))
                .Select(file => playedReader.GetXmlDocument(file)), ClientListedRows.Available, SelectLevel);
        }
        moveTypes = new SkillMoveTypes(xmlReader);
        Console.WriteLine($"Skill motions with a move type: {moveTypes.Count} ({moveTypes.Conflicts} given two types)");
        directions = new SkillAttackDirections(xmlReader);
        Console.WriteLine($"Skill attacks with a target order other than nearest first: {directions.Count} ({directions.Conflicts} given two orders)");
    }

    // Of two of a skill's levels with the same value, the one the client's FUN_14028faa0 keeps
    // (CFeatureManager::SelectFeatureElement).
    private static System.Xml.XmlElement SelectLevel(System.Xml.XmlElement a, System.Xml.XmlElement b) =>
        PlayedNpcStats.SelectFeatureElement(a, b, feature => FeatureLocaleFilter.Features.GetValueOrDefault(feature), FeatureLocaleFilter.Locale);

    // A skill no client path gives to a player is marked NoPlayerSource (PlayerSkillSources): the cast handler refuses
    // it outside the job trees. Nothing is marked without a played client or without the item ingest's items.
    protected override IEnumerable<StoredSkillMetadata> Map() {
        List<StoredSkillMetadata> skills = MapSkills().ToList();
        if (playedReader == null || played.Count == 0 || ItemMapper.ItemMetadataById.Count == 0) {
            return skills;
        }
        foreach ((int id, (System.Xml.XmlElement skill, _)) in playedSkills) {
            AddCastEdges(id, PlayerSkillSources.Edges(skill));
        }
        var granted = new HashSet<int>();
        foreach (M2dReader reader in new[] { xmlReader, playedReader }) {
            foreach (Maple2.File.IO.Crypto.Common.PackFileEntry file in reader.Files.Where(file => file.Name.StartsWith("table/"))) {
                switch (Path.GetFileName(file.Name)) {
                    case "job.xml":
                        granted.UnionWith(PlayerSkillSources.TreeSkills(reader.GetXmlDocument(file)));
                        break;
                    case "skillset.xml":
                        granted.UnionWith(PlayerSkillSources.SkillSetSkills(reader.GetXmlDocument(file)));
                        break;
                    case "constants.xml":
                        granted.UnionWith(PlayerSkillSources.ConstantSkills(reader.GetXmlDocument(file)));
                        break;
                }
            }
        }
        foreach (ItemMetadata item in ItemMapper.ItemMetadataById.Values) {
            if (item.Skill is { } itemSkill) {
                granted.Add(itemSkill.Id);
                granted.Add(itemSkill.WeaponId);
            }
        }
        HashSet<int> castable = PlayerSkillSources.Castable(played.ToDictionary(entry => entry.Key, entry => entry.Value.MainType), castEdges, granted);
        int marked = 0;
        for (int i = 0; i < skills.Count; i++) {
            if (!castable.Contains(skills[i].Id)) {
                skills[i] = skills[i] with { Property = skills[i].Property with { NoPlayerSource = true } };
                marked++;
            }
        }
        Console.WriteLine($"Skills no client path gives to a player (refused outside the job trees): {marked} of {skills.Count} ({castable.Count} castable)");
        return skills;
    }

    private void AddCastEdges(int id, IEnumerable<int> targets) {
        foreach (int target in targets) {
            if (target == 0 || target == id) {
                continue;
            }
            if (!castEdges.TryGetValue(id, out HashSet<int>? set)) {
                castEdges[id] = set = [];
            }
            set.Add(target);
        }
    }

    private IEnumerable<StoredSkillMetadata> MapSkills() {
        List<long> magicPaths = [];
        List<long> cubeMagicPaths = [];
        int clientAdded = 0, clientUnmatched = 0, clientSkills = 0, clientNotBuffOwned = 0;
        int playedLevelsAdded = 0, playedSkillsAdded = 0, playedConditionRows = 0, playedLevelsOfPackSkills = 0, playedLevelsOfItemSkills = 0;
        var packIds = new HashSet<int>();
        foreach ((int id, string name, SkillData data) in parser.Parse()) {
            packIds.Add(id);
            if (data.basic == null) continue; // Old_JobChange_01
            PlayedClientSkills.Entry? client = played.TryGetValue(id, out PlayedClientSkills.Entry entry) ? entry : null;
            Debug.Assert(data.basic.kinds.groupIDs.Length <= 1);
            AddCastEdges(id, data.level.SelectMany(level => (level.combo?.outputSkill ?? []).Concat(level.changeSkill?.changeSkillID ?? []))
                .Append(data.basic.stateAttr.useDefaultSkillID).Append(data.basic.kinds.jump));


            // Note: 90000775 has cubeMagicPathID="2147483647" which should be cubeMagicPathID="9000073111"
            Dictionary<short, SkillMetadataLevel> levels = data.level.ToDictionary(
                level => level.value,
                level => new SkillMetadataLevel(
                    // The played client's hand weapons and level cooldown stand in for the data pack's (PlayedClientSkills).
                    Condition: WithClientCooldown(id, level.value, client is { } played ? level.beginCondition.Convert() with {
                        Weapon = played.Weapons.Select(weapon =>
                        new BeginConditionWeapon(new ItemType(1, (byte) weapon.LeftHand), new ItemType(1, (byte) weapon.RightHand))).ToArray()
                    }
                        : level.beginCondition.Convert()),
                    Change: level.changeSkill?.Convert(),
                    AutoTargeting: level.autoTargeting?.Convert(),
                    Consume: new SkillMetadataConsume(
                        Meso: level.consume.money,
                        UseItem: level.consume.useItem,
                        HpRate: level.consume.hpRate,
                        Stat: WithClientCost(id, level.value, level.consume.stat.ToDictionary())),
                    Detect: new SkillMetadataDetect(
                        IncludeCaster: level.detectProperty?.includeCaster == 1,
                        Distance: level.detectProperty?.distance ?? 0),
                    Recovery: new SkillMetadataRecovery(
                        SpValue: level.recoveryProperty.spValue,
                        SpRate: level.recoveryProperty.spRate),
                    Skills: LevelSkills(id, level),
                    Motions: Motions(id, level),
                    Upgrade: level.upgrade == null ? null : new SkillMetadataUpgrade(
                        Level: level.upgrade.level,
                        SkillIds: level.upgrade.skillIDs,
                        SkillLevels: level.upgrade.skillLevels,
                        QuestIds: level.upgrade.questIDs),
                    OnlyManualActiveSplash: level.splashManualActiveProperty?.onlyManualActiveSplash == true
                ));

            // The played client's levels of the skill that the data pack lacks (PlayedClientSkills.BuildLevel), with
            // what the client does not read taken from the data pack's nearest level (PlayedClientSkills.WithPackLevel):
            // an item skill's level stays an item skill's.
            if (playedSkills.TryGetValue(id, out var playedSkill)) {
                short[] packValues = levels.Keys.Order().ToArray();
                foreach ((short value, System.Xml.XmlElement level) in playedSkill.Levels) {
                    if (value < 1 || levels.ContainsKey(value)) {
                        continue;
                    }
                    SkillMetadataLevel built = PlayedClientSkills.BuildLevel(playedSkill.Skill, level);
                    if (PlayedClientSkills.NearestLevel(packValues, value) is { } nearest) {
                        built = PlayedClientSkills.WithPackLevel(built, levels[nearest]);
                        playedLevelsOfPackSkills++;
                        if (built.Consume.UseItem) playedLevelsOfItemSkills++;
                    }
                    levels.Add(value, built);
                    playedLevelsAdded++;
                }
            }

            // The played client's effects of a level or attack that the data pack's rows lack (PlayedClientActionAdditionals).
            AddClientEffects(id, levels, ref clientAdded, ref clientUnmatched, ref clientSkills, ref clientNotBuffOwned);

            yield return new StoredSkillMetadata(
                Id: id,
                Name: name,
                Property: new SkillMetadataProperty(
                    Type: (SkillType) data.basic.kinds.type,
                    SubType: (SkillSubType) data.basic.kinds.subType,
                    RangeType: (RangeType) data.basic.kinds.rangeType,
                    AttackType: (AttackType) data.basic.ui.attackType,
                    Element: (Element) data.basic.kinds.element,
                    State: string.IsNullOrEmpty(data.basic.kinds.state)
                        ? ActorState.None
                        : Enum.GetValues<ActorState>()
                            .FirstOrDefault(enumValue =>
                                enumValue.GetType()
                                    .GetField(enumValue.ToString())
                                    ?.GetCustomAttribute<DescriptionAttribute>()
                                    ?.Description == data.basic.kinds.state),
                    ContinueSkill: data.basic.kinds.continueSkill,
                    SpRecoverySkill: data.basic.kinds.spRecoverySkill,
                    ImmediateActive: data.basic.kinds.immediateActive,
                    UnrideOnHit: data.basic.kinds.unrideOnHit,
                    UnrideOnUse: data.basic.kinds.unrideOnUse,
                    ReleaseObjectWeapon: data.basic.kinds.releaseObjectWeapon,
                    Emotion: data.basic.kinds.emotion,
                    SkillGroup: data.basic.kinds.groupIDs.FirstOrDefault(),
                    MaxLevel: levels.Keys.Max(),
                    MainType: client?.MainType ?? 0),
                State: new SkillMetadataState(
                    InBattle: data.basic.stateAttr.battle == 1,
                    SuperArmor: (SuperArmor) data.basic.stateAttr.superArmor,
                    UseInGameTime: data.basic.stateAttr.useInGameTime == 1,
                    IgnoreReduceCooldown: data.basic.stateAttr.ignoreReduceCooldown == 1,
                    CooldownGroupId: data.basic.stateAttr.cooldownGroupID,
                    RechargeMaxCount: client?.RechargeMaxCount ?? data.basic.stateAttr.rechargeMaxCount,
                    RechargeShared: client?.RechargeShared ?? false,
                    ClearCooldownOnPvpZone: data.basic.stateAttr.clearCooldownFromPVPZone != 0,
                    SuperArmorApplyField: data.basic.stateAttr.superArmorApplyFieldType,
                    SplashMaxCount: client?.SplashMaxCount ?? data.basic.stateAttr.splashMaxCount),
                Levels: levels
            );
        }
        // The played client's skills that the data pack lacks: the client casts them and fires them from other skills'
        // attacks like any other (PlayedClientSkills.BuildLevel, BuildProperty, BuildState).
        Dictionary<int, string> names = playedSkills.Count > 0 ? parser.LoadSkillNames() : [];
        foreach ((int id, (System.Xml.XmlElement skill, List<(short Value, System.Xml.XmlElement Level)> playedLevels)) in playedSkills.OrderBy(entry => entry.Key)) {
            if (packIds.Contains(id)) {
                continue;
            }
            var levels = new Dictionary<short, SkillMetadataLevel>();
            foreach ((short value, System.Xml.XmlElement level) in playedLevels) {
                if (value >= 1) {
                    levels.TryAdd(value, PlayedClientSkills.BuildLevel(skill, level));
                }
            }
            if (levels.Count == 0) {
                continue;
            }
            AddClientEffects(id, levels, ref clientAdded, ref clientUnmatched, ref clientSkills, ref clientNotBuffOwned);
            playedSkillsAdded++;
            playedLevelsAdded += levels.Count;
            playedConditionRows += PlayedClientSkills.UnreadConditionRows(skill);
            yield return new StoredSkillMetadata(
                Id: id,
                Name: names.GetValueOrDefault(id, string.Empty),
                Property: PlayedClientSkills.BuildProperty(skill, levels.Keys.Max()),
                State: PlayedClientSkills.BuildState(id, skill),
                Levels: levels);
        }
        if (playedSkills.Count > 0) {
            Console.WriteLine($"Skills and levels added from the played client's skill data: {playedSkillsAdded} skills, {playedLevelsAdded} levels ({playedConditionRows} condition rows of the added skills with no place in the server's condition; {playedLevelsOfPackSkills} levels of data pack skills given the rest of their condition and the item use from the nearest data pack level, {playedLevelsOfItemSkills} of them an item skill's)");
        }
        if (clientEffects.Count > 0) {
            Console.WriteLine($"Skill effects added from the played client's skill data: {clientAdded} in {clientSkills} skills ({clientUnmatched} attack effects with no attack of their point)");
            Console.WriteLine($"Effect buff tests given hasNotBuffOwner from the played client's skill data: {clientNotBuffOwned}");
        }
        if (clientSplashActive.Count > 0) {
            Console.WriteLine($"Skill attacks whose played splash skills to fire have another point: {splashActivePointMismatches} (left out)");
        }
        if (clientAttackMethods.Count > 0) {
            Console.WriteLine($"Skill attacks whose played attack method has another point: {attackMethodPointMismatches} (the data pack's kept)");
        }
        if (clientCooldowns.Count > 0) {
            Console.WriteLine($"Skill levels given the played client's cooldown over the data pack's: {clientCooldownsTaken}");
        }
        if (clientCosts.Count > 0) {
            Console.WriteLine($"Skill levels given the played client's spirit and stamina cost over the data pack's: {clientCostsTaken}");
        }
        if (clientDamageRates.Count > 0) {
            Console.WriteLine($"Skill attacks given the played client's damage rate over the data pack's: {clientDamageRatesTaken} ({damageRatePointMismatches} with another point at that place, the data pack's kept)");
        }
        if (clientRanges.Count > 0) {
            Console.WriteLine($"Skill attacks given the played client's range shape over the data pack's: {clientRangesTaken}, target count: {clientTargetCountsTaken} ({rangePointMismatches} with another point at that place, the data pack's kept; {clientTargetCountsZero} with no client target count, the data pack's kept)");
        }
        if (clientPushes.Count > 0) {
            Console.WriteLine($"Skill attacks given the played client's push over the data pack's: {clientPushesTaken} ({pushPointMismatches} with another point at that place, the data pack's kept)");
        }
        if (clientArrows.Count > 0) {
            Console.WriteLine($"Skill attacks given the played client's arrow over the data pack's: {clientArrowsTaken} ({arrowPointMismatches} with another point at that place, the data pack's kept)");
        }
        if (playedSkills.Count > 0) {
            Console.WriteLine($"Skill attacks put in the played client's order: {clientAttacksMoved} data pack attacks at another place, {clientAttacksBuilt} built from the client's row, {packAttacksDropped} data pack attacks the client lacks left out; {clientSplashesAdded} splash rows of the client's attacks added, {clientSplashValues} given the client's values; {clientMotionListsTaken} levels given the client's motions (another motion count)");
            int attackRows = linksDropped.Count(row => row.Attack >= 0), levelRows = linksDropped.Count - attackRows;
            Console.WriteLine($"Data pack link rows the played client lacks left out: {attackRows} attack rows in {linksDropped.Where(row => row.Attack >= 0).Select(row => row.Skill).Distinct().Count()} skills, {levelRows} level rows in {linksDropped.Where(row => row.Attack < 0).Select(row => row.Skill).Distinct().Count()} skills");
            foreach ((int skill, short value, int motionIndex, int attackIndex, string ids) in linksDropped) {
                Console.WriteLine(attackIndex < 0 ? $"  link left out: {skill} level {value} -> {ids}" : $"  link left out: {skill} level {value} motion {motionIndex} attack {attackIndex} -> {ids}");
            }
        }
        if (clientMotions.Count > 0) {
            Console.WriteLine($"Skill motions given the played client's sequence name, sequence speed, move distance and splash life tick over the data pack's: {clientMotionsTaken} ({motionPointMismatches} with other attack points, or another motion count in the level and another sequence, at that place, the data pack's kept)");
        }
    }

    // The played client's effects of a level or attack that the data pack's rows lack (PlayedClientActionAdditionals).
    private void AddClientEffects(int id, Dictionary<short, SkillMetadataLevel> levels, ref int clientAdded, ref int clientUnmatched,
            ref int clientSkills, ref int clientNotBuffOwned) {
        bool clientAddedHere = false;
        foreach (short value in levels.Keys.ToArray()) {
            if (!clientEffects.TryGetValue((id, value), out PlayedClientActionAdditionals.LevelUses? uses)) {
                continue;
            }
            levels[value] = PlayedClientActionAdditionals.MarkNotBuffOwned(levels[value], uses, out int marked);
            clientNotBuffOwned += marked;
            levels[value] = PlayedClientActionAdditionals.Merge(levels[value], uses, (effectId, level) => packEffects.Contains((effectId, level)),
                out int added, out int unmatched);
            clientAdded += added;
            clientUnmatched += unmatched;
            clientAddedHere |= added > 0;
        }
        if (clientAddedHere) clientSkills++;
    }

    // The played client's type where it has the attack at that place with the same point, else the data pack's.
    private int AttackMethod(int id, short level, int motion, int attack, string point, int pack) {
        if (!clientAttackMethods.TryGetValue((id, level, motion, attack), out (int Method, string Point) played)) {
            return pack;
        }
        if (played.Point != point) {
            attackMethodPointMismatches++;
            return pack;
        }
        return played.Method;
    }

    // The played client's damage rate where it has the attack at that place with the same point, else the data pack's.
    private float DamageRate(int id, short level, int motion, int attack, string point, float pack) {
        if (!clientDamageRates.TryGetValue((id, level, motion, attack), out (float Rate, string Point) played)) {
            return pack;
        }
        if (played.Point != point) {
            damageRatePointMismatches++;
            return pack;
        }
        if (played.Rate != pack) {
            clientDamageRatesTaken++;
        }
        return played.Rate;
    }

    // The played client's whole hit shape where it has the attack at that place with the same point, else the data
    // pack's (PlayedClientSkills.WithShape).
    private SkillMetadataRange WithClientRange(int id, short level, int motion, int attack, string point, SkillMetadataRange pack) {
        if (!clientRanges.TryGetValue((id, level, motion, attack), out PlayedClientSkills.AttackRange? played)) {
            return pack;
        }
        if (played.Point != point) {
            rangePointMismatches++;
            return pack;
        }
        SkillMetadataRange taken = PlayedClientSkills.WithShape(pack, played);
        if (taken != pack) {
            clientRangesTaken++;
        }
        return taken;
    }
    // The level's motions: the played client's own where its level has another motion count
    // (PlayedClientSkills.ClientMotions), else the data pack's, each with the client's values (Motion).
    private SkillMetadataMotion[] Motions(int id, SkillLevelData level) {
        if (playedSkills.TryGetValue(id, out var playedSkill)
            && playedSkill.Levels.FirstOrDefault(entry => entry.Value == level.value).Level is { } clientLevel
            && PlayedClientSkills.ClientMotions(clientLevel, level.motion.Count) is { } client) {
            clientMotionListsTaken++;
            return client;
        }
        return level.motion.Select((motion, motionIndex) => Motion(id, level.value, motionIndex, level.motion.Count, motion)).ToArray();
    }

    // A data pack motion, its attacks in the played client's order where the client pairs them (MotionAttacks), and the
    // client's timing where its motion at that place has the same attack points (WithClientMotion).
    private SkillMetadataMotion Motion(int id, short level, int motionIndex, int motions, SkillMotionData motion) {
        SkillMetadataAttack[] attacks = MotionAttacks(id, level, motionIndex, motions, motion, out IReadOnlyDictionary<string, byte> points);
        return new SkillMetadataMotion(
            WithClientMotion(id, level, motionIndex, motions, string.Join(",", attacks.Select(attack => attack.Point)), new SkillMetadataMotionProperty(
                SequenceName: motion.motionProperty.sequenceName,
                SequenceSpeed: motion.motionProperty.sequenceSpeed,
                MoveDistance: motion.motionProperty.movedistance,
                FaceTarget: motion.motionProperty.faceTarget,
                MoveType: moveTypes.Get(id, level, motionIndex),
                SplashLifeTick: motion.motionProperty.splashLifeTick,
                SplashInvokeCoolTick: motion.motionProperty.splashInvokeCoolTick)),
            Attacks: attacks,
            AttackPoints: points);
    }

    // A motion's attacks in the played client's order where the client's level has as many motions as the data pack's
    // (PlayedClientSkills.MotionAttacks): the client fires an attack by its place in its motion (StateSkill), so each
    // client attack takes the data pack attack its point pairs with (PlayedClientSkills.AlignAttacks), with the
    // client's splash rows the data pack's attack lacks (PlayedClientSkills.MissingSplashes); a client attack with no
    // data pack attack is built from the client's row (PlayedClientSkills.BuildAttack), and a data pack attack no client
    // attack takes is left out. Else the data pack's attacks as they are.
    private SkillMetadataAttack[] MotionAttacks(int id, short level, int motionIndex, int motions, SkillMotionData motion,
            out IReadOnlyDictionary<string, byte> points) {
        List<SkillAttackData> pack = motion.attack;
        System.Xml.XmlElement[]? client = playedSkills.TryGetValue(id, out var playedSkill)
            && playedSkill.Levels.FirstOrDefault(entry => entry.Value == level).Level is { } clientLevel
            ? PlayedClientSkills.MotionAttacks(clientLevel, motionIndex, motions) : null;
        if (client == null) {
            points = motion.CollectAttackPoints();
            return pack.Select((attack, attackIndex) => PackAttack(id, level, motionIndex, attackIndex, attackIndex, attack)).ToArray();
        }

        string[] clientPoints = client.Select(attack => attack.GetAttribute("point")).ToArray();
        int[] pairs = PlayedClientSkills.AlignAttacks(pack.Select(attack => attack.point).ToArray(), clientPoints);
        var attacks = new SkillMetadataAttack[client.Length];
        for (int i = 0; i < client.Length; i++) {
            if (pairs[i] < 0) {
                attacks[i] = PlayedClientSkills.BuildAttack(client[i]);
                clientAttacksBuilt++;
                continue;
            }
            if (pairs[i] != i) clientAttacksMoved++;
            SkillMetadataAttack attack = PackAttack(id, level, motionIndex, i, pairs[i], pack[pairs[i]]);
            SkillEffectMetadata[] taken = PlayedClientSkills.WithClientSplashes(client[i], attack.Skills, out int changed);
            if (changed > 0) {
                attack = attack with { Skills = taken };
                clientSplashValues += changed;
            }
            attack = DropLinksClientLacks(id, level, motionIndex, i, client[i], attack);
            SkillEffectMetadata[] splashes = PlayedClientSkills.MissingSplashes(client[i], attack.Skills.Concat(attack.SkillsOnDamage));
            if (splashes.Length > 0) {
                attack = attack with { Skills = [.. attack.Skills, .. splashes] };
                clientSplashesAdded += splashes.Length;
            }
            attacks[i] = attack;
        }
        packAttacksDropped += pack.Count - pairs.Count(pair => pair >= 0);
        points = PlayedClientSkills.AttackPoints(clientPoints);
        return attacks;
    }

    // A paired attack without the data pack's link rows (on hit and on damage) the client's attack lacks
    // (PlayedClientSkills.WithoutRowsClientLacks), each logged in linksDropped.
    private SkillMetadataAttack DropLinksClientLacks(int id, short level, int motionIndex, int attackIndex, System.Xml.XmlElement client, SkillMetadataAttack attack) {
        var dropped = new List<SkillEffectMetadata>();
        SkillEffectMetadata[] skills = PlayedClientSkills.WithoutRowsClientLacks(client, attack.Skills, dropped);
        SkillEffectMetadata[] onDamage = PlayedClientSkills.WithoutRowsClientLacks(client, attack.SkillsOnDamage, dropped);
        if (dropped.Count == 0) {
            return attack;
        }
        LogDropped(id, level, motionIndex, attackIndex, dropped);
        return attack with { Skills = skills, SkillsOnDamage = onDamage };
    }

    // A level's own link rows: the data pack's, without the ones the played client's level lacks where the client
    // has the level (PlayedClientSkills.WithoutRowsClientLacks over the level's <actionAdditional> and <actionSkill>
    // rows; the client reads only the former, the latter are counted as the client's too, so nothing it names is
    // left out).
    private SkillEffectMetadata[] LevelSkills(int id, SkillLevelData level) {
        SkillEffectMetadata[] pack = level.conditionSkill.Select(skill => skill.Convert()).ToArray();
        if (!playedSkills.TryGetValue(id, out var playedSkill)
            || playedSkill.Levels.FirstOrDefault(entry => entry.Value == level.value).Level is not { } clientLevel) {
            return pack;
        }
        var dropped = new List<SkillEffectMetadata>();
        SkillEffectMetadata[] kept = PlayedClientSkills.WithoutRowsClientLacks(clientLevel, pack, dropped);
        LogDropped(id, level.value, -1, -1, dropped);
        return kept;
    }

    private void LogDropped(int id, short level, int motionIndex, int attackIndex, List<SkillEffectMetadata> dropped) {
        foreach (SkillEffectMetadata row in dropped) {
            linksDropped.Add((id, level, motionIndex, attackIndex, string.Join(",", row.Skills.Select(skill => $"{skill.Id}/{skill.Level}"))));
        }
    }

    // A data pack attack, with the played client's values of the client attack at that place in the motion
    // (attackIndex) where the client has one; its target order is the data pack's attack's (packIndex).
    private SkillMetadataAttack PackAttack(int id, short level, int motionIndex, int attackIndex, int packIndex, SkillAttackData attack) {
        return new SkillMetadataAttack(

            Point: attack.point,
            PointGroup: attack.pointGroupID,
            TargetCount: TargetCount(id, level, motionIndex, attackIndex, attack.point, attack.targetCount),
            MagicPathId: attack.magicPathID,
            CubeMagicPathId: attack.cubeMagicPathID == int.MaxValue ? 9000073111 : attack.cubeMagicPathID,
            HitImmuneBreak: attack.hitImmuneBreak,
            BrokenOffence: attack.brokenOffence,
            CompulsionTypes: attack.compulsionType.Select(type => (CompulsionType) type).ToArray(),
            Pet: attack.petTamingProperty != null ? new SkillMetadataPet(
                TamingGroup: attack.petTamingProperty.tamingGroup,
                TrapLevel: attack.petTamingProperty.trapLevel,
                TamingPoint: attack.petTamingProperty.tamingPoint,
                ForcedTaming: attack.petTamingProperty.forcedTaming
            ) : null,
            Range: WithClientRange(id, level, motionIndex, attackIndex, attack.point, Convert(attack.rangeProperty)),
            Arrow: WithClientArrow(id, level, motionIndex, attackIndex, attack.point, new SkillMetadataArrow(
                Overlap: attack.arrowProperty.overlap,
                Explosion: attack.arrowProperty.explosion,
                RayPhysXTest: attack.arrowProperty.rayPhysxTest,
                NonTarget: (SkillTargetType) attack.arrowProperty.nonTarget,
                BounceType: (BounceType) attack.arrowProperty.bounceType,
                BounceCount: attack.arrowProperty.bounceCount,
                BounceRadius: attack.arrowProperty.bounceRadius,
                BounceOverlap: attack.arrowProperty.bounceType > 0 && attack.arrowProperty.bounceOverlap,
                Collision: attack.arrowProperty.collision,
                CollisionAdd: attack.arrowProperty.collisionAdd,
                RayType: attack.arrowProperty.rayType)),
            Damage: new SkillMetadataDamage(
                Count: attack.damageProperty.count,
                Rate: DamageRate(id, level, motionIndex, attackIndex, attack.point, attack.damageProperty.rate),
                HitSpeed: attack.damageProperty.hitSpeedRate,
                HitDelay: attack.damageProperty.hitPauseTime,
                IsConstDamage: attack.damageProperty.isConstDamageValue != 0,
                Value: attack.damageProperty.value,
                DamageByTargetMaxHp: attack.damageProperty.damageByTargetMaxHP,
                SuperArmorBreak: attack.damageProperty.superArmorBreak,
                Push: WithClientPush(id, level, motionIndex, attackIndex, attack.point, PlayedClientSkills.IsPush(attack.damageProperty.push) ? new SkillMetadataPush(
                    Type: attack.damageProperty.push,
                    EaseType: attack.damageProperty.pushEaseType,
                    ApplyField: attack.damageProperty.pushApplyField,
                    Distance: attack.damageProperty.pushdistance,
                    UpDistance: attack.damageProperty.pushUpDistance,
                    Down: attack.damageProperty.pushDown,
                    Fall: attack.damageProperty.pushFall,
                    Duration: attack.damageProperty.pushduration,
                    Probability: attack.damageProperty.pushprob,
                    Priority: attack.damageProperty.pushPriority,
                    PriorityHitImmune: attack.damageProperty.pushPriorityHitImmune
                ) : null),
                ConstDamageByTargetHp: attack.damageProperty.constDamageByTargetHP,
                ConstDamageByTargetSp: attack.damageProperty.constDamageByTargetSP
            ),
            Skills: attack.conditionSkill.Where(skill => !skill.dependOnDamageCount).Select(skill => skill.Convert()).ToArray(),
            SkillsOnDamage: attack.conditionSkill.Where(skill => skill.dependOnDamageCount).Select(skill => skill.Convert()).ToArray(),
            GrabTargetType: attack.grabTargetType,
            Direction: directions.Get(id, level, motionIndex, packIndex),
            AttackMethodType: AttackMethod(id, level, motionIndex, attackIndex, attack.point, (int) attack.attackMethodType),
            SplashActiveSkillIds: SplashActive(id, level, motionIndex, attackIndex, attack.point)
        );
    }


    // The played client's sequence name, sequence speed, move distance, splash life tick and key move where it has the
    // motion at that place (PlayedClientSkills.SameMotion), else the data pack's (which has no key move); the rest of
    // the motion is the data pack's.
    private SkillMetadataMotionProperty WithClientMotion(int id, short level, int motion, int motions, string points, SkillMetadataMotionProperty pack) {
        if (!clientMotions.TryGetValue((id, level, motion), out PlayedClientSkills.MotionTiming? played)) {
            return pack;
        }
        if (!PlayedClientSkills.SameMotion(played, motions, pack.SequenceName, points)) {
            motionPointMismatches++;
            return pack;
        }
        SkillMetadataMotionProperty taken = pack with {
            SequenceName = PlayedClientSkills.SequenceName(played, pack.SequenceName),
            SequenceSpeed = played.SequenceSpeed,
            MoveDistance = played.MoveDistance,
            SplashLifeTick = played.SplashLifeTick,
            KeyMove = played.KeyMove,
        };
        if (taken != pack) {
            clientMotionsTaken++;
        }
        return taken;
    }

    // The played client's push where it has the attack at that place with the same point, else the data pack's. As
    // for the data pack's, only a push type of 0 is no push (PlayedClientSkills.IsPush).
    private SkillMetadataPush? WithClientPush(int id, short level, int motion, int attack, string point, SkillMetadataPush? pack) {
        if (!clientPushes.TryGetValue((id, level, motion, attack), out (PlayedClientSkills.AttackPush Push, string Point) played)) {
            return pack;
        }
        if (played.Point != point) {
            pushPointMismatches++;
            return pack;
        }
        PlayedClientSkills.AttackPush push = played.Push;
        SkillMetadataPush? taken = PlayedClientSkills.IsPush(push.Type) ? new SkillMetadataPush(
            Type: push.Type,
            EaseType: push.EaseType,
            ApplyField: push.ApplyField,
            Distance: push.Distance,
            UpDistance: push.UpDistance,
            Down: push.Down,
            Fall: push.Fall,
            Duration: push.Duration,
            Probability: push.Probability,
            Priority: push.Priority,
            PriorityHitImmune: push.PriorityHitImmune) : null;
        if (taken != pack) {
            clientPushesTaken++;
        }
        return taken;
    }

    // The played client's arrow where it has the attack at that place with the same point, else the data pack's
    // (PlayedClientSkills.TakeArrow).
    private SkillMetadataArrow WithClientArrow(int id, short level, int motion, int attack, string point, SkillMetadataArrow pack) {
        if (!clientArrows.TryGetValue((id, level, motion, attack), out (PlayedClientSkills.AttackArrow Arrow, string Point) played)) {
            return pack;
        }
        if (PlayedClientSkills.TakeArrow(played.Arrow, played.Point, point) is not { } taken) {
            arrowPointMismatches++;
            return pack;
        }
        if (taken != pack) {
            clientArrowsTaken++;
        }
        return taken;
    }

    // The played client's target count where it has the attack at that place with the same point and gives one, else
    // the data pack's (PlayedClientSkills.WithTargetCount).
    private int TargetCount(int id, short level, int motion, int attack, string point, int pack) {
        if (!clientRanges.TryGetValue((id, level, motion, attack), out PlayedClientSkills.AttackRange? played) || played.Point != point) {
            return pack;
        }
        int taken = PlayedClientSkills.WithTargetCount(pack, played.TargetCount);
        if (played.TargetCount == 0 && pack != 0) {
            clientTargetCountsZero++;
        }
        if (taken != pack) {
            clientTargetCountsTaken++;
        }
        return taken;
    }

    // The played client's splashActiveSkillID where it has the attack at that place with the same point, else none.
    private int[]? SplashActive(int id, short level, int motion, int attack, string point) {
        if (!clientSplashActive.TryGetValue((id, level, motion, attack), out (int[] Ids, string Point) played)) {
            return null;
        }
        if (played.Point != point) {
            splashActivePointMismatches++;
            return null;
        }
        return played.Ids;
    }

    // The level's cooldown in seconds as the played client counts it (its level cooldown in milliseconds), where its
    // level gives one; else the data pack's beginCondition cooldownTime.
    private Maple2.Model.Metadata.BeginCondition WithClientCooldown(int skillId, short level, Maple2.Model.Metadata.BeginCondition condition) {
        if (!clientCooldowns.TryGetValue((skillId, level), out int cooldownMs)) {
            return condition;
        }
        float seconds = cooldownMs / 1000f;
        if (seconds == condition.CooldownTime) {
            return condition;
        }
        clientCooldownsTaken++;
        return condition with { CooldownTime = seconds };
    }

    // The consume stats with the played client's level spirit and stamina cost (its level sp and ep, 0 when not
    // given) where the client has the level; else the data pack's.
    private Dictionary<BasicAttribute, long> WithClientCost(int skillId, short level, Dictionary<BasicAttribute, long> stat) {
        if (!clientCosts.TryGetValue((skillId, level), out (int Sp, int Ep) cost)
            || PlayedClientSkills.WithLevelCost(stat, cost) is not { } taken) {
            return stat;
        }
        clientCostsTaken++;
        return taken;
    }

    private static SkillRegion RegionType(string rangeType) {
        return rangeType switch {
            "box" => SkillRegion.Box,
            "cylinder" => SkillRegion.Cylinder,
            "circle" => SkillRegion.Cylinder,
            "frustum" => SkillRegion.Frustum,
            "hole_cylinder" => SkillRegion.HoleCylinder,
            "1200" => SkillRegion.None, // skill/60/60012051.xml
            _ => SkillRegion.None,
        };
    }

    private static SkillMetadataRange Convert(RegionSkill region) {
        return new SkillMetadataRange(
            Type: RegionType(region.rangeType),
            Distance: region.distance,
            Height: region.height,
            Width: region.width,
            EndWidth: region.endWidth,
            RotateZDegree: region.rangeZRotateDegree,
            RangeAdd: region.rangeAdd,
            RangeOffset: region.rangeOffset,
            IncludeCaster: (IncludeCasterType) region.includeCaster,
            ApplyTarget: (ApplyTargetType) region.applyTarget,
            CastTarget: (SkillTargetType) region.castTarget,
            Select: ConvertSelect(region),
            IgnoreNpcClasses: region.applyTargetIgnoreNpcRanks.Length > 0 ? region.applyTargetIgnoreNpcRanks : null,
            HasNotBuffId: region.targetHasNotBuffID
        );
    }

    // The client loads the buff, party and guild fields only when targetSelectType is set, and the stat sort always
    // (Unk_RangeProperty::LoadElement); its search filters by the first and sorts by the second (CBaseCollision).
    // Select modes other than 2 and 3 remove no target but put the passing ones first, so any mode keeps its fields.
    private static SkillMetadataRangeSelect? ConvertSelect(RegionSkill region) {
        bool filters = region.targetSelectType != 0;
        if (!filters && region.targetStatCompareFunction == 0) {
            return null;
        }
        return new SkillMetadataRangeSelect(
            Mode: filters ? region.targetSelectType : 0,
            BuffId: filters ? region.targetHasBuffID : 0,
            BuffFromCaster: filters && region.targetHasBuffOwner,
            SameParty: filters && region.targetSelectParty,
            SameGuild: filters && region.targetSelectGuild,
            Stat: (BasicAttribute) region.targetStatCompare,
            StatSort: region.targetStatCompareFunction);
    }
}
