using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using Maple2.File.IO;
using Maple2.File.IO.Crypto.Common;
using Maple2.Model.Enum;

namespace Maple2.File.Ingest.Helpers;

// The final stats of each additional effect level of the data pack, which the parser does not carry:
// <FinalStatusProperty><Stat final<stat>rate= final<stat>value= /></FinalStatusProperty> and the StatusProperty
// attributes finalCapV/R, finalAbpV/R, finalAtpV/R and finalEvpV/R. The client has no final stats; its
// additional/<nnn>.xml writes the same rows as SpecialAbility types (70002184: finalhprate -0.9 and finalmsprate -0.9
// are hpRate -900 and msp -90), so the ingest takes the stats from the client's level (AdditionalEffectMapper).
// Read from the same files as the parser, by effect id and level; a level row that is not available for the locale
// and features is skipped, and the first available row of a level is kept.
public sealed partial class FinalStatusRows {
    // Status: a StatusProperty attribute (finalCap/Abp/Atp/EvpV/R). The client reads none of them (no final name is in
    // its source), and the KR pack's only nonzero ones are effect ids (40500027 and 40600027: finalCapV 50000017;
    // 40500032 and 40600032: 60011012), so they are never added as stats.
    public sealed record Row(string Name, BasicAttribute Attribute, bool Rate, float Value, bool Status = false);

    // What Apply left out, for the ingest log.
    public sealed class Counts {
        // StatusProperty rows (ids, see Row) the pack fallback skipped.
        public int StatusRows { get; set; }
        // Client rows not on an attribute of the level's final stats (the Develop levels' offensive_* and pen, ...).
        public int DroppedClientRows { get; set; }
        // Client rows on an attribute the pack's level already has a stat of the same kind of (skipped).
        public int DoubledClientRows { get; set; }
        // Pack final values with a fraction, cut to a whole value (none in KR).
        public int TruncatedValues { get; set; }
    }

    // The data pack's stat names (the parser's StatValue order).
    private static readonly Dictionary<string, BasicAttribute> Stats = new(StringComparer.OrdinalIgnoreCase) {
        ["str"] = BasicAttribute.Strength,
        ["dex"] = BasicAttribute.Dexterity,
        ["int"] = BasicAttribute.Intelligence,
        ["luk"] = BasicAttribute.Luck,
        ["hp"] = BasicAttribute.Health,
        ["hp_rgp"] = BasicAttribute.HpRegen,
        ["hp_inv"] = BasicAttribute.HpRegenInterval,
        ["sp"] = BasicAttribute.Spirit,
        ["sp_rgp"] = BasicAttribute.SpRegen,
        ["sp_inv"] = BasicAttribute.SpRegenInterval,
        ["ep"] = BasicAttribute.Stamina,
        ["ep_rgp"] = BasicAttribute.StaminaRegen,
        ["ep_inv"] = BasicAttribute.StaminaRegenInterval,
        ["asp"] = BasicAttribute.AttackSpeed,
        ["msp"] = BasicAttribute.MovementSpeed,
        ["atp"] = BasicAttribute.Accuracy,
        ["evp"] = BasicAttribute.Evasion,
        ["cap"] = BasicAttribute.CriticalRate,
        ["cad"] = BasicAttribute.CriticalDamage,
        ["car"] = BasicAttribute.CriticalEvasion,
        ["ndd"] = BasicAttribute.Defense,
        ["abp"] = BasicAttribute.PerfectGuard,
        ["jmp"] = BasicAttribute.JumpHeight,
        ["pap"] = BasicAttribute.PhysicalAtk,
        ["map"] = BasicAttribute.MagicalAtk,
        ["par"] = BasicAttribute.PhysicalRes,
        ["mar"] = BasicAttribute.MagicalRes,
        ["wapmin"] = BasicAttribute.MinWeaponAtk,
        ["wapmax"] = BasicAttribute.MaxWeaponAtk,
        ["dmg"] = BasicAttribute.Damage,
        ["pen"] = BasicAttribute.Piercing,
        ["rmsp"] = BasicAttribute.MountSpeed,
        ["bap"] = BasicAttribute.BonusAtk,
        ["bap_pet"] = BasicAttribute.PetBonusAtk,
    };

    [GeneratedRegex("^final(?<stat>[a-z_]+?)(?<kind>rate|value)$", RegexOptions.IgnoreCase)]
    private static partial Regex StatName();

    [GeneratedRegex("^final(?<stat>Cap|Abp|Atp|Evp)(?<kind>[VR])$")]
    private static partial Regex StatusName();

    private readonly Dictionary<(int Id, short Level), List<Row>> rows = new();

    public int Count => rows.Count;
    public IEnumerable<(int Id, short Level)> Keys => rows.Keys;

    public FinalStatusRows(M2dReader xmlReader) {
        foreach (PackFileEntry entry in xmlReader.Files.Where(file => file.Name.StartsWith("additionaleffect/") && file.Name.EndsWith(".xml"))) {
            if (!int.TryParse(Path.GetFileNameWithoutExtension(entry.Name), out int id)) {
                continue;
            }
            XmlNodeList? levels = xmlReader.GetXmlDocument(entry).SelectNodes("//level");
            if (levels == null) {
                continue;
            }
            var seen = new HashSet<short>();
            foreach (XmlElement level in levels.OfType<XmlElement>()) {
                if (!ClientListedRows.Available(level)) {
                    continue;
                }
                XmlElement? basic = Child(level, "BasicProperty");
                if (basic == null || !short.TryParse(basic.GetAttribute("level"), out short value) || !seen.Add(value)) {
                    continue;
                }
                List<Row> found = Read(Child(level, "StatusProperty"), Child(level, "FinalStatusProperty") is { } final ? Child(final, "Stat") : null);
                if (found.Count > 0) {
                    rows[(id, value)] = found;
                }
            }
        }
    }

    // The nonzero final stats of a level's StatusProperty and FinalStatusProperty/Stat.
    public static List<Row> Read(XmlElement? status, XmlElement? stat) {
        var found = new List<Row>();
        foreach (XmlAttribute attribute in status?.Attributes.OfType<XmlAttribute>() ?? []) {
            Match match = StatusName().Match(attribute.Name);
            if (match.Success && Number(attribute.Value) is var number && number != 0
                && Stats.TryGetValue(match.Groups["stat"].Value, out BasicAttribute basic)) {
                found.Add(new Row(attribute.Name, basic, match.Groups["kind"].Value == "R", number, Status: true));
            }
        }
        foreach (XmlAttribute attribute in stat?.Attributes.OfType<XmlAttribute>() ?? []) {
            Match match = StatName().Match(attribute.Name);
            if (match.Success && Number(attribute.Value) is var number && number != 0
                && Stats.TryGetValue(match.Groups["stat"].Value, out BasicAttribute basic)) {
                found.Add(new Row(attribute.Name, basic, match.Groups["kind"].Value.Equals("rate", StringComparison.OrdinalIgnoreCase), number));
            }
        }
        return found;
    }

    // Adds a level's final stats to its stats: the client level's rows on the final stats' attributes where the client
    // has the level (client not null; ClientSpecialAbility.BasicAttributes), else each final rate as the stat's rate and
    // each final value as its value (Chosen here; AdditionalEffectMapper.Status). StatusProperty rows are never added
    // (Row). Chosen here: a client row on an attribute the pack's level already has a stat of the same kind (rate or
    // value) of is skipped, since the client keeps one value per type and the pack's own row is already in. True where
    // the client's rows were used.
    public static bool Apply(ClientSpecialAbility specialAbility, ClientSpecialAbility.Stats stats, IReadOnlyList<Row> rows, IReadOnlyDictionary<string, int>? client, Counts? counts = null) {
        counts ??= new Counts();
        if (client != null) {
            var attributes = rows.Where(row => !row.Status).Select(row => row.Attribute).ToHashSet();
            var packValues = stats.Values.Where(pair => pair.Value != 0).Select(pair => pair.Key).ToHashSet();
            var packRates = stats.Rates.Where(pair => pair.Value != 0).Select(pair => pair.Key).ToHashSet();
            foreach ((string type, int value) in client) {
                IReadOnlyList<BasicAttribute> on = ClientSpecialAbility.BasicAttributes(type);
                if (!on.Any(attributes.Contains)) {
                    counts.DroppedClientRows++;
                } else if (specialAbility.Rate(type) is { } rate && on.Any((rate ? packRates : packValues).Contains)) {
                    counts.DoubledClientRows++;
                } else {
                    specialAbility.Add(stats, type, value);
                }
            }
            return true;
        }
        foreach (Row row in rows) {
            if (row.Status) {
                counts.StatusRows++;
            } else if (row.Rate) {
                stats.Rates[row.Attribute] = stats.Rates.GetValueOrDefault(row.Attribute) + row.Value;
            } else {
                if (row.Value != MathF.Truncate(row.Value)) {
                    counts.TruncatedValues++;
                }
                stats.Values[row.Attribute] = stats.Values.GetValueOrDefault(row.Attribute) + (long) row.Value;
            }
        }
        return false;
    }

    public IReadOnlyList<Row>? Get(int id, short level) => rows.TryGetValue((id, level), out List<Row>? found) ? found : null;

    private static XmlElement? Child(XmlElement parent, string name) {
        return parent.ChildNodes.OfType<XmlElement>().FirstOrDefault(child => child.Name == name);
    }

    private static float Number(string text) {
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : 0;
    }
}
