namespace Maple2.Model.Game.Pvp;

public enum RankDuelPhase {
    // The field is made; the duelists are on their way.
    Waiting,
    // Both are in: the round intro runs (Pvp 27), then the fight starts (Pvp 26).
    Countdown,
    // The duelists can hit each other.
    Fighting,
    // A round ended and the match goes on: the next round's intro comes after RoundBreakMs.
    Break,
    // The result is out; the duelists stay to read it, then leave.
    Ended,
    // The duelists were sent out.
    Closed,
}

public enum RankDuelEnd {
    // One duelist won RoundsToWin rounds, won more rounds when the last one ran out, or the other left the match.
    Win,
    // The rounds ran out with both on the same number of round wins.
    Draw,
    // Nobody came: no duel took place.
    Void,
}

// How a Win came: the loser died in the round that gave the winner its last round win, left during the match, or never
// fought (it had not come by JoinMs, or left before the first fight started); or the last round ran out with the winner
// ahead in round wins (Rounds).
public enum RankDuelWinBy {
    None,
    Death,
    LeftFight,
    Walkover,
    Rounds,
}

public enum RankDuelEventType {
    Round,
    Fight,
    // A round ended and the match goes on: Winner and Loser for a round won by a death, End Draw for a round that ran out.
    RoundEnd,
    Result,
    Exit,
    // One duelist came after the result: it is told the duel is over and sent out at once.
    Dismiss,
}

// What the field must do after a call: send the round intro (Round, for the round number) or the fight start, end a
// round, send the result, send the duelists out, or send one late duelist out (Character). Winner and Loser are set for
// a Result of RankDuelEnd.Win and a RoundEnd of RankDuelEnd.Win.
public readonly record struct RankDuelEvent(RankDuelEventType Type, RankDuelEnd End = RankDuelEnd.Win, long Winner = 0, long Loser = 0, long Character = 0, int Round = 0);

// The state of one rank duel match field: a best of MaxRounds rounds between two characters. Not thread safe; times
// are ticks in milliseconds passed in by the caller.
//
// From the client: the round intro with its round number (Pvp 27, its "GetReady" sounds), the fight start (Pvp 26,
// "Fight"), the win, lose and draw intros (Pvp 28, 29, 30) and the result dialog (Pvp 18) with each side's score
// (CPvpRankDuelComponent::OnMessage, pvprankduelcomponent.cpp; UIPvPRankDuelResultDialog::FUN_140f90b10). The duel's
// observer dialog draws each side's round wins as win marks, two per side (setWinMark, m_winPointL1/L2 and
// m_winPointR1/R2 in uipvpobserverdialog.gfx): a match is won with RoundsToWin round wins, so at most MaxRounds are
// played. Chosen here (the client and the data do not settle them): a round is won by the opponent's death; a round
// whose fight runs FightMs with both standing counts for nobody; after the last round the one with more round wins
// wins, with the same number it is a draw; the next round's intro comes RoundBreakMs after a round ends; the intro
// lasts CountdownMs before the fight; a duelist who leaves before the result loses; a duelist who has not come by
// JoinMs loses to one who has, and with nobody there the match is void; the duelists stay ResultMs after the result.
public sealed class RankDuelMatchState {
    public const long JoinMs = 40_000;
    public const long CountdownMs = 3_000;
    public const long FightMs = 180_000;
    public const long ResultMs = 10_000;
    // Longer than the arena maps' autoRevivalTime (3 s, fielddata 65010001 to 65010010), so a duelist who died is back
    // before the next round's intro.
    public const long RoundBreakMs = 5_000;
    public const int RoundsToWin = 2;
    public const int MaxRounds = 2 * RoundsToWin - 1;

    public long First { get; }
    public long Second { get; }
    public RankDuelPhase Phase { get; private set; } = RankDuelPhase.Waiting;
    public long Winner { get; private set; }
    public long Loser { get; private set; }
    public RankDuelWinBy WinBy { get; private set; }
    // The round being played or last played: 1 from the first round's intro on, 0 before.
    public int Round { get; private set; }

    private readonly Dictionary<long, int> wins = [];

    // The rounds the duelist won so far (the result dialog's score, the observer dialog's win marks).
    public int Wins(long characterId) => wins.GetValueOrDefault(characterId);

    private readonly HashSet<long> present = [];
    // The duelists whose health a hit of the opponent took during the fight.
    private readonly HashSet<long> hurt = [];
    private readonly long joinDeadline;
    private long phaseTick;

    public RankDuelMatchState(long first, long second, long createdTick) {
        First = first;
        Second = second;
        joinDeadline = createdTick + JoinMs;
    }

    public bool IsDuelist(long characterId) => characterId == First || characterId == Second;

    public long Opponent(long characterId) => characterId == First ? Second : characterId == Second ? First : 0;

    public bool IsPresent(long characterId) => present.Contains(characterId);

    // Whether target can be hit by caster: the two duelists, during the fight.
    public bool Hostile(long caster, long target) {
        return Phase == RankDuelPhase.Fighting && caster != target && IsDuelist(caster) && IsDuelist(target);
    }

    // A hit of the opponent took health from the duelist during the fight.
    public void Hurt(long characterId) {
        if (Phase == RankDuelPhase.Fighting && IsDuelist(characterId)) {
            hurt.Add(characterId);
        }
    }

    // A perfect win, for the perfect intro (Pvp 31, UIEventIntroduce::OnPerfect, pvprankduelcomponent.cpp): chosen
    // here (the client and the data do not say when it is shown), a win by the loser's death where no hit of the
    // loser took health from the winner during any round's fight. A loser that left, or never fought, gives no perfect.
    public bool Perfect => Phase is RankDuelPhase.Ended or RankDuelPhase.Closed && WinBy == RankDuelWinBy.Death && !hurt.Contains(Winner);

    public List<RankDuelEvent> Enter(long characterId, long tick) {
        var events = new List<RankDuelEvent>();
        if (!IsDuelist(characterId)) {
            return events;
        }
        // After the result (a late join, a slow load) there is nothing left to play: the field sends it out.
        if (Phase is RankDuelPhase.Ended or RankDuelPhase.Closed) {
            events.Add(new RankDuelEvent(RankDuelEventType.Dismiss, Character: characterId));
            return events;
        }
        present.Add(characterId);
        if (Phase == RankDuelPhase.Waiting && present.Count == 2) {
            StartRound(events, tick);
        }
        return events;
    }

    public List<RankDuelEvent> Leave(long characterId, long tick) {
        var events = new List<RankDuelEvent>();
        if (!present.Remove(characterId)) {
            return events;
        }
        if (Phase is RankDuelPhase.Waiting or RankDuelPhase.Countdown or RankDuelPhase.Fighting or RankDuelPhase.Break) {
            // Before the first fight started it is a walkover; once a fight started the match was fought.
            bool fought = Phase is RankDuelPhase.Fighting or RankDuelPhase.Break || Round > 1;
            End(events, tick, Opponent(characterId), characterId, fought ? RankDuelWinBy.LeftFight : RankDuelWinBy.Walkover);
        }
        return events;
    }

    public List<RankDuelEvent> Death(long characterId, long tick) {
        var events = new List<RankDuelEvent>();
        if (Phase == RankDuelPhase.Fighting && IsDuelist(characterId)) {
            long winner = Opponent(characterId);
            int won = wins[winner] = Wins(winner) + 1;
            if (won >= RoundsToWin || (Round >= MaxRounds && won > Wins(characterId))) {
                End(events, tick, winner, characterId, RankDuelWinBy.Death);
            } else if (Round >= MaxRounds) {
                // The last round, and the death evens the wins: a draw, as the last round's timeout with equal wins.
                Phase = RankDuelPhase.Ended;
                phaseTick = tick;
                events.Add(new RankDuelEvent(RankDuelEventType.Result, RankDuelEnd.Draw));
            } else {
                Phase = RankDuelPhase.Break;
                phaseTick = tick;
                events.Add(new RankDuelEvent(RankDuelEventType.RoundEnd, RankDuelEnd.Win, winner, characterId, Round: Round));
            }
        }
        return events;
    }

    public List<RankDuelEvent> Update(long tick) {
        var events = new List<RankDuelEvent>();
        switch (Phase) {
            case RankDuelPhase.Waiting when tick >= joinDeadline:
                if (present.Count == 0) {
                    Phase = RankDuelPhase.Ended;
                    phaseTick = tick;
                    events.Add(new RankDuelEvent(RankDuelEventType.Result, RankDuelEnd.Void));
                } else {
                    long here = present.First();
                    End(events, tick, here, Opponent(here), RankDuelWinBy.Walkover);
                }
                break;
            case RankDuelPhase.Countdown when tick >= phaseTick + CountdownMs:
                Phase = RankDuelPhase.Fighting;
                phaseTick = tick;
                events.Add(new RankDuelEvent(RankDuelEventType.Fight));
                break;
            case RankDuelPhase.Fighting when tick >= phaseTick + FightMs:
                if (Round < MaxRounds) {
                    Phase = RankDuelPhase.Break;
                    phaseTick = tick;
                    events.Add(new RankDuelEvent(RankDuelEventType.RoundEnd, RankDuelEnd.Draw, Round: Round));
                } else if (Wins(First) != Wins(Second)) {
                    long winner = Wins(First) > Wins(Second) ? First : Second;
                    End(events, tick, winner, Opponent(winner), RankDuelWinBy.Rounds);
                } else {
                    Phase = RankDuelPhase.Ended;
                    phaseTick = tick;
                    events.Add(new RankDuelEvent(RankDuelEventType.Result, RankDuelEnd.Draw));
                }
                break;
            case RankDuelPhase.Break when tick >= phaseTick + RoundBreakMs:
                StartRound(events, tick);
                break;
            case RankDuelPhase.Ended when tick >= phaseTick + ResultMs:
                Phase = RankDuelPhase.Closed;
                events.Add(new RankDuelEvent(RankDuelEventType.Exit));
                break;
        }
        return events;
    }

    private void StartRound(List<RankDuelEvent> events, long tick) {
        Round++;
        Phase = RankDuelPhase.Countdown;
        phaseTick = tick;
        events.Add(new RankDuelEvent(RankDuelEventType.Round, Round: Round));
    }

    private void End(List<RankDuelEvent> events, long tick, long winner, long loser, RankDuelWinBy winBy) {
        Phase = RankDuelPhase.Ended;
        phaseTick = tick;
        Winner = winner;
        Loser = loser;
        WinBy = winBy;
        events.Add(new RankDuelEvent(RankDuelEventType.Result, RankDuelEnd.Win, winner, loser));
    }
}
