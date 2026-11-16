using System.Xml;
using Maple2.File.IO;
using Maple2.Model.Validators;

namespace Maple2.File.Ingest.Helpers;

// The individual drop boxes an item use opens, read from the item functions of the data pack's and the played
// client's item data (every environment: a box named by any row counts). Only these boxes get the played client's
// groups the server's rows lack (ClientDropBoxes): the client's rows have no weights or drop chances, so a monster or
// other field box stays on the server's rows. The places follow
// ItemBoxManager: OpenItemBox "global,?,item,box" (or its xml list's individualDropBoxId, or the played client's
// param1, which PlayedItemData reads as "0,1,0,param1"), SelectItemBox "count,box", OpenItemBoxWithKey
// "key,amount,item,?,?,box", OpenItemBoxLimitedQuantity individualDropBoxId, the Lullu and pet boxes' commonBoxId and
// unCommonBoxId (a list for the pet box). A gacha box names its box through the gacha table, so it is not here. A
// played client's param1 that is a global drop box (a globalDropItemBox row of the server pack: the mission boxes
// name 30003-30006) is not an individual box, as ClientItemBoxes puts it in OpenItemBox's global place.
public static class ItemBoxDropIds {
    public static HashSet<string> Collect(M2dReader? pack, M2dReader? played, IReadOnlySet<int>? globalBoxes = null) {
        var ids = new HashSet<string>();
        foreach (M2dReader reader in new[] { pack, played }.OfType<M2dReader>()) {
            foreach (var file in reader.Files.Where(file => IsItemFile(file.Name))) {
                XmlDocument document = reader.GetXmlDocument(file);
                foreach (XmlElement function in document.GetElementsByTagName("function").OfType<XmlElement>()) {
                    ids.UnionWith(Of(function.GetAttribute("name"), function.GetAttribute("parameter"), function.GetAttribute("param1"), globalBoxes));
                }
            }
        }
        return ids;
    }

    private static bool IsItemFile(string name) =>
        name.StartsWith("item/", StringComparison.OrdinalIgnoreCase) || name.StartsWith("itemdata/", StringComparison.OrdinalIgnoreCase);

    // The box ids one function names (none for a function that opens no individual box).
    public static IEnumerable<string> Of(string name, string parameter, string param1 = "", IReadOnlySet<int>? globalBoxes = null) {
        if (name is "OpenItemBox" && parameter.Length == 0 && param1.Length > 0) {
            int box = ItemBoxRules.Arg(ItemBoxRules.FunctionArgs(param1), 0);
            return globalBoxes != null && globalBoxes.Contains(box) ? [] : Positive(box);
        }
        if (parameter.TrimStart().StartsWith('<')) {
            return name switch {
                "OpenItemBox" or "OpenItemBoxLimitedQuantity" => Attributes(parameter, "individualDropBoxId"),
                "OpenItemBoxLullu" or "OpenItemBoxLulluSimple" or "OpenPetBox" => Attributes(parameter, "commonBoxId", "unCommonBoxId"),
                _ => [],
            };
        }
        int[] args = ItemBoxRules.FunctionArgs(parameter);
        return name switch {
            "OpenItemBox" => Positive(ItemBoxRules.Arg(args, 3)),
            "SelectItemBox" => Positive(ItemBoxRules.Arg(args, 1)),
            "OpenItemBoxWithKey" => Positive(ItemBoxRules.Arg(args, 5)),
            _ => [],
        };
    }

    private static IEnumerable<string> Positive(int id) => id > 0 ? [id.ToString()] : [];

    private static IEnumerable<string> Attributes(string parameter, params string[] names) {
        var document = new XmlDocument();
        try {
            document.LoadXml(parameter.Trim().Replace("< v ", "<v "));
        } catch (XmlException) {
            return [];
        }
        XmlElement root = document.DocumentElement!;
        // OpenPetBox lists several boxes in one attribute (ItemUseParameters.ParseIntArray).
        return names.SelectMany(name => root.GetAttribute(name).Split(',', StringSplitOptions.TrimEntries))
            .Where(value => int.TryParse(value, out int id) && id > 0).ToList();
    }
}
