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

    public bool Ready => RoomId != 0;
    public IEnumerable<long> Characters => [First, Second];
}

// The rank duel matchmaker of the World server (Pvp 14 register, 15 unregister, 16 join). Not thread safe: the
// caller holds one lock around every call. Times are ticks in milliseconds passed in by the caller.
//
// Chosen here (the client does not settle them): the queue pairs the two characters that waited longest, at
// once; every character has the same rating (no rating is kept yet), so the server constants
// rankDuelPvpMatchingTime and rankDuelPvpMatchingMinGap change nothing. A match that nobody joined within
// NoShowMs is cancelled; a match whose field never reported the end goes after LifetimeMs.
public sealed class RankDuelQueue {
    // The match-found box counts down 15 s (CPvpRankDuelComponent::OnMessage, SetTimer(0xf, ...)); the
    // rest is the move into the field.
    public const long NoShowMs = 45_000;
    public const long LifetimeMs = 15 * 60_000;

    private readonly LinkedList<long> waiting = [];
    private readonly Dictionary<long, int> matchOf = [];
    private readonly Dictionary<int, RankDuelMatch> matches = [];
    private int nextMatchId = 1;

    public bool IsQueued(long characterId) => waiting.Contains(characterId);

    public RankDuelMatch? MatchOf(long characterId) {
        return matchOf.TryGetValue(characterId, out int matchId) ? matches.GetValueOrDefault(matchId) : null;
    }

    public RankDuelMatch? Get(int matchId) => matches.GetValueOrDefault(matchId);

    // Queues the character; when a second one waits, the two longest waiting are taken out as a match
    // (paired), whose field the caller then makes (SetRoom) or gives up (Cancel).
    public RankDuelError Register(long characterId, long tick, out RankDuelMatch? paired) {
        paired = null;
        if (IsQueued(characterId)) {
            return RankDuelError.Queued;
        }
        if (matchOf.ContainsKey(characterId)) {
            return RankDuelError.Busy;
        }

        waiting.AddLast(characterId);
        if (waiting.Count < 2) {
            return RankDuelError.none;
        }

        long first = waiting.First!.Value;
        waiting.RemoveFirst();
        long second = waiting.First!.Value;
        waiting.RemoveFirst();
        paired = new RankDuelMatch { Id = nextMatchId++, First = first, Second = second, CreatedTick = tick };
        matches[paired.Id] = paired;
        matchOf[first] = paired.Id;
        matchOf[second] = paired.Id;
        return RankDuelError.none;
    }

    public RankDuelError Unregister(long characterId) {
        return waiting.Remove(characterId) ? RankDuelError.none : RankDuelError.NotQueued;
    }

    // A logout: a queued character leaves the queue. A matched one stays: its field counts it as absent.
    public bool Remove(long characterId) => waiting.Remove(characterId);

    public bool SetRoom(int matchId, int channel, int mapId, int roomId) {
        if (!matches.TryGetValue(matchId, out RankDuelMatch? match) || roomId == 0) {
            return false;
        }
        match.Channel = channel;
        match.MapId = mapId;
        match.RoomId = roomId;
        return true;
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
