using Maple2.File.IO;
using Maple2.File.Ingest.Helpers;
using Maple2.File.Parser;
using Maple2.File.Parser.Xml.AdditionalEffect;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Tools.Extensions;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Maple2.File.Ingest.Mapper;

public class AdditionalEffectMapper : TypeMapper<AdditionalEffectMetadata> {
    private readonly AdditionalEffectParser parser;
    private readonly CooldownGroupRates groupRates;
    private readonly FinalStatusRows finals;
    private readonly ClientSpecialAbility specialAbility;
    private int finalClientLevels, finalPackLevels;
    private readonly FinalStatusRows.Counts finalCounts = new();

    // The played client's own invoke effects and effect groups (PlayedClientAdditionalEffects); empty without a played client.
    private readonly Dictionary<int, PlayedClientAdditionalEffects.Entry> played = [];

    public AdditionalEffectMapper(M2dReader xmlReader, M2dReader? playedReader = null) {
        parser = new AdditionalEffectParser(xmlReader);
        groupRates = new CooldownGroupRates(xmlReader);
        Console.WriteLine($"Additional effects with a cooldown group reduction: {groupRates.Count}");
        finals = new FinalStatusRows(xmlReader);
        specialAbility = new ClientSpecialAbility(playedReader, xmlReader);
        if (playedReader != null) {
            played = PlayedClientAdditionalEffects.Read(playedReader.Files.Where(file => PlayedClientAdditionalEffects.IsAdditionalFile(file.Name))
                .Select(file => playedReader.GetXmlDocument(file)), ClientListedRows.Available);
            Console.WriteLine($"Additional effects in the played client's data: {played.Count} ({played.Values.Count(entry => entry.GroupId != 0)} in an effect group, " +
                              $"{played.Values.Sum(entry => entry.Levels.Values.Count(invoke => invoke != null))} levels with an invoke effect)");
        }
    }

    // The played client's invoke effect stands in for the data pack's at every level the client has (none where its
    // level has none); the pack's is kept for a level the client does not have.
    private AdditionalEffectMetadataInvokeEffect? InvokeEffect(int id, short level, InvokeEffectProperty? pack) {
        if (played.TryGetValue(id, out PlayedClientAdditionalEffects.Entry? entry) && entry.Levels.TryGetValue(level, out AdditionalEffectMetadataInvokeEffect? invoke)) {
            return invoke;
        }
        return Convert(pack);
    }

    // The skillUseDisable mask of the played client's level (0 where its level has none); the data pack's for a
    // level the client does not have. The pack's masks differ (49018002: 12 where the client has 6; 71000051,
    // 71000068 and 71000074: 2 where the client has none), and the server refused casts the client sends.
    private int SkillUseDisableMask(int id, short level, int pack) {
        return played.TryGetValue(id, out PlayedClientAdditionalEffects.Entry? entry) && entry.SkillUseDisable.TryGetValue(level, out int mask) ? mask : pack;
    }

    protected override IEnumerable<AdditionalEffectMetadata> Map() {
        int packMasks = 0, masks = 0, changed = 0;
        var packIds = new HashSet<int>();
        foreach ((int id, IList<AdditionalEffectData> datas) in parser.Parse()) {
            packIds.Add(id);
            foreach (AdditionalEffectData data in datas) {
                int packMask = (int) data.MotionProperty.skillUseDisable;
                int mask = SkillUseDisableMask(id, data.BasicProperty.level, packMask);
                packMasks += packMask != 0 ? 1 : 0;
                masks += mask != 0 ? 1 : 0;
                changed += mask != packMask ? 1 : 0;
                yield return Build(id, data, mask);
            }
        }
        Console.WriteLine($"Additional effect levels with a skillUseDisable mask: {masks} ({packMasks} in the data pack, {changed} taken from the played client)");
        Console.WriteLine($"Additional effect levels with final stats: {finals.Count} ({finalClientLevels} from the played client's level, {finalPackLevels} from the data pack's rows; " +
                          $"client types without a server stat: {(specialAbility.Unmapped.Count == 0 ? "none" : string.Join(", ", specialAbility.Unmapped.Select(pair => $"{pair.Key} {pair.Value}")))}; " +
                          $"client rows dropped: {finalCounts.DroppedClientRows} not on a final stat's attribute, {finalCounts.DoubledClientRows} on an attribute the pack's level already has; " +
                          $"pack rows skipped: {finalCounts.StatusRows} StatusProperty ids; values cut to a whole number: {finalCounts.TruncatedValues})");

        // The played client's effects the data pack does not have, where every child of every level is one read in full
        // (PlayedClientAdditionalEffects.BasicOnly): 10600280, the charge Focused Fire 10600181 puts on its caster.
        int own = 0, ownLevels = 0;
        foreach ((int id, PlayedClientAdditionalEffects.Entry entry) in played.OrderBy(pair => pair.Key)) {
            if (packIds.Contains(id) || entry.OwnLevels == null) {
                continue;
            }
            own++;
            foreach (AdditionalEffectData data in PlayedOnly(entry.OwnLevels)) {
                ownLevels++;
                yield return Build(id, data, 0);
            }
        }
        Console.WriteLine($"Additional effects only the played client has, taken over: {own} ({ownLevels} levels; " +
                          $"{played.Count(pair => !packIds.Contains(pair.Key) && pair.Value.OwnLevels == null)} left out for a child the ingest does not read)");
    }

    // A pack row for each played level, read by the pack's serializer from a level with its <Basic> as the
    // BasicProperty, every other property element empty and an empty <stat> in <beginCondition> (no <RideeProperty>,
    // <ModifyOverlapCountProperty>: Build reads them absent as none).
    public static IEnumerable<AdditionalEffectData> PlayedOnly(IReadOnlyDictionary<short, System.Xml.XmlElement> levels) {
        var serializer = new System.Xml.Serialization.XmlSerializer(typeof(AdditionalEffectData), new System.Xml.Serialization.XmlRootAttribute("level"));
        string[] empty = typeof(AdditionalEffectData).GetFields()
            .Where(field => field.FieldType.IsClass && field.FieldType != typeof(string) && !field.FieldType.IsGenericType
                            && field.Name is not (nameof(AdditionalEffectData.BasicProperty) or nameof(AdditionalEffectData.RideeProperty)
                                or nameof(AdditionalEffectData.ModifyOverlapCountProperty)))
            .Select(field => field.Name)
            .ToArray();
        foreach ((short level, System.Xml.XmlElement row) in levels.OrderBy(pair => pair.Key)) {
            var document = new System.Xml.XmlDocument();
            System.Xml.XmlElement root = document.CreateElement("level");
            document.AppendChild(root);
            System.Xml.XmlElement basic = document.CreateElement(nameof(AdditionalEffectData.BasicProperty));
            if (row.ChildNodes.OfType<System.Xml.XmlElement>().FirstOrDefault(child => child.Name == "Basic") is { } played) {
                foreach (System.Xml.XmlAttribute attribute in played.Attributes) {
                    basic.SetAttribute(attribute.Name, attribute.Value);
                }
            }
            basic.SetAttribute("level", level.ToString());
            root.AppendChild(basic);
            foreach (string name in empty) {
                root.AppendChild(document.CreateElement(name));
            }
            root[nameof(AdditionalEffectData.beginCondition)]!.AppendChild(document.CreateElement("stat"));
            yield return (AdditionalEffectData) serializer.Deserialize(new System.Xml.XmlNodeReader(document))!;
        }
    }

    private AdditionalEffectMetadata Build(int id, AdditionalEffectData data, int mask) {
        return new AdditionalEffectMetadata(
            Id: id,
            Level: data.BasicProperty.level,
            Condition: data.beginCondition.Convert(),
            Property: new AdditionalEffectMetadataProperty(
                Type: (BuffType) data.BasicProperty.buffType,
                SubType: (BuffSubType) data.BasicProperty.buffSubType,
                Category: (BuffCategory) data.BasicProperty.buffCategory,
                EventType: (BuffEventType) data.BasicProperty.eventBuffType,
                Group: data.BasicProperty.group,
                DurationTick: data.BasicProperty.durationTick,
                IntervalTick: data.BasicProperty.intervalTick,
                DelayTick: data.BasicProperty.delayTick,
                MaxCount: data.BasicProperty.maxBuffCount,
                UseInGameTime: data.BasicProperty.useInGameTime,
                KeepOnDeath: data.BasicProperty.deadKeepEffect,
                RemoveOnLogout: data.BasicProperty.logoutClearEffect,
                RemoveOnLeaveField: data.BasicProperty.leaveFieldClearEffect,
                RemoveOnPvpZone: data.BasicProperty.clearEffectFromPVPZone,
                KeepOnEnterPvpZone: data.BasicProperty.doNotClearEffectFromEnterPVPZone,
                CasterIndividualBuff: data.BasicProperty.casterIndividualEffect,
                Exp: data.ExpProperty.value,
                CooldownTime: (int) TimeSpan.FromSeconds(float.TryParse(data.BasicProperty.coolDownTime, out float cooldown) ? cooldown : 0).TotalMilliseconds,
                RideId: data.RideeProperty == null! ? 0 : data.RideeProperty.rideeID,
                ImmuneBreak: data.MotionProperty.abnormalImmuneBreak,
                Stun: data.MotionProperty.stun,
                KeepCondition: (BuffKeepCondition) data.BasicProperty.keepCondition,
                ResetCondition: (BuffResetCondition) data.BasicProperty.resetCondition,
                DotCondition: (BuffDotCondition) data.BasicProperty.dotCondition,
                ClearOnDistanceFromCaster: int.TryParse(data.BasicProperty.clearDistanceFromCaster, out int distance) ? distance : 0,
                DisableRevivalHere: data.BasicProperty.disableRevivalHere,
                SkillUseDisableIds: data.MotionProperty.skillUseDisableIDs is { Length: > 0 } disabled ? disabled : null,
                CooldownGroupRates: groupRates.Get(id, data.BasicProperty.level),
                InvokeGroupId: played.TryGetValue(id, out PlayedClientAdditionalEffects.Entry? playedEntry) ? playedEntry.GroupId : 0,
                SkillUseDisableMask: mask,
                IgnoreReduceTime: data.BasicProperty.ignoreReduceTimeCondition,
                ClearCooldownOnPvpZone: data.BasicProperty.clearCooldownFromPVPZone,
                SuperArmor: data.MotionProperty.superArmor,
                SuperArmorApplyField: data.MotionProperty.superArmorApplyFieldType,
                ForceApplyTarget: data.BasicProperty.forceApplyTarget,
                AttackPossible: AttackPossible(data.BasicProperty),
                PvpOnOff: data.BasicProperty.pvpOnOff),
            Consume: new AdditionalEffectMetadataConsume(
                HpRate: data.ConsumeProperty.hpRate,
                SpRate: data.ConsumeProperty.spRate),
            Reflect: Convert(data.ReflectProperty),
            Update: Convert(data) with {
                RemoveRecharge = played.TryGetValue(id, out PlayedClientAdditionalEffects.Entry? removeEntry)
                                 && removeEntry.RemoveRecharge.TryGetValue(data.BasicProperty.level, out AdditionalEffectMetadataUpdate.RemoveRechargeUses? remove) ? remove : null,
            },
            Status: Status(id, data.BasicProperty.level, Convert(data.StatusProperty, data.OffensiveProperty, data.DefensiveProperty)),
            Recovery: Convert(data.RecoveryProperty),
            Dot: new AdditionalEffectMetadataDot(
                Damage: Convert(data.DotDamageProperty),
                Buff: Convert(data.DotBuffProperty)),
            Shield: Convert(data.ShieldProperty),
            InvokeEffect: InvokeEffect(id, data.BasicProperty.level, data.InvokeEffectProperty),
            ModifyOverlapCount: data.ModifyOverlapCountProperty == null! ? [] : data.ModifyOverlapCountProperty.effectCodes
                .Zip(data.ModifyOverlapCountProperty.offsetCounts)
                .Select(tuple => new AdditionalEffectMetadataModifyOverlapCount(
                    Id: tuple.First,
                    OffsetCount: tuple.Second))
                .ToArray(),
            Skills: data.conditionSkill.Concat(data.splashSkill).Where(skill => !skill.activeByIntervalTick).Select(skill => skill.Convert()).ToArray(),
            TickSkills: data.conditionSkill.Concat(data.splashSkill).Where(skill => skill.activeByIntervalTick).Select(skill => skill.Convert()).ToArray());
    }

    // null for an effect that names none of them: the holder takes every hit.
    private static AttackPossibleMetadata? AttackPossible(BasicProperty basic) {
        int[] skills = basic.attackPossibleSkillIDs ?? [];
        int[] dots = basic.attackPossibleDotEffectIDs ?? [];
        if (skills.Length == 0 && dots.Length == 0 && basic.attackPossibleEffectID == 0) {
            return null;
        }
        return new AttackPossibleMetadata(skills, dots, basic.attackPossibleEffectID);
    }

    // The final stats of a level (FinalStatusRows), added to its stats. The client has no final stats: it writes them
    // as SpecialAbility types of its level, and not in one way (finalmsprate -0.5 is msp -50, a flat value, in
    // 70002231; finalmsprate 0.5 is mspRate 500 in 73000001; finalpaprate and finalmaprate 0.252 are finalAttackPower
    // 252 in 89000002). So where the played client has the level, its rows on the final stats' attributes are added
    // (ClientSpecialAbility.BasicAttributes; finalAttackPower for finalpap/maprate, wapRate for finalwapmin/maxrate), and
    // none where its level has none (40500027: finalCapV 50000017, an id, where the client's level is empty).
    // Chosen here: for a level the client does not have (70002541, 120030062; 89000014-89000018, whose client rows are
    // Develop-only), each final rate is added as the stat's rate and each final value as its value.
    private AdditionalEffectMetadataStatus Status(int id, short level, AdditionalEffectMetadataStatus status) {
        IReadOnlyList<FinalStatusRows.Row>? rows = finals?.Get(id, level);
        if (rows == null) {
            return status;
        }
        var stats = new ClientSpecialAbility.Stats {
            Values = new Dictionary<BasicAttribute, long>(status.Values),
            Rates = new Dictionary<BasicAttribute, float>(status.Rates),
            SpecialValues = new Dictionary<SpecialAttribute, float>(status.SpecialValues),
            SpecialRates = new Dictionary<SpecialAttribute, float>(status.SpecialRates),
            PhysicalDamageTakenRate = status.PhysicalDamageTakenRate,
            MagicalDamageTakenRate = status.MagicalDamageTakenRate,
        };
        Dictionary<string, int>? client = played.TryGetValue(id, out PlayedClientAdditionalEffects.Entry? entry)
                                          && entry.SpecialAbilities.TryGetValue(level, out Dictionary<string, int>? found) ? found : null;
        if (FinalStatusRows.Apply(specialAbility, stats, rows, client, finalCounts)) {
            finalClientLevels++;
        } else {
            finalPackLevels++;
        }
        return status with {
            Values = stats.Values,
            Rates = stats.Rates,
            SpecialValues = stats.SpecialValues,
            SpecialRates = stats.SpecialRates,
            PhysicalDamageTakenRate = stats.PhysicalDamageTakenRate,
            MagicalDamageTakenRate = stats.MagicalDamageTakenRate,
        };
    }

    private static AdditionalEffectMetadataUpdate Convert(AdditionalEffectData data) {
        CancelEffectProperty cancel = data.CancelEffectProperty;
        AdditionalEffectMetadataUpdate.CancelEffect? cancelEffect = null;
        if (cancel.cancelEffectCodes.Length != 0 || cancel.cancelBuffCategories.Length != 0) {
            cancelEffect = new AdditionalEffectMetadataUpdate.CancelEffect(
                CheckSameCaster: cancel.cancelCheckSameCaster,
                PassiveEffect: cancel.cancelPassiveEffect,
                Ids: cancel.cancelEffectCodes,
                Categories: Array.ConvertAll(cancel.cancelBuffCategories, category => (BuffCategory) category));
        }

        ModifyEffectDurationProperty modify = data.ModifyEffectDurationProperty;
        var modifyDuration = new AdditionalEffectMetadataUpdate.ModifyDuration[modify.effectCodes.Length];
        for (int i = 0; i < modifyDuration.Length; i++) {
            modifyDuration[i] = new AdditionalEffectMetadataUpdate.ModifyDuration(modify.effectCodes[i], modify.durationFactors[i], modify.durationValues[i]);
        }

        return new AdditionalEffectMetadataUpdate(
            Cancel: cancelEffect,
            ImmuneIds: data.ImmuneEffectProperty.immuneEffectCodes,
            ImmuneCategories: Array.ConvertAll(data.ImmuneEffectProperty.immuneBuffCategories, category => (BuffCategory) category),
            ResetCooldown: data.ResetSkillCoolDownTimeProperty.skillCodes,
            Duration: modifyDuration);
    }

    private static AdditionalEffectMetadataReflect Convert(ReflectProperty reflect) {
        var values = new Dictionary<BasicAttribute, long>();
        var rates = new Dictionary<BasicAttribute, float>();

        values.AddIfNotDefault(BasicAttribute.PhysicalAtk, reflect.physicalReflectionValue);
        values.AddIfNotDefault(BasicAttribute.MagicalAtk, reflect.magicalReflectionValue);

        rates.AddIfNotDefault(BasicAttribute.PhysicalAtk, reflect.physicalReflectionRate);
        rates.AddIfNotDefault(BasicAttribute.MagicalAtk, reflect.magicalReflectionRate);
        return new AdditionalEffectMetadataReflect(
            Rate: reflect.reflectionRate,
            EffectId: reflect.reflectionAdditionalEffectId,
            EffectLevel: reflect.reflectionAdditionalEffectLevel,
            Count: reflect.reflectionCount,
            PhysicalRateLimit: reflect.physicalReflectionRateLimit,
            MagicalRateLimit: reflect.magicalReflectionRateLimit,
            Values: values,
            Rates: rates);
    }

    private static AdditionalEffectMetadataStatus Convert(StatusProperty status, OffensiveProperty offensive, DefensiveProperty defensive) {
        var values = new Dictionary<BasicAttribute, long>();
        var rates = new Dictionary<BasicAttribute, float>();
        var specialValues = new Dictionary<SpecialAttribute, float>();
        var specialRates = new Dictionary<SpecialAttribute, float>();

        if (status.Stat != null) {
            foreach (BasicAttribute attribute in Enum.GetValues<BasicAttribute>()) {
                values.AddIfNotDefault(attribute, status.Stat.Value((byte) attribute));
                rates.AddIfNotDefault(attribute, status.Stat.Rate((byte) attribute));
            }
        }

        if (status.SpecialAbility != null) {
            foreach (SpecialAttribute attribute in Enum.GetValues<SpecialAttribute>()) {
                byte attributeIndex = attribute.OptionIndex();
                if (attributeIndex == byte.MaxValue) {
                    continue;
                }

                specialValues.AddIfNotDefault(attribute, status.SpecialAbility.Value(attributeIndex));
                specialRates.AddIfNotDefault(attribute, status.SpecialAbility.Rate(attributeIndex));
            }
        }

        specialValues.AddIfNotDefault(SpecialAttribute.OffensiveMagicalDamage, offensive.mapDamageV);
        specialRates.AddIfNotDefault(SpecialAttribute.OffensiveMagicalDamage, offensive.mapDamageR);
        specialValues.AddIfNotDefault(SpecialAttribute.OffensivePhysicalDamage, offensive.papDamageV);
        specialRates.AddIfNotDefault(SpecialAttribute.OffensivePhysicalDamage, offensive.papDamageR);

        var resistances = new Dictionary<BasicAttribute, float>();
        resistances.AddIfNotDefault(BasicAttribute.MaxWeaponAtk, status.resWapR);
        resistances.AddIfNotDefault(BasicAttribute.BonusAtk, status.resBapR);
        resistances.AddIfNotDefault(BasicAttribute.CriticalDamage, status.resCadR);
        resistances.AddIfNotDefault(BasicAttribute.Accuracy, status.resAtpR);
        resistances.AddIfNotDefault(BasicAttribute.Evasion, status.resEvpR);
        resistances.AddIfNotDefault(BasicAttribute.Piercing, status.resPenR);
        resistances.AddIfNotDefault(BasicAttribute.AttackSpeed, status.resAspR);

        Debug.Assert(status.compulsionEventTypes.Length <= 1 && status.compulsionEventRate.Length <= 1);
        var compulsionEventType = BuffCompulsionEventType.None;
        if (status.compulsionEventTypes.Length > 0) {
            compulsionEventType = (BuffCompulsionEventType) status.compulsionEventTypes[0];
        }

        AdditionalEffectMetadataStatus.CompulsionEvent? compulsionEvent = null;
        if (compulsionEventType != BuffCompulsionEventType.None) {
            float compulsionEventRate = 0;
            if (status.compulsionEventRate.Length > 0) {
                compulsionEventRate = status.compulsionEventRate[0];
            }

            compulsionEvent = new AdditionalEffectMetadataStatus.CompulsionEvent(compulsionEventType, compulsionEventRate, status.compulsionEventSkillCodes);
        } else {
            // Ensure these fields are not set without a CompulsionEventType
            Debug.Assert(status.compulsionEventRate.Length == 0 && status.compulsionEventSkillCodes.Length == 0);
        }

        AdditionalEffectMetadataStatus.StatConversion? conversion = null;
        if (status.statChangeRate != 0) {
            conversion = new AdditionalEffectMetadataStatus.StatConversion((BasicAttribute) status.statChangeBase, (BasicAttribute) status.statChangeResult, status.statChangeRate);
        }

        return new AdditionalEffectMetadataStatus(
            Values: values,
            Rates: rates,
            SpecialValues: specialValues,
            SpecialRates: specialRates,
            Resistances: resistances,
            DeathResistanceHp: status.deathResistanceHP,
            Compulsion: compulsionEvent,
            Conversion: conversion,
            ImmuneBreak: offensive.hitImmuneBreak,
            Invincible: defensive.invincible != 0,
            InvincibleReflect: defensive.invincible == 2,
            PhysicalDamageTakenRate: defensive.papDamageR,
            MagicalDamageTakenRate: defensive.mapDamageR,
            ItemRecoveryRate: status.itemRecoveryHPRate,
            HealRecoveryRate: status.healRecoveryHPRate);
    }

    private static AdditionalEffectMetadataRecovery? Convert(RecoveryProperty recovery) {
        if (recovery is { RecoveryRate: <= 0, hpValue: <= 0, hpRate: <= 0, spValue: <= 0, spRate: <= 0, spConsumeRate: <= 0, epValue: <= 0, epRate: <= 0 }) {
            return null;
        }

        return new AdditionalEffectMetadataRecovery(
            RecoveryRate: recovery.RecoveryRate,
            HpValue: recovery.hpValue,
            HpRate: recovery.hpRate,
            HpConsumeRate: recovery.hpConsumeRate,
            SpValue: recovery.spValue,
            SpRate: recovery.spRate,
            SpConsumeRate: recovery.spConsumeRate,
            EpValue: recovery.epValue,
            EpRate: recovery.epRate,
            DisableCrit: recovery.disableCriticalRecovery);
    }

    private static AdditionalEffectMetadataDot.DotDamage? Convert(DotDamageProperty dotDamage) {
        if (dotDamage is { value: <= 0, rate: <= 0, damageByTargetMaxHP: <= 0, spValue: 0, epValue: 0 }) {
            return null;
        }

        return new AdditionalEffectMetadataDot.DotDamage(
            Type: (AttackType) dotDamage.type,
            Element: (Element) dotDamage.element,
            UseGrade: dotDamage.useGrade,
            Rate: dotDamage.rate,
            HpValue: (int) dotDamage.value,
            SpValue: dotDamage.spValue,
            EpValue: dotDamage.epValue,
            DamageByTargetMaxHp: dotDamage.damageByTargetMaxHP,
            RecoverHpByDamage: dotDamage.casterRecoveryHpByDamage,
            IsConstDamage: dotDamage.isConstDotDamageValue,
            NotKill: dotDamage.notKill);
    }

    private static AdditionalEffectMetadataDot.DotBuff? Convert(DotBuffProperty? dotBuff) {
        if (dotBuff is not { buffID: > 0 }) {
            return null;
        }

        return new AdditionalEffectMetadataDot.DotBuff(Target: (DotTargetType) dotBuff.target, Id: dotBuff.buffID, Level: dotBuff.buffLevel);
    }

    private static AdditionalEffectMetadataShield? Convert(ShieldProperty shield) {
        if (shield is { hpValue: <= 0, hpByTargetMaxHP: <= 0 }) {
            return null;
        }

        return new AdditionalEffectMetadataShield(HpValue: shield.hpValue, HpByTargetMaxHp: shield.hpByTargetMaxHP);
    }

    [return: NotNullIfNotNull(nameof(invokeEffect))]
    private static AdditionalEffectMetadataInvokeEffect? Convert(InvokeEffectProperty? invokeEffect) {
        if (invokeEffect is null) {
            return null;
        }

        return new AdditionalEffectMetadataInvokeEffect(
            Types: invokeEffect.types.Select(type => (InvokeEffectType) type).ToArray(),
            Values: invokeEffect.values,
            Rates: invokeEffect.rates,
            EffectId: invokeEffect.effectID,
            EffectGroupId: invokeEffect.effectGroupID,
            SkillId: invokeEffect.skillID,
            SkillGroupId: invokeEffect.skillGroupID);
    }
}
