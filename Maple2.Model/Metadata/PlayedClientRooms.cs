using System.Xml;

namespace Maple2.Model.Metadata;

// The dungeon rooms whose entry the played client limits, read from that client's own room table
// (table/dungeonroom.xml). DungeonRoom::Load (commonfiles/dungeonroom.cpp:377) reads the row's limitEnter
// bool and GetLimitType gives 2 for it; the room window then shows the cooldown icon while the record has no
// count left (UIDungeonRoomInfo::IsCooldown, uidungeonroomdialog.cpp:1798). The table keeps the available
// rows only (FUN_14028faa0 with IsElementAvailable, commonutil.cpp:1748).
public static class PlayedClientRooms {
    public const string RoomTable = "table/dungeonroom.xml";

    public static HashSet<int> LimitEnter(XmlDocument roomTable, Func<XmlElement, bool> available) {
        var ids = new HashSet<int>();
        XmlNodeList? rows = roomTable.SelectNodes("ms2/dungeonRoom");
        if (rows == null) {
            return ids;
        }
        foreach (XmlNode node in rows) {
            if (node is not XmlElement row || !available(row) || !int.TryParse(row.GetAttribute("dungeonRoomID"), out int id)) {
                continue;
            }
            string value = row.GetAttribute("limitEnter");
            if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase)) {
                ids.Add(id);
            }
        }
        return ids;
    }
}
