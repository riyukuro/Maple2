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
        if (Ended || round < Rounds) {
            return false;
        }
        Ended = true;
        return true;
    }
}
