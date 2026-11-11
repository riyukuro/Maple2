using System.Xml;
using Maple2.Model.Game;

namespace Maple2.Model.Metadata;

// The items the played client knows, read from that client's own item data (itemdata/<nnn>.xml). The client
// keeps an <item> only when one of its <environment> children passes the locale and feature test
// (CItemDataTable::LoadItemDataFile, commonfiles/itemdatatable.cpp:743, with FUN_14028f8d0,
// commonfiles/commonutil.cpp:1064); it finds no item data for any other id. Drop rows and reward items that
// name such an id are left out of the ingest when a played client is given.
public static class PlayedClientItems {
    public const string ItemFolder = "itemdata/";

    public static bool IsItemFile(string name) => name.StartsWith(ItemFolder, StringComparison.OrdinalIgnoreCase);

    // The ids of the item data files. available is the environment filter (feature and locale).
    public static HashSet<int> Ids(IEnumerable<XmlDocument> itemFiles, Func<XmlElement, bool> available) {
        var ids = new HashSet<int>();
        foreach (XmlDocument file in itemFiles) {
            XmlNodeList? items = file.SelectNodes("ms2/item");
            if (items == null) {
                continue;
            }
            foreach (XmlNode node in items) {
                if (node is not XmlElement item || !int.TryParse(item.GetAttribute("id"), out int id) || id == 0) {
                    continue;
                }
                foreach (XmlNode child in item.ChildNodes) {
                    if (child is XmlElement { Name: "environment" } environment && available(environment)) {
                        ids.Add(id);
                        break;
                    }
                }
            }
        }
        return ids;
    }

    // A row is kept when each item id it names (0 = none) is known; a row with a second item (itemID2, the
    // other gender's item) goes when either is unknown, as the server could hand out either.
    public static bool Known(IEnumerable<int> itemIds, IReadOnlySet<int> known) => itemIds.All(id => id == 0 || known.Contains(id));

    public record struct Count(int Dropped, int Kept) {
        public override string ToString() => $"{Dropped} rows dropped, {Kept} kept";
    }

    // The server's drop boxes (Server.m2d individualItemDrop) without rows of unknown items. A group left with no
    // rows goes, and a box left with no groups goes: opening its box item then fails before the box is used
    // (ItemBoxManager: a box id missing from the table is s_err_cannot_open_multi_itembox_inventory_fail), and a
    // monster drop from it drops nothing (ItemDropManager), where an empty box would use the box up for nothing.
    public static Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>> Keep(
            IReadOnlyDictionary<int, IDictionary<int, IndividualDropItemTable.Entry>> boxes, IReadOnlySet<int> known, out Count count, out int boxesDropped) {
        var results = new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>>();
        int dropped = 0, kept = 0;
        boxesDropped = 0;
        foreach ((int boxId, IDictionary<int, IndividualDropItemTable.Entry> groups) in boxes) {
            var keptGroups = new Dictionary<int, IndividualDropItemTable.Entry>();
            foreach ((int groupId, IndividualDropItemTable.Entry group) in groups) {
                List<IndividualDropItemTable.Item> items = group.Items.Where(item => Known(item.Ids, known)).ToList();
                dropped += group.Items.Count - items.Count;
                kept += items.Count;
                if (items.Count > 0) {
                    keptGroups.Add(groupId, group with { Items = items });
                }
            }
            if (keptGroups.Count > 0) {
                results.Add(boxId, keptGroups);
            } else {
                boxesDropped++;
            }
        }
        count = new Count(dropped, kept);
        return results;
    }

    // The client's drop tables (Xml.m2d table/individualitemdrop*.xml and their region copies), the same way.
    public static Dictionary<int, Dictionary<byte, IList<IndividualItemDropTable.Entry>>> Keep(
            IReadOnlyDictionary<int, Dictionary<byte, IList<IndividualItemDropTable.Entry>>> boxes, IReadOnlySet<int> known, out Count count, out int boxesDropped) {
        var results = new Dictionary<int, Dictionary<byte, IList<IndividualItemDropTable.Entry>>>();
        int dropped = 0, kept = 0;
        boxesDropped = 0;
        foreach ((int boxId, Dictionary<byte, IList<IndividualItemDropTable.Entry>> groups) in boxes) {
            var keptGroups = new Dictionary<byte, IList<IndividualItemDropTable.Entry>>();
            foreach ((byte groupId, IList<IndividualItemDropTable.Entry> rows) in groups) {
                List<IndividualItemDropTable.Entry> keptRows = rows.Where(row => Known(row.ItemIds, known)).ToList();
                dropped += rows.Count - keptRows.Count;
                kept += keptRows.Count;
                if (keptRows.Count > 0) {
                    keptGroups.Add(groupId, keptRows);
                }
            }
            if (keptGroups.Count > 0) {
                results.Add(boxId, keptGroups);
            } else {
                boxesDropped++;
            }
        }
        count = new Count(dropped, kept);
        return results;
    }

    // An NPC shop row (Server.m2d shop_game) is kept when its item and its payment item (paymentItemID, 0 = paid
    // otherwise) are known. The client reads each sold item of the shop list into a CItem
    // (CNpcShopProductCli::ReadPacket, client/ms2/npcshopproductbase.cpp:139) and goes on to CItem::ReadFromStream
    // (commonfiles/item.cpp:2655) even when CItem::FUN_140523c40 (item.cpp:2760) found no item data for the id, so
    // the shop window crashes the client on a row of an unknown item. The payment item is left out with it, as the
    // window could not name or count what the row costs. The shop itself stays, with the rows it has left.
    public static bool KnownShopItem(int itemId, int paymentItemId, IReadOnlySet<int> known, ref Count count) {
        bool keep = Known([itemId, paymentItemId], known);
        count = keep ? count with { Kept = count.Kept + 1 } : count with { Dropped = count.Dropped + 1 };
        return keep;
    }

    // Whether the server may create an item of this data: not when the played client lacks it (the client crashes
    // on the item packet, ItemMetadataProperty.NotInPlayedClient). Item factories return no item then, as for an
    // id without item data.
    public static bool CanCreate(ItemMetadata metadata) => !metadata.Property.NotInPlayedClient;

    // Reward content items (table/rewardcontentitemtable.xml) without unknown items. A level range left with no
    // items stays and gives none (GameSession adds each item of the range); its exp and meso come from other tables.
    public static RewardItem[] Keep(RewardItem[] items, IReadOnlySet<int> known, ref Count count) {
        RewardItem[] keptItems = items.Where(item => Known([item.ItemId], known)).ToArray();
        count = new Count(count.Dropped + items.Length - keptItems.Length, count.Kept + keptItems.Length);
        return keptItems;
    }
}
