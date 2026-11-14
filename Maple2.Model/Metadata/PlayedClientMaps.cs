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

    // The main types each row's skillUseDisable lists (<environment><property skillUseDisable="1,2">), as the client
    // reads them into its field mask (CFieldData::vfunc_1, fielddata.cpp:708): an empty list for a row whose
    // environment lists none. available: whether an environment's feature and locale are on; of several available
    // environments the last is taken (chosen here; in the KR table no field has two that differ). A row with no
    // available environment is left out.
    public static Dictionary<int, int[]> SkillUseDisable(XmlDocument fieldTable, Func<XmlElement, bool> available) {
        var lists = new Dictionary<int, int[]>();
        XmlNodeList? rows = fieldTable.SelectNodes("ms2/fieldData");
        if (rows == null) {
            return lists;
        }
        foreach (XmlNode node in rows) {
            if (node is not XmlElement row || !int.TryParse(row.GetAttribute("id"), out int id)) {
                continue;
            }
            foreach (XmlElement environment in row.ChildNodes.OfType<XmlElement>().Where(child => child.Name == "environment" && available(child))) {
                string list = environment.ChildNodes.OfType<XmlElement>().FirstOrDefault(child => child.Name == "property")?.GetAttribute("skillUseDisable") ?? "";
                lists[id] = list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(text => int.TryParse(text, out int type) ? type : -1).Where(type => type >= 0).ToArray();
            }
        }
        return lists;
    }

    // Whether each row's skills with an EP cost may be cast (<environment><ui useEPSkill="0">), as the client reads it
    // (CFieldDataCli::vfunc_1, fielddatacli.cpp:228; 1 when the attribute is absent, fielddata.h:118) and tests it in
    // its cast check (skillslot.cpp:3111-3115). Of several available environments the last is taken, as for
    // SkillUseDisable. A row with no available environment is left out.
    public static Dictionary<int, bool> UseEpSkill(XmlDocument fieldTable, Func<XmlElement, bool> available) {
        var flags = new Dictionary<int, bool>();
        XmlNodeList? rows = fieldTable.SelectNodes("ms2/fieldData");
        if (rows == null) {
            return flags;
        }
        foreach (XmlNode node in rows) {
            if (node is not XmlElement row || !int.TryParse(row.GetAttribute("id"), out int id)) {
                continue;
            }
            foreach (XmlElement environment in row.ChildNodes.OfType<XmlElement>().Where(child => child.Name == "environment" && available(child))) {
                string flag = environment.ChildNodes.OfType<XmlElement>().FirstOrDefault(child => child.Name == "ui")?.GetAttribute("useEPSkill") ?? "";
                flags[id] = !int.TryParse(flag, out int value) || value != 0;
            }
        }
        return flags;
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
