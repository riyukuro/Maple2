using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Model;

/// <summary>
/// The state of a Dark Stream run on its field, without the field, so the rules can be tested.
/// </summary>
/// <remarks>
/// The shipped scripts drive the run: start_game gives the number of rounds, each round is start_round, then
/// spawn_monster (the spawn points and the score they give), then the round's spawns are all dead, then
/// clear_round. The scripts have no fail and no dungeon clear: the run ends when the round start_game named is
/// cleared, and the end banner and the way out follow. The score of a spawn_monster is the score of its spawns:
/// a spawn gives it once all its monsters are dead (a time-attack script spawns the same spawn point again
/// after that, and so gives the score again).
/// </remarks>
public class DarkStreamGame {
    public int Rounds { get; }
    public int Round { get; private set; }
    // The script's damage penalty of the round. Kept, not applied: neither the client nor the scripts say what
    // it takes from the score.
    public int DamagePenalty { get; private set; }
    public int Score { get; private set; }
    public bool Ended { get; private set; }
    // The last round the script cleared.
    public int RoundsCleared { get; private set; }

    private readonly Dictionary<int, int> spawnScores = new();

    public DarkStreamGame(int rounds) {
        Rounds = rounds;
    }

    public void StartRound(int round, int damagePenalty) {
        Round = round;
        DamagePenalty = damagePenalty;
    }

    public void SetSpawnScore(int spawnId, int score) {
        spawnScores[spawnId] = score;
    }

    /// <summary>
    /// The last monster of a spawn point died: the spawn's score is added once.
    /// </summary>
    /// <returns>Whether the score changed.</returns>
    public bool SpawnCleared(int spawnId) {
        if (Ended || !spawnScores.Remove(spawnId, out int score)) {
            return false;
        }
        Score += score;
        return score != 0;
    }

    /// <summary>
    /// The script cleared a round.
    /// </summary>
    /// <returns>Whether it was the last round, which ends the run (once).</returns>
    public bool ClearRound(int round) {
        if (Ended) {
            return false;
        }
        RoundsCleared = Math.Max(RoundsCleared, round);
        if (round < Rounds) {
            return false;
        }
        Ended = true;
        return true;
    }

    public readonly record struct Reward(int Exp, int Meso, int Habi, IReadOnlyList<RewardItem> Items);

    /// <summary>
    /// The reward of a run (table/darkstream.xml): the row of the rounds cleared, or the row of the most rounds
    /// below them when the table has no row for them. The table gives the exp as a rate; the exp is the rate
    /// times the Dark Stream room's rewardExp, the rule the dungeon rooms' own rewardExpRate follows. Neither
    /// the client nor the table says so (the client reads rewardExp and the rate but uses neither, and the exp
    /// table has no Dark Stream row), so the rule is inferred.
    /// </summary>
    public static Reward? RewardOf(IReadOnlyDictionary<int, DarkStreamTable.Entry> table, int roundsCleared, long roomExp) {
        DarkStreamTable.Entry? entry = table.Values
            .Where(row => row.Round <= roundsCleared)
            .MaxBy(row => row.Round);
        if (entry == null) {
            return null;
        }
        long exp = (long) Math.Round(roomExp * (double) entry.ExpRate);
        return new Reward((int) Math.Clamp(exp, 0, int.MaxValue), entry.Meso, entry.Habi, entry.Items);
    }
}
