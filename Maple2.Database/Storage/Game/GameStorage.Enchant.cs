using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    private Item RepairEnchant(Item item) {
        if (item.Enchant is { Enchants: > 0 } && item.Enchant.BasicOptions.Values.All(option => option.Value == 0)) {
            RepairEnchant(serverTableMetadata.EnchantOptionTable, item);
        }
        if (item.LimitBreak is { Level: > 0 }) {
            RepairLimitBreak(serverTableMetadata.UnlimitedEnchantOptionTable, item);
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

    // A limit break made while a basic rate was stored as 0 holds only the flat part of each basic
    // option. Each level adds its table row, so the sum over the item's levels gives the options back.
    // Only an attribute that has a rate in the table and holds exactly its flat sum is rewritten.
    public static void RepairLimitBreak(UnlimitedEnchantOptionTable table, Item item) {
        if (item.LimitBreak is not { Level: > 0 } || !table.Entries.TryGetValue(item.Type.Type, out Dictionary<int, UnlimitedEnchantOptionTable.Option>? levels)) {
            return;
        }

        var flat = new Dictionary<BasicAttribute, int>();
        var rate = new Dictionary<BasicAttribute, int>();
        for (int level = 1; level <= item.LimitBreak.Level; level++) {
            if (!levels.TryGetValue(level, out UnlimitedEnchantOptionTable.Option? option)) {
                // The table does not cover this item's level: what it holds cannot be checked.
                return;
            }
            foreach ((BasicAttribute attribute, int value) in option.Values) {
                flat[attribute] = flat.GetValueOrDefault(attribute) + value;
            }
            foreach ((BasicAttribute attribute, float value) in option.Rates) {
                rate[attribute] = rate.GetValueOrDefault(attribute) + new BasicOption(value).Value;
            }
        }
        foreach ((BasicAttribute attribute, int sum) in rate) {
            int stored = item.LimitBreak.BasicOptions.TryGetValue(attribute, out BasicOption existing) ? existing.Value : 0;
            int flatSum = flat.GetValueOrDefault(attribute);
            if (sum != 0 && stored == flatSum) {
                item.LimitBreak.BasicOptions[attribute] = new BasicOption(flatSum + sum);
            }
        }
    }
}
