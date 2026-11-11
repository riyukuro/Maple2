namespace Maple2.Model.Metadata;

// table/petevolution.xml: the evolution points a pet needs before it can evolve, by the pet item's level
// limit (requireItemLevel) and rarity (Grade1-4). The client reads the same columns
// (CExpTable::LoadPetEvolution); a rarity with no column needs 0 points, which means it does not evolve.
public record PetEvolutionTable(IReadOnlyDictionary<int, PetEvolutionTable.Entry> Entries) : Table {
    public record Entry(
        int RequireItemLevel,
        int[] Grades); // index = rarity 1-4

    public int Need(int levelLimit, int rarity) {
        if (!Entries.TryGetValue(levelLimit, out Entry? entry)) {
            return 0;
        }
        if (rarity < 1 || rarity > entry.Grades.Length) {
            return 0;
        }
        return entry.Grades[rarity - 1];
    }
}
