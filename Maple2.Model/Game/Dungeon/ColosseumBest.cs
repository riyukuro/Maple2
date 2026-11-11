using Maple2.Model.Metadata;

namespace Maple2.Model.Game.Dungeon;

// A character's best Colosseum run of a season: the rounds cleared, the run's seconds and when it was set (epoch
// seconds). A run replaces it with more rounds, or the same rounds in fewer seconds: the first run sets it, an
// equal run keeps the older record.
public record ColosseumBest(int Rounds, int Seconds, long SetTime) {
    public static bool Replaces(ColosseumBest? best, int rounds, int seconds) =>
        best == null || rounds > best.Rounds || (rounds == best.Rounds && seconds < best.Seconds);

    // The Colosseum season (seasondata_bosscolosseum, weekly) that holds the time: start <= time < end. 0 when none does.
    public static int SeasonAt(IReadOnlyDictionary<int, SeasonDataTable.Entry> seasons, DateTime time) {
        foreach (SeasonDataTable.Entry season in seasons.Values) {
            if (season.StartTime <= time && time < season.EndTime) {
                return season.Id;
            }
        }
        return 0;
    }
}
