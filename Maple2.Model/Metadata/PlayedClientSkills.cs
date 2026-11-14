using System.Globalization;
using System.Numerics;
using System.Xml;
using Maple2.Model.Enum;

namespace Maple2.Model.Metadata;

// What the played client's own skill data (skilldata/<nnn>.xml) says of a skill where the server's data pack says
// nothing or something else. The client reads these from the first <basic> of each <skill> and its <stateAttr>
// (SkillData load, commonfiles/skilloverlapcompare.cpp:817):
// - mainType: the server pack's skill data has none. The client takes the caster off the ride before a cast of a
//   skill of the main types in UnrideMainType, whatever its unrideOnUse says (SkillData::IsUnrideOnUse).
// - rechargeMaxCount: the uses the client lets out before it stops casting (CSkillSystem::GetRechargeRemainCount).
// - rechargeShareOrigin with originID: the skill counts its uses on its origin skill (SkillData::GetRechargeId).
// - the <weapon lh rh> rows of <condition>: the hand weapons the client casts the skill with (SkillCondition::Load,
//   CSkillSystem::FUN_14176a8c0). The data pack's beginCondition differs for two skills (11000146, 10100061).
// - attackMethodType of each <attack> (SkillAttackData::Load, 0 when not given): the data pack gives it for 2 skills
//   only. The client's search of an attack of type 1 takes neither the range offset nor its z rotation
//   (SkillLevelData::FUN_1408043f0); see ReadAttackMethods.
public static partial class PlayedClientSkills {
    public const string SkillFolder = "skilldata/";

    public readonly record struct Entry(int MainType, int RechargeMaxCount, bool RechargeShared, (int LeftHand, int RightHand)[] Weapons,
        int SplashMaxCount = 0);

    public static bool IsSkillFile(string name) => name.StartsWith(SkillFolder, StringComparison.OrdinalIgnoreCase);

    public static Dictionary<int, Entry> Read(IEnumerable<XmlDocument> skillFiles) {
        var entries = new Dictionary<int, Entry>();
        foreach ((int id, (XmlElement skill, _)) in SelectSkills(skillFiles)) {
            XmlElement? basic = skill.ChildNodes.OfType<XmlElement>().FirstOrDefault(child => child.Name == "basic");
            XmlElement? state = basic?.ChildNodes.OfType<XmlElement>().FirstOrDefault(child => child.Name == "stateAttr");
            int origin = Number(skill, "originID");
            XmlElement? condition = basic?.ChildNodes.OfType<XmlElement>().FirstOrDefault(child => child.Name == "condition");
            (int, int)[] weapons = condition?.ChildNodes.OfType<XmlElement>().Where(child => child.Name == "weapon")
                .Select(weapon => (Number(weapon, "lh"), Number(weapon, "rh"))).ToArray() ?? [];
            entries[id] = new Entry(
                MainType: Number(basic, "mainType"),
                RechargeMaxCount: Number(state, "rechargeMaxCount"),
                RechargeShared: Number(state, "rechargeShareOrigin") != 0 && origin != 0 && origin != id,
                Weapons: weapons,
                SplashMaxCount: Number(state, "splashMaxCount"));
        }
        return entries;
    }

    // The client's pick of the <skill> elements and their <level> elements: SkillDataTable::LoadSkillFile adds every
    // <skill> of each file and AddSkill (commonfiles/skilloverlapcompare.cpp:1295) replaces an id it has, so the last
    // <skill> of an id is the one kept. SkillData::Load (skilloverlapcompare.cpp:882) takes its levels through
    // FUN_14028faa0 (commonutil.cpp:1747): a <level> the feature or locale does not let in is skipped, and of two with
    // the same value text the one select picks takes the place (the first one is kept without select).
    public static Dictionary<int, (XmlElement Skill, List<(short Value, XmlElement Level)> Levels)> SelectSkills(
            IEnumerable<XmlDocument> skillFiles, Func<XmlElement, bool>? available = null,
            Func<XmlElement, XmlElement, XmlElement>? select = null) {
        var skillsById = new Dictionary<int, XmlElement>();
        foreach (XmlDocument file in skillFiles) {
            XmlNodeList? skills = file.SelectNodes("ms2/skill");
            if (skills == null) {
                continue;
            }
            foreach (XmlElement skill in skills.OfType<XmlElement>()) {
                if (int.TryParse(skill.GetAttribute("id"), out int id) && id != 0) {
                    skillsById[id] = skill;
                }
            }
        }
        var result = new Dictionary<int, (XmlElement, List<(short, XmlElement)>)>();
        foreach ((int id, XmlElement skill) in skillsById) {
            var slots = new List<XmlElement>();
            var slotByKey = new Dictionary<string, int>();
            foreach (XmlElement level in skill.ChildNodes.OfType<XmlElement>().Where(child => child.Name == "level")) {
                if (available != null && !available(level)) {
                    continue;
                }
                string key = level.GetAttribute("value");
                if (slotByKey.TryGetValue(key, out int slot)) {
                    if (select != null && select(slots[slot], level) == level) {
                        slots[slot] = level;
                    }
                    continue;
                }
                slotByKey[key] = slots.Count;
                slots.Add(level);
            }
            var levels = new List<(short, XmlElement)>();
            foreach (XmlElement level in slots) {
                if (short.TryParse(level.GetAttribute("value"), out short value)) {
                    levels.Add((value, level));
                }
            }
            result[id] = (skill, levels);
        }
        return result;
    }

    // The attackMethodType of every attack of the client's levels, by skill id, level, the motion's place in the level
    // and the attack's place in the motion (0 kept, so that a level the client has overrides the data pack's), with
    // the attack's point, so the ingest takes it only for the data pack's attack of the same point. The skills and
    // levels are the client's pick (SelectSkills).
    public static Dictionary<(int Skill, short Level, int Motion, int Attack), (int Method, string Point)> ReadAttackMethods(
            IEnumerable<XmlDocument> skillFiles, Func<XmlElement, bool>? available = null, Func<XmlElement, XmlElement, XmlElement>? select = null) {
        var methods = new Dictionary<(int, short, int, int), (int, string)>();
        foreach ((int id, (_, List<(short, XmlElement)> levels)) in SelectSkills(skillFiles, available, select)) {
            foreach ((short value, XmlElement level) in levels) {
                int motionIndex = 0;
                foreach (XmlElement motion in level.ChildNodes.OfType<XmlElement>().Where(child => child.Name == "motion")) {
                    int attackIndex = 0;
                    foreach (XmlElement attack in motion.ChildNodes.OfType<XmlElement>().Where(child => child.Name == "attack")) {
                        methods[(id, value, motionIndex, attackIndex++)] = (Number(attack, "attackMethodType"), attack.GetAttribute("point"));
                    }
                    motionIndex++;
                }
            }
        }
        return methods;
    }

    // The splashActiveSkillID list of every attack of the client's levels that has one (SkillAttackData::Load,
    // skillattackdata.cpp:597, a comma list), by skill id, level, the motion's place in the level and the attack's
    // place in the motion, with the attack's point. The data pack's skill data has no such attribute. The skills and
    // levels are the client's pick (SelectSkills).
    public static Dictionary<(int Skill, short Level, int Motion, int Attack), (int[] Ids, string Point)> ReadSplashActive(
            IEnumerable<XmlDocument> skillFiles, Func<XmlElement, bool>? available = null, Func<XmlElement, XmlElement, XmlElement>? select = null) {
        var result = new Dictionary<(int, short, int, int), (int[], string)>();
        foreach ((int id, (_, List<(short, XmlElement)> levels)) in SelectSkills(skillFiles, available, select)) {
            foreach ((short value, XmlElement level) in levels) {
                int motionIndex = 0;
                foreach (XmlElement motion in level.ChildNodes.OfType<XmlElement>().Where(child => child.Name == "motion")) {
                    int attackIndex = 0;
                    foreach (XmlElement attack in motion.ChildNodes.OfType<XmlElement>().Where(child => child.Name == "attack")) {
                        int[] ids = attack.GetAttribute("splashActiveSkillID")
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Select(text => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int skillId) ? skillId : 0)
                            .Where(skillId => skillId > 0)
                            .ToArray();
                        if (ids.Length > 0) {
                            result[(id, value, motionIndex, attackIndex)] = (ids, attack.GetAttribute("point"));
                        }
                        attackIndex++;
                    }
                    motionIndex++;
                }
            }
        }
        return result;
    }

    // The damage rate of every attack of the client's levels, by skill id, level, the motion's place in the level and
    // the attack's place in the motion, with the attack's point (SkillDamage::Load reads the rate of the attack's
    // <damage>, skillattackdata.cpp:258; 0 when the attack has no <damage> or no rate, SkillDamage::SkillDamage). The
    // server takes an attack by its place in the motion (SkillRecord.Attack), as the client sends it. The skills and
    // levels are the client's pick (SelectSkills).
    public static Dictionary<(int Skill, short Level, int Motion, int Attack), (float Rate, string Point)> ReadAttackDamageRates(
            IEnumerable<XmlDocument> skillFiles, Func<XmlElement, bool>? available = null, Func<XmlElement, XmlElement, XmlElement>? select = null) {
        var result = new Dictionary<(int, short, int, int), (float, string)>();
        foreach ((int id, (_, List<(short, XmlElement)> levels)) in SelectSkills(skillFiles, available, select)) {
            foreach ((short value, XmlElement level) in levels) {
                int motionIndex = 0;
                foreach (XmlElement motion in level.ChildNodes.OfType<XmlElement>().Where(child => child.Name == "motion")) {
                    int attackIndex = 0;
                    foreach (XmlElement attack in motion.ChildNodes.OfType<XmlElement>().Where(child => child.Name == "attack")) {
                        XmlElement? damage = attack.ChildNodes.OfType<XmlElement>().FirstOrDefault(child => child.Name == "damage");
                        result[(id, value, motionIndex, attackIndex++)] = (Real(damage, "rate"), attack.GetAttribute("point"));
                    }
                    motionIndex++;
                }
            }
        }
        return result;
    }

    // The hit shape (rangeType, distance, height, width, endWidth, rangeAdd, rangeOffset, rangeZRotateDegree) and the
    // target count of every attack of the client's levels, by skill id, level, the motion's place in the level and the
    // attack's place in the motion, with the attack's point. Unk_RangeShape::Load reads the shape from the attack's
    // <range> (unit_1408001f0, :67-84; an empty type and 0 when not given, a vector only as three comma-separated
    // values) and SkillAttackData::Load reads targetCount (skillattackdata.cpp:577, 0 when not given). The skills and
    // levels are the client's pick (SelectSkills).
    public static Dictionary<(int Skill, short Level, int Motion, int Attack), AttackRange> ReadAttackRanges(
            IEnumerable<XmlDocument> skillFiles, Func<XmlElement, bool>? available = null, Func<XmlElement, XmlElement, XmlElement>? select = null) {
        var result = new Dictionary<(int, short, int, int), AttackRange>();
        foreach ((int id, (_, List<(short, XmlElement)> levels)) in SelectSkills(skillFiles, available, select)) {
            foreach ((short value, XmlElement level) in levels) {
                int motionIndex = 0;
                foreach (XmlElement motion in level.ChildNodes.OfType<XmlElement>().Where(child => child.Name == "motion")) {
                    int attackIndex = 0;
                    foreach (XmlElement attack in motion.ChildNodes.OfType<XmlElement>().Where(child => child.Name == "attack")) {
                        XmlElement? range = attack.ChildNodes.OfType<XmlElement>().FirstOrDefault(child => child.Name == "range");
                        result[(id, value, motionIndex, attackIndex++)] = new AttackRange(range?.GetAttribute("rangeType") ?? "",
                            Real(range, "distance"), Real(range, "height"), Real(range, "width"), Real(range, "endWidth"),
                            Point(range, "rangeAdd"), Point(range, "rangeOffset"), Real(range, "rangeZRotateDegree"),
                            Number(attack, "targetCount"), attack.GetAttribute("point"));
                    }
                    motionIndex++;
                }
            }
        }
        return result;
    }

    public record AttackRange(string Type, float Distance, float Height, float Width, float EndWidth, Vector3 RangeAdd,
        Vector3 RangeOffset, float RotateZDegree, int TargetCount, string Point);

    // The data pack's range with the client's whole hit shape. CSkillCollisionManager builds the shape from the type,
    // distance, height, width and end width (skillcollisionmanager.cpp:147-208) and StateSkill::FUN_1417e95a0 places it
    // by the range offset (stateskill.cpp:1167), so no part of the shape is kept from the data pack. The target
    // properties (includeCaster, applyTarget, castTarget, the target select and filters) stay the data pack's.
    public static SkillMetadataRange WithShape(SkillMetadataRange pack, AttackRange played) {
        return pack with {
            Type = ShapeType(played.Type),
            Distance = played.Distance,
            Height = played.Height,
            Width = played.Width,
            EndWidth = played.EndWidth,
            RotateZDegree = played.RotateZDegree,
            RangeAdd = played.RangeAdd,
            RangeOffset = played.RangeOffset,
        };
    }

    // The client's shape types (Unk_RangeShape::Load): any other rangeType, as "circle" or none, gives no shape
    // (CSkillCollisionManager makes none for it), so it hits nothing.
    public static SkillRegion ShapeType(string rangeType) {
        return rangeType switch {
            "box" => SkillRegion.Box,
            "cylinder" => SkillRegion.Cylinder,
            "frustum" => SkillRegion.Frustum,
            "hole_cylinder" => SkillRegion.HoleCylinder,
            _ => SkillRegion.None,
        };
    }

    // The attack's target count: the client's where it gives one, else the data pack's. The client cuts an attack's
    // targets to its targetCount (StateSkill::PickAttackTargets, stateskill.cpp:2585-2586, through
    // CSkillSystem::FUN_141769410, skillslot.cpp:2877), so 0 picks none; the KR client gives 0 only in 10800202 (Dark
    // Swamp's pulses, which hit nothing there while 10800204, a skill the data pack does not have, deals the damage),
    // and taking it alone would leave the server's swamp without its effect.
    public static int WithTargetCount(int pack, int played) => played != 0 ? played : pack;

    // The sequence name, sequence speed, move distance, splash life tick and key move of every motion of the client's
    // levels, by skill id, level and the motion's place in the level, with the points of its attacks in order and the
    // number of motions in the level. SkillMotionData::Load reads them from the <motion> (skillleveldata.cpp:1161, 1162,
    // 1177, 1192, 1189; "", 1, 0, 0 and 0 when not given, SkillMotionData's ctor). The skills and levels are the
    // client's pick (SelectSkills).
    public static Dictionary<(int Skill, short Level, int Motion), MotionTiming> ReadMotionTimings(
            IEnumerable<XmlDocument> skillFiles, Func<XmlElement, bool>? available = null, Func<XmlElement, XmlElement, XmlElement>? select = null) {
        var result = new Dictionary<(int, short, int), MotionTiming>();
        foreach ((int id, (_, List<(short, XmlElement)> levels)) in SelectSkills(skillFiles, available, select)) {
            foreach ((short value, XmlElement level) in levels) {
                int motionIndex = 0;
                XmlElement[] motions = level.ChildNodes.OfType<XmlElement>().Where(child => child.Name == "motion").ToArray();
                foreach (XmlElement motion in motions) {
                    string points = string.Join(",", motion.ChildNodes.OfType<XmlElement>().Where(child => child.Name == "attack")
                        .Select(attack => attack.GetAttribute("point")));
                    result[(id, value, motionIndex++)] = new MotionTiming(motion.GetAttribute("sequenceName"), Real(motion, "sequenceSpeed", 1f),
                        Real(motion, "movedistance"), Number(motion, "splashLifeTick"), points, motions.Length, Real(motion, "keyMove"));
                }
            }
        }
        return result;
    }

    public record MotionTiming(string SequenceName, float SequenceSpeed, float MoveDistance, int SplashLifeTick, string Points, int Motions,
        float KeyMove = 0);

    // A client motion pairs with the data pack's at the same place when the motion has the same attack points in order
    // and either both levels have as many motions or both name the same sequence there: a motion put in or taken out
    // before it shifts every place after it, and a motion without attacks matches on its points alone, so points alone
    // do not pair. Differing names do not refuse a pair of the same count, as the client's name is the one it plays (KR
    // 11000145 and 11000146 play Striker_GigantikBlow1_A where the data pack names another sequence or none).
    public static bool SameMotion(MotionTiming played, int motions, string sequenceName, string points) =>
        played.Points == points && (played.Motions == motions || (played.SequenceName.Length > 0 && played.SequenceName == sequenceName));

    // The sequence a paired motion plays: the client's, also where it names none. A level whose motions name no
    // sequence (SkillLevelData::HasSequenceName) is cast without the skill state and fires its first attack at once
    // (MyPC::vfunc_102 -> StateSkill::FireFirstAttack), which the npc cast does too (SkillCastRules.StartOf), so the
    // data pack's name would play an animation and wait for keys the client never has (KR 50100036 and 50100480).
    public static string SequenceName(MotionTiming played, string pack) => played.SequenceName;

    // The push of every attack of the client's levels, by skill id, level, the motion's place in the level and the
    // attack's place in the motion, with the attack's point. SkillDamage::Load reads it from the attack's <damage>
    // (skillattackdata.cpp:271-283; the probability is 1 and the rest 0 when not given, SkillDamage::SkillDamage). The
    // skills and levels are the client's pick (SelectSkills).
    public static Dictionary<(int Skill, short Level, int Motion, int Attack), (AttackPush Push, string Point)> ReadAttackPushes(
            IEnumerable<XmlDocument> skillFiles, Func<XmlElement, bool>? available = null, Func<XmlElement, XmlElement, XmlElement>? select = null) {
        var result = new Dictionary<(int, short, int, int), (AttackPush, string)>();
        foreach ((int id, (_, List<(short, XmlElement)> levels)) in SelectSkills(skillFiles, available, select)) {
            foreach ((short value, XmlElement level) in levels) {
                int motionIndex = 0;
                foreach (XmlElement motion in level.ChildNodes.OfType<XmlElement>().Where(child => child.Name == "motion")) {
                    int attackIndex = 0;
                    foreach (XmlElement attack in motion.ChildNodes.OfType<XmlElement>().Where(child => child.Name == "attack")) {
                        XmlElement? damage = attack.ChildNodes.OfType<XmlElement>().FirstOrDefault(child => child.Name == "damage");
                        var push = new AttackPush(Number(damage, "push"), PushEaseType(Number(damage, "pushEaseType")), Number(damage, "pushApplyField"),
                            Real(damage, "pushdistance"), Real(damage, "pushUpDistance"), Number(damage, "pushDown"), Number(damage, "pushFall") != 0,
                            Real(damage, "pushduration"), Real(damage, "pushprob", 1f), Number(damage, "pushPriority"),
                            Number(damage, "pushPriorityHitImmune"));
                        result[(id, value, motionIndex, attackIndex++)] = (push, attack.GetAttribute("point"));
                    }
                    motionIndex++;
                }
            }
        }
        return result;
    }

    public record AttackPush(int Type, int EaseType, int ApplyField, float Distance, float UpDistance, int Down, bool Fall, float Duration,
        float Probability, int Priority, int PriorityHitImmune);

    // The arrow of every attack of the client's levels, by skill id, level, the motion's place in the level and the
    // attack's place in the motion, with the attack's point. SkillArrow::Load reads it from the attack's <arrow>
    // (skillattackdata.cpp:99-121): overlap, explosion, rayPhysxTest, nonTarget, bounceType, bounceCount, bounceRadius,
    // bounceOverlap, collision, collisionAdd and rayType, each left at SkillArrow::SkillArrow's default when not given
    // (0, but 1 for bounceOverlap; a vector only as three comma-separated values, FUN_14028f060), and all of them when
    // the attack has no <arrow>. The skills and levels are the client's pick (SelectSkills).
    public static Dictionary<(int Skill, short Level, int Motion, int Attack), (AttackArrow Arrow, string Point)> ReadAttackArrows(
            IEnumerable<XmlDocument> skillFiles, Func<XmlElement, bool>? available = null, Func<XmlElement, XmlElement, XmlElement>? select = null) {
        var result = new Dictionary<(int, short, int, int), (AttackArrow, string)>();
        foreach ((int id, (_, List<(short, XmlElement)> levels)) in SelectSkills(skillFiles, available, select)) {
            foreach ((short value, XmlElement level) in levels) {
                int motionIndex = 0;
                foreach (XmlElement motion in level.ChildNodes.OfType<XmlElement>().Where(child => child.Name == "motion")) {
                    int attackIndex = 0;
                    foreach (XmlElement attack in motion.ChildNodes.OfType<XmlElement>().Where(child => child.Name == "attack")) {
                        XmlElement? arrow = attack.ChildNodes.OfType<XmlElement>().FirstOrDefault(child => child.Name == "arrow");
                        var taken = new AttackArrow(Number(arrow, "overlap"), Number(arrow, "explosion"), Number(arrow, "rayPhysxTest"),
                            Number(arrow, "nonTarget"), Number(arrow, "bounceType"), Number(arrow, "bounceCount"), Real(arrow, "bounceRadius"),
                            Number(arrow, "bounceOverlap", 1), Point(arrow, "collision"), Point(arrow, "collisionAdd"), Number(arrow, "rayType"));
                        result[(id, value, motionIndex, attackIndex++)] = (taken, attack.GetAttribute("point"));
                    }
                    motionIndex++;
                }
            }
        }
        return result;
    }

    public record AttackArrow(int Overlap, int Explosion, int RayPhysxTest, int NonTarget, int BounceType, int BounceCount, float BounceRadius,
        int BounceOverlap, Vector3 Collision, Vector3 CollisionAdd, int RayType);

    // The arrow an attack takes from the played client's where it has the attack at that place with the same point,
    // else null (the data pack's is kept). The bounce overlap is kept only for a bouncing arrow, as for the data pack's.
    public static SkillMetadataArrow? TakeArrow(AttackArrow arrow, string playedPoint, string point) {
        if (playedPoint != point) {
            return null;
        }
        return new SkillMetadataArrow(
            Overlap: arrow.Overlap != 0,
            Explosion: arrow.Explosion != 0,
            RayPhysXTest: arrow.RayPhysxTest != 0,
            NonTarget: (SkillTargetType) arrow.NonTarget,
            BounceType: (BounceType) arrow.BounceType,
            BounceCount: arrow.BounceCount,
            BounceRadius: arrow.BounceRadius,
            BounceOverlap: arrow.BounceType > 0 && arrow.BounceOverlap != 0,
            Collision: arrow.Collision,
            CollisionAdd: arrow.CollisionAdd,
            RayType: arrow.RayType);
    }

    // A push type other than 0 is a push: the client tests SkillDamage::GetPush against 0 for the hit reaction and
    // super armor (pc.cpp:902, 929), so -1 and 3 count though they move nothing (pcbase.cpp:4135-4172).
    public static bool IsPush(int type) => type != 0;

    // The client's push ease types are 0 to 2; any other value is 0 (skillattackdata.cpp:276-277).
    private static int PushEaseType(int value) => value is >= 0 and <= 2 ? value : 0;

    // The cooldown of each of the client's levels that gives one, in milliseconds, by skill id and level
    // (SkillLevelData::Load reads the level's cooldown attribute; SkillLevelData::GetCooldown is the length the
    // client's skill slot counts down and its tooltip shows). The data pack's beginCondition cooldownTime differs
    // for 95 KR skills. A level without the attribute is left out (the client counts 0 there). The skills and levels
    // are the client's pick (SelectSkills).
    public static Dictionary<(int Skill, short Level), int> ReadLevelCooldowns(IEnumerable<XmlDocument> skillFiles,
            Func<XmlElement, bool>? available = null, Func<XmlElement, XmlElement, XmlElement>? select = null) {
        var result = new Dictionary<(int, short), int>();
        foreach ((int id, (_, List<(short, XmlElement)> levels)) in SelectSkills(skillFiles, available, select)) {
            foreach ((short value, XmlElement level) in levels) {
                if (level.HasAttribute("cooldown")) {
                    result[(id, value)] = Number(level, "cooldown");
                }
            }
        }
        return result;
    }

    // The spirit and stamina cost of each of the client's levels, by skill id and level (SkillLevelData::Load reads
    // the level's sp and ep, 0 when not given, skillleveldata.cpp:926-927). The client checks the caster's spirit and
    // stamina against them before it sends a cast (CSkillSystem::FUN_14176a530, skillslot.cpp:3108). The data pack's
    // consume stat differs for 39 KR skills (469 levels). The skills and levels are the client's pick (SelectSkills).
    public static Dictionary<(int Skill, short Level), (int Sp, int Ep)> ReadLevelCosts(IEnumerable<XmlDocument> skillFiles,
            Func<XmlElement, bool>? available = null, Func<XmlElement, XmlElement, XmlElement>? select = null) {
        var result = new Dictionary<(int, short), (int, int)>();
        foreach ((int id, (_, List<(short, XmlElement)> levels)) in SelectSkills(skillFiles, available, select)) {
            foreach ((short value, XmlElement level) in levels) {
                result[(id, value)] = (Number(level, "sp"), Number(level, "ep"));
            }
        }
        return result;
    }

    // The consume stats with the client's level cost in place of the data pack's spirit and stamina (0 left out, as
    // the data pack's are); the other stats are kept. Null when they are the same.
    public static Dictionary<BasicAttribute, long>? WithLevelCost(IReadOnlyDictionary<BasicAttribute, long> stat, (int Sp, int Ep) cost) {
        if (stat.GetValueOrDefault(BasicAttribute.Spirit) == cost.Sp && stat.GetValueOrDefault(BasicAttribute.Stamina) == cost.Ep) {
            return null;
        }
        var result = new Dictionary<BasicAttribute, long>(stat);
        result.Remove(BasicAttribute.Spirit);
        result.Remove(BasicAttribute.Stamina);
        if (cost.Sp != 0) {
            result[BasicAttribute.Spirit] = cost.Sp;
        }
        if (cost.Ep != 0) {
            result[BasicAttribute.Stamina] = cost.Ep;
        }
        return result;
    }

    // The main types whose skills take the caster off the ride at the cast.
    public static bool UnrideMainType(int mainType) => mainType is 1 or 4 or 7 or (>= 11 and <= 15) or (>= 17 and <= 19);

    // TiXml's QueryDoubleAttribute (sscanf "%lf") cast to float: a missing attribute or one with no number in front
    // keeps the fallback, and the number in front is read alone ("1.5x" reads 1.5).
    private static float Real(XmlElement? element, string attribute, float fallback = 0f) {
        return element != null && element.HasAttribute(attribute) && ScanReal(element.GetAttribute(attribute), out double value)
            ? (float) value : fallback;
    }

    // FUN_14028f060 (commonutil.cpp:1172): the attribute's comma tokens, empty ones skipped as CString::Tokenize does,
    // each read by atof (0 with no number in front); anything but three tokens is the zero vector.
    private static Vector3 Point(XmlElement? element, string attribute) {
        string[] values = element?.GetAttribute(attribute).Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (values.Length != 3) {
            return Vector3.Zero;
        }
        var result = new float[3];
        for (int i = 0; i < 3; i++) {
            result[i] = ScanReal(values[i], out double value) ? (float) value : 0f;
        }
        return new Vector3(result[0], result[1], result[2]);
    }

    // TiXml's QueryIntAttribute (sscanf "%d"): a missing attribute or one with no digit in front keeps the fallback,
    // and the digits in front are read alone ("1.0" and " 1x" read 1).
    private static int Number(XmlElement? element, string attribute, int fallback = 0) {
        return element != null && element.HasAttribute(attribute) && ScanInt(element.GetAttribute(attribute), out int value) ? value : fallback;
    }

    // The number at the front of the text, after white space, as sscanf "%d" takes it: a sign and at least one digit.
    private static bool ScanInt(string text, out int value) {
        int start = SkipSpace(text);
        int end = start < text.Length && text[start] is '+' or '-' ? start + 1 : start;
        int digits = end;
        while (end < text.Length && char.IsAsciiDigit(text[end])) {
            end++;
        }
        value = 0;
        return end > digits && int.TryParse(text.AsSpan(start, end - start), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    // The number at the front of the text, after white space, as sscanf "%lf" and atof take it: a sign, digits with an
    // optional fraction (at least one digit) and an optional exponent.
    private static bool ScanReal(string text, out double value) {
        int start = SkipSpace(text);
        int end = start < text.Length && text[start] is '+' or '-' ? start + 1 : start;
        int digits = 0;
        while (end < text.Length && char.IsAsciiDigit(text[end])) {
            end++;
            digits++;
        }
        if (end < text.Length && text[end] == '.') {
            end++;
            while (end < text.Length && char.IsAsciiDigit(text[end])) {
                end++;
                digits++;
            }
        }
        value = 0;
        if (digits == 0) {
            return false;
        }
        if (end < text.Length && text[end] is 'e' or 'E') {
            int exponent = end + 1 < text.Length && text[end + 1] is '+' or '-' ? end + 2 : end + 1;
            int exponentEnd = exponent;
            while (exponentEnd < text.Length && char.IsAsciiDigit(text[exponentEnd])) {
                exponentEnd++;
            }
            if (exponentEnd > exponent) {
                end = exponentEnd;
            }
        }
        return double.TryParse(text.AsSpan(start, end - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    // The C locale's isspace.
    private static int SkipSpace(string text) {
        int index = 0;
        while (index < text.Length && text[index] is ' ' or '\t' or '\n' or '\v' or '\f' or '\r') {
            index++;
        }
        return index;
    }
}
