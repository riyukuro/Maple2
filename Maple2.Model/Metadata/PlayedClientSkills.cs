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
public static class PlayedClientSkills {
    public const string SkillFolder = "skilldata/";

    public readonly record struct Entry(int MainType, int RechargeMaxCount, bool RechargeShared);

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
                entries[id] = new Entry(
                    MainType: Number(basic, "mainType"),
                    RechargeMaxCount: Number(state, "rechargeMaxCount"),
                    RechargeShared: Number(state, "rechargeShareOrigin") != 0 && origin != 0 && origin != id);
            }
        }
        return entries;
    }

    // The main types whose skills take the caster off the ride at the cast.
    public static bool UnrideMainType(int mainType) => mainType is 1 or 4 or 7 or (>= 11 and <= 15) or (>= 17 and <= 19);

    private static int Number(XmlElement? element, string attribute) {
        return element != null && int.TryParse(element.GetAttribute(attribute), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
    }
}
