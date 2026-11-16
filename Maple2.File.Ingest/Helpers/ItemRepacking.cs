using System.Xml;

namespace Maple2.File.Ingest.Helpers;

// The repack scroll ids of an item as ItemProperty::Load reads them (itemdatastruct.cpp:1147-1155): its rePackingScrollID,
// and under the GlobalTransferType feature (0x84) the globalRePackingScrollID ids appended after them (XmlSplit only
// adds to the array, xmlattr.h); an empty list takes any scroll (CItemRePackingScrollTable, itemrepackingscrolldata.cpp:159).
// The client checks its own item data: the data folder's pack (a development pack) often holds the list only in the
// global attribute (11200072: the played client's rePackingScrollID "10000001,10000003" is the pack's
// globalRePackingScrollID), so an item the played client has takes the played client's list (Played).
public static class ItemRepacking {
    public static int[] ScrollIds(int[]? own, int[]? global, bool globalTransferType) =>
        globalTransferType && global is { Length: > 0 } ? [.. own ?? [], .. global] : own ?? [];

    // The played client's scroll ids of each item, for items whose available environments agree on them.
    public static Dictionary<int, int[]> Played(IEnumerable<XmlDocument> itemFiles, Func<XmlElement, bool> available, bool globalTransferType) {
        var lists = new Dictionary<int, int[]>();
        var mixed = new HashSet<int>();
        foreach (XmlDocument file in itemFiles) {
            XmlNodeList? items = file.SelectNodes("ms2/item");
            if (items == null) {
                continue;
            }
            foreach (XmlElement item in items.OfType<XmlElement>()) {
                if (!int.TryParse(item.GetAttribute("id"), out int id) || id == 0) {
                    continue;
                }
                foreach (XmlElement environment in item.ChildNodes.OfType<XmlElement>().Where(child => child.Name == "environment" && available(child))) {
                    XmlElement? property = environment["property"];
                    int[] list = ScrollIds(Ids(property?.GetAttribute("rePackingScrollID")),
                        property?.HasAttribute("globalRePackingScrollID") == true ? Ids(property.GetAttribute("globalRePackingScrollID")) : null, globalTransferType);
                    if (lists.TryGetValue(id, out int[]? other) && !other.SequenceEqual(list)) {
                        mixed.Add(id);
                    }
                    lists[id] = list;
                }
            }
        }
        foreach (int id in mixed) {
            lists.Remove(id);
        }
        return lists;
    }

    private static int[] Ids(string? value) =>
        (value ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(text => int.TryParse(text, out int id) ? id : 0).Where(id => id != 0).ToArray();
}
