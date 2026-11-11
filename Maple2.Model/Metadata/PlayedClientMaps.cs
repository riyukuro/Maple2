using System.Xml;

namespace Maple2.Model.Metadata;

// The maps the played client cannot load, read from that client's own field table (table/fielddata.xml):
// a pack built for a client version keeps a row for every map id but empties the rows of maps that version
// does not have, so the row has no environment with an xblock name. The client is sent a map by id only
// (RequestFieldEnter) and looks the xblock up in this table; with an empty row it loads nothing and hangs.
public static class PlayedClientMaps {
    public const string FieldTable = "table/fielddata.xml";

    // The ids of the rows without an xblock name. A row counts as loadable when any of its environments
    // names an xblock. A table with no rows gives an empty set.
    public static HashSet<int> Missing(XmlDocument fieldTable) {
        var missing = new HashSet<int>();
        XmlNodeList? rows = fieldTable.SelectNodes("ms2/fieldData");
        if (rows == null) {
            return missing;
        }
        foreach (XmlNode node in rows) {
            if (node is not XmlElement row || !int.TryParse(row.GetAttribute("id"), out int id)) {
                continue;
            }
            bool loadable = false;
            foreach (XmlNode xblock in row.SelectNodes("environment/xblock")!) {
                if (xblock is XmlElement element && element.GetAttribute("name").Length > 0) {
                    loadable = true;
                    break;
                }
            }
            if (!loadable) {
                missing.Add(id);
            }
        }
        return missing;
    }

    // The ids of all rows, loadable or not.
    public static HashSet<int> Rows(XmlDocument fieldTable) {
        var ids = new HashSet<int>();
        XmlNodeList? rows = fieldTable.SelectNodes("ms2/fieldData");
        if (rows == null) {
            return ids;
        }
        foreach (XmlNode node in rows) {
            if (node is XmlElement row && int.TryParse(row.GetAttribute("id"), out int id)) {
                ids.Add(id);
            }
        }
        return ids;
    }

    // Whether the played client lacks a map: its row is empty, or the table has no row for it at all (the
    // client finds nothing to load either way). A table without any row says nothing about any map.
    public static bool Lacks(HashSet<int> rows, HashSet<int> missing, int mapId) {
        return rows.Count > 0 && (missing.Contains(mapId) || !rows.Contains(mapId));
    }
}
