using System.Xml;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;

namespace Maple2.File.Ingest.Helpers;

// A StampEvent row: value1 is <ms2><tag event/><stamp quest special x y/>...</ms2>, value2 is
// <ms2><reward special count item01 item02/>...</ms2>, value3 the complete reward "id,rank,count" and value4 the
// key (empty for 0).
public static class StampEventReader {
    // Null when the row cannot be read; text that is not XML is such a row, it does not stop the ingest.
    public static StampEvent? Parse(string value1, string value2, string value3, string value4) {
        if (string.IsNullOrEmpty(value1) || string.IsNullOrEmpty(value2)) {
            return null;
        }
        try {
            return ParseXml(value1, value2, value3, value4);
        } catch (XmlException) {
            return null;
        }
    }

    private static StampEvent? ParseXml(string value1, string value2, string value3, string value4) {
        var value1Xml = new XmlDocument();
        value1Xml.LoadXml(value1);
        if (value1Xml.DocumentElement is not { Name: "ms2" } root1) {
            return null;
        }
        var stamps = new List<StampEvent.Stamp>();
        foreach (XmlNode node in root1.ChildNodes) {
            if (node.Name != "stamp" || !int.TryParse(node.Attributes?["quest"]?.Value, out int questId) || questId <= 0) {
                continue;
            }
            int.TryParse(node.Attributes?["x"]?.Value, out int x);
            int.TryParse(node.Attributes?["y"]?.Value, out int y);
            stamps.Add(new StampEvent.Stamp(questId, Flag(node.Attributes?["special"]?.Value), x, y));
        }
        if (stamps.Count == 0) {
            return null;
        }

        var value2Xml = new XmlDocument();
        value2Xml.LoadXml(value2);
        if (value2Xml.DocumentElement is not { Name: "ms2" } root2) {
            return null;
        }
        var rewards = new List<StampEvent.Reward>();
        foreach (XmlNode node in root2.ChildNodes) {
            if (node.Name != "reward" || !int.TryParse(node.Attributes?["count"]?.Value, out int count) || count <= 0) {
                continue;
            }
            rewards.Add(new StampEvent.Reward(
                Count: count,
                Special: Flag(node.Attributes?["special"]?.Value),
                Item01: LongTermAttendRules.ParseItem(node.Attributes?["item01"]?.Value),
                Item02: LongTermAttendRules.ParseItem(node.Attributes?["item02"]?.Value)));
        }

        int.TryParse(value4, out int key);
        return new StampEvent(
            EventTag: root1.SelectSingleNode("tag")?.Attributes?["event"]?.Value ?? string.Empty,
            Key: key,
            Stamps: stamps.ToArray(),
            Rewards: rewards.ToArray(),
            CompleteReward: LongTermAttendRules.ParseItem(value3));
    }

    private static bool Flag(string? text) {
        return int.TryParse(text, out int value) && value != 0;
    }
}
