namespace Maple2.Model.Metadata;

// table/itemenchanttransform.xml, rows already filtered by locale. The client offers a gear item for the
// transfer when one row matches its id, equip slot, enchant level, rarity and required level (first row wins).
public record ItemEnchantTransformTable(IReadOnlyList<ItemEnchantTransformTable.Entry> Entries) : Table {
    public record Entry(
        int Rarity,
        int LimitLevel,
        int EnchantLevel,
        int[] Slots,
        int[] ItemIds,
        long NeedMeso,
        Output[] Outputs);

    public record Output(int ItemId, int Rarity, int Amount);
}
