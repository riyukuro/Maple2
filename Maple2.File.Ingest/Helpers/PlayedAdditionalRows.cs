using System.Globalization;
using System.Text.Json;
using System.Xml;
using Maple2.Model.Metadata;

namespace Maple2.File.Ingest.Helpers;

// The played client's own row of an additional effect level (additional/<nnn>.xml, AdditionalData::Load,
// additionalactionadditional.cpp:707) written as the data pack's row (additionaleffect/<id>.xml), so the pack's
// serializer and the mapper's one Build read it. The names were matched on the effects both data sets have:
// - <additional isDebuff mainType category skillGroupType>: <BasicProperty buffType> 2 for a debuff, buffSubType
//   1 << mainType, buffCategory, skillGroupType;
// - <Basic> and the children of the same class (SameClass): <BasicProperty>, <MotionProperty>, ... as they are;
// - <Condition>: <beginCondition>, its owner attributes on <owner>, its id lists as the pack's child rows (Condition);
// - <Additional>: a <conditionSkill> with its owner, caster and target tests in a <beginCondition> (Additional);
// - <Skill>: a <splashSkill splash="1">.
// What has no pack name is listed in unread; the mapper takes no effect with one over.
public static class PlayedAdditionalRows {
    // <X> to <XProperty>, the same attributes and children.
    public static readonly IReadOnlySet<string> SameClass = new HashSet<string> {
        "Basic", "Motion", "CancelEffect", "ImmuneEffect", "ResetSkillCoolDownTime", "ModifyEffectDuration", "ModifyOverlapCount",
        "FinalStatus", "Offensive", "Defensive", "Recovery", "Exp", "DotDamage", "DotBuff", "Consume", "Reflect", "UI", "Shield",
        "MesoGuard", "SpecialEffect", "Ridee",
    };

    // Read from the played rows elsewhere (PlayedClientAdditionalEffects): not written here.
    public static readonly IReadOnlySet<string> ReadElsewhere = new HashSet<string> { "InvokeEffect", "RemoveRechargingCount" };

    // The children an effect only the client has may have to be taken over: those whose mapped metadata the A/B
    // (AdditionalEffectMapper.Compare) found equal to the pack's on the effects both have wherever the client's own
    // two rows (additional/ and additionaleffect/) agree, and those the server does not read (UI, SpecialEffect,
    // MesoGuard) or reads from the played rows elsewhere. Out: <Skill>, as the pack's nonTargetActive (39 of 867
    // shared levels) is no attribute of the client's row; <Status> and <SpecialAbility>, not mapped.
    public static readonly IReadOnlySet<string> Gated = new HashSet<string> {
        "Basic", "Motion", "Condition", "Additional", "CancelEffect", "ImmuneEffect", "ResetSkillCoolDownTime",
        "ModifyEffectDuration", "ModifyOverlapCount", "Recovery", "Exp", "DotDamage", "DotBuff", "Consume", "Reflect", "Shield", "Ridee",
        "UI", "SpecialEffect", "MesoGuard", "InvokeEffect", "RemoveRechargingCount",
    };

    // Why the ingest does not take an effect over: each child not in Gated and each attribute with no pack name.
    public static IReadOnlyCollection<string> Unread(PlayedClientAdditionalEffects.Entry entry) {
        var unread = new SortedSet<string>(StringComparer.Ordinal);
        foreach ((short level, XmlElement row) in entry.Rows) {
            foreach (XmlElement child in row.ChildNodes.OfType<XmlElement>().Where(child => !Gated.Contains(child.Name))) {
                unread.Add(child.Name);
            }
            ToPack(entry.Effect!, row, level, unread);
        }
        return unread;
    }

    // The metadata each child of a played level makes, as JSON, for the A/B. SpecialAbility, Status, FinalStatus,
    // Offensive and Defensive all make Status.
    // Basic leaves out AttackPossible (attackPossible*) and PvpOnOff, which Build also reads from <BasicProperty>:
    // no KR level both data sets have differs on them (16 played levels set them, each as the pack does), so TakeOver
    // keeps the pack's; a locale where they differ would need them here and in TakeOver.
    public static Dictionary<string, string> Parts(AdditionalEffectMetadata metadata) {
        AdditionalEffectMetadataProperty property = metadata.Property;
        string status = Json(metadata.Status);
        return new Dictionary<string, string> {
            ["Basic"] = Json(new object[] {
                metadata.Level, property.Type, property.SubType, property.Category, property.EventType, property.Group, property.DurationTick,
                property.IntervalTick, property.DelayTick, property.MaxCount, property.UseInGameTime, property.KeepOnDeath, property.RemoveOnLogout,
                property.RemoveOnLeaveField, property.RemoveOnPvpZone, property.KeepOnEnterPvpZone, property.CasterIndividualBuff, property.CooldownTime,
                property.KeepCondition, property.ResetCondition, property.DotCondition, property.ClearOnDistanceFromCaster, property.DisableRevivalHere,
                property.IgnoreReduceTime, property.ClearCooldownOnPvpZone, property.ForceApplyTarget,
            }),
            ["Motion"] = Json(new object?[] { property.ImmuneBreak, property.Stun, property.SkillUseDisableIds, property.SuperArmor, property.SuperArmorApplyField }),
            ["Exp"] = Json(property.Exp),
            ["Ridee"] = Json(property.RideId),
            ["Condition"] = Json(metadata.Condition),
            ["Additional"] = Json(metadata.Skills.Concat(metadata.TickSkills).Where(skill => skill.Condition != null)),
            ["Skill"] = Json(metadata.Skills.Concat(metadata.TickSkills).Where(skill => skill.Splash != null)),
            ["CancelEffect"] = Json(metadata.Update.Cancel),
            ["ImmuneEffect"] = Json(new object[] { metadata.Update.ImmuneIds, metadata.Update.ImmuneCategories }),
            ["ResetSkillCoolDownTime"] = Json(metadata.Update.ResetCooldown),
            ["ModifyEffectDuration"] = Json(metadata.Update.Duration),
            ["Recovery"] = Json(metadata.Recovery),
            ["DotDamage"] = Json(metadata.Dot.Damage),
            ["DotBuff"] = Json(metadata.Dot.Buff),
            ["Shield"] = Json(metadata.Shield),
            ["Reflect"] = Json(metadata.Reflect),
            ["Consume"] = Json(metadata.Consume),
            ["ModifyOverlapCount"] = Json(metadata.ModifyOverlapCount),
            ["SpecialAbility"] = status,
            ["Status"] = status,
            ["FinalStatus"] = status,
            ["Offensive"] = status,
            ["Defensive"] = status,
        };
    }

    // The parts the ingest takes over on a level both data sets have (AdditionalEffectMapper.Map): what the played
    // client loads is its <additional> row (SkillDataTableCli::LoadAdditionalFile, skilldatatablecli.cpp:354), the
    // pack's rows (and the client's own legacy additionaleffect/ files, never read) are older. Out: Skill (the splash
    // skills), Status and the final stats, kept from the pack.
    public static readonly IReadOnlyList<string> TakenOver = [
        "Basic", "Motion", "Exp", "Ridee", "Condition", "Additional", "CancelEffect", "ImmuneEffect", "ResetSkillCoolDownTime",
        "ModifyEffectDuration", "Recovery", "DotDamage", "DotBuff", "Shield", "Reflect", "Consume", "ModifyOverlapCount",
    ];

    // The attributes of a played level with no pack name (ToPack's unread, "<child>.<attribute>"), as the part
    // whose metadata they would change: a part with one is not taken over (its mapped test would be wider).
    private static string? PartOf(string unread) {
        return unread.StartsWith("additional.", StringComparison.Ordinal) ? "Basic"
            : unread.StartsWith("Condition.", StringComparison.Ordinal) ? "Condition"
            : unread.StartsWith("Additional.", StringComparison.Ordinal) ? "Additional"
            : null;
    }

    // The children of a played level the takeover leaves to the pack by design: the splash skills and the final
    // stats (which the final-stat ingest reads from the client's FinalStatus itself). The status stays the pack's
    // too, but a played <Status> that differs from the pack's StatusProperty holds the level (Holding).
    public static readonly IReadOnlySet<string> KeptFromPack = new HashSet<string> { "Skill", "Status", "FinalStatus" };

    // The children of a played level the ingest does not read and the pack does not keep (Offensive, Defensive,
    // Spread, ...; SpecialAbility where it is not read, readsSpecialAbility, SpecialAbility below): the client runs them with the rest
    // of the row, so the row is taken whole or not at all. A <Status> holds where it and the pack's StatusProperty
    // (packRow, the pack's level as its serializer writes it) differ on an attribute (StatusDiffers): the client's
    // compulsion event of 10700193, 10700196 and 10700281, which the pack's rows lack, would be lost while the level
    // took the client's timing; a level without one holds where the pack's StatusProperty sets an attribute the
    // client's <Status> has (the client's level has none of it).
    public static IReadOnlyList<string> Holding(XmlElement row, XmlElement? packRow = null, bool readsSpecialAbility = false) {
        IEnumerable<string> holding = row.ChildNodes.OfType<XmlElement>()
            .Where(child => !Gated.Contains(child.Name) && !(readsSpecialAbility && child.Name == "SpecialAbility") && (!KeptFromPack.Contains(child.Name) || child.Name == "Status" && StatusDiffers(child, packRow)))
            .Select(child => child.Name);
        if (row["Status"] == null && StatusDiffers(null, packRow)) {
            holding = holding.Append("Status");
        }
        return holding.Distinct().Order(StringComparer.Ordinal).ToList();
    }

    // The attributes of the client's <Status> (AdditionalData::LoadLevel, additionalactionadditional.cpp:1211-1225);
    // the pack's StatusProperty has them under the same names (resAspR and statChange*, which it has too, the
    // client does not read).
    public static readonly IReadOnlySet<string> StatusRead = new HashSet<string>(StringComparer.Ordinal) {
        "itemRecoveryHPRate", "healRecoveryHPRate", "resWapR", "resBapR", "resCadR", "resAtpR", "resEvpR", "resPenR", "deathResistanceHP",
        "compulsionEventTypes", "compulsionEventRate", "compulsionEventSkillCodes",
    };

    // Whether a played <Status> (null for none) and the pack's StatusProperty differ: on an attribute the played row
    // has, or on one the client reads (StatusRead) the pack's has.
    private static bool StatusDiffers(XmlElement? status, XmlElement? packRow) {
        XmlElement? pack = packRow?["StatusProperty"];
        IEnumerable<string> names = (status?.Attributes.OfType<XmlAttribute>().Select(attribute => attribute.Name) ?? [])
            .Concat(pack?.Attributes.OfType<XmlAttribute>().Select(attribute => attribute.Name).Where(StatusRead.Contains) ?? []);
        return names.Any(name => !Numbers(status?.GetAttribute(name) ?? "").SequenceEqual(Numbers(pack?.GetAttribute(name) ?? "")));
    }

    // A list as the pack's parser reads it ("1,2", "0.1", "true"): numbers and flags (true 1, false 0) by value, other
    // text as it is; 0 for an empty list's lone value.
    private static IEnumerable<string> Numbers(string list) {
        string[] values = Split(list).Select(Value).ToArray();
        return values.Length == 1 && values[0] == "0" ? [] : values;
    }

    private static string Value(string value) {
        if (bool.TryParse(value, out bool flag)) {
            return flag ? "1" : "0";
        }
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
            ? number.ToString("R", CultureInfo.InvariantCulture) : value;
    }

    // TakeOver for a level whose played row is row: a level with a holding child stays the pack's whole until that
    // child is read (Shadow Stance 10800211 would lose its shield and last 60 s without the client's +30 physical
    // damage). taken still gets the parts that differ; holding gets the children that held them back. With the
    // client's SpecialAbility table (table; abilities, the level's <SpecialAbility> rows), its stats are read too
    // (SpecialAbility): "SpecialAbility" is taken where they differ from the pack's, or holds the level.
    public static AdditionalEffectMetadata TakeOver(AdditionalEffectMetadata pack, AdditionalEffectMetadata mapped, IEnumerable<string> unread, XmlElement row,
                                                    XmlElement? packRow, ICollection<string> taken, out IReadOnlyList<string> holding,
                                                    ClientSpecialAbility? table = null, IReadOnlyDictionary<string, int>? abilities = null, bool finalRows = false) {
        var hold = Holding(row, packRow, table != null).ToList();
        AdditionalEffectMetadata metadata = TakeOver(pack, mapped, unread, taken);
        if (table != null) {
            string? reason = SpecialAbility(table, abilities, pack.Status, finalRows, out AdditionalEffectMetadataStatus? status);
            if (status != null) {
                taken.Add("SpecialAbility");
                metadata = metadata with { Status = status };
            }
            if (reason != null) {
                hold.Add(reason);
            }
        }
        holding = hold.Distinct().Order(StringComparer.Ordinal).ToList();
        return holding.Count > 0 ? pack : metadata;
    }

    // The stats of a played level's <SpecialAbility> rows (abilities, type to value; none for none). The client
    // keeps a type's row where the table gives it a value type (the 4-byte field at +8 of a table row,
    // additionalactionadditional.cpp:1240, is SpecialAbilityInfo's m_nValueType, specialability.h:24) and drops the
    // rest; unmapped gets each type it keeps that has no server stat (ClientSpecialAbility.Add).
    public static ClientSpecialAbility.Stats SpecialAbilityStats(ClientSpecialAbility table, IReadOnlyDictionary<string, int>? abilities, ICollection<string> unmapped) {
        var stats = new ClientSpecialAbility.Stats();
        foreach ((string type, int value) in abilities ?? new Dictionary<string, int>()) {
            if (value != 0 && table.Rate(type) != null && !table.Add(stats, type, value)) {
                unmapped.Add(type);
            }
        }
        return stats;
    }

    // The client's <SpecialAbility> against the pack's stats (pack: the level's status as Build makes it, final stats
    // added): status gets the pack's with the client's stats in place of its own (values, rates, special values and
    // rates, damage taken rates, conversion; the resistances, compulsion, recovery rates, immune break and
    // invincible stay the pack's), null where they are the same. The reason a level holds instead, else null:
    // - "SpecialAbility": a type the client keeps that has no server stat;
    // - "SpecialAbility.FinalStatus": the level has final stats (finalRows), which the client writes as its
    //   SpecialAbility rows, not in one way (AdditionalEffectMapper.Status);
    // - "SpecialAbility.Pack": the pack has stats and the client's level none.
    public static string? SpecialAbility(ClientSpecialAbility table, IReadOnlyDictionary<string, int>? abilities, AdditionalEffectMetadataStatus pack, bool finalRows,
                                         out AdditionalEffectMetadataStatus? status) {
        var unmapped = new List<string>();
        ClientSpecialAbility.Stats client = SpecialAbilityStats(table, abilities, unmapped);
        status = null;
        if (SameStats(client, pack)) {
            return unmapped.Count > 0 ? "SpecialAbility" : null;
        }
        status = WithStats(pack, client);
        if (unmapped.Count > 0) {
            return "SpecialAbility";
        }
        if (finalRows) {
            return "SpecialAbility.FinalStatus";
        }
        return Empty(client) ? "SpecialAbility.Pack" : null;
    }

    // The status with the client's stats in place of its own.
    public static AdditionalEffectMetadataStatus WithStats(AdditionalEffectMetadataStatus status, ClientSpecialAbility.Stats stats) {
        return status with {
            Values = NonZero(stats.Values),
            Rates = NonZero(stats.Rates),
            SpecialValues = NonZero(stats.SpecialValues),
            SpecialRates = NonZero(stats.SpecialRates),
            PhysicalDamageTakenRate = stats.PhysicalDamageTakenRate,
            MagicalDamageTakenRate = stats.MagicalDamageTakenRate,
            Conversion = stats.Conversion,
        };
    }

    private static bool Empty(ClientSpecialAbility.Stats stats) {
        return NonZero(stats.Values).Count == 0 && NonZero(stats.Rates).Count == 0 && NonZero(stats.SpecialValues).Count == 0 && NonZero(stats.SpecialRates).Count == 0
               && Same(stats.PhysicalDamageTakenRate, 0) && Same(stats.MagicalDamageTakenRate, 0) && stats.Conversion == null;
    }

    private static bool SameStats(ClientSpecialAbility.Stats client, AdditionalEffectMetadataStatus pack) {
        return Same(client.Values, pack.Values) && Same(client.Rates, pack.Rates) && Same(client.SpecialValues, pack.SpecialValues)
               && Same(client.SpecialRates, pack.SpecialRates)
               && Same(client.PhysicalDamageTakenRate, pack.PhysicalDamageTakenRate) && Same(client.MagicalDamageTakenRate, pack.MagicalDamageTakenRate)
               && Same(client.Conversion, pack.Conversion);
    }

    private static bool Same(AdditionalEffectMetadataStatus.StatConversion? a, AdditionalEffectMetadataStatus.StatConversion? b) {
        if (a == null || b == null) {
            return a == null && b == null;
        }
        return a.BaseAttribute == b.BaseAttribute && a.ResultAttribute == b.ResultAttribute && Same(a.Rate, b.Rate);
    }

    private static Dictionary<TKey, long> NonZero<TKey>(IReadOnlyDictionary<TKey, long> values) where TKey : notnull {
        return values.Where(pair => pair.Value != 0).ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private static Dictionary<TKey, float> NonZero<TKey>(IReadOnlyDictionary<TKey, float> values) where TKey : notnull {
        return values.Where(pair => !Same(pair.Value, 0)).ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private static bool Same<TKey>(IReadOnlyDictionary<TKey, long> a, IReadOnlyDictionary<TKey, long> b) where TKey : notnull {
        return a.Keys.Concat(b.Keys).All(key => a.GetValueOrDefault(key) == b.GetValueOrDefault(key));
    }

    private static bool Same<TKey>(IReadOnlyDictionary<TKey, float> a, IReadOnlyDictionary<TKey, float> b) where TKey : notnull {
        return a.Keys.Concat(b.Keys).All(key => Same(a.GetValueOrDefault(key), b.GetValueOrDefault(key)));
    }

    // Floats the same to the client's thousandths (the pack's 0.03 is the client's 30 / 1000f).
    private static bool Same(float a, float b) => Math.Abs(a - b) <= 1e-5f * Math.Max(1f, Math.Max(Math.Abs(a), Math.Abs(b)));

    // The pack's metadata of a level with each TakenOver part that differs replaced by the played level's (mapped),
    // where none of the level's unread attributes is on that part. taken gets the parts replaced.
    public static AdditionalEffectMetadata TakeOver(AdditionalEffectMetadata pack, AdditionalEffectMetadata mapped, IEnumerable<string> unread, ICollection<string> taken) {
        Dictionary<string, string> before = Parts(pack);
        Dictionary<string, string> after = Parts(mapped);
        var blocked = unread.Select(PartOf).OfType<string>().ToHashSet();
        foreach (string part in TakenOver) {
            if (before[part] != after[part] && !blocked.Contains(part)) {
                taken.Add(part);
            }
        }
        if (taken.Count == 0) {
            return pack;
        }

        AdditionalEffectMetadataProperty property = pack.Property;
        AdditionalEffectMetadataProperty client = mapped.Property;
        if (taken.Contains("Basic")) {
            property = property with {
                Type = client.Type,
                SubType = client.SubType,
                Category = client.Category,
                EventType = client.EventType,
                Group = client.Group,
                DurationTick = client.DurationTick,
                IntervalTick = client.IntervalTick,
                DelayTick = client.DelayTick,
                MaxCount = client.MaxCount,
                UseInGameTime = client.UseInGameTime,
                KeepOnDeath = client.KeepOnDeath,
                RemoveOnLogout = client.RemoveOnLogout,
                RemoveOnLeaveField = client.RemoveOnLeaveField,
                RemoveOnPvpZone = client.RemoveOnPvpZone,
                KeepOnEnterPvpZone = client.KeepOnEnterPvpZone,
                CasterIndividualBuff = client.CasterIndividualBuff,
                CooldownTime = client.CooldownTime,
                KeepCondition = client.KeepCondition,
                ResetCondition = client.ResetCondition,
                DotCondition = client.DotCondition,
                ClearOnDistanceFromCaster = client.ClearOnDistanceFromCaster,
                DisableRevivalHere = client.DisableRevivalHere,
                IgnoreReduceTime = client.IgnoreReduceTime,
                ClearCooldownOnPvpZone = client.ClearCooldownOnPvpZone,
                ForceApplyTarget = client.ForceApplyTarget,
            };
        }
        if (taken.Contains("Motion")) {
            property = property with {
                ImmuneBreak = client.ImmuneBreak,
                Stun = client.Stun,
                SkillUseDisableIds = client.SkillUseDisableIds,
                SuperArmor = client.SuperArmor,
                SuperArmorApplyField = client.SuperArmorApplyField,
            };
        }
        if (taken.Contains("Exp")) {
            property = property with { Exp = client.Exp };
        }
        if (taken.Contains("Ridee")) {
            property = property with { RideId = client.RideId };
        }

        AdditionalEffectMetadataUpdate update = pack.Update;
        if (taken.Contains("CancelEffect")) {
            update = update with { Cancel = mapped.Update.Cancel };
        }
        if (taken.Contains("ImmuneEffect")) {
            update = update with { ImmuneIds = mapped.Update.ImmuneIds, ImmuneCategories = mapped.Update.ImmuneCategories };
        }
        if (taken.Contains("ResetSkillCoolDownTime")) {
            update = update with { ResetCooldown = mapped.Update.ResetCooldown };
        }
        if (taken.Contains("ModifyEffectDuration")) {
            update = update with { Duration = mapped.Update.Duration };
        }

        // The condition skills are the client's, the splash skills the pack's, condition skills first as Build has them.
        SkillEffectMetadata[] skills = pack.Skills, tickSkills = pack.TickSkills;
        if (taken.Contains("Additional")) {
            skills = mapped.Skills.Where(skill => skill.Condition != null).Concat(pack.Skills.Where(skill => skill.Condition == null)).ToArray();
            tickSkills = mapped.TickSkills.Where(skill => skill.Condition != null).Concat(pack.TickSkills.Where(skill => skill.Condition == null)).ToArray();
        }

        return pack with {
            Property = property,
            Condition = taken.Contains("Condition") ? mapped.Condition : pack.Condition,
            Update = update,
            Recovery = taken.Contains("Recovery") ? mapped.Recovery : pack.Recovery,
            Dot = new AdditionalEffectMetadataDot(
                Damage: taken.Contains("DotDamage") ? mapped.Dot.Damage : pack.Dot.Damage,
                Buff: taken.Contains("DotBuff") ? mapped.Dot.Buff : pack.Dot.Buff),
            Shield = taken.Contains("Shield") ? mapped.Shield : pack.Shield,
            Reflect = taken.Contains("Reflect") ? mapped.Reflect : pack.Reflect,
            Consume = taken.Contains("Consume") ? mapped.Consume : pack.Consume,
            ModifyOverlapCount = taken.Contains("ModifyOverlapCount") ? mapped.ModifyOverlapCount : pack.ModifyOverlapCount,
            Skills = skills,
            TickSkills = tickSkills,
        };
    }

    // The children that make the metadata's Status.
    public static readonly IReadOnlySet<string> StatusChildren = new HashSet<string> { "SpecialAbility", "Status", "FinalStatus", "Offensive", "Defensive" };

    // The children of a played level the A/B compares (Basic always: the <additional> row's attributes go there too).
    public static IEnumerable<string> PartsOf(XmlElement level) {
        return level.ChildNodes.OfType<XmlElement>().Select(child => child.Name).Append("Basic").Distinct();
    }

    private static string Json(object? value) => JsonSerializer.Serialize(value);

    // <Condition> attributes the pack has on <beginCondition> itself.
    private static readonly IReadOnlySet<string> ConditionTop = new HashSet<string> {
        "applyPvPZoneType", "allowDeadState", "allowBattleRidingState", "onlyBattleRidingState", "onlyShadowWorld", "requireDurationWithoutMove",
    };

    // <Condition> attributes the pack has on <beginCondition><owner>.
    private static readonly IReadOnlySet<string> ConditionOwner = new HashSet<string> {
        "hasBuffID", "hasBuffCount", "hasBuffLevel", "hasNotBuffID", "checkActivateBuff", "eventCondition", "ignoreOwnerEvent",
        "requireStates", "requireSubStates", "requireMasteryTypes", "requireMasteryValues",
        "targetCheckRange", "targetCheckMinRange", "targetFriendly", "targetCountSign", "targetInRangeCount",
    };

    // <Condition> id lists the pack has as one child row per id: <requireMapCodes code/>, ...
    private static readonly IReadOnlyDictionary<string, (string Element, string Attribute)> ConditionLists = new Dictionary<string, (string, string)> {
        ["fieldIDs"] = ("requireMapCodes", "code"),
        ["usedSkillIDs"] = ("requireSkillCodes", "code"),
        ["jobCode"] = ("job", "code"),
        ["dungeonGroupType"] = ("requireDungeonRoomGroupTypes", "type"),
        ["mapCategory"] = ("requireMapCategoryCodes", "code"),
        ["mapContinent"] = ("requireMapContinentCodes", "code"),
    };

    // <Additional> attributes the pack has on <conditionSkill> under the same name.
    private static readonly IReadOnlySet<string> AdditionalSame = new HashSet<string> {
        "level", "skillID", "skillTarget", "skillOwner", "randomCast", "linkSkillID", "activeByIntervalTick",
    };

    // <Additional> attributes the pack has in the <conditionSkill>'s <beginCondition>: (sub element or "" for the
    // condition itself, attribute).
    private static readonly IReadOnlyDictionary<string, (string Element, string Attribute)> AdditionalCondition = new Dictionary<string, (string, string)> {
        ["probability"] = ("", "probability"),
        ["isAllowDead"] = ("", "allowDeadState"),
        ["ownerEvent"] = ("owner", "eventCondition"),
        ["ownerEventIgnore"] = ("owner", "ignoreOwnerEvent"),
        ["ownerEventSkillID"] = ("owner", "eventSkillID"),
        ["ownerEventNoSkillID"] = ("owner", "eventIgnoreSkillID"),
        ["ownerEventAdditionalID"] = ("owner", "eventEffectID"),
        ["ownerAdditionalID"] = ("owner", "hasBuffID"),
        ["ownerAdditionalLevel"] = ("owner", "hasBuffLevel"),
        ["ownerAdditionalCount"] = ("owner", "hasBuffCount"),
        ["ownerAdditionalMine"] = ("owner", "hasBuffOwner"),
        ["ownerNoAdditionalID"] = ("owner", "hasNotBuffID"),
        ["casterAdditionalID"] = ("caster", "hasBuffID"),
        ["casterAdditionalCount"] = ("caster", "hasBuffCount"),
        ["casterEvent"] = ("caster", "eventCondition"),
        ["targetAdditionalID"] = ("target", "hasBuffID"),
        ["targetAdditionalMine"] = ("target", "hasBuffOwner"),
        ["targetNoAdditionalID"] = ("target", "hasNotBuffID"),
        ["targetNpcIDs"] = ("target", "NpcIDs"),
    };

    // <Additional> hp and sp thresholds the pack has as a <compareStat>: (sub element, stat, func).
    private static readonly IReadOnlyDictionary<string, (string Element, string Stat, string Func)> AdditionalStat = new Dictionary<string, (string, string, string)> {
        ["ownerHpOver"] = ("owner", "hp", "Greater"),
        ["ownerSpLess"] = ("owner", "sp", "Less"),
        ["targetHpLess"] = ("target", "hp", "LessEquals"),
    };

    // The client's <Condition casterHpOp> names to the pack's func names.
    private static string Func(string op) => op switch {
        "GreaterEqual" => "GreaterEquals",
        "LessEqual" => "LessEquals",
        _ => op,
    };

    // The pack's <level> row of a played level. unread gets "<child>" for a child with no pack row and
    // "<child>.<attribute>" for an attribute with no pack name.
    public static XmlElement ToPack(XmlElement additional, XmlElement level, short levelId, ICollection<string> unread) {
        var document = new XmlDocument();
        XmlElement root = document.CreateElement("level");
        document.AppendChild(root);

        XmlElement basic = document.CreateElement("BasicProperty");
        if (additional.GetAttribute("isDebuff") == "1") {
            basic.SetAttribute("buffType", "2");
        }
        // No mainType is the client's BuffMainType_None: the pack's rows have no buffSubType either (3487 of the 3741
        // shared effects with none; 252 have 1, a bit the server does not test).
        if (int.TryParse(additional.GetAttribute("mainType"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int mainType)) {
            basic.SetAttribute("buffSubType", (1 << mainType).ToString(CultureInfo.InvariantCulture));
        }
        if (additional.HasAttribute("category")) {
            basic.SetAttribute("buffCategory", additional.GetAttribute("category"));
        }
        // skillGroupType keeps its name on <BasicProperty> (no server read); any other attribute has no pack name.
        if (additional.HasAttribute("skillGroupType")) {
            basic.SetAttribute("skillGroupType", additional.GetAttribute("skillGroupType"));
        }
        foreach (XmlAttribute attribute in additional.Attributes) {
            if (attribute.Name is not ("id" or "isDebuff" or "mainType" or "category" or "skillGroupType")) {
                unread.Add("additional." + attribute.Name);
            }
        }
        root.AppendChild(basic);

        foreach (XmlElement child in level.ChildNodes.OfType<XmlElement>()) {
            switch (child.Name) {
                case "Basic":
                    CopyAttributes(child, basic);
                    break;
                case "Condition":
                    root.AppendChild(Condition(document, child, unread));
                    break;
                case "Additional":
                    root.AppendChild(Additional(document, child, unread));
                    break;
                case "Skill": {
                        XmlElement splash = document.CreateElement("splashSkill");
                        CopyAttributes(child, splash);
                        splash.SetAttribute("splash", "1");
                        // AdditionalActionSkill::Load (additionalactionadditional.cpp:681) reads chainDistance and no chain
                        // flag; the pack has chain="1" with each chainDistance.
                        if (child.HasAttribute("chainDistance")) {
                            splash.SetAttribute("chain", "1");
                        }
                        root.AppendChild(splash);
                        break;
                    }
                default:
                    if (SameClass.Contains(child.Name)) {
                        root.AppendChild(document.ImportNode(Renamed(child, child.Name + "Property"), true));
                    } else if (!ReadElsewhere.Contains(child.Name)) {
                        unread.Add(child.Name);
                    }
                    break;
            }
        }
        basic.SetAttribute("level", levelId.ToString(CultureInfo.InvariantCulture));
        return root;
    }

    // The pack's names the played rows write (ToPack over every played level): "<element>/.../" for an element and
    // "<element>/.../@<attribute>" for an attribute, the root's children at "<element>/".
    public static IReadOnlySet<string> Written(IEnumerable<PlayedClientAdditionalEffects.Entry> entries) {
        var written = new HashSet<string>(StringComparer.Ordinal);
        foreach (PlayedClientAdditionalEffects.Entry entry in entries.Where(entry => entry.Effect != null)) {
            foreach ((short level, XmlElement row) in entry.Rows) {
                Collect(ToPack(entry.Effect!, row, level, new List<string>()), "", written);
            }
        }
        return written;
    }

    private static void Collect(XmlElement element, string path, ISet<string> written) {
        foreach (XmlAttribute attribute in element.Attributes) {
            written.Add(path + "@" + attribute.Name);
        }
        foreach (XmlElement child in element.ChildNodes.OfType<XmlElement>()) {
            written.Add(path + child.Name + "/");
            Collect(child, path + child.Name + "/", written);
        }
    }

    // The pack's names the client's additional effect schema has no name for (AdditionalData::LoadLevel and its
    // loaders, additionalactionadditional.cpp; LoadCondition at :1577 and SkillHasBuffCondition::Load,
    // skilloverlapcompare.cpp:323), so a played row never writes them: "<element>/.../" for an element and
    // "<element>/.../@<attribute>" for an attribute, the root's children at "<element>/".
    public static readonly IReadOnlySet<string> PackOnly = new HashSet<string>(StringComparer.Ordinal) {
        // The serializer's namespaces and the pack's feature tag.
        "@xmlns:xsd", "@xmlns:xsi", "@feature",
        // The status and final stats are built from the pack's and the client's rows apart (Build, FinalStatusRows);
        // the invoke effect is the client's own (PlayedClientAdditionalEffects); the client has no meso guard.
        "StatusProperty/", "FinalStatusProperty/", "InvokeEffectProperty/", "MesoGuardProperty/",
        // The client's <Defensive> reads only invincible.
        "DefensiveProperty/@mapDamageR", "DefensiveProperty/@papDamageR",
        // The client's <Motion> has none of these.
        "MotionProperty/@IgnoreOptimalHideInvokeEffect", "MotionProperty/@IgnoreOptimalHideKeepEffect", "MotionProperty/@IgnoreOptimalHideStopEffect",
        "MotionProperty/@holdWeapon", "MotionProperty/@useFixedZRotation",
        // The client's <Condition> and the condition skills' conditions: the client tests a buff's count with its
        // overlap condition (targetCheckRange ... CountSign) and its has-buff lists by id only (SkillHasBuffCondition
        // reads no level or count compare; SkillOverlapCompare has no caller outside its own file).
        "beginCondition/@allowMapleSurvival", "beginCondition/@gender", "beginCondition/@invokeEffectFactor", "beginCondition/@level",
        "beginCondition/@probability", "beginCondition/stat/",
        "beginCondition/caster/@hasBuffCountCompare", "beginCondition/caster/@hasBuffLevel", "beginCondition/caster/@hasSkillLevel",
        "beginCondition/owner/@hasBuffCountCompare", "beginCondition/owner/@hasSkillLevel",
        "beginCondition/target/@eventCondition", "beginCondition/target/@hasBuffCountCompare", "beginCondition/target/@hasSkillLevel",
        "conditionSkill/@fireCount", "conditionSkill/@overlapCount",
        "conditionSkill/beginCondition/@allowMapleSurvival", "conditionSkill/beginCondition/@gender",
        "conditionSkill/beginCondition/@invokeEffectFactor", "conditionSkill/beginCondition/@level", "conditionSkill/beginCondition/stat/",
        "conditionSkill/beginCondition/caster/@hasBuffCountCompare", "conditionSkill/beginCondition/caster/@hasNotBuffID",
        "conditionSkill/beginCondition/caster/@hasSkillLevel",
        "conditionSkill/beginCondition/owner/@hasBuffCountCompare", "conditionSkill/beginCondition/owner/@hasSkillLevel",
        "conditionSkill/beginCondition/target/@hasBuffCountCompare", "conditionSkill/beginCondition/target/@hasBuffLevel",
        "conditionSkill/beginCondition/target/@hasSkillLevel",
        // The splash skills are the pack's (TakeOver keeps them); these are the client's splash rows have not.
        "splashSkill/@nonTargetActive", "splashSkill/@overlapCount", "splashSkill/beginCondition/",
    };

    // What the client has no name for keeps the pack's value: each attribute and element of the pack's row (pack,
    // written back by the pack's serializer) on PackOnly that the played level's row (played, ToPack's) lacks is
    // copied onto it, so a part that differs only there is not taken and a part taken keeps it (the pack's
    // beginCondition allowMapleSurvival: the client's <Condition> has no such attribute, and the item buffs with it
    // would no longer start on a Maple Survival map). A beginCondition (the level's, or a condition skill's) or one
    // of its sub conditions the played row has not is made for it; a condition skill only where the played row's
    // skill at the same place is the same skill; no other element is made. kept gets each name copied whose value is
    // not empty, 0 or false; unlisted (where given) each pack attribute with such a value neither on PackOnly nor in
    // written (the names the played rows write, Written), which a locale's data would need on the list.
    public static void KeepPackOnly(XmlElement pack, XmlElement played, ICollection<string> kept, IReadOnlySet<string>? written = null, ICollection<string>? unlisted = null) {
        Keep(pack, () => played, "", kept, written, unlisted);
    }

    private static void Keep(XmlElement pack, Func<XmlElement?> played, string path, ICollection<string> kept, IReadOnlySet<string>? written, ICollection<string>? unlisted) {
        foreach (XmlAttribute attribute in pack.Attributes) {
            string name = path + "@" + attribute.Name;
            if (!PackOnly.Contains(name)) {
                if (written != null && !written.Contains(name) && !Default(attribute.Value)) {
                    unlisted?.Add(name);
                }
                continue;
            }
            if (played() is { } target && !target.HasAttribute(attribute.Name)) {
                target.SetAttribute(attribute.Name, attribute.Value);
                if (!Default(attribute.Value)) {
                    kept.Add(name);
                }
            }
        }
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        HashSet<string>? had = null;
        foreach (XmlElement child in pack.ChildNodes.OfType<XmlElement>().ToList()) {
            int place = index.GetValueOrDefault(child.Name);
            index[child.Name] = place + 1;
            string sub = path + child.Name + "/";
            if (PackOnly.Contains(sub)) {
                if (played() is { } target) {
                    had ??= target.ChildNodes.OfType<XmlElement>().Select(element => element.Name).ToHashSet(StringComparer.Ordinal);
                    if (had.Contains(child.Name)) {
                        continue;
                    }
                    target.AppendChild(target.OwnerDocument.ImportNode(child, true));
                    if (child.HasAttributes || child.HasChildNodes) {
                        kept.Add(sub);
                    }
                }
                continue;
            }
            XmlElement? found = null;
            bool looked = false;
            Keep(child, () => {
                if (!looked) {
                    looked = true;
                    found = Place(played(), child, place, sub);
                }
                return found;
            }, sub, kept, written, unlisted);
        }
    }

    private static bool Default(string value) => value is "" or "0" or "false" or "False";

    // The played row's element for the pack's child at place among its kind, made where it may be.
    private static XmlElement? Place(XmlElement? parent, XmlElement child, int place, string path) {
        if (parent == null) {
            return null;
        }
        XmlElement? found = parent.ChildNodes.OfType<XmlElement>().Where(element => element.Name == child.Name).ElementAtOrDefault(place);
        if (child.Name == "conditionSkill") {
            return found != null && found.GetAttribute("skillID") == child.GetAttribute("skillID") ? found : null;
        }
        if (found != null || !path.Contains("beginCondition/", StringComparison.Ordinal)) {
            return found;
        }
        return place == 0 ? (XmlElement) parent.AppendChild(parent.OwnerDocument.CreateElement(child.Name))! : null;
    }

    private static XmlElement Condition(XmlDocument document, XmlElement condition, ICollection<string> unread) {
        XmlElement begin = document.CreateElement("beginCondition");
        foreach (XmlAttribute attribute in condition.Attributes) {
            string name = attribute.Name;
            if (ConditionTop.Contains(name)) {
                begin.SetAttribute(name, attribute.Value);
            } else if (ConditionOwner.Contains(name)) {
                Sub(begin, "owner").SetAttribute(name, attribute.Value);
            } else if (ConditionLists.TryGetValue(name, out (string Element, string Attribute) list)) {
                foreach (string value in Split(attribute.Value)) {
                    XmlElement row = document.CreateElement(list.Element);
                    row.SetAttribute(list.Attribute, value);
                    begin.AppendChild(row);
                }
            } else if (name is "equipHandL" or "equipHandR") {
                // One <weapon lh rh> row: the client's left and right hand item types (0 for a hand not tested).
                XmlElement weapon = begin["weapon"] ?? (XmlElement) begin.AppendChild(document.CreateElement("weapon"))!;
                weapon.SetAttribute("lh", condition.HasAttribute("equipHandL") ? condition.GetAttribute("equipHandL") : "0");
                weapon.SetAttribute("rh", condition.HasAttribute("equipHandR") ? condition.GetAttribute("equipHandR") : "0");
            } else if (name == "targetAdditionalID") {
                Sub(begin, "target").SetAttribute("hasBuffID", attribute.Value);
            } else if (name == "casterAdditionalID") {
                Sub(begin, "caster").SetAttribute("hasBuffID", attribute.Value);
            } else if (name == "casterAdditionalCount") {
                Sub(begin, "caster").SetAttribute("hasBuffCount", attribute.Value);
            } else if (name == "casterHpRate") {
                XmlElement stat = document.CreateElement("compareStat");
                stat.SetAttribute("hp", attribute.Value);
                stat.SetAttribute("func", Func(condition.GetAttribute("casterHpOp")));
                Sub(begin, "caster").AppendChild(stat);
            } else if (name != "casterHpOp") {
                unread.Add("Condition." + name);
            }
        }
        // <owner>, <caster>, <target>: the pack's sub conditions, the same attributes and <compareStat> rows.
        foreach (XmlElement child in condition.ChildNodes.OfType<XmlElement>()) {
            if (child.Name is not ("owner" or "caster" or "target")) {
                unread.Add("Condition." + child.Name);
                continue;
            }
            XmlElement sub = Sub(begin, child.Name);
            CopyAttributes(child, sub);
            foreach (XmlNode node in child.ChildNodes.OfType<XmlElement>()) {
                sub.AppendChild(document.ImportNode(node, true));
            }
        }
        return begin;
    }

    private static XmlElement Additional(XmlDocument document, XmlElement additional, ICollection<string> unread) {
        XmlElement skill = document.CreateElement("conditionSkill");
        XmlElement begin = document.CreateElement("beginCondition");
        skill.AppendChild(begin);
        foreach (XmlAttribute attribute in additional.Attributes) {
            string name = attribute.Name;
            if (AdditionalSame.Contains(name)) {
                skill.SetAttribute(name, attribute.Value);
            } else if (AdditionalCondition.TryGetValue(name, out (string Element, string Attribute) target)) {
                (target.Element.Length == 0 ? begin : Sub(begin, target.Element)).SetAttribute(target.Attribute, attribute.Value);
            } else if (AdditionalStat.TryGetValue(name, out (string Element, string Stat, string Func) compare)) {
                XmlElement stat = document.CreateElement("compareStat");
                stat.SetAttribute(compare.Stat, attribute.Value);
                stat.SetAttribute("func", compare.Func);
                Sub(begin, compare.Element).AppendChild(stat);
            } else if (name == "targetNpcOnly") {
                // The pack's <target hasSkillID="1">; the server does not read it.
                Sub(begin, "target").SetAttribute("hasSkillID", attribute.Value);
            } else {
                // ownerHpLess (the pack has Less and LessEquals for it), ownerRange*, targetAdditionalCount,
                // casterNoAdditionalID: no pack name.
                unread.Add("Additional." + name);
            }
        }
        return skill;
    }

    private static XmlElement Sub(XmlElement begin, string name) {
        return begin[name] ?? (XmlElement) begin.AppendChild(begin.OwnerDocument.CreateElement(name))!;
    }

    private static void CopyAttributes(XmlElement from, XmlElement to) {
        foreach (XmlAttribute attribute in from.Attributes) {
            to.SetAttribute(attribute.Name, attribute.Value);
        }
    }

    private static XmlElement Renamed(XmlElement element, string name) {
        XmlElement renamed = element.OwnerDocument.CreateElement(name);
        CopyAttributes(element, renamed);
        foreach (XmlNode node in element.ChildNodes) {
            renamed.AppendChild(node.CloneNode(true));
        }
        return renamed;
    }

    private static IEnumerable<string> Split(string list) {
        return list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
