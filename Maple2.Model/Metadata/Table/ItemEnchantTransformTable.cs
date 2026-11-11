namespace Maple2.Model.Metadata;

// table/itemenchanttransform.xml, rows already filtered by locale. The client offers a gear item for the
// transform when a row matches its id, equip slot, enchant level and rarity; LimitLevel is not in the client's
// key (see Maple2.Server.Game.Util.EnchantTransform.Find), and of two rows with the same key the last wins.
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
