using System.Xml;
using System.Xml.Serialization;
using Maple2.File.Parser.Xml.Item;

namespace Maple2.File.Ingest.Utils;

/// <summary>
/// The ItemData of an item that only the played client has, read from that client's itemdata/&lt;nnn&gt;.xml. Its
/// &lt;environment&gt; holds the sections the data folder's item/&lt;id&gt;.xml has, with these differences
/// (CItemData::Load, commonfiles/itemdata.cpp:330-420):
/// - the slots and customize sections are in itemmodel/&lt;nnn&gt;.xml, found by &lt;tool itemPreset&gt;;
/// - the weapon skill, ride, pet or shop id is &lt;property param1&gt;, by the item's type;
/// - an item box names its box id in &lt;function param1&gt; (and param2), the server's "0,1,0,&lt;id&gt;" parameter;
///   an instrument names its instrument in &lt;property param1&gt;;
/// - &lt;property sell price&gt; may hold one price, which the client uses at every rarity;
/// - the item is a skin when its skinType is not 0 (CItemData::HasSkinType, itemdata.cpp:758);
/// - &lt;customize defaultColorIndex&gt; may list colors the client cycles through; the server keeps the first.
/// The &lt;option&gt; is left out: its constantID and randomID are the client's option rows, which the server's
/// option tables do not have, so such an item has no options rather than ones made up for it.
/// An attribute the played file leaves out holds the client's default, which is the parser's default for every
/// field the server reads (CItemData(), itemdata.cpp:318; ItemLimit(), itemdatastruct.cpp:559; ItemProperty(),
/// itemdatastruct.cpp:1114), except the scale range of a slot, which is 0 to 1 when not named.
/// </summary>
public static class PlayedItemData {
    private static readonly XmlSerializer Serializer = new(typeof(ItemData), new XmlRootAttribute("environment") { Namespace = "" });

    // The section and attribute that <property param1> fills, by the item's type (CItemData::Load reads param1 and
    // param2 as plain ints; the type decides what they name): the weapon skill of a type 1 item of subtype 0x50 to
    // 0x53 (FUN_14053dc90, itemdata.cpp:1142), the pet (GetPetId), ride (GetRideeId) and shop (GetShopId) ids. A
    // type 5 item's param1 is its glide id (GetGlideItemId, itemdata.cpp:1187), which the server does not read.
    private static readonly Dictionary<int, (string Element, string Attribute)> Param1Targets = new() {
        [1] = ("objectWeaponSkill", "skillID"),
        [9] = ("ride", "rideMonster"),
        [11] = ("pet", "petID"),
        [20] = ("Shop", "systemShopID"),
    };

    private static bool Param1Applies(int type, string subtype) =>
        type != 1 || int.TryParse(subtype, out int value) && value is >= 0x50 and <= 0x53;

    public const int SellRarities = 6;

    /// <param name="environment">The &lt;environment&gt; of the played client's &lt;item&gt; that the client keeps
    /// (PlayedNpcStats.SelectEnvironment).</param>
    /// <param name="model">The &lt;ItemModel&gt; of itemmodel/&lt;nnn&gt;.xml named by its &lt;tool itemPreset&gt;, if any.</param>
    public static ItemData Read(XmlElement environment, XmlElement? model) {
        var document = new XmlDocument();
        XmlElement root = document.CreateElement("environment");
        document.AppendChild(root);

        foreach (XmlElement child in environment.ChildNodes.OfType<XmlElement>()) {
            if (child.Name == "option") {
                continue;
            }
            root.AppendChild(document.ImportNode(child, true));
        }
        if (model != null) {
            foreach (string name in new[] { "slots", "customize" }) {
                if (root[name] == null && model[name] is { } section) {
                    root.AppendChild(document.ImportNode(section, true));
                }
            }
        }
        // The client splits defaultColorIndex into a list and cycles through it every colorDelay
        // (ItemModelHead::Load, itemdatastruct.cpp:937); the server keeps one default color, the first.
        if (root["customize"] is { } customize && customize.GetAttribute("defaultColorIndex").Split(',') is { Length: > 1 } colors) {
            customize.SetAttribute("defaultColorIndex", colors[0].Trim());
        }
        foreach (XmlElement scale in root.SelectNodes("slots/slot/scale")!.OfType<XmlElement>()) {
            if (!scale.HasAttribute("min")) scale.SetAttribute("min", "0");
            if (!scale.HasAttribute("max")) scale.SetAttribute("max", "1");
        }

        XmlElement? property = root["property"];
        if (property != null) {
            if (property["sell"] is { } sell) {
                string[] prices = sell.GetAttribute("price").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (prices.Length == 1) {
                    sell.SetAttribute("price", string.Join(",", Enumerable.Repeat(prices[0], SellRarities)));
                }
            }
            if (int.TryParse(property.GetAttribute("skinType"), out int skinType) && skinType != 0) {
                property.SetAttribute("skin", "1");
            } else {
                property.RemoveAttribute("skin");
            }
            string param1 = property.GetAttribute("param1");
            if (param1.Length > 0 && int.TryParse(property.GetAttribute("type"), out int type)
                && Param1Targets.TryGetValue(type, out (string Element, string Attribute) target)
                && Param1Applies(type, property.GetAttribute("subtype"))) {
                XmlElement? element = root[target.Element];
                if (element == null) {
                    element = document.CreateElement(target.Element);
                    root.AppendChild(element);
                }
                if (!element.HasAttribute(target.Attribute)) {
                    element.SetAttribute(target.Attribute, param1);
                }
            }
        }

        if (root["function"] is { } function && !function.HasAttribute("parameter")) {
            string parameter = function.GetAttribute("name") switch {
                "OpenItemBox" when function.HasAttribute("param1") => function.HasAttribute("param2")
                    ? $"0,1,0,{function.GetAttribute("param1")},{function.GetAttribute("param2")}"
                    : $"0,1,0,{function.GetAttribute("param1")}",
                "OpenInstrument" => property?.GetAttribute("param1") ?? "",
                _ => "",
            };
            function.SetAttribute("parameter", parameter);
        }

        using var reader = new XmlNodeReader(root);
        return (ItemData) Serializer.Deserialize(reader)!;
    }

    /// <summary>Whether the environment names client option rows (left out by <see cref="Read"/>).</summary>
    public static bool HasClientOption(XmlElement environment) =>
        environment["option"] is { } option
        && (NonZero(option.GetAttribute("constantID")) || NonZero(option.GetAttribute("randomID")));

    private static bool NonZero(string value) => value.Length > 0 && value != "0";
}
