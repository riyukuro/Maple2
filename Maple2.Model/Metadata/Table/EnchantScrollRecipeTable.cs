using Maple2.Model.Game;

namespace Maple2.Model.Metadata;

// table/enchantscrollrecipe.xml: the magic scroll recipes of the mastery window's scroll tab
// (CEnchantScrollRecipe::Read). The reward scroll and the reward items are "id,rank,count"; an ingredient is
// "id[:tag],rank,count".
public record EnchantScrollRecipeTable(IReadOnlyDictionary<int, EnchantScrollRecipeTable.Entry> Entries) : Table {
    public record Entry(
        int Id,
        int RecipeType,
        int Slot,
        ItemComponent? RewardScroll,
        long RequiredMeso,
        IReadOnlyList<ItemComponent> RequiredItems,
        IReadOnlyList<ItemComponent> RewardItems);
}
