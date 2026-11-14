namespace Maple2.Model.Metadata;

// The client's table/enchantlimittable.xml (CItemEnchantLimitTable::Load, commonfiles/itemenchantlimittable.cpp): by
// gear subtype (<limit slot>), the band of required levels (minLv, maxLv) an item may be enchanted in and the grade it
// stops at (maxGrade). A subtype without a row is never enchanted.
public record EnchantLimitTable(IReadOnlyDictionary<int, EnchantLimitTable.Limit> Slots) : ServerTable {
    public record Limit(int MinLevel, int MaxLevel, int MaxGrade);
}
