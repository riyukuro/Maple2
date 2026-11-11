using System.Xml;
using Maple2.File.IO;
using Maple2.File.IO.Crypto.Common;

namespace Maple2.File.Ingest.Helpers;

// The server pack's copy of the enchant scroll table (table/Server/enchantScroll.xml) has two attributes the client's
// copy (table/enchantscroll.xml, read by the parser) does not: successProp, one weight per grade in the grade list
// (all 10000 except the random scrolls, e.g. "4970,3600,1000,500,90,40" for grades 6..11), and disableBreak (3 rows).
// The parser has no type for this file, so the two are read here by scroll id. Rows of one id that differ by locale
// or feature have the same values in today's pack, so the first row of an id is kept.
public static class ServerEnchantScroll {
    public const string File = "table/Server/enchantScroll.xml";

    public readonly record struct Entry(int[] Grades, int[] Rates, bool DisableBreak);

    public static Dictionary<int, Entry> Read(M2dReader? reader) {
        PackFileEntry? entry = reader?.Files.FirstOrDefault(e => e.Name.EndsWith(File));
        if (reader == null || entry == null) {
            return new Dictionary<int, Entry>();
        }
        return Read(reader.GetXmlReader(entry));
    }

    public static Dictionary<int, Entry> Read(XmlReader xml) {
        var results = new Dictionary<int, Entry>();
        while (xml.Read()) {
            if (xml.NodeType != XmlNodeType.Element || xml.Name != "scroll") {
                continue;
            }
            if (!int.TryParse(xml.GetAttribute("id"), out int id) || results.ContainsKey(id)) {
                continue;
            }
            int[] grades = IntList(xml.GetAttribute("grade"));
            int[] rates = IntList(xml.GetAttribute("successProp"));
            string? disableBreak = xml.GetAttribute("disableBreak");
            results[id] = new Entry(grades, rates, disableBreak is "1" or "true" or "True");
        }
        return results;
    }

    // The rates in the order of the sorted grade list the server keeps, or null when the row has no rate for each
    // grade (then the server keeps its even pick).
    public static int[]? RatesFor(Entry? entry, int[] sortedGrades) {
        if (entry is not { } row || row.Rates.Length != row.Grades.Length) {
            return null;
        }
        var rates = new int[sortedGrades.Length];
        for (int i = 0; i < sortedGrades.Length; i++) {
            int index = Array.IndexOf(row.Grades, sortedGrades[i]);
            if (index < 0) {
                return null;
            }
            rates[i] = row.Rates[index];
        }
        return rates;
    }

    private static int[] IntList(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return [];
        }
        var list = new List<int>();
        foreach (string part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            if (int.TryParse(part, out int number)) {
                list.Add(number);
            }
        }
        return list.ToArray();
    }
}
