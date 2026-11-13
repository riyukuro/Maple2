using Maple2.Model.Enum;

namespace Maple2.Model.Metadata;

public record GemstoneUpgradeTable(IReadOnlyDictionary<int, GemstoneUpgradeTable.Entry> Entries) : Table {
    // EquipPart: the row's equipPart, 4 for an outfit (skin) gem, 3 for the others (ItemSocketRules.GemstoneUpgradeRate).
    public record Entry(short Level, int NextItemId, IReadOnlyList<Ingredient> Ingredients, int EquipPart = 0);
    // An ingredient is "id:Tag" (any item with the tag) or a bare item id (that item, e.g. another copy of the gem):
    // the client counts by the tag when there is one, else by the id (GetOwnedItemCount).
    public record Ingredient(ItemTag ItemTag, int Amount, int ItemId = 0);
}
