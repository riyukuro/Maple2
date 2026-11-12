namespace Maple2.Server.Game.Model;

/// <summary>
/// The state of a Boom Boom Ocean arcade game (arcade 2, field 51000003) on its field, without the field, so the
/// rules can be tested.
/// </summary>
/// <remarks>
/// The shipped script drives the game: start_game gives the lives, set_skill_score the score of each skill that
/// hits a player (the field's projectiles, negative) and of each buff a player picks up (positive), then 25 rounds
/// of start_round (the round time and the time score rate) and clear_round 20 seconds later; end_game follows the
/// last round or a player reaching the fail box. Our choices, as the scripts do not say: a hit by a skill with a
/// negative score also takes a life, as does a death; the score stays at 0 or above; a round's time score is its
/// whole seconds survived (at most the round time) times the rate, given at clear_round, or at the end for the round
/// still open.
/// </remarks>
public class BoomBoomOceanGame : ArcadeGame {
    public const int ArcadeId = 2;

    private readonly Dictionary<int, int> skillScores = new();
    private int timeScoreRate;
    private long roundStartTick;
    private int roundDuration;
    private bool roundOpen;

    public BoomBoomOceanGame(int lifeCount, IEnumerable<long>? characterIds = null) : base(ArcadeId, lifeCount, 0, characterIds) { }

    public void SetSkillScore(int id, int score) {
        skillScores[id] = score;
    }

    public void StartRound(int round, int duration, int scoreRate, long nowTick) {
        if (Ended) {
            return;
        }
        Opened(round);
        roundDuration = Math.Max(duration, 0);
        timeScoreRate = scoreRate;
        roundStartTick = nowTick;
        roundOpen = true;
    }

    /// <summary>
    /// A skill hit a game player or a buff was given to one: its score, when the game gives it one.
    /// </summary>
    /// <param name="id">The skill or buff id.</param>
    /// <param name="lostLife">Whether a negative score took a life.</param>
    /// <returns>Whether the score or the lives changed.</returns>
    public bool Scored(int id, out bool lostLife) {
        lostLife = false;
        if (Ended || !skillScores.TryGetValue(id, out int score) || score == 0) {
            return false;
        }
        Score = (int) Math.Clamp((long) Score + score, 0, int.MaxValue);
        if (score < 0) {
            lostLife = Died();
        }
        return true;
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
        return round == Round ? CloseRound(nowTick) : 0;
    }

    /// <summary>
    /// The time score of the round still open, given when the game ends.
    /// </summary>
    public int CloseRound(long nowTick) {
        if (!roundOpen) {
            return 0;
        }
        roundOpen = false;
        int bonus = TimeScore(timeScoreRate, Math.Min(nowTick - roundStartTick, roundDuration));
        Score = (int) Math.Min((long) Score + bonus, int.MaxValue);
        return bonus;
    }

    public static int TimeScore(int rate, long msSurvived) {
        if (rate <= 0 || msSurvived <= 0) {
            return 0;
        }
        return (int) Math.Min(msSurvived / 1000 * rate, int.MaxValue);
    }
}
