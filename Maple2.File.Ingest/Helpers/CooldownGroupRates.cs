using System.Globalization;
using System.Xml;
using Maple2.File.IO;
using Maple2.File.IO.Crypto.Common;
using Maple2.Model.Metadata;

namespace Maple2.File.Ingest.Helpers;

// The cooldown group reductions of each additional effect (SpecialEffectProperty type 4, values
// "<v groupID= reduceRate= />"), which the parser does not carry. While its holder has the effect, the client
// starts the cooldown of a skill of that cooldown group shorter by the rate (CSkillSystem::FUN_1417664e0,
// skillslot.cpp). Read from the same files as the parser, by effect id and level; a level row that is not available
// for the locale and features is skipped, and so is a property whose probability is under 1 (the client rolls
// for it at each cooldown, which the server cannot repeat). An absent probability is 1 (SpecialEffects.Probability).
public sealed class CooldownGroupRates {
    private readonly Dictionary<(int Id, short Level), List<CooldownGroupRate>> rates = new();

    public int Count => rates.Count;

    public CooldownGroupRates(M2dReader xmlReader) {
        foreach (PackFileEntry entry in xmlReader.Files.Where(file => file.Name.StartsWith("additionaleffect/") && file.Name.EndsWith(".xml"))) {
            if (!int.TryParse(Path.GetFileNameWithoutExtension(entry.Name), out int id)) {
                continue;
            }
            XmlNodeList? levels = xmlReader.GetXmlDocument(entry).SelectNodes("//level");
            if (levels == null) {
                continue;
            }
            foreach (XmlElement level in levels.OfType<XmlElement>()) {
                if (ClientListedRows.Available(level)) {
                    Add(id, level);
                }
            }
        }
    }

    private void Add(int id, XmlElement level) {
        XmlElement? basic = level.ChildNodes.OfType<XmlElement>().FirstOrDefault(node => node.Name == "BasicProperty");
        if (basic == null || !short.TryParse(basic.GetAttribute("level"), out short value) || rates.ContainsKey((id, value))) {
            return;
        }
        List<CooldownGroupRate> found = Read(level.ChildNodes.OfType<XmlElement>()
            .Where(node => node.Name == "SpecialEffectProperty")
            .Select(node => (node.GetAttribute("type"), node.GetAttribute("probability"), node.GetAttribute("values"))));
        if (found.Count > 0) {
            rates[(id, value)] = found;
        }
    }

    // The group reduction among a level's special effect properties: the first one, as the client keeps one.
    public static List<CooldownGroupRate> Read(IEnumerable<(string Type, string Probability, string Values)> properties) {
        var found = new List<CooldownGroupRate>();
        foreach ((string type, string probability, string values) in properties) {
            if (type != "4" || SpecialEffects.Probability(probability) < 1) {
                continue;
            }
            var fragment = new XmlDocument();
            try {
                fragment.LoadXml($"<r>{values}</r>");
            } catch (XmlException) {
                continue;
            }
            foreach (XmlElement v in fragment.DocumentElement!.ChildNodes.OfType<XmlElement>()) {
                if (int.TryParse(v.GetAttribute("groupID"), out int group) && group != 0
                    && float.TryParse(v.GetAttribute("reduceRate"), NumberStyles.Float, CultureInfo.InvariantCulture, out float rate) && rate != 0) {
                    // The client keeps one group and rate per effect.
                    found.Add(new CooldownGroupRate(group, rate));
                    return found;
                }
            }
        }
        return found;
    }

    public CooldownGroupRate[]? Get(int id, short level) => rates.TryGetValue((id, level), out List<CooldownGroupRate>? found) ? found.ToArray() : null;
}
