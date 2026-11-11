using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    private Item RepairEnchant(Item item) {
        if (item.Enchant is { Enchants: > 0 } && item.Enchant.BasicOptions.Values.All(option => option.Value == 0)) {
            RepairEnchant(serverTableMetadata.EnchantOptionTable, item);
        }
        return item;
    }

    // An item enchanted while a basic rate was stored as 0 holds 0 for every enchant option. The options
    // are a function of the item and its enchant level, so they are rebuilt from the table: each level
    // adds rate * 1000, the int the client reads as a rate.
    public static void RepairEnchant(EnchantOptionTable table, Item item) {
        if (item.Enchant is not { Enchants: > 0 } || item.Enchant.BasicOptions.Values.Any(option => option.Value != 0)) {
            return;
        }

        var options = new Dictionary<BasicAttribute, BasicOption>();
        for (int level = 1; level <= Math.Min(item.Enchant.Enchants, 15); level++) {
            EnchantOptionMetadata? entry = table.Entries.Values.FirstOrDefault(
                e => e.Slot == item.Type.Type &&
                     e.EnchantLevel == level &&
                     e.Rarity == item.Rarity &&
                     e.MinLevel <= item.Metadata.Limit.Level &&
                     e.MaxLevel >= item.Metadata.Limit.Level);
            if (entry == null) {
                continue;
            }
            foreach (BasicAttribute attribute in entry.Attributes) {
                options[attribute] = options.GetValueOrDefault(attribute) + new BasicOption(entry.Rate);
            }
        }
        foreach ((BasicAttribute attribute, BasicOption option) in options) {
            item.Enchant.BasicOptions[attribute] = option;
        }
    }
}
