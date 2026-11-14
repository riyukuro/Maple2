namespace Maple2.Model.Metadata;

// table/paybacktable.xml as PaybackTable::Load reads it (commonfiles/paybacktable.cpp): the rows the feature and
// locale filter keep, each an adventure level gained in the month and its weight (payback_percent); a level 0 row is
// skipped and the rest sorted by level. The first_month_* columns are read by no client code (GetLevels ignores its
// context), so they are not kept.
public record PaybackTable(IReadOnlyList<PaybackTable.Level> Levels) : Table {
    public static readonly PaybackTable Empty = new(Array.Empty<Level>());

    public record Level(int AdventureLevel, int Percent);
}
