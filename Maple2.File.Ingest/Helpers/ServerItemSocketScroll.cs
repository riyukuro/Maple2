using System.Xml;
using Maple2.File.IO;
using Maple2.File.IO.Crypto.Common;

namespace Maple2.File.Ingest.Helpers;

// The server pack's copy of the socket scroll table (table/Server/itemSocketScroll.xml) has two attributes the
// client's copy (table/itemsocketscroll.xml, read by the parser) does not: successProb, the unlock's chance out of
// 10000, and openCount, the sockets the scroll opens. CItemSocketScrollDataServer::Read reads both only for a type 1
// scroll (commonfiles/itemsocketscrolldata.cpp), and every KR row is type 1. The parser has no type for this file, so
// the two are read here by scroll id; the first row of an id is kept.
public static class ServerItemSocketScroll {
    public const string File = "table/Server/itemSocketScroll.xml";

    public readonly record struct Entry(int SuccessProb, byte OpenCount);

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
            if (xml.GetAttribute("type") is { } type && type.Trim() != "1") {
                continue;
            }
            if (!int.TryParse(xml.GetAttribute("successProb"), out int successProb)
                || !byte.TryParse(xml.GetAttribute("openCount"), out byte openCount)) {
                continue;
            }
            results[id] = new Entry(successProb, openCount);
        }
        return results;
    }
}
