using System.Xml;
using System.Xml.Serialization;
using Maple2.File.IO;
using Maple2.File.IO.Crypto.Common;
using IndividualItemDrop = Maple2.File.Parser.Xml.Table.Server.IndividualItemDrop;
using IndividualItemDropRoot = Maple2.File.Parser.Xml.Table.Server.IndividualItemDropRoot;

namespace Maple2.File.Ingest.Helpers;

// The server pack's monster and box drop table. The community server pack (Server.m2d) began as a KMS server pack;
// later commits replaced or refilled its individualItemDrop_Final.xml with GMS2/NA data and kept the original
// table next to it as individualItemDrop_Final_Backup.xml. MS2_ORIGINAL_DROP_TABLE=1 reads that original instead.
// Both files have the same layout and are read with the parser's own type, so the locale and feature filter is the
// same; only the file differs.
public static class ServerDropTable {
    public const string Option = "MS2_ORIGINAL_DROP_TABLE";
    public const string TodayFile = "table/Server/individualItemDrop_Final.xml";
    public const string OriginalFile = "table/Server/individualItemDrop_Final_Backup.xml";

    private static readonly XmlSerializer Serializer = new(typeof(IndividualItemDropRoot));

    // Unset, empty, 0 or false: today's table. 1 or true: the original. Anything else stops the ingest, so a typo
    // does not silently ingest the other table.
    public static bool UseOriginal(string? value) {
        return value?.Trim().ToLowerInvariant() switch {
            null or "" or "0" or "false" => false,
            "1" or "true" => true,
            _ => throw new ArgumentException($"{Option} must be 1/true or 0/false, not '{value}'"),
        };
    }

    public static string FileName(bool original) => original ? OriginalFile : TodayFile;

    // The same read as ServerTableParser.ParseIndividualItemDrop (parser 2.3.7), from the given file. GetEntry
    // matches by name ending, and the two names do not end alike, so each finds only its own file.
    public static IEnumerable<(int Id, IndividualItemDrop Box)> Read(M2dReader reader, string file) {
        PackFileEntry? entry = reader.Files.FirstOrDefault(e => e.Name.EndsWith(file));
        if (entry == null) {
            throw new FileNotFoundException($"The server pack has no {file}");
        }
        return Read(reader.GetXmlReader(entry));
    }

    // The same read with the boxes the server's table has no available row for taken from the played client's copy
    // of the table (ClientDropBoxes); counts is null without a client table.
    public static IEnumerable<(int Id, IndividualItemDrop Box)> Read(M2dReader reader, string file, XmlDocument? client, out ClientDropBoxes.Counts? counts) {
        counts = null;
        if (client == null) {
            return Read(reader, file);
        }
        PackFileEntry? entry = reader.Files.FirstOrDefault(e => e.Name.EndsWith(file));
        if (entry == null) {
            throw new FileNotFoundException($"The server pack has no {file}");
        }
        var server = new XmlDocument();
        server.LoadXml(reader.GetString(entry));
        counts = ClientDropBoxes.Add(server, client, ClientListedRows.Available);
        return Read(new XmlNodeReader(server)).ToList();
    }

    public static IEnumerable<(int Id, IndividualItemDrop Box)> Read(XmlReader xml) {
        var root = (IndividualItemDropRoot) Serializer.Deserialize(xml)!;
        foreach (IndividualItemDrop box in root.dropBox) {
            yield return (box.dropBoxID, box);
        }
    }
}
