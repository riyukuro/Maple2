namespace Maple2.Model.Game.Pvp;

// One individual arena match: its characters, and its field once made. Joined holds those that asked to enter.
public sealed class ArenaMatch {
    public required int Id { get; init; }
    public required int Arena { get; init; }
    public required IReadOnlyList<long> Characters { get; init; }
    public required long CreatedTick { get; init; }
    public int Channel { get; set; }
    public int MapId { get; set; }
    public int RoomId { get; set; }
    public HashSet<long> Joined { get; } = [];
    // The characters that did not join in time and were let go (ArenaQueue.Release).
    public HashSet<long> Released { get; } = [];

    public bool Ready => RoomId != 0;
}

// The individual arena matchmaker of the World server (Pvp 9 register with the arena, 10 unregister, 11 join). Not
// thread safe: the caller holds one lock around every call. Times are ticks in milliseconds passed in by the caller.
//
// Chosen here (the client and the data do not settle them): a match takes the arena's playerCount
// (individualarena.xml) of the longest waiting characters as soon as that many wait; with fewer, all the waiting
// ones (at least MinPlayers) once the longest has waited GatherMs. The arena map's own script then waits for its
// players and starts the round. A match nobody joined within NoShowMs, or any match after LifetimeMs, is dropped.
public sealed class ArenaQueue {
    public const int MinPlayers = 2;
    public const long GatherMs = 30_000;
    // The match box counts down 25 s (CSceneStateGame::OnPvp 11, SetTimer(0x19, ...)); the rest is the move.
    public const long NoShowMs = 60_000;
    public const long LifetimeMs = 20 * 60_000;

    private sealed record Waiter(long CharacterId, int Arena, int PlayerCount, long Since);

    private readonly List<Waiter> waiting = [];
    private readonly Dictionary<long, int> matchOf = [];
    private readonly Dictionary<int, ArenaMatch> matches = [];
    private int nextMatchId = 1;

    public bool IsQueued(long characterId) => waiting.Any(waiter => waiter.CharacterId == characterId);

    public ArenaMatch? MatchOf(long characterId) {
        return matchOf.TryGetValue(characterId, out int matchId) ? matches.GetValueOrDefault(matchId) : null;
    }

    public RankDuelError Register(long characterId, int arena, int playerCount, long tick, out List<ArenaMatch> made) {
        made = [];
        // Queued for this arena already: it stays. For another arena: it moves to this one (its wait starts over).
        Waiter? queued = waiting.FirstOrDefault(waiter => waiter.CharacterId == characterId);
        if (queued != null && queued.Arena == arena) {
            return RankDuelError.Queued;
        }
        if (queued != null) {
            waiting.Remove(queued);
        }
        if (matchOf.ContainsKey(characterId)) {
            return RankDuelError.Busy;
        }
        waiting.Add(new Waiter(characterId, arena, Math.Max(MinPlayers, playerCount), tick));
        made = Pair(tick);
        return RankDuelError.none;
    }

    public RankDuelError Unregister(long characterId) {
        return Remove(characterId) ? RankDuelError.none : RankDuelError.NotQueued;
    }

    public bool Remove(long characterId) => waiting.RemoveAll(waiter => waiter.CharacterId == characterId) > 0;

    // The matches the waiting characters make at this tick, by arena.
    public List<ArenaMatch> Pair(long tick) {
        var made = new List<ArenaMatch>();
        foreach (IGrouping<int, Waiter> arena in waiting.GroupBy(waiter => waiter.Arena).ToList()) {
            List<Waiter> order = arena.OrderBy(waiter => waiter.Since).ToList();
            int size = order[0].PlayerCount;
            while (order.Count >= size || (order.Count >= MinPlayers && tick - order[0].Since >= GatherMs)) {
                List<Waiter> taken = order.Take(size).ToList();
                order.RemoveRange(0, taken.Count);
                waiting.RemoveAll(taken.Contains);
                var match = new ArenaMatch {
                    Id = nextMatchId++, Arena = arena.Key, Characters = taken.Select(waiter => waiter.CharacterId).ToList(), CreatedTick = tick,
                };
                matches[match.Id] = match;
                foreach (long characterId in match.Characters) {
                    matchOf[characterId] = match.Id;
                }
                made.Add(match);
            }
        }
        return made;
    }

    // The longest wait in each arena's queue now, in milliseconds (Pvp 12, the arena window's wait labels). Chosen
    // here: what the window shows is how long the first in line has waited; an arena nobody waits for is not listed.
    public Dictionary<int, long> WaitTimes(long tick) {
        return waiting.GroupBy(waiter => waiter.Arena)
            .ToDictionary(arena => arena.Key, arena => tick - arena.Min(waiter => waiter.Since));
    }

    public bool SetRoom(int matchId, int channel, int mapId, int roomId) {
        if (!matches.TryGetValue(matchId, out ArenaMatch? match) || roomId == 0) {
            return false;
        }
        match.Channel = channel;
        match.MapId = mapId;
        match.RoomId = roomId;
        return true;
    }

    public RankDuelError Join(long characterId, out ArenaMatch? match) {
        match = MatchOf(characterId);
        if (match is not { Ready: true }) {
            match = null;
            return RankDuelError.NoMatch;
        }
        match.Joined.Add(characterId);
        return RankDuelError.none;
    }

    public ArenaMatch? End(int matchId) {
        if (!matches.Remove(matchId, out ArenaMatch? match)) {
            return null;
        }
        foreach (long characterId in match.Characters.Where(id => !match.Released.Contains(id))) {
            matchOf.Remove(characterId);
        }
        return match;
    }

    public List<ArenaMatch> EndChannel(int channel) {
        List<ArenaMatch> ended = matches.Values.Where(match => match.Ready && match.Channel == channel).ToList();
        foreach (ArenaMatch match in ended) {
            End(match.Id);
        }
        return ended;
    }

    // The characters of made matches that did not join within NoShowMs (a no on the box, or its time ran out): they
    // leave the match, so they can queue again; the match goes on for those that joined.
    public List<(long CharacterId, int Arena)> Release(long tick) {
        var released = new List<(long, int)>();
        foreach (ArenaMatch match in matches.Values.Where(match => match.Ready && tick - match.CreatedTick >= NoShowMs)) {
            foreach (long characterId in match.Characters) {
                if (match.Joined.Contains(characterId) || !match.Released.Add(characterId)) {
                    continue;
                }
                matchOf.Remove(characterId);
                released.Add((characterId, match.Arena));
            }
        }
        return released;
    }

    public List<ArenaMatch> Expire(long tick) {
        List<ArenaMatch> expired = matches.Values
            .Where(match => tick - match.CreatedTick >= LifetimeMs
                            || (match.Ready && match.Joined.Count == 0 && tick - match.CreatedTick >= NoShowMs))
            .ToList();
        foreach (ArenaMatch match in expired) {
            End(match.Id);
        }
        return expired;
    }
}
