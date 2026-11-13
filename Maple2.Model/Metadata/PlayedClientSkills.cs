using System.Globalization;
using System.Xml;

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
public static class PlayedClientSkills {
    public const string SkillFolder = "skilldata/";

    public readonly record struct Entry(int MainType, int RechargeMaxCount, bool RechargeShared, (int LeftHand, int RightHand)[] Weapons);

    public static bool IsSkillFile(string name) => name.StartsWith(SkillFolder, StringComparison.OrdinalIgnoreCase);

    public static Dictionary<int, Entry> Read(IEnumerable<XmlDocument> skillFiles) {
        var entries = new Dictionary<int, Entry>();
        foreach (XmlDocument file in skillFiles) {
            XmlNodeList? skills = file.SelectNodes("ms2/skill");
            if (skills == null) {
                continue;
            }
            foreach (XmlNode node in skills) {
                if (node is not XmlElement skill || !int.TryParse(skill.GetAttribute("id"), out int id) || id == 0 || entries.ContainsKey(id)) {
                    continue;
                }
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
                    Weapons: weapons);
            }
        }
        return entries;
    }

    // The attackMethodType of every attack of the client's levels, by skill id, level, the motion's place in the level
    // and the attack's place in the motion (0 kept, so that a level the client has overrides the data pack's). The
    // first <skill> of an id and the first <level> of a value are read, as in Read.
    // Each attack's type with its point, so the ingest takes it only for the data pack's attack of the same point.
    // The first <level> of a value is kept; duplicateLevels counts the later ones (the client's FUN_14028faa0 picks
    // among them by feature, which this does not do; KR has none).
    public static Dictionary<(int Skill, short Level, int Motion, int Attack), (int Method, string Point)> ReadAttackMethods(
            IEnumerable<XmlDocument> skillFiles, out int duplicateLevels) {
        var methods = new Dictionary<(int, short, int, int), (int, string)>();
        duplicateLevels = 0;
        var skillIds = new HashSet<int>();
        foreach (XmlDocument file in skillFiles) {
            XmlNodeList? skills = file.SelectNodes("ms2/skill");
            if (skills == null) {
                continue;
            }
            foreach (XmlElement skill in skills.OfType<XmlElement>()) {
                if (!int.TryParse(skill.GetAttribute("id"), out int id) || id == 0 || !skillIds.Add(id)) {
                    continue;
                }
                var levelValues = new HashSet<short>();
                foreach (XmlElement level in skill.ChildNodes.OfType<XmlElement>().Where(child => child.Name == "level")) {
                    if (!short.TryParse(level.GetAttribute("value"), out short value)) {
                        continue;
                    }
                    if (!levelValues.Add(value)) {
                        duplicateLevels++;
                        continue;
                    }
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
        }
        return methods;
    }

    // The splashActiveSkillID list of every attack of the client's levels that has one (SkillAttackData::Load,
    // skillattackdata.cpp:597, a comma list), by skill id, level, the motion's place in the level and the attack's
    // place in the motion, with the attack's point. The data pack's skill data has no such attribute. The first
    // <skill> of an id and the first <level> of a value are read, as in ReadAttackMethods.
    public static Dictionary<(int Skill, short Level, int Motion, int Attack), (int[] Ids, string Point)> ReadSplashActive(
            IEnumerable<XmlDocument> skillFiles) {
        var result = new Dictionary<(int, short, int, int), (int[], string)>();
        var skillIds = new HashSet<int>();
        foreach (XmlDocument file in skillFiles) {
            XmlNodeList? skills = file.SelectNodes("ms2/skill");
            if (skills == null) {
                continue;
            }
            foreach (XmlElement skill in skills.OfType<XmlElement>()) {
                if (!int.TryParse(skill.GetAttribute("id"), out int id) || id == 0 || !skillIds.Add(id)) {
                    continue;
                }
                var levelValues = new HashSet<short>();
                foreach (XmlElement level in skill.ChildNodes.OfType<XmlElement>().Where(child => child.Name == "level")) {
                    if (!short.TryParse(level.GetAttribute("value"), out short value) || !levelValues.Add(value)) {
                        continue;
                    }
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
        }
        return result;
    }

    // The cooldown of each of the client's levels that gives one, in milliseconds, by skill id and level
    // (SkillLevelData::Load reads the level's cooldown attribute; SkillLevelData::GetCooldown is the length the
    // client's skill slot counts down and its tooltip shows). The data pack's beginCondition cooldownTime differs
    // for 95 KR skills. A level without the attribute is left out (the client counts 0 there). The first <skill> of
    // an id and the first <level> of a value are read, as in ReadSplashActive.
    public static Dictionary<(int Skill, short Level), int> ReadLevelCooldowns(IEnumerable<XmlDocument> skillFiles) {
        var result = new Dictionary<(int, short), int>();
        var skillIds = new HashSet<int>();
        foreach (XmlDocument file in skillFiles) {
            XmlNodeList? skills = file.SelectNodes("ms2/skill");
            if (skills == null) {
                continue;
            }
            foreach (XmlElement skill in skills.OfType<XmlElement>()) {
                if (!int.TryParse(skill.GetAttribute("id"), out int id) || id == 0 || !skillIds.Add(id)) {
                    continue;
                }
                var levelValues = new HashSet<short>();
                foreach (XmlElement level in skill.ChildNodes.OfType<XmlElement>().Where(child => child.Name == "level")) {
                    if (!short.TryParse(level.GetAttribute("value"), out short value) || !levelValues.Add(value) || !level.HasAttribute("cooldown")) {
                        continue;
                    }
                    result[(id, value)] = Number(level, "cooldown");
                }
            }
        }
        return result;
    }

    // The main types whose skills take the caster off the ride at the cast.
    public static bool UnrideMainType(int mainType) => mainType is 1 or 4 or 7 or (>= 11 and <= 15) or (>= 17 and <= 19);

    private static int Number(XmlElement? element, string attribute) {
        return element != null && int.TryParse(element.GetAttribute(attribute), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
    }
}
