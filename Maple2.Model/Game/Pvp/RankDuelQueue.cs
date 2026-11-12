namespace Maple2.Model.Game.Pvp;

// The World's answer to a rank duel request (PvpResponse.error).
public enum RankDuelError {
    none = 0,
    // The character is in a match already.
    Busy = 1,
    // An unregister from a character that is not queued.
    NotQueued = 2,
    // A join with no match, or with a match whose field is not made yet.
    NoMatch = 3,
    // A register from a character that is queued already: it stays queued.
    Queued = 4,
}

// One match of two characters. MapId and RoomId are set once the match field is made; Joined holds the
// characters that asked to enter it (Pvp 16 after the match-found box).
public sealed class RankDuelMatch {
    public required int Id { get; init; }
    public required long First { get; init; }
    public required long Second { get; init; }
    public required long CreatedTick { get; init; }
    public int Channel { get; set; }
    public int MapId { get; set; }
    public int RoomId { get; set; }
    public HashSet<long> Joined { get; } = [];
    // Its fight started: it can be watched (Observe).
    public bool Observable { get; set; }
    public long ObservableTick { get; set; }

    public bool Ready => RoomId != 0;
    public IEnumerable<long> Characters => [First, Second];
}

// The rank duel matchmaker of the World server (Pvp 14 register, 15 unregister, 16 join). Not thread safe: the
// caller holds one lock around every call. Times are ticks in milliseconds passed in by the caller.
//
// Chosen here (the client does not settle them): the longest waiting character is paired first, with the waiting
// character of the closest rating (then the longest waiting) inside the gap its wait allows
// (RankDuelRules.AllowedGap, from the server constants rankDuelPvpMatchingTime and rankDuelPvpMatchingMinGap); the
// queue is tried again on every register and by the caller's timer (Pair), as the window widens with the wait. A
// match that nobody joined within NoShowMs is cancelled; a match whose field never reported the end goes after
// LifetimeMs.
public sealed class RankDuelQueue {
    // The match-found box counts down 15 s (CPvpRankDuelComponent::OnMessage, SetTimer(0xf, ...)); the
    // rest is the move into the field.
    public const long NoShowMs = 45_000;
    public const long LifetimeMs = 15 * 60_000;

    private sealed record Waiter(long CharacterId, int Rating, long Since);

    private readonly List<Waiter> waiting = [];
    private readonly Dictionary<long, int> matchOf = [];
    private readonly Dictionary<int, RankDuelMatch> matches = [];
    private int nextMatchId = 1;

    // The server constants of the window; 0 allows every gap.
    public int MatchingTimeSeconds { get; set; }
    public int MatchingMinGap { get; set; }

    public bool IsQueued(long characterId) => waiting.Any(waiter => waiter.CharacterId == characterId);

    public RankDuelMatch? MatchOf(long characterId) {
        return matchOf.TryGetValue(characterId, out int matchId) ? matches.GetValueOrDefault(matchId) : null;
    }

    public RankDuelMatch? Get(int matchId) => matches.GetValueOrDefault(matchId);

    // Queues the character with its rating, then pairs what the windows allow (paired: the first match made,
    // with the character or not), whose field the caller then makes (SetRoom) or gives up (End).
    public RankDuelError Register(long characterId, long tick, out RankDuelMatch? paired, int rating = RankDuelRecord.StartRating) {
        RankDuelError error = Register(characterId, tick, rating, out List<RankDuelMatch> made);
        paired = made.FirstOrDefault();
        return error;
    }

    // The same with every match made (a widened window can pair others too).
    public RankDuelError Register(long characterId, long tick, int rating, out List<RankDuelMatch> made) {
        made = [];
        if (IsQueued(characterId)) {
            return RankDuelError.Queued;
        }
        if (matchOf.ContainsKey(characterId)) {
            return RankDuelError.Busy;
        }

        waiting.Add(new Waiter(characterId, rating, tick));
        made = Pair(tick);
        return RankDuelError.none;
    }

    // The matches the windows allow at this tick, taken out of the queue: one pass over the waiters, the longest
    // waiting first, each taking the closest rating left inside its window (O(n^2) for n waiters).
    public List<RankDuelMatch> Pair(long tick) {
        var made = new List<RankDuelMatch>();
        if (waiting.Count < 2) {
            return made;
        }
        List<Waiter> order = waiting.OrderBy(waiter => waiter.Since).ToList();
        var taken = new HashSet<Waiter>();
        foreach (Waiter first in order) {
            if (taken.Contains(first)) {
                continue;
            }
            long gap = RankDuelRules.AllowedGap(tick - first.Since, MatchingTimeSeconds, MatchingMinGap);
            Waiter? second = null;
            long best = long.MaxValue;
            foreach (Waiter other in order) {
                if (other == first || taken.Contains(other)) {
                    continue;
                }
                long distance = Math.Abs((long) other.Rating - first.Rating);
                if (distance <= gap && distance < best) {
                    second = other;
                    best = distance;
                }
            }
            if (second == null) {
                continue;
            }
            taken.Add(first);
            taken.Add(second);
            var match = new RankDuelMatch { Id = nextMatchId++, First = first.CharacterId, Second = second.CharacterId, CreatedTick = tick };
            matches[match.Id] = match;
            matchOf[match.First] = match.Id;
            matchOf[match.Second] = match.Id;
            made.Add(match);
        }
        waiting.RemoveAll(taken.Contains);
        return made;
    }

    public RankDuelError Unregister(long characterId) {
        return Remove(characterId) ? RankDuelError.none : RankDuelError.NotQueued;
    }

    // A logout: a queued character leaves the queue. A matched one stays: its field counts it as absent.
    public bool Remove(long characterId) => waiting.RemoveAll(waiter => waiter.CharacterId == characterId) > 0;

    public bool SetRoom(int matchId, int channel, int mapId, int roomId) {
        if (!matches.TryGetValue(matchId, out RankDuelMatch? match) || roomId == 0) {
            return false;
        }
        match.Channel = channel;
        match.MapId = mapId;
        match.RoomId = roomId;
        return true;
    }

    // The match field's fight started: the match can be watched.
    public bool SetObservable(int matchId, long tick) {
        if (!matches.TryGetValue(matchId, out RankDuelMatch? match) || !match.Ready) {
            return false;
        }
        match.Observable = true;
        match.ObservableTick = tick;
        return true;
    }

    // The match a character with no match of its own watches: the one whose fight started last. Chosen here (the
    // client's offer, Pvp 0x19, names no match).
    public RankDuelMatch? Observe(long characterId) {
        if (matchOf.ContainsKey(characterId) || IsQueued(characterId)) {
            return null;
        }
        return matches.Values.Where(match => match.Observable).OrderByDescending(match => match.ObservableTick).FirstOrDefault();
    }

    // The join after the match-found box: the field to enter, once it is made.
    public RankDuelError Join(long characterId, out RankDuelMatch? match) {
        match = MatchOf(characterId);
        if (match is not { Ready: true }) {
            match = null;
            return RankDuelError.NoMatch;
        }
        match.Joined.Add(characterId);
        return RankDuelError.none;
    }

    // Ends a match (its field ended it, or its field could not be made): both characters are free again.
    public RankDuelMatch? End(int matchId) {
        if (!matches.Remove(matchId, out RankDuelMatch? match)) {
            return null;
        }
        matchOf.Remove(match.First);
        matchOf.Remove(match.Second);
        return match;
    }

    // Ends the matches whose field is on a channel that went down: their End never comes.
    public List<RankDuelMatch> EndChannel(int channel) {
        List<RankDuelMatch> ended = matches.Values.Where(match => match.Ready && match.Channel == channel).ToList();
        foreach (RankDuelMatch match in ended) {
            End(match.Id);
        }
        return ended;
    }

    // The matches to end at this tick: a made match that nobody joined within NoShowMs, or any match older
    // than LifetimeMs. They are removed; the caller tells the characters that did not join.
    public List<RankDuelMatch> Expire(long tick) {
        List<RankDuelMatch> expired = matches.Values
            .Where(match => tick - match.CreatedTick >= LifetimeMs
                            || (match.Ready && match.Joined.Count == 0 && tick - match.CreatedTick >= NoShowMs))
            .ToList();
        foreach (RankDuelMatch match in expired) {
            End(match.Id);
        }
        return expired;
    }
}
