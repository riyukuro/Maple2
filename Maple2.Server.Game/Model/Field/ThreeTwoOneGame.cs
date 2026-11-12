namespace Maple2.Server.Game.Model;

/// <summary>
/// The state of a Three Two One arcade game (arcades 3, 4 and 5, fields 51000004, 51000005 and 51000006) on its
/// field, without the field, so the rules can be tested.
/// </summary>
/// <remarks>
/// The shipped scripts drive the game: start_game gives the lives and the starting score, then each round is
/// start_round (the client's direction buttons show for ui_duration seconds), result_round (the direction the NPC
/// turns to, 1 to 3), the script's ThreeTwoOneResult user value 2 seconds later (1: the players won, 0: they lost),
/// result_round2 and clear_round, and the script loops while a player stays in the game box. A player wins a round
/// by picking a direction other than the NPC's (c2s PlayArcade mode 11, the client's UIMicroGameCoupleDance
/// buttons); a game player without a pick loses it. The scripts' intro: five losses end the game, and the longer the
/// game lasts the higher the score.
/// </remarks>
public class ThreeTwoOneGame : ArcadeGame {
    public const int FirstArcadeId = 3;
    public const int LastArcadeId = 5;
    // The score a won round adds (our choice: the scripts give none).
    public const int WinScore = 1000;

    // The rounds won in a row.
    public int Streak { get; private set; }
    // The last result_round's result, applied by result_round2.
    public bool? LastWin { get; private set; }

    private readonly Dictionary<long, int> picks = new();
    private bool roundOpen;

    public ThreeTwoOneGame(int arcadeId, int lifeCount, int initScore, IEnumerable<long>? characterIds = null)
        : base(arcadeId, lifeCount, initScore, characterIds) { }

    public static bool IsArcade(int arcadeId) => arcadeId is >= FirstArcadeId and <= LastArcadeId;

    public void StartRound(int round) {
        if (Ended) {
            return;
        }
        Opened(round);
        picks.Clear();
        LastWin = null;
        roundOpen = true;
    }

    /// <summary>
    /// A game player's pick of the open round (the last one counts). A pick outside 1 to 3, after result_round or
    /// from a character not in the game is dropped.
    /// </summary>
    /// <returns>Whether the pick was kept.</returns>
    public bool Pick(long characterId, int direction) {
        if (Ended || !roundOpen || direction is < 1 or > 3 || !Plays(characterId)) {
            return false;
        }
        picks[characterId] = direction;
        return true;
    }

    /// <summary>
    /// The NPC turned to a direction: the players win the round when each game player still in the field picked
    /// and none picked that direction.
    /// </summary>
    /// <returns>Whether the players won.</returns>
    public bool ResultRound(int direction, IEnumerable<long> presentPlayers) {
        roundOpen = false;
        bool win = !Ended;
        bool any = false;
        foreach (long characterId in presentPlayers) {
            if (!Plays(characterId)) {
                continue;
            }
            any = true;
            if (!picks.TryGetValue(characterId, out int pick) || pick == direction) {
                win = false;
            }
        }
        LastWin = win && any;
        return LastWin.Value;
    }

    /// <summary>
    /// result_round2: a won round adds the score and the streak; a lost one takes a life and ends the streak.
    /// </summary>
    /// <returns>Whether a life was lost.</returns>
    public bool ResultRound2() {
        if (Ended || LastWin is not { } win) {
            return false;
        }
        LastWin = null;
        if (win) {
            Score += WinScore;
            Streak++;
            return false;
        }
        Streak = 0;
        return Died();
    }

    public void ClearRound(int round) {
        if (!Ended) {
            Cleared(round);
        }
    }
}
