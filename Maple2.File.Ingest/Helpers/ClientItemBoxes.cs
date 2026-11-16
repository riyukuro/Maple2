using System.Xml;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;

namespace Maple2.File.Ingest.Helpers;

// The box an item opens, as the played client names it: <function name="OpenItemBox" param1="box" param2="amount" />
// (ItemFunction::GetBoxId returns param1, itemdatastruct.cpp). The data folder's pack is a development pack, and for
// some items both have, its function names another box or none: the Level Up Mission Box 30000928 has
// "0,1,30000301,0" (a fixed Time Drum) where the client names box 30003, the global box of the mission level-up
// rewards (experience potions and vouchers by level), and other boxes name an older individual box id. The client's
// box is the item's then. A global box id (a globalDropItemBox row of the server pack) goes in OpenItemBox's global
// place (Arg0), any other in its individual place (Arg3), as ItemBoxManager reads them.
public static class ClientItemBoxes {
    public readonly record struct Box(int Id, int Amount);

    // The played client's OpenItemBox box of each item, for items whose available environments agree on it.
    public static Dictionary<int, Box> Read(IEnumerable<XmlDocument> itemFiles, Func<XmlElement, bool> available) {
        var boxes = new Dictionary<int, Box>();
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
                    XmlElement? function = environment["function"];
                    Box? box = function?.GetAttribute("name") == "OpenItemBox" && int.TryParse(function.GetAttribute("param1"), out int boxId) && boxId > 0
                        ? new Box(boxId, int.TryParse(function.GetAttribute("param2"), out int amount) ? amount : 0)
                        : null;
                    if (box == null || (boxes.TryGetValue(id, out Box other) && other != box)) {
                        mixed.Add(id);
                        continue;
                    }
                    boxes[id] = box.Value;
                }
            }
        }
        foreach (int id in mixed) {
            boxes.Remove(id);
        }
        return boxes;
    }

    // Whether the pack's function already opens the box: OpenItemBox naming it as its global or individual box.
    public static bool Names(string name, string parameter, int box) {
        if (name != "OpenItemBox") {
            return false;
        }
        if (parameter.TrimStart().StartsWith('<')) {
            var document = new XmlDocument();
            try {
                document.LoadXml(parameter.Trim().Replace("< v ", "<v "));
            } catch (XmlException) {
                return false;
            }
            XmlElement root = document.DocumentElement!;
            return root.GetAttribute("globalDropBoxId") == box.ToString() || root.GetAttribute("individualDropBoxId") == box.ToString();
        }
        int[] args = ItemBoxRules.FunctionArgs(parameter);
        return ItemBoxRules.Arg(args, 0) == box || ItemBoxRules.Arg(args, 3) == box;
    }

    // The OpenItemBox parameter of the client's box, in ItemBoxManager's places (an amount goes in Arg4).
    public static string Parameter(Box box, bool global) {
        string parameter = global ? $"{box.Id},1,0,0" : $"0,1,0,{box.Id}";
        return box.Amount > 0 ? $"{parameter},{box.Amount}" : parameter;
    }

    // The fields the server reads of an item whose box the played client names, taken from the played client's item
    // (client: the item built from its played environment, as an item only the client has). The release client
    // reuses some of those ids for another item (20301431 is the client's Halloween weapon box, a mini-game
    // ticket in the pack), and the client shows and counts its own row: its name (searches, logs), stack size,
    // category (meret market tab), sell prices, life, the level, transfer and shop-sale limits, and the repack and
    // trade-count deduction it reads (ItemLimit and ItemProperty::Load, itemdatastruct.cpp). The bag tab type and
    // the rank are the client's already (PlayedClientItems.ClientType, ClientRank). The rest stays the pack's: the
    // client reads no tradableCount, tradeLimitRank or priceCustom, so its row has only defaults for them. A client
    // item with no name keeps the pack's name.
    public static ItemMetadata ClientFields(ItemMetadata pack, ItemMetadata client) {
        ItemMetadata taken = pack with {
            Name = string.IsNullOrEmpty(client.Name) ? pack.Name : client.Name,
            Life = client.Life,
            Property = pack.Property with {
                SlotMax = client.Property.SlotMax,
                Category = client.Property.Category,
                SellPrices = Values(pack.Property.SellPrices, client.Property.SellPrices),
                TradableCountDeduction = client.Property.TradableCountDeduction,
                RepackCount = client.Property.RepackCount,
                RepackConsumeCount = client.Property.RepackConsumeCount,
                RepackScrollIds = Values(pack.Property.RepackScrollIds, client.Property.RepackScrollIds),
            },
            Limit = pack.Limit with {
                Level = client.Limit.Level,
                TransferType = client.Limit.TransferType,
                ShopSell = client.Limit.ShopSell,
            },
        };
        return taken == pack ? pack : taken;
    }

    // The client's list, or the pack's when they hold the same values (a missing entry is 0, as Shop.ListPrice reads
    // a rarity past the list); records compare arrays by reference, so the same values keep the same array.
    private static T[] Values<T>(T[] pack, T[]? client) where T : struct, IEquatable<T> {
        client ??= [];
        int length = Math.Max(pack.Length, client.Length);
        for (int index = 0; index < length; index++) {
            T a = index < pack.Length ? pack[index] : default;
            T b = index < client.Length ? client[index] : default;
            if (!a.Equals(b)) {
                return client;
            }
        }
        return pack;
    }
}
