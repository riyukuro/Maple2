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
    public static Dictionary<(int Skill, short Level, int Motion, int Attack), int> ReadAttackMethods(IEnumerable<XmlDocument> skillFiles) {
        var methods = new Dictionary<(int, short, int, int), int>();
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
                            methods[(id, value, motionIndex, attackIndex++)] = Number(attack, "attackMethodType");
                        }
                        motionIndex++;
                    }
                }
            }
        }
        return methods;
    }

    // The main types whose skills take the caster off the ride at the cast.
    public static bool UnrideMainType(int mainType) => mainType is 1 or 4 or 7 or (>= 11 and <= 15) or (>= 17 and <= 19);

    private static int Number(XmlElement? element, string attribute) {
        return element != null && int.TryParse(element.GetAttribute(attribute), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
    }
}
