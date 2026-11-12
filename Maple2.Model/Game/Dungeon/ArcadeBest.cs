using Maple2.Model.Metadata;

namespace Maple2.Model.Game.Dungeon;

// A character's best score of an arcade game in an arcade season and the time it was set (epoch seconds). A game
// replaces it only with a higher score: the first game sets it, an equal score keeps the older record.
public record ArcadeBest(int ArcadeId, int Score, long SetTime) {
    public static bool Replaces(ArcadeBest? best, int score) => best == null || score > best.Score;

    // The arcade season (seasondata, type 4, weekly) that holds the time: start <= time < end. 0 when none does,
    // as the client's CSeasonDataTable::GetCurrentID gives 0 without a current season.
    public static int SeasonAt(IReadOnlyDictionary<int, SeasonDataTable.Entry> seasons, DateTime time) => ColosseumBest.SeasonAt(seasons, time);
}
