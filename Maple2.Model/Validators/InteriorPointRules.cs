using Maple2.Model.Enum;

namespace Maple2.Model.Validators;

// interior_point: the decoration score of a home. The rows read "raise the decoration score" (89050037, 89050067,
// 89050097 for 700, 300 and 100) and "reach 1,100 decoration score" (trophy 23500024). The counter is
// the score itself, reported when a cube is placed, so it moves in steps of 50, 100 and 200, the points of single
// decoration goals.
public static class InteriorPointRules {
    // The number of cubes of a category that meets its decoration goal.
    private static readonly Dictionary<HousingCategory, int> Goals = new() {
        { HousingCategory.Bed, 1 },
        { HousingCategory.Table, 1 },
        { HousingCategory.SofasChairs, 2 },
        { HousingCategory.Storage, 1 },
        { HousingCategory.WallDecoration, 1 },
        { HousingCategory.WallTiles, 3 },
        { HousingCategory.Bathroom, 1 },
        { HousingCategory.Lighting, 1 },
        { HousingCategory.Electronics, 1 },
        { HousingCategory.Fences, 2 },
        { HousingCategory.NaturalTerrain, 4 },
    };

    // The score of a home from its cubes per category: 100 for each goal met, 50 for tables and storage, 200 for
    // electronics; 1,100 with every goal met.
    public static int Score(IReadOnlyDictionary<HousingCategory, int> cubes) {
        int score = 0;
        foreach ((HousingCategory category, int goal) in Goals) {
            if (!cubes.TryGetValue(category, out int count) || count < goal) {
                continue;
            }
            score += category switch {
                HousingCategory.Table or HousingCategory.Storage => 50,
                HousingCategory.Electronics => 200,
                _ => 100,
            };
        }
        return score;
    }

    // The counter is the score, not a sum of scores. Chosen here: it keeps the highest score when furniture is
    // taken away.
    public static long Counter(long counter, long score) => Math.Max(counter, score);

    // A score moves a counter only when it is above it.
    public static bool Advances(long counter, long score) => score > counter;
}
