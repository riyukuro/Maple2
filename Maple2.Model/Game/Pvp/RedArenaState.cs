namespace Maple2.Model.Game.Pvp;

public enum RedArenaEventType {
    // Two fighters are picked: s_dualprepareplayer.
    Prepare,
    // Their fight starts: s_dualstartcombat; each fighter gets its PvP point.
    Start,
    // A fight ended: the winner (if any) keeps the ring with its win count (s_dualwin1 to 9), the loser is out; the
    // points go back to 0.
    Result,
    // No challenger is left, or the time ran out: the zone ends.
    End,
}

public readonly record struct RedArenaEvent(RedArenaEventType Type, long First = 0, long Second = 0, long Winner = 0, int Wins = 0);

// The Red Arena's PvP zone (set_pvp_zone type 1, the client's PvP mode 1): random face-offs in the ring. From the
// data: the arena's script promises the reward to "the hero who survives all of the random face-offs to the very
// end", and the client's PvP strings name the steps (s_dualprepareplayer, s_dualstartcombat, s_dualwin1 to 8 for the
// win count and s_dualwin9 "defeated all challengers"). Chosen here (nothing settles the timing or the order): the
// participants are the players in the zone's box when the fight starts; the first face-off is two of them at random,
// each next one the winner against a random participant that has not fought; a face-off is announced PrepareMs
// before it starts and lasts at most FightMs; it ends when a fighter dies or leaves (the other wins), or with no
// winner when the time runs out (both are out); the zone ends when no challenger is left (s_dualwin9 for the last
// winner) or its match time ends. The champion (the last winner, if any) is the zone's winner.
public sealed class RedArenaState {
    public const long PrepareMs = 3_000;
    public const long FightMs = 30_000;

    public int BoxId { get; }
    public bool Active { get; private set; }
    public bool Ended { get; private set; }
    public long ActiveTick { get; }
    public long EndTick { get; }
    public long Champion { get; private set; }
    public int ChampionWins { get; private set; }
    public long FighterA { get; private set; }
    public long FighterB { get; private set; }
    public bool Fighting { get; private set; }

    private readonly List<long> waiting = [];
    private readonly HashSet<long> participants = [];
    private readonly Random random;
    private long stepTick;

    public RedArenaState(int boxId, long tick, int prepareSeconds, int matchSeconds, Random? random = null) {
        BoxId = boxId;
        ActiveTick = tick + Math.Max(0, prepareSeconds) * 1000L;
        EndTick = ActiveTick + Math.Max(0, matchSeconds) * 1000L;
        this.random = random ?? Random.Shared;
    }

    public bool DueToActivate(long tick) => !Active && !Ended && tick >= ActiveTick;
    public bool IsParticipant(long characterId) => participants.Contains(characterId);

    public List<RedArenaEvent> Activate(IEnumerable<long> players, long tick) {
        Active = true;
        foreach (long characterId in players.Distinct()) {
            participants.Add(characterId);
            waiting.Add(characterId);
        }
        return Next(tick);
    }

    // Only the two fighters, during their fight, are each other's targets.
    public bool Hostile(long caster, long target) {
        return Active && Fighting && caster != target
               && (caster == FighterA || caster == FighterB) && (target == FighterA || target == FighterB);
    }

    public List<RedArenaEvent> Update(long tick) {
        var events = new List<RedArenaEvent>();
        if (!Active || Ended) {
            return events;
        }
        if (tick >= EndTick) {
            if (Fighting) {
                events.Add(Finish(0, FighterA, FighterB));
            }
            events.Add(End());
            return events;
        }
        if (FighterA != 0 && !Fighting && tick >= stepTick) {
            Fighting = true;
            stepTick = tick + FightMs;
            events.Add(new RedArenaEvent(RedArenaEventType.Start, FighterA, FighterB));
        } else if (Fighting && tick >= stepTick) {
            events.Add(Finish(0, FighterA, FighterB));
            events.AddRange(Next(tick));
        }
        return events;
    }

    // A fighter died or left: the other wins. A waiting participant that leaves is out.
    public List<RedArenaEvent> Out(long characterId, long tick) {
        var events = new List<RedArenaEvent>();
        if (!Active || Ended) {
            return events;
        }
        waiting.Remove(characterId);
        if (characterId == FighterA || characterId == FighterB) {
            long winner = characterId == FighterA ? FighterB : FighterA;
            events.Add(Finish(winner, characterId));
            events.AddRange(Next(tick));
        }
        return events;
    }

    private RedArenaEvent Finish(long winner, params long[] losers) {
        Fighting = false;
        long a = FighterA;
        long b = FighterB;
        FighterA = 0;
        FighterB = 0;
        if (winner != 0) {
            ChampionWins = winner == Champion ? ChampionWins + 1 : 1;
            Champion = winner;
        } else if (losers.Contains(Champion)) {
            Champion = 0;
            ChampionWins = 0;
        }
        return new RedArenaEvent(RedArenaEventType.Result, a, b, winner, winner != 0 ? ChampionWins : 0);
    }

    private List<RedArenaEvent> Next(long tick) {
        var events = new List<RedArenaEvent>();
        long first = Champion;
        if (first == 0) {
            if (waiting.Count < 2) {
                events.Add(End());
                return events;
            }
            first = Take();
        } else if (waiting.Count == 0) {
            events.Add(End());
            return events;
        }
        long second = Take();
        FighterA = first;
        FighterB = second;
        stepTick = tick + PrepareMs;
        events.Add(new RedArenaEvent(RedArenaEventType.Prepare, first, second));
        return events;
    }

    private long Take() {
        int index = random.Next(waiting.Count);
        long characterId = waiting[index];
        waiting.RemoveAt(index);
        return characterId;
    }

    private RedArenaEvent End() {
        Ended = true;
        Fighting = false;
        FighterA = 0;
        FighterB = 0;
        return new RedArenaEvent(RedArenaEventType.End, Winner: Champion, Wins: ChampionWins);
    }
}
