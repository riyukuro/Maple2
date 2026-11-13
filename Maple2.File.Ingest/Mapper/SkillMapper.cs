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

    public SkillMapper(M2dReader xmlReader, string language, M2dReader? playedReader = null) {
        parser = new SkillParser(xmlReader, language);
        if (playedReader != null) {
            played = PlayedClientSkills.Read(playedReader.Files.Where(file => PlayedClientSkills.IsSkillFile(file.Name))
                .Select(file => playedReader.GetXmlDocument(file)));
            Console.WriteLine($"Skills in the played client's skill data: {played.Count} ({played.Values.Count(entry => PlayedClientSkills.UnrideMainType(entry.MainType))} of a main type that leaves the ride, {played.Values.Count(entry => entry.RechargeMaxCount > 0)} with recharge uses, {played.Values.Count(entry => entry.Weapons.Length > 0)} with hand weapons)");
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
            foreach ((int effectId, PlayedClientAdditionalEffects.Entry entry) in PlayedClientAdditionalEffects.Read(playedReader.Files
                         .Where(file => PlayedClientAdditionalEffects.IsAdditionalFile(file.Name)).Select(file => playedReader.GetXmlDocument(file)), ClientListedRows.Available)) {
                if (packIds.Contains(effectId) || entry.Rows.Count == 0 || PlayedAdditionalRows.Unread(entry).Count > 0) {
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

    protected override IEnumerable<StoredSkillMetadata> Map() {
        List<long> magicPaths = [];
        List<long> cubeMagicPaths = [];
        int clientAdded = 0, clientUnmatched = 0, clientSkills = 0, clientNotBuffOwned = 0;
        foreach ((int id, string name, SkillData data) in parser.Parse()) {
            if (data.basic == null) continue; // Old_JobChange_01
            PlayedClientSkills.Entry? client = played.TryGetValue(id, out PlayedClientSkills.Entry entry) ? entry : null;
            Debug.Assert(data.basic.kinds.groupIDs.Length <= 1);


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
                    Skills: level.conditionSkill.Select(skill => skill.Convert()).ToArray(),
                    Motions: level.motion.Select((motion, motionIndex) => new SkillMetadataMotion(
                        WithClientMotion(id, level.value, motionIndex, string.Join(",", motion.attack.Select(attack => attack.point)), new SkillMetadataMotionProperty(
                            SequenceName: motion.motionProperty.sequenceName,
                            SequenceSpeed: motion.motionProperty.sequenceSpeed,
                            MoveDistance: motion.motionProperty.movedistance,
                            FaceTarget: motion.motionProperty.faceTarget,
                            MoveType: moveTypes.Get(id, level.value, motionIndex),
                            SplashLifeTick: motion.motionProperty.splashLifeTick,
                            SplashInvokeCoolTick: motion.motionProperty.splashInvokeCoolTick)),
                        Attacks: motion.attack.Select((attack, attackIndex) => new SkillMetadataAttack(
                            Point: attack.point,
                            PointGroup: attack.pointGroupID,
                            TargetCount: TargetCount(id, level.value, motionIndex, attackIndex, attack.point, attack.targetCount),
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
                            Range: WithClientRange(id, level.value, motionIndex, attackIndex, attack.point, Convert(attack.rangeProperty)),
                            Arrow: new SkillMetadataArrow(
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
                                RayType: attack.arrowProperty.rayType),
                            Damage: new SkillMetadataDamage(
                                Count: attack.damageProperty.count,
                                Rate: DamageRate(id, level.value, motionIndex, attackIndex, attack.point, attack.damageProperty.rate),
                                HitSpeed: attack.damageProperty.hitSpeedRate,
                                HitDelay: attack.damageProperty.hitPauseTime,
                                IsConstDamage: attack.damageProperty.isConstDamageValue != 0,
                                Value: attack.damageProperty.value,
                                DamageByTargetMaxHp: attack.damageProperty.damageByTargetMaxHP,
                                SuperArmorBreak: attack.damageProperty.superArmorBreak,
                                Push: attack.damageProperty.push > 0 ? new SkillMetadataPush(
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
                                ) : null,
                                ConstDamageByTargetHp: attack.damageProperty.constDamageByTargetHP,
                                ConstDamageByTargetSp: attack.damageProperty.constDamageByTargetSP
                            ),
                            Skills: attack.conditionSkill.Where(skill => !skill.dependOnDamageCount).Select(skill => skill.Convert()).ToArray(),
                            SkillsOnDamage: attack.conditionSkill.Where(skill => skill.dependOnDamageCount).Select(skill => skill.Convert()).ToArray(),
                            GrabTargetType: attack.grabTargetType,
                            Direction: directions.Get(id, level.value, motionIndex, attackIndex),
                            AttackMethodType: AttackMethod(id, level.value, motionIndex, attackIndex, attack.point, (int) attack.attackMethodType),
                            SplashActiveSkillIds: SplashActive(id, level.value, motionIndex, attackIndex, attack.point)
                        )).ToArray(),
                        AttackPoints: motion.CollectAttackPoints()
                    )).ToArray(),
                    Upgrade: level.upgrade == null ? null : new SkillMetadataUpgrade(
                        Level: level.upgrade.level,
                        SkillIds: level.upgrade.skillIDs,
                        SkillLevels: level.upgrade.skillLevels,
                        QuestIds: level.upgrade.questIDs),
                    OnlyManualActiveSplash: level.splashManualActiveProperty?.onlyManualActiveSplash == true
                ));

            // The played client's effects of a level or attack that the data pack's rows lack (PlayedClientActionAdditionals).
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
                    SuperArmorApplyField: data.basic.stateAttr.superArmorApplyFieldType),
                Levels: levels
            );
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
        if (clientMotions.Count > 0) {
            Console.WriteLine($"Skill motions given the played client's sequence speed, move distance and splash life tick over the data pack's: {clientMotionsTaken} ({motionPointMismatches} with other attack points at that place, the data pack's kept)");
        }
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

    // The played client's sequence speed, move distance and splash life tick where it has the motion at that place with
    // the same attack points in the same order, else the data pack's; the rest of the motion is the data pack's.
    private SkillMetadataMotionProperty WithClientMotion(int id, short level, int motion, string points, SkillMetadataMotionProperty pack) {
        if (!clientMotions.TryGetValue((id, level, motion), out PlayedClientSkills.MotionTiming? played)) {
            return pack;
        }
        if (played.Points != points) {
            motionPointMismatches++;
            return pack;
        }
        SkillMetadataMotionProperty taken = pack with {
            SequenceSpeed = played.SequenceSpeed,
            MoveDistance = played.MoveDistance,
            SplashLifeTick = played.SplashLifeTick,
        };
        if (taken != pack) {
            clientMotionsTaken++;
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
