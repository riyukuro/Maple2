namespace Maple2.Model.Game.Pvp;

public enum RankDuelPhase {
    // The field is made; the duelists are on their way.
    Waiting,
    // Both are in: the round intro runs (Pvp 27), then the fight starts (Pvp 26).
    Countdown,
    // The duelists can hit each other.
    Fighting,
    // The result is out; the duelists stay to read it, then leave.
    Ended,
    // The duelists were sent out.
    Closed,
}

public enum RankDuelEnd {
    // One duelist lost the round (died) or left the match.
    Win,
    // The fight time ran out with both standing.
    Draw,
    // Nobody came: no duel took place.
    Void,
}

public enum RankDuelEventType {
    Round,
    Fight,
    Result,
    Exit,
    // One duelist came after the result: it is told the duel is over and sent out at once.
    Dismiss,
}

// What the field must do after a call: send the round intro or the fight start, send the result, send the
// duelists out, or send one late duelist out (Character). Winner and Loser are set for a Result of RankDuelEnd.Win.
public readonly record struct RankDuelEvent(RankDuelEventType Type, RankDuelEnd End = RankDuelEnd.Win, long Winner = 0, long Loser = 0, long Character = 0);

// The state of one rank duel match field: one round between two characters. Not thread safe; times are
// ticks in milliseconds passed in by the caller.
//
// From the client: the round intro (Pvp 27, its "GetReady" sounds), the fight start (Pvp 26, "Fight"), the
// win, lose and draw intros (Pvp 28, 29, 30) and the result dialog (Pvp 18) (CPvpRankDuelComponent::OnMessage,
// pvprankduelcomponent.cpp). Chosen here (the client and the data do not settle them): one round; the intro
// lasts CountdownMs before the fight; the fight lasts FightMs and ends in a draw when both stand; a duelist who
// dies or leaves during the countdown or the fight loses; a duelist who has not come by JoinMs loses to one who
// has, and with nobody there the match is void; the duelists stay ResultMs after the result.
public sealed class RankDuelMatchState {
    public const long JoinMs = 40_000;
    public const long CountdownMs = 3_000;
    public const long FightMs = 180_000;
    public const long ResultMs = 10_000;

    public long First { get; }
    public long Second { get; }
    public RankDuelPhase Phase { get; private set; } = RankDuelPhase.Waiting;
    public long Winner { get; private set; }
    public long Loser { get; private set; }

    private readonly HashSet<long> present = [];
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
            Phase = RankDuelPhase.Countdown;
            phaseTick = tick;
            events.Add(new RankDuelEvent(RankDuelEventType.Round));
        }
        return events;
    }

    public List<RankDuelEvent> Leave(long characterId, long tick) {
        var events = new List<RankDuelEvent>();
        if (!present.Remove(characterId)) {
            return events;
        }
        if (Phase is RankDuelPhase.Waiting or RankDuelPhase.Countdown or RankDuelPhase.Fighting) {
            End(events, tick, Opponent(characterId), characterId);
        }
        return events;
    }

    public List<RankDuelEvent> Death(long characterId, long tick) {
        var events = new List<RankDuelEvent>();
        if (Phase == RankDuelPhase.Fighting && IsDuelist(characterId)) {
            End(events, tick, Opponent(characterId), characterId);
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
                    End(events, tick, here, Opponent(here));
                }
                break;
            case RankDuelPhase.Countdown when tick >= phaseTick + CountdownMs:
                Phase = RankDuelPhase.Fighting;
                phaseTick = tick;
                events.Add(new RankDuelEvent(RankDuelEventType.Fight));
                break;
            case RankDuelPhase.Fighting when tick >= phaseTick + FightMs:
                Phase = RankDuelPhase.Ended;
                phaseTick = tick;
                events.Add(new RankDuelEvent(RankDuelEventType.Result, RankDuelEnd.Draw));
                break;
            case RankDuelPhase.Ended when tick >= phaseTick + ResultMs:
                Phase = RankDuelPhase.Closed;
                events.Add(new RankDuelEvent(RankDuelEventType.Exit));
                break;
        }
        return events;
    }

    private void End(List<RankDuelEvent> events, long tick, long winner, long loser) {
        Phase = RankDuelPhase.Ended;
        phaseTick = tick;
        Winner = winner;
        Loser = loser;
        events.Add(new RankDuelEvent(RankDuelEventType.Result, RankDuelEnd.Win, winner, loser));
    }
}
