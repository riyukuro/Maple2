using Maple2.Model.Enum;
using Maple2.Model.Game;

namespace Maple2.Model.Metadata;

public record EnchantScrollTable(IReadOnlyDictionary<int, EnchantScrollMetadata> Entries) : Table;

public record EnchantScrollMetadata(
    EnchantScrollType Type,
    short MinLevel,
    short MaxLevel,
    int[] Enchants,
    int[] ItemTypes,
    int[] Rarities,
    // From the server pack's copy of the table (successProp): one weight per entry of Enchants, or null for data
    // ingested before it was read (then each grade is equally likely).
    int[]? Rates = null,
    // From the server pack's copy (disableBreak), sent to the client's scroll dialog.
    bool DisableBreak = false);

public record ItemExchangeScrollTable(IReadOnlyDictionary<int, ItemExchangeScrollMetadata> Entries) : Table;

public record ItemExchangeScrollMetadata(
    ItemComponent RecipeScroll,
    ItemComponent RewardItem,
    int TradeCountDeduction,
    long RequiredMeso,
    IReadOnlyList<ItemComponent> RequiredItems);

public record ItemRemakeScrollTable(IReadOnlyDictionary<int, ItemRemakeScrollMetadata> Entries) : Table;

public record ItemRemakeScrollMetadata(
    short MinLevel,
    short MaxLevel,
    int[] ItemTypes,
    int[] Rarities,
    bool RollAttribute,
    RollValueType RollValueType,
    bool OnlyPet);

public enum RollValueType {
    None = 0,
    Normal = 1,
    HigherEnd = 2,
}

public record ItemRepackingScrollTable(IReadOnlyDictionary<int, ItemRepackingScrollMetadata> Entries) : Table;

public record ItemRepackingScrollMetadata(
    short MinLevel,
    short MaxLevel,
    int[] ItemTypes,
    int[] Rarities,
    bool IsPet);

public record ItemSocketScrollTable(IReadOnlyDictionary<int, ItemSocketScrollMetadata> Entries) : Table;

public record ItemSocketScrollMetadata(
    short MinLevel,
    short MaxLevel,
    int[] ItemTypes,
    int[] Rarities,
    byte SocketCount,
    int TradableCountDeduction);
