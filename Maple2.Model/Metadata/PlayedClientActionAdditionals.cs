using System.Globalization;
using System.Xml;
using Maple2.Model.Enum;

namespace Maple2.Model.Metadata;

// The effects the played client's skill data (skilldata/<nnn>.xml) gives a skill's level and its attacks: the
// <actionAdditional> rows of a <level> and of each <motion>'s <attack>, which the client loads into the skill's
// SkillActionAdditional list (m_vAdditional). The data pack's skill/ data gives most of them as conditionSkill rows
// with the same effect id and level; the ingest adds the ones it does not (SkillMapper).
// Only the beginCondition fields seen with these rows are read (allowBattleRidingState, onlyBattleRidingState,
// probability); a row whose
// condition has any other field is counted as unsupported and left out. useTargetCountFactor (probability scaled by
// the targets hit) has no reader in the client source, so it is not taken over: the plain probability is kept.
public static class PlayedClientActionAdditionals {
    public readonly record struct Use(int EffectId, short Level, int Target, int Owner, bool OnDamage, int OverlapCount, int LinkSkillId,
                                      bool AllowOnBattleMount, float Probability, bool OnlyOnBattleMount = false);

    // An attack's uses, with the attack's point.
    public record AttackUses(string Point, Use[] Uses);

    // The level's own uses, and each attack's by motion index and the attack's place in the motion (the client keeps
    // them per <attack>, SkillAttackData::m_vAdditional).
    public record LevelUses(Use[] Level, IReadOnlyDictionary<(int Motion, int Attack), AttackUses> Attacks);

    private static readonly HashSet<string> ConditionFields = ["allowBattleRidingState", "onlyBattleRidingState", "probability", "useTargetCountFactor"];

    public static Dictionary<(int SkillId, short Level), LevelUses> Read(IEnumerable<XmlDocument> skillFiles, out int unsupported) {
        unsupported = 0;
        var result = new Dictionary<(int, short), LevelUses>();
        foreach (XmlDocument file in skillFiles) {
            XmlNodeList? skills = file.SelectNodes("ms2/skill");
            if (skills == null) {
                continue;
            }
            foreach (XmlElement skill in skills.OfType<XmlElement>()) {
                if (!int.TryParse(skill.GetAttribute("id"), out int id) || id == 0) {
                    continue;
                }
                foreach (XmlElement level in Children(skill, "level")) {
                    if (!short.TryParse(level.GetAttribute("value"), out short value) || result.ContainsKey((id, value))) {
                        continue;
                    }
                    Use[] own = Uses(level, ref unsupported);
                    var attacks = new Dictionary<(int, int), AttackUses>();
                    int motionIndex = 0;
                    foreach (XmlElement motion in Children(level, "motion")) {
                        int attackIndex = 0;
                        foreach (XmlElement attack in Children(motion, "attack")) {
                            Use[] uses = Uses(attack, ref unsupported);
                            if (uses.Length > 0) {
                                attacks[(motionIndex, attackIndex)] = new AttackUses(attack.GetAttribute("point"), uses);
                            }
                            attackIndex++;
                        }
                        motionIndex++;
                    }
                    if (own.Length > 0 || attacks.Count > 0) {
                        result[(id, value)] = new LevelUses(own, attacks);
                    }
                }
            }
        }
        return result;
    }

    // The level with the client's uses its rows lack added: to the level's own effects, and to the attack at the same
    // place of the same motion (to its on-damage effects for a dependOnDamageCount use), or, when the attack there has
    // another point, to the motion's first attack with the use's point. A use is added only for an effect id and level
    // none of the level's rows give, anywhere in the level (an effect the data pack gives on one attack is not added
    // to another), and only for one the data pack has (the client data names effects that are in no effect file, e.g.
    // 10200102..10200108 for 10200101). Unmatched counts the attack uses whose motion has no attack with their point.
    public static SkillMetadataLevel Merge(SkillMetadataLevel level, LevelUses uses, Func<int, short, bool> effectExists, out int added, out int unmatched) {
        HashSet<(int, short)> given = level.Skills
            .Concat(level.Motions.SelectMany(motion => motion.Attacks).SelectMany(attack => attack.Skills.Concat(attack.SkillsOnDamage)))
            .SelectMany(effect => effect.Skills).Select(skill => (skill.Id, skill.Level)).ToHashSet();
        Use[] Missing(IEnumerable<Use> candidates) => candidates.Where(use => effectExists(use.EffectId, use.Level) && given.Add((use.EffectId, use.Level))).ToArray();

        Use[] own = Missing(uses.Level);
        int count = own.Length;
        // The pack attack each client attack's uses go to, in the client's attack order.
        var placed = new Dictionary<(int, int), List<Use>>();
        int unplaced = 0;
        foreach (((int motion, int attack), AttackUses attackUses) in uses.Attacks.OrderBy(entry => entry.Key)) {
            SkillMetadataAttack[] packAttacks = motion < level.Motions.Length ? level.Motions[motion].Attacks : [];
            int index = attack < packAttacks.Length && packAttacks[attack].Point == attackUses.Point
                ? attack
                : Array.FindIndex(packAttacks, other => other.Point == attackUses.Point);
            if (index < 0) {
                unplaced += attackUses.Uses.Length;
                continue;
            }
            if (!placed.TryGetValue((motion, index), out List<Use>? list)) {
                placed[(motion, index)] = list = [];
            }
            list.AddRange(attackUses.Uses);
        }
        SkillMetadataMotion[] motions = level.Motions.Select((motion, motionIndex) => motion with {
            Attacks = motion.Attacks.Select((attack, attackIndex) => {
                if (!placed.TryGetValue((motionIndex, attackIndex), out List<Use>? attackUses)) {
                    return attack;
                }
                Use[] extra = Missing(attackUses);
                if (extra.Length == 0) {
                    return attack;
                }
                count += extra.Length;
                return attack with {
                    Skills = [.. attack.Skills, .. extra.Where(use => !use.OnDamage).Select(ToEffect)],
                    SkillsOnDamage = [.. attack.SkillsOnDamage, .. extra.Where(use => use.OnDamage).Select(ToEffect)],
                };
            }).ToArray(),
        }).ToArray();
        added = count;
        unmatched = unplaced;
        return count == 0 ? level : level with { Skills = [.. level.Skills, .. own.Select(ToEffect)], Motions = motions };
    }

    // The effect row the server applies for a use, as the ingest makes one of a conditionSkill (TriggerSkill.Convert).
    public static SkillEffectMetadata ToEffect(Use use) {
        var owner = System.Enum.IsDefined((SkillTargetType) use.Owner) && use.Owner > 0 ? (SkillTargetType) use.Owner : SkillTargetType.Owner;
        var condition = new BeginCondition(Level: 1, Mesos: 0, Gender: Gender.All, JobCode: [], Probability: use.Probability, CooldownTime: 0,
            DurationWithoutDamage: 0, DurationWithoutMoving: 0, OnlyShadowWorld: false, OnlyFlyableMap: false, AllowDead: false,
            AllowOnBattleMount: use.AllowOnBattleMount, OnlyOnBattleMount: use.OnlyOnBattleMount, AllowOnSurvival: false, DungeonGroupType: [],
            Stat: new Dictionary<BasicAttribute, long>(), Maps: [], MapTypes: [], Continents: [], ActiveSkill: [], Weapon: [],
            Target: null, Owner: null, Caster: null);
        return new SkillEffectMetadata(
            FireCount: 1,
            Skills: [new SkillEffectMetadata.Skill(use.EffectId, use.Level, use.LinkSkillId)],
            Condition: new SkillEffectMetadataCondition(condition, owner, (SkillTargetType) use.Target, use.OverlapCount, RandomCast: false),
            Splash: null);
    }

    private static Use[] Uses(XmlElement parent, ref int unsupported) {
        var uses = new List<Use>();
        foreach (XmlElement row in Children(parent, "actionAdditional")) {
            int effectId = Number(row, "additionalID");
            if (effectId == 0) {
                continue;
            }
            XmlElement? condition = Children(row, "beginCondition").FirstOrDefault();
            if (condition != null && (condition.ChildNodes.OfType<XmlElement>().Any() || condition.Attributes.OfType<XmlAttribute>().Any(attribute => !ConditionFields.Contains(attribute.Name)))) {
                unsupported++;
                continue;
            }
            string link = row.GetAttribute("linkSkillID");
            uses.Add(new Use(
                EffectId: effectId,
                Level: (short) Number(row, "level", 1),
                Target: Number(row, "skillTarget"),
                Owner: Number(row, "skillOwner", 1),
                OnDamage: Flag(row, "dependOnDamageCount"),
                OverlapCount: Number(row, "overlapCount", 1),
                LinkSkillId: int.TryParse(link.Split(',')[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int linkId) ? linkId : 0,
                AllowOnBattleMount: condition != null && Flag(condition, "allowBattleRidingState"),
                Probability: condition != null && float.TryParse(condition.GetAttribute("probability"), NumberStyles.Float, CultureInfo.InvariantCulture, out float rate) ? rate : 1f,
                OnlyOnBattleMount: condition != null && Flag(condition, "onlyBattleRidingState")));
        }
        return uses.ToArray();
    }

    private static IEnumerable<XmlElement> Children(XmlElement parent, string name) =>
        parent.ChildNodes.OfType<XmlElement>().Where(child => child.Name == name);

    // The client reads these flags with TiXmlElement::QueryBoolAttribute (XmlGetBool, SkillBeginCondition::Load): a
    // value starting with "true", "yes" or "1" in any case is set, any other value is not.
    private static bool Flag(XmlElement element, string attribute) => QueryBool(element.GetAttribute(attribute));

    public static bool QueryBool(string text) =>
        text.StartsWith("true", StringComparison.OrdinalIgnoreCase) || text.StartsWith("yes", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith('1');

    private static int Number(XmlElement element, string attribute, int fallback = 0) {
        string text = element.GetAttribute(attribute);
        return text.Length > 0 && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;
    }
}
