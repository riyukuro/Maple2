using System.Xml;
using Maple2.Model.Metadata;

namespace Maple2.File.Ingest.Helpers;

// The survival pass rewards of the server's own NA tables (table/na in the server's Xml) that name an item the played
// client has as another item. The release client reuses some global ids: the paid pass gives the Royale Experience
// Tonics 20301787-20301789 (SurvivalLevelExp in the server pack), which the KMS2 client has as a Starlight Festival
// firework (FestivalEvent) and two titles (TitleScroll). The restore patch's survivalPassReward_paid.xml blanks those
// 11 rewards and keeps the slots, so the server leaves the same rewards out and keeps the levels: it never gives the
// client's firework or titles as a pass reward. An item is another item when the played client's function name is
// not the server pack's (both from their available environments); without a played client nothing is dropped.
public static class SurvivalPassRewards {
    // The reward types whose ids are items (MapleSurvivalProgress gives them; additionalEffect ids are buffs).
    public static bool NamesItems(string type) => type is "item" or "genderItem";

    // The function name of an item element's available environments ("" for an environment without one); null when
    // none is available or they disagree.
    public static string? FunctionName(XmlElement item, Func<XmlElement, bool> available) {
        string? name = null;
        foreach (XmlElement environment in item.ChildNodes.OfType<XmlElement>().Where(child => child.Name == "environment" && available(child))) {
            string own = environment["function"]?.GetAttribute("name") ?? "";
            if (name != null && name != own) {
                return null;
            }
            name = own;
        }
        return name;
    }

    // The played client's function name of each item (itemdata/<nnn>.xml), for items whose available environments agree.
    public static Dictionary<int, string> PlayedFunctions(IEnumerable<XmlDocument> itemFiles, Func<XmlElement, bool> available) {
        var names = new Dictionary<int, string>();
        foreach (XmlDocument file in itemFiles) {
            foreach (XmlElement item in file.SelectNodes("ms2/item")?.OfType<XmlElement>() ?? []) {
                if (int.TryParse(item.GetAttribute("id"), out int id) && id != 0 && FunctionName(item, available) is string name) {
                    names[id] = name;
                }
            }
        }
        return names;
    }

    // The server pack's item file of an id (item/2/03/20301787.xml).
    public static string ServerItemFile(int id) {
        string digits = id.ToString("D8");
        return $"item/{digits[0]}/{digits.Substring(1, 2)}/{digits}.xml";
    }

    // Whether the played client has the item as another item than the server pack's.
    public static bool Reused(int id, IReadOnlyDictionary<int, string> played, Func<int, string?> server) =>
        played.TryGetValue(id, out string? client) && server(id) is string own && client != own;

    // The rewards without those that name a reused item; dropped counts the rewards left out.
    public static List<MapleSurvivalTable.Reward> Keep(IEnumerable<MapleSurvivalTable.Reward> rewards, Func<int, bool> reused, ref int dropped) {
        var kept = new List<MapleSurvivalTable.Reward>();
        foreach (MapleSurvivalTable.Reward reward in rewards) {
            if (NamesItems(reward.Type) && reward.Ids.Any(reused)) {
                dropped++;
                continue;
            }
            kept.Add(reward);
        }
        return kept;
    }
}
