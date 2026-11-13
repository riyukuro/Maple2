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
