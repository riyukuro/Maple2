using System.Globalization;
using System.Xml;
using Maple2.Model.Enum;

namespace Maple2.Model.Metadata;

// What the played client's own additional effect data (additional/<nnn>.xml) says of an effect's invoke effect and
// its effect group, where the server's data pack says something else. The client reads, per level, the
// <InvokeEffect effectID effectGroupID skillID skillGroupID types values rates> row, and files each effect under the
// groupIDs of its first level's <Basic> (SkillDataTableCli::LoadAdditionalFile, skilldatatablecli.cpp:362;
// AdditionalData::GetFirstLevelGroupIDs). The data pack differs:
// - its invoke types for skills are 1, 2 and 56 where the client's are 1001-1004 (mapped back in ClientType);
// - 264 of its skill damage rows and 86 of its dot damage, healing and duration rows have other values (10100241: a rate of 0.03 where the
//   client has 0.23), and its type 38 and 40 rows have no value (90050806: 0 where the client has 20);
// - its 24 duration rows of the client's type 4 (milliseconds) are type 3 with half the time (10400281: 5000, 10000);
// - its <BasicProperty group> is not the client's groupIDs (0, or another group: 10900222 has 10900062 for the
//   client's 10900241), so an invoke on an effect group never matched.
public static class PlayedClientAdditionalEffects {
    public const string AdditionalFolder = "additional/";

    // GroupId: the first level's groupIDs. Levels: every level the client has, with its invoke effect or null.
    // SkillUseDisable: every level's <Motion skillUseDisable> mask, 0 where the level has none (the client's
    // m_motion.m_nSkillUseDisable, summed over the enabled buffs by AdditionalCliList::FUN_140972a00).
    public sealed record Entry(int GroupId, Dictionary<short, AdditionalEffectMetadataInvokeEffect?> Levels) {
        public Dictionary<short, int> SkillUseDisable { get; init; } = [];
    }

    public static bool IsAdditionalFile(string name) => name.StartsWith(AdditionalFolder, StringComparison.OrdinalIgnoreCase);

    // available: whether a <level> row is on (AdditionalData::Load, additionalactionadditional.cpp:714, skips a level
    // whose feature or locale is off before it reads the level, so the first level is the first one on).
    public static Dictionary<int, Entry> Read(IEnumerable<XmlDocument> files, Func<XmlElement, bool> available) {
        var entries = new Dictionary<int, Entry>();
        foreach (XmlDocument file in files) {
            XmlNodeList? effects = file.SelectNodes("ms2/additional");
            if (effects == null) {
                continue;
            }
            foreach (XmlNode node in effects) {
                if (node is not XmlElement effect || !int.TryParse(effect.GetAttribute("id"), out int id) || id == 0) {
                    continue;
                }
                // A later row of the same id replaces the earlier one, as the client's load does.
                var levels = new Dictionary<short, AdditionalEffectMetadataInvokeEffect?>();
                var useDisable = new Dictionary<short, int>();
                int groupId = 0;
                bool first = true;
                foreach (XmlElement level in effect.ChildNodes.OfType<XmlElement>().Where(child => child.Name == "level" && available(child))) {
                    if (!short.TryParse(level.GetAttribute("level"), out short levelId)) {
                        continue;
                    }
                    if (first) {
                        groupId = Number(Child(level, "Basic"), "groupIDs");
                        first = false;
                    }
                    levels[levelId] = Invoke(Child(level, "InvokeEffect"));
                    useDisable[levelId] = Number(Child(level, "Motion"), "skillUseDisable");
                }
                entries[id] = new Entry(groupId, levels) { SkillUseDisable = useDisable };
            }
        }
        return entries;
    }

    // The server's number of a client invoke type: the client's skill types 1001-1004 and its millisecond duration
    // type 4 go to the server's; the others keep their number.
    public static InvokeEffectType ClientType(int type) => type switch {
        1001 or 1002 => InvokeEffectType.ReduceCooldown,
        1003 => InvokeEffectType.ReduceSpiritCost,
        1004 => InvokeEffectType.IncreaseSkillDamage,
        4 => InvokeEffectType.IncreaseEffectDuration,
        _ => (InvokeEffectType) type,
    };

    public static AdditionalEffectMetadataInvokeEffect? Invoke(XmlElement? row) {
        if (row == null) {
            return null;
        }
        int[] types = Numbers(row.GetAttribute("types")).Select(value => (int) value).ToArray();
        float[] values = Pad(Numbers(row.GetAttribute("values")), types.Length);
        float[] rates = Pad(Numbers(row.GetAttribute("rates")), types.Length);
        return new AdditionalEffectMetadataInvokeEffect(
            Types: types.Select(ClientType).ToArray(),
            Values: values,
            Rates: rates,
            EffectId: Number(row, "effectID"),
            EffectGroupId: Number(row, "effectGroupID"),
            SkillId: Number(row, "skillID"),
            SkillGroupId: Number(row, "skillGroupID"));
    }

    private static XmlElement? Child(XmlElement parent, string name) {
        return parent.ChildNodes.OfType<XmlElement>().FirstOrDefault(child => child.Name == name);
    }

    private static float[] Numbers(string list) {
        return list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(text => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : 0f).ToArray();
    }

    private static float[] Pad(float[] values, int length) {
        return values.Length >= length ? values[..length] : values.Concat(Enumerable.Repeat(0f, length - values.Length)).ToArray();
    }

    private static int Number(XmlElement? element, string attribute) {
        return element != null && int.TryParse(element.GetAttribute(attribute), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
    }
}
