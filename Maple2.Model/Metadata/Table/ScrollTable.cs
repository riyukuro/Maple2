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
    IReadOnlyList<ItemComponent> RequiredItems,
    // The row's type (CExchangeItemInfo::m_eType, itemexchangescrolltable.cpp:108): an upgrade row takes a target
    // item (RequireEquip), which may be enchanted up to MaxGrade, and gives the reward in its place.
    ItemExchangeScrollType Type = ItemExchangeScrollType.Exchange,
    int MaxGrade = 0,
    ItemComponent? RequireEquip = null);

// The client's row types (UIItemExchangeScrollDialog::FUN_140f52880, uiitemexchangescrolldialog.cpp: 1, 2, 3).
public enum ItemExchangeScrollType {
    Exchange = 1,
    Upgrade = 2,
    Trading = 3,
}

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
    byte SocketCount, // the server pack's openCount
    int TradableCountDeduction,
    // From the server pack's copy of the table (successProb): the unlock's chance out of 10000. Data ingested before
    // it was read has 10000, an unlock that always opens.
    int SuccessRate = 10000);
