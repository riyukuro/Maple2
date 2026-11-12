using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Model;

/// <summary>
/// The state of a Spring Farm arcade game (arcade 1, field 51000001) on its field, without the field, so the rules
/// can be tested.
/// </summary>
/// <remarks>
/// The shipped script drives the game: start_game gives the lives, set_interact_score the score of each use of an
/// interact object, each round is spawn_monster (the spawn points and the score they give), start_round (the round
/// time and the time score), then the round's monsters all dead, then clear_round; end_game follows the last round
/// or the round time running out. A spawn gives its score once all its monsters are dead, as in Dark Stream. With
/// the time score type "remain", clearing the round adds the round's whole seconds left times the rate.
/// </remarks>
public class SpringFarmGame : ArcadeGame {
    public const int ArcadeId = 1;
    public const string RemainTimeScore = "remain";

    private readonly Dictionary<int, int> spawnScores = new();
    private readonly Dictionary<int, int> interactScores = new();
    // The objects that keep their state when used (Ui, Telescope) that have scored in this game.
    private readonly HashSet<string> scoredObjects = new();
    private string timeScoreType = string.Empty;
    private int timeScoreRate;
    private long roundEndTick;
    private bool roundOpen;

    public SpringFarmGame(int lifeCount, IEnumerable<long>? characterIds = null) : base(ArcadeId, lifeCount, 0, characterIds) { }

    public void SetInteractScore(int interactId, int score) {
        interactScores[interactId] = score;
    }

    public void SetSpawnScore(int spawnId, int score) {
        spawnScores[spawnId] = score;
    }

    public void StartRound(int round, string scoreType, int scoreRate, int roundDuration, long nowTick) {
        Opened(round);
        timeScoreType = scoreType;
        timeScoreRate = scoreRate;
        roundEndTick = nowTick + Math.Max(roundDuration, 0);
        roundOpen = true;
    }

    /// <summary>
    /// A use of an interact object: its score is added each time it reacts. An object that keeps its state when used
    /// (Ui, Telescope: no reset brings it back) scores once per object in the game.
    /// </summary>
    /// <returns>Whether the score changed.</returns>
    public bool Interacted(int interactId, string entityId = "", bool keepsState = false) {
        if (Ended || !interactScores.TryGetValue(interactId, out int score) || score == 0) {
            return false;
        }
        if (keepsState && !scoredObjects.Add(entityId)) {
            return false;
        }
        Score += score;
        return true;
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
    /// The script cleared a round. The round started last gives its time score once.
    /// </summary>
    /// <returns>The time score added.</returns>
    public int ClearRound(int round, long nowTick) {
        if (Ended) {
            return 0;
        }
        Cleared(round);
        if (!roundOpen || round != Round) {
            return 0;
        }
        roundOpen = false;
        int bonus = TimeScore(timeScoreType, timeScoreRate, roundEndTick - nowTick);
        Score += bonus;
        return bonus;
    }

    // "remain": the whole seconds left of the round times the rate. The script has no other type.
    public static int TimeScore(string type, int rate, long msLeft) {
        if (type != RemainTimeScore || rate <= 0 || msLeft <= 0) {
            return 0;
        }
        return (int) Math.Min(msLeft / 1000 * rate, int.MaxValue);
    }

    /// <summary>
    /// The reward of a game (table/Server/arcadeReward.xml): the row of the rounds cleared, or of the most rounds
    /// below them. Its exp rate scales the arcade exp curve at the character's level.
    /// </summary>
    public static Reward RewardOf(ArcadeRewardTable table, int roundsCleared) => RewardOf(table, ArcadeId, roundsCleared);
}
