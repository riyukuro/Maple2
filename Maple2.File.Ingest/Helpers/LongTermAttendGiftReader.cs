using System.Xml;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;

namespace Maple2.File.Ingest.Helpers;

// A LongTermAttendGift row: value1 is <ms><info title maxattendance merat eventTag/></ms>, value2 is
// <ms><reward count item01 item02/>...</ms>.
public static class LongTermAttendGiftReader {
    public static LongTermAttendGift? Parse(string value1, string value2) {
        if (string.IsNullOrEmpty(value1) || string.IsNullOrEmpty(value2)) {
            return null;
        }

        var value1Xml = new XmlDocument();
        value1Xml.LoadXml(value1);
        XmlNode? info = value1Xml.FirstChild is { Name: "ms" } ? value1Xml.FirstChild.SelectSingleNode("info") : null;
        if (info == null) {
            return null;
        }
        if (!int.TryParse(info.Attributes?["maxattendance"]?.Value, out int maxAttendance) || maxAttendance <= 0) {
            return null;
        }
        if (!int.TryParse(info.Attributes?["merat"]?.Value, out int merat) || merat < 0) {
            return null;
        }

        var value2Xml = new XmlDocument();
        value2Xml.LoadXml(value2);
        if (value2Xml.FirstChild is not { Name: "ms" }) {
            return null;
        }
        var rewards = new List<LongTermAttendGift.Reward>();
        foreach (XmlNode node in value2Xml.FirstChild.ChildNodes) {
            if (node.Name != "reward" || !int.TryParse(node.Attributes?["count"]?.Value, out int day) || day <= 0) {
                continue;
            }
            rewards.Add(new LongTermAttendGift.Reward(
                Day: day,
                Item01: LongTermAttendRules.ParseItem(node.Attributes?["item01"]?.Value),
                Item02: LongTermAttendRules.ParseItem(node.Attributes?["item02"]?.Value)));
        }

        return new LongTermAttendGift(
            Title: info.Attributes?["title"]?.Value ?? string.Empty,
            EventTag: info.Attributes?["eventTag"]?.Value ?? string.Empty,
            MaxAttendance: maxAttendance,
            Merat: merat,
            Rewards: rewards.ToArray());
    }
}
