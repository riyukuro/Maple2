using Maple2.Model.Enum;

namespace Maple2.Model.Metadata;

// The client's inventory tab names, by the client's tab number (0-14): the stringcommon value of each tab's key
// (InventoryTab.StringKey, as GetInventoryTabName reads them). The client fills the tab of s_err_inventory_tab_full
// with this name; a server notice arg is plain text, so the server sends the name itself.
public record InventoryTabNameTable(IReadOnlyDictionary<int, string> Names) : Table {
    public static readonly InventoryTabNameTable Empty = new(new Dictionary<int, string>());

    // The name of the tab an item of this type goes to. Chosen here: a tab without a name (a metadata db ingested
    // before this table, or a key the strings lack) gets the server's type name, e.g. "Gear".
    public string Name(InventoryType type) {
        return Names.TryGetValue(type.ToClient(), out string? name) && !string.IsNullOrEmpty(name) ? name : type.ToString();
    }
}
