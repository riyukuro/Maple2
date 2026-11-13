using Maple2.Model.Enum;

namespace Maple2.Model.Metadata;

public record GemstoneUpgradeTable(IReadOnlyDictionary<int, GemstoneUpgradeTable.Entry> Entries) : Table {
    // EquipPart: the row's equipPart, 4 for an outfit (skin) gem, 3 for the others (ItemSocketRules.GemstoneUpgradeRate).
    public record Entry(short Level, int NextItemId, IReadOnlyList<Ingredient> Ingredients, int EquipPart = 0);
    public record Ingredient(ItemTag ItemTag, int Amount);
}
