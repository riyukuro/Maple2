using System.Xml.Linq;
using Maple2.Model.Metadata;

namespace Maple2.File.Ingest.Helpers;

// value1 of a DungeonEnterTimeLimit row, read as the client reads it (GameEventDungeonEnterTimeLimit,
// commonfiles/gameevent.cpp:2372): each child of the root gives dungeonID and rangeTime "hh:mm-hh:mm"; a child
// without a room, or whose range starts after its end, is left out.
public static class DungeonEnterTimeReader {
    public static DungeonEnterTime[] Parse(string xml) {
        XElement? root;
        try {
            root = XDocument.Parse(xml).Root;
        } catch (System.Xml.XmlException) {
            return [];
        }
        if (root == null) {
            return [];
        }

        var times = new List<DungeonEnterTime>();
        foreach (XElement node in root.Elements()) {
            if (!int.TryParse((string?) node.Attribute("dungeonID"), out int dungeonId) || dungeonId == 0) {
                continue;
            }
            string[] range = ((string?) node.Attribute("rangeTime") ?? "").Split('-');
            int start = 0;
            int end = 0;
            if (range.Length == 2) {
                start = Seconds(range[0]);
                end = Seconds(range[1]);
            }
            if (start > end) {
                continue;
            }
            times.Add(new DungeonEnterTime(dungeonId, start, end));
        }
        return times.ToArray();
    }

    // "hh", "hh:mm" or "hh:mm:ss" as seconds of the day (Unk_DayTime::Parse, timeutil.cpp:241); 0 when it is not a
    // time of day.
    private static int Seconds(string text) {
        string[] parts = text.Trim().Split(':');
        int minute = 0;
        int second = 0;
        if (!int.TryParse(parts[0], out int hour) || (parts.Length > 1 && !int.TryParse(parts[1], out minute)) || (parts.Length > 2 && !int.TryParse(parts[2], out second))) {
            return 0;
        }
        if (hour is < 0 or >= 24 || minute is < 0 or >= 60 || second is < 0 or >= 60) {
            return 0;
        }
        return hour * 3600 + minute * 60 + second;
    }
}
