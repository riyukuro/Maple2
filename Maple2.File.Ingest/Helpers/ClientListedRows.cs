using System.Xml;
using System.Xml.Serialization;
using M2dXmlGenerator;
using Maple2.File.IO;
using Maple2.File.IO.Crypto.Common;
using Maple2.File.Parser.Tools;

namespace Maple2.File.Ingest.Helpers;

// A table/<region>/ file follows the data pack (PackTables), while its rows are filtered for LOCALE. The NA pack tags
// most rows of some tables locale="NA" with no row for KR, so under LOCALE=KR the filter keeps no row of those ids
// (dungeonroom.xml: 6 of 101 rooms, none between level 10 and 30; chapterbook.xml: 0 of 27). The played KR client's own
// copy of the table lists the ids it offers. A row id the filter leaves without a row is kept when that client lists
// the id in a row that is available for LOCALE: the pack's row for its own region, with each attribute the client row
// sets taken from the client row (chosen here: where the two differ, the client's value, as the player sees it), and
// the attributes only the pack's row has kept as they are. Ids the client does not list stay dropped.
public static class ClientListedRows {
    public sealed record Counts(int Ids, int Kept, int Added, int Dropped, IReadOnlyList<string> AddedIds, IReadOnlyList<string> PackValues);

    // The played client's copy of a region table: table/<locale>/<name> when it has one, else table/<name>.
    public static XmlDocument? PlayedTable(M2dReader? playedReader, string locale, string name) {
        if (playedReader == null) {
            return null;
        }
        string regional = $"table/{locale.ToLowerInvariant()}/{name}";
        PackFileEntry? entry = playedReader.Files.FirstOrDefault(file => file.Name == regional)
                               ?? playedReader.Files.FirstOrDefault(file => file.Name == $"table/{name}");
        return entry == null ? null : playedReader.GetXmlDocument(entry);
    }

    // The data pack's table/<pack region>/<name> (else table/<name>) with the client-listed rows added, read with the
    // parser's root type T; rows come back as the parser's filter resolves them. The read is the parser's own
    // (parser 2.3.7: ParseChapterBook first drops empty attributes, Sanitizer.RemoveEmpty; ParseDungeonRoom does not).
    public static T Read<T>(M2dReader xmlReader, M2dReader? playedReader, string name, string rowTag, string key, bool removeEmpty, out Counts? counts) where T : class {
        string regional = $"table/{PackTables.Locale.ToLowerInvariant()}/{name}";
        PackFileEntry entry = xmlReader.Files.FirstOrDefault(file => file.Name == regional)
                              ?? xmlReader.Files.First(file => file.Name == $"table/{name}");
        var serializer = new XmlSerializer(typeof(T));
        var server = new XmlDocument();
        string xml = xmlReader.GetString(entry);
        server.LoadXml(removeEmpty ? Sanitizer.RemoveEmpty(xml) : xml);
        counts = null;
        XmlDocument? client = string.Equals(FeatureLocaleFilter.Locale, PackTables.Locale, StringComparison.OrdinalIgnoreCase)
            ? null
            : PlayedTable(playedReader, FeatureLocaleFilter.Locale, name);
        if (client != null) {
            counts = Add(server, client, rowTag, key, FeatureLocaleFilter.Locale, PackTables.Locale, Available, RowReads(serializer));
        }
        using var reader = new XmlNodeReader(server);
        return (T) serializer.Deserialize(reader)!;
    }

    public static bool Available(XmlElement row) => FeatureLocaleFilter.FeatureEnabled(row.GetAttribute("feature")) && FeatureLocaleFilter.HasLocale(row.GetAttribute("locale"));

    // Whether a row alone reads with the root's serializer (an attribute value the parser cannot read throws).
    public static Func<XmlElement, bool> RowReads(XmlSerializer serializer) => row => Parses(serializer, row);

    private static bool Parses(XmlSerializer serializer, XmlElement row) {
        try {
            var doc = new XmlDocument();
            doc.LoadXml("<ms2>" + row.OuterXml + "</ms2>");
            using var reader = new XmlNodeReader(doc);
            serializer.Deserialize(reader);
            return true;
        } catch (InvalidOperationException) {
            return false;
        }
    }

    // Adds to the server document's root one row for each id that has no available row while the client document
    // lists it in an available row. The added row is the last row of the id tagged with the pack's region, with the
    // client row's attributes over it (its feature too, or none when the client row has none), locale set to the
    // locale, and the client row's child elements in place of its own when the client row has any. A client value
    // the parser cannot read (parses false) is left at the pack's value and named in PackValues.
    public static Counts Add(XmlDocument server, XmlDocument client, string rowTag, string key, string locale, string packLocale,
            Func<XmlElement, bool> available, Func<XmlElement, bool> parses) {
        XmlElement root = server.DocumentElement!;
        var groups = new Dictionary<string, List<XmlElement>>();
        foreach (XmlElement row in root.ChildNodes.OfType<XmlElement>().Where(row => row.Name == rowTag)) {
            string id = row.GetAttribute(key);
            if (!groups.TryGetValue(id, out List<XmlElement>? rows)) {
                groups[id] = rows = [];
            }
            rows.Add(row);
        }
        var listed = new Dictionary<string, XmlElement>();
        foreach (XmlElement row in client.DocumentElement!.ChildNodes.OfType<XmlElement>().Where(row => row.Name == rowTag && available(row))) {
            listed[row.GetAttribute(key)] = row;
        }

        int kept = 0;
        var added = new List<string>();
        var packValues = new List<string>();
        foreach ((string id, List<XmlElement> rows) in groups) {
            if (rows.Any(available)) {
                kept++;
                continue;
            }
            XmlElement? pack = rows.LastOrDefault(row => string.Equals(row.GetAttribute("locale"), packLocale, StringComparison.OrdinalIgnoreCase));
            if (pack == null || !listed.TryGetValue(id, out XmlElement? clientRow)) {
                continue;
            }
            var merged = (XmlElement) pack.CloneNode(true);
            merged.RemoveAttribute("feature");
            merged.SetAttribute("locale", locale);
            if (clientRow.ChildNodes.OfType<XmlElement>().Any()) {
                foreach (XmlElement child in merged.ChildNodes.OfType<XmlElement>().ToList()) {
                    merged.RemoveChild(child);
                }
                foreach (XmlElement child in clientRow.ChildNodes.OfType<XmlElement>()) {
                    merged.AppendChild(server.ImportNode(child, true));
                }
            }
            foreach (XmlAttribute attribute in clientRow.Attributes) {
                // An empty value is no value (the parser's reads drop it or cannot read it).
                if (attribute.Name == "locale" || attribute.Value.Length == 0) {
                    continue;
                }
                string? before = merged.HasAttribute(attribute.Name) ? merged.GetAttribute(attribute.Name) : null;
                merged.SetAttribute(attribute.Name, attribute.Value);
                if (parses(merged)) {
                    continue;
                }
                if (before == null) {
                    merged.RemoveAttribute(attribute.Name);
                } else {
                    merged.SetAttribute(attribute.Name, before);
                }
                packValues.Add($"{id}.{attribute.Name}");
            }
            if (!available(merged) || !parses(merged)) {
                continue;
            }
            root.AppendChild(merged);
            added.Add(id);
        }
        return new Counts(groups.Count, kept, added.Count, groups.Count - kept - added.Count, added, packValues);
    }
}
