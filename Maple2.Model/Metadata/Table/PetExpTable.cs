namespace Maple2.Model.Metadata;

// table/petexp.xml: the exp a pet needs to go from a level to the next, by level and pet rarity
// (normal, rare, elite, excellent, legendary, artifact = rarity 1-6). The client keeps a [7][51] array
// (CExpTable::LoadPetExp); a missing row or column reads as 0.
public record PetExpTable(IReadOnlyDictionary<int, long[]> Entries) : Table {
    public const int Columns = 6;

    // CExpTable::GetPetExp: rarity 7 or more and level above 50 need long.MaxValue (no level up);
    // otherwise the row value, scaled by calc_petExp for the pet item's level limit.
    public long Need(int level, int rarity, int levelLimit) {
        if (rarity >= 7 || level > 50) {
            return long.MaxValue;
        }
        long value = 0;
        if (rarity >= 1 && Entries.TryGetValue(level, out long[]? row) && rarity <= row.Length) {
            value = row[rarity - 1];
        }
        return PetExpScale(value, levelLimit);
    }

    // calc_petExp in the client's Lua: x1 for a level 50 item, x1.5 for 60, x2.25 for 70, else x1.
    public static long PetExpScale(long exp, int levelLimit) => (long) (exp * LevelMultiplier(levelLimit));

    public static double LevelMultiplier(int levelLimit) => levelLimit switch {
        60 => 1.5,
        70 => 2.25,
        _ => 1,
    };
}
