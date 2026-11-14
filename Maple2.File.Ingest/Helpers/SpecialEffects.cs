using System.Globalization;
using System.Xml;
using Maple2.File.IO;
using Maple2.File.IO.Crypto.Common;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.File.Ingest.Helpers;

// The special effect of each additional effect level (SpecialEffectProperty, which the parser does not carry): the
// client loads one per level (additionalactionadditional.cpp:1346, the first one) with its type, probability, values
// and activateMsg. Read from the same files as the parser, by effect id and level; a level row that is not
// available for the locale and features is skipped. Type 4 is read apart (CooldownGroupRates), as the client
// applies it; the others are kept here for the server (SpecialEffectRules).
public sealed class SpecialEffects {
    private readonly Dictionary<(int Id, short Level), SpecialEffectMetadata> effects = new();

    public int Count => effects.Count;

    public SpecialEffects(M2dReader xmlReader) {
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
        if (basic == null || !short.TryParse(basic.GetAttribute("level"), out short value) || effects.ContainsKey((id, value))) {
            return;
        }
        XmlElement? special = level.ChildNodes.OfType<XmlElement>().FirstOrDefault(node => node.Name == "SpecialEffectProperty");
        if (special == null) {
            return;
        }
        SpecialEffectMetadata? effect = Read(special.GetAttribute("type"), special.GetAttribute("probability"),
            special.GetAttribute("values"), special.GetAttribute("activateMsg"));
        if (effect != null) {
            effects[(id, value)] = effect;
        }
    }

    // The probability as the client keeps it: 1 when absent (SpecialEffectProperty::Clear sets 1.0).
    public static float Probability(string probability) {
        return float.TryParse(probability, NumberStyles.Float, CultureInfo.InvariantCulture, out float chance) ? chance : 1f;
    }

    // One SpecialEffectProperty; null for type 0, type 4 (CooldownGroupRates) or a type that is not a number.
    public static SpecialEffectMetadata? Read(string type, string probability, string values, string activateMsg) {
        if (!int.TryParse(type, out int kind) || kind == 0 || kind == 4) {
            return null;
        }
        Dictionary<string, string> v = Values(values);
        return new SpecialEffectMetadata(
            Type: kind,
            Probability: Probability(probability),
            ActivateMessage: activateMsg,
            ExpRate: Float(v, "improveExpRate"),
            MesoRate: Float(v, "improveMesoRate"),
            RueRate: Float(v, "improveLuRate"),
            MasteryRate: Float(v, "improveMasteryRate"),
            OnlyBigSize: Int(v, "onlyBigSize") != 0,
            RepeatType: Int(v, "repeatType"),
            RewardRate: Float(v, "improveRewardRate"),
            ExpType: v.TryGetValue("expType", out string? exp) && int.TryParse(exp, out int expKind) ? ClientExpType(expKind) : ExpType.none,
            GlobalDropBoxId: Int(v, "globalDropBoxId"),
            IndividualDropBoxId: Int(v, "individualDropBoxId"),
            EnchantLevel: Int(v, "enchantLevel"),
            ImproveRate: Float(v, "improveRate"),
            ItemIds: Ints(v, "itemIDs"),
            NpcRanks: Ints(v, "npcRank"),
            ReduceRate: Float(v, "reduceRate"));
    }

    // The client's exp kind number (commonenum.cpp Parse_14021af40) as the server's ExpType, which orders them apart.
    public static ExpType ClientExpType(int value) {
        return value switch {
            0 => ExpType.mapCommon,
            1 => ExpType.mapHidden,
            2 => ExpType.taxi,
            3 => ExpType.telescope,
            4 => ExpType.rareChest,
            5 => ExpType.rareChestFirst,
            6 => ExpType.normalChest,
            7 => ExpType.expDrop,
            9 => ExpType.musicMastery1,
            _ => ExpType.none,
        };
    }

    // The attributes of the values' first <v>.
    private static Dictionary<string, string> Values(string values) {
        var result = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(values)) {
            return result;
        }
        var fragment = new XmlDocument();
        try {
            fragment.LoadXml($"<r>{values}</r>");
        } catch (XmlException) {
            return result;
        }
        XmlElement? first = fragment.DocumentElement!.ChildNodes.OfType<XmlElement>().FirstOrDefault(node => node.Name == "v");
        if (first != null) {
            foreach (XmlAttribute attribute in first.Attributes) {
                result[attribute.Name] = attribute.Value;
            }
        }
        return result;
    }

    private static float Float(Dictionary<string, string> v, string name) {
        return v.TryGetValue(name, out string? text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : 0;
    }

    private static int Int(Dictionary<string, string> v, string name) {
        return v.TryGetValue(name, out string? text) && int.TryParse(text, out int value) ? value : 0;
    }

    private static int[]? Ints(Dictionary<string, string> v, string name) {
        if (!v.TryGetValue(name, out string? text)) {
            return null;
        }
        int[] ids = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, out int id) ? id : 0).Where(id => id != 0).ToArray();
        return ids.Length > 0 ? ids : null;
    }

    public SpecialEffectMetadata? Get(int id, short level) => effects.TryGetValue((id, level), out SpecialEffectMetadata? found) ? found : null;
}
