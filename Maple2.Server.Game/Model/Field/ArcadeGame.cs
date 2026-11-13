using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Model;

/// <summary>
/// The state every arcade game of a field keeps (table/arcadegame.xml): the arcade id, the lives of the life bar,
/// the score of the score dialog, the round and the players, without the field, so the rules can be tested.
/// </summary>
public abstract class ArcadeGame {
    public int Id { get; }
    public int LifeCount { get; }
    public int Lives { get; private set; }
    public int Score { get; protected set; }
    public bool Ended { get; private set; }
    public int Round { get; private set; }
    // The last round the script cleared.
    public int RoundsCleared { get; private set; }

    // The characters in the field at start_game: the end pays only them.
    private readonly HashSet<long> players = new();

    protected ArcadeGame(int id, int lifeCount, int initScore, IEnumerable<long>? characterIds) {
        Id = id;
        LifeCount = Math.Max(lifeCount, 0);
        Lives = LifeCount;
        Score = Math.Max(initScore, 0);
        if (characterIds != null) {
            players.UnionWith(characterIds);
        }
    }

    /// <summary>
    /// Whether the character played this game (was in the field at start_game).
    /// </summary>
    public bool Plays(long characterId) => players.Contains(characterId);

    public IReadOnlyCollection<long> Players => players;

    /// <summary>
    /// Whether the character still has the game's score dialog and life bar open: a player of a game not yet ended.
    /// The end closes them with the Result; a player who leaves before it gets no Result.
    /// </summary>
    public bool OpenFor(long characterId) => !Ended && Plays(characterId);

    protected void Opened(int round) {
        Round = round;
    }

    protected void Cleared(int round) {
        RoundsCleared = Math.Max(RoundsCleared, round);
    }

    /// <summary>
    /// A life is lost (a death, a missed round).
    /// </summary>
    /// <returns>Whether a life was lost.</returns>
    public bool Died() {
        if (Ended || Lives <= 0) {
            return false;
        }
        Lives--;
        return true;
    }

    /// <summary>
    /// Ends the game (once): the script's end_game, or the last life lost.
    /// </summary>
    /// <returns>Whether this call ended it.</returns>
    public bool End() {
        if (Ended) {
            return false;
        }
        Ended = true;
        return true;
    }

    public readonly record struct Reward(float ExpRate, int Meso);

    /// <summary>
    /// The reward of a game (table/Server/arcadeReward.xml): the arcade's row of the rounds cleared, or of the most
    /// rounds below them. Its exp rate scales the arcade exp curve at the character's level.
    /// </summary>
    public static Reward RewardOf(ArcadeRewardTable table, int arcadeId, int roundsCleared) {
        ArcadeRewardTable.Entry? entry = table.Of(arcadeId, roundsCleared);
        return entry == null ? new Reward(0, 0) : new Reward(entry.ExpRate, entry.Meso);
    }

    public Reward RewardOf(ArcadeRewardTable table) => RewardOf(table, Id, RoundsCleared);
}
