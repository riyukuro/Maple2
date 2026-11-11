using System.Globalization;
using System.Xml;

namespace Maple2.Model.Metadata;

// Reads the reactor tables the way the client's CReactorDataTable does.
public static class ReactorTableReader {
    public static ReactorTable Read(XmlDocument reactors, XmlDocument reactorStates, Func<XmlElement, bool> keep) {
        var states = new Dictionary<int, ReactorTable.State>();
        foreach (XmlElement node in Elements(reactorStates, "/ms2/reactorState")) {
            if (!TryInt(node.GetAttribute("id"), out int stateId) || states.ContainsKey(stateId)) {
                continue;
            }

            var conditions = new List<ReactorTable.ConditionItem>();
            foreach (XmlElement condition in node.ChildNodes.OfType<XmlElement>().Where(child => child.Name == "condItem")) {
                // A condItem without a code is no condition.
                if (!TryInt(condition.GetAttribute("code"), out int itemId)) {
                    continue;
                }
                conditions.Add(new ReactorTable.ConditionItem(
                    ItemId: itemId,
                    Rarity: Int(condition.GetAttribute("rank"), 1),
                    Consume: Int(condition.GetAttribute("consume"), 0),
                    CheckCount: Int(condition.GetAttribute("checkCount"), 0)));
            }

            states.Add(stateId, new ReactorTable.State(
                Id: stateId,
                Type: ParseStateType(node.GetAttribute("type")),
                NextTimeSec: Int(node.GetAttribute("nextTimeSec"), 0),
                ExpireTimeSec: Int(node.GetAttribute("expireTimeSec"), 0),
                ConditionItems: conditions.ToArray()));
        }

        var results = new Dictionary<int, ReactorTable.Entry>();
        foreach (XmlElement node in Elements(reactors, "/ms2/reactor")) {
            if (!keep(node) || !TryInt(node.GetAttribute("id"), out int id)) {
                continue;
            }

            // "[100, 101, 102, 103]": the client fails the row when a listed state is missing.
            var reactorStateList = new List<ReactorTable.State>();
            bool valid = true;
            foreach (string part in node.GetAttribute("reactorStateList").Trim('[', ']', ' ').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
                if (!TryInt(part, out int stateId) || !states.TryGetValue(stateId, out ReactorTable.State? state)) {
                    valid = false;
                    break;
                }
                reactorStateList.Add(state);
            }
            if (!valid) {
                continue;
            }

            // The client's map keeps the last row of an id.
            results[id] = new ReactorTable.Entry(
                Id: id,
                ReactDistance: float.TryParse(node.GetAttribute("reactDistance"), NumberStyles.Float, CultureInfo.InvariantCulture, out float distance) ? distance : 0,
                ReactTimeMs: Int(node.GetAttribute("reactTimeMSec"), 15000),
                GameEventId: Int(node.GetAttribute("gameEventId"), 0),
                States: reactorStateList.ToArray(),
                Rewards: ParseRewards(node.GetAttribute("rewardItemListXml")));
        }

        return new ReactorTable(results);
    }

    public static ReactorStateType ParseStateType(string value) {
        if (value.Equals("connect", StringComparison.OrdinalIgnoreCase)) return ReactorStateType.Connect;
        if (value.Equals("sleep", StringComparison.OrdinalIgnoreCase)) return ReactorStateType.Sleep;
        if (value.Equals("reward", StringComparison.OrdinalIgnoreCase)) return ReactorStateType.Reward;
        return ReactorStateType.None;
    }

    // <items><v itemID="" count="" grade="" /></items>
    private static ReactorTable.Reward[] ParseRewards(string xml) {
        if (string.IsNullOrWhiteSpace(xml)) {
            return [];
        }

        var document = new XmlDocument();
        document.LoadXml(xml);
        var rewards = new List<ReactorTable.Reward>();
        foreach (XmlElement node in document.DocumentElement?.ChildNodes.OfType<XmlElement>().Where(child => child.Name == "v") ?? []) {
            if (!TryInt(node.GetAttribute("itemID"), out int itemId)) {
                continue;
            }
            rewards.Add(new ReactorTable.Reward(itemId, Int(node.GetAttribute("grade"), 1), Int(node.GetAttribute("count"), 1)));
        }
        return rewards.ToArray();
    }

    private static IEnumerable<XmlElement> Elements(XmlDocument document, string path) {
        return document.SelectNodes(path)?.OfType<XmlElement>() ?? [];
    }

    private static bool TryInt(string value, out int result) {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    private static int Int(string value, int fallback) {
        return TryInt(value, out int result) ? result : fallback;
    }
}
