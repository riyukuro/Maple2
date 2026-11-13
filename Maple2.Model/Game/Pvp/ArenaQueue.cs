namespace Maple2.Model.Game.Pvp;

// One individual arena match: its characters, and its field once made. Joined holds those that asked to enter.
public sealed class ArenaMatch {
    public required int Id { get; init; }
    public required int Arena { get; init; }
    public required IReadOnlyList<long> Characters { get; init; }
    // Each character's unit: the squad's first member for a squad (Maple Survival squads), else the character itself.
    public IReadOnlyDictionary<long, long> Units { get; init; } = new Dictionary<long, long>();
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

    // Unit: the waiters that go into a match together (a squad: the squad's first member); a lone waiter is its own.
    private sealed record Waiter(long CharacterId, int Arena, int PlayerCount, long Since, int MinPlayers = MinPlayers, long Unit = 0) {
        public long UnitId => Unit != 0 ? Unit : CharacterId;
    }

    private readonly List<Waiter> waiting = [];
    private readonly Dictionary<long, int> matchOf = [];
    private readonly Dictionary<int, ArenaMatch> matches = [];
    private int nextMatchId = 1;

    public bool IsQueued(long characterId) => waiting.Any(waiter => waiter.CharacterId == characterId);

    public int QueuedArena(long characterId) => waiting.FirstOrDefault(waiter => waiter.CharacterId == characterId)?.Arena ?? 0;

    public ArenaMatch? MatchOf(long characterId) {
        return matchOf.TryGetValue(characterId, out int matchId) ? matches.GetValueOrDefault(matchId) : null;
    }

    public RankDuelError Register(long characterId, int arena, int playerCount, long tick, out List<ArenaMatch> made) =>
        Register(characterId, arena, playerCount, tick, MinPlayers, out made);

    // minPlayers: the fewest a match of this arena may start with after GatherMs (the survival match takes its full
    // count only).
    public RankDuelError Register(long characterId, int arena, int playerCount, long tick, int minPlayers, out List<ArenaMatch> made) {
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
        int size = Math.Max(MinPlayers, playerCount);
        waiting.Add(new Waiter(characterId, arena, size, tick, Math.Clamp(minPlayers, MinPlayers, size)));
        made = Pair(tick);
        return RankDuelError.none;
    }

    // A squad (Maple Survival, 0xC2 mode 4: the party's members, its chief first) waits as one unit: a match takes all
    // of it or none of it. Refused (Busy) when a member waits or plays already.
    public RankDuelError RegisterSquad(IReadOnlyList<long> members, int arena, int playerCount, long tick, int minPlayers, out List<ArenaMatch> made) {
        made = [];
        if (members.Count == 0 || members.Any(member => IsQueued(member) || matchOf.ContainsKey(member))) {
            return RankDuelError.Busy;
        }
        int size = Math.Max(MinPlayers, playerCount);
        foreach (long member in members.Distinct()) {
            waiting.Add(new Waiter(member, arena, size, tick, Math.Clamp(minPlayers, MinPlayers, size), members[0]));
        }
        made = Pair(tick);
        return RankDuelError.none;
    }

    // The characters waiting with this one (its squad, itself included); empty when it does not wait.
    public List<long> UnitOf(long characterId) {
        Waiter? waiter = waiting.FirstOrDefault(entry => entry.CharacterId == characterId);
        return waiter == null ? [] : waiting.Where(entry => entry.UnitId == waiter.UnitId).Select(entry => entry.CharacterId).ToList();
    }

    public RankDuelError Unregister(long characterId) {
        return Remove(characterId) ? RankDuelError.none : RankDuelError.NotQueued;
    }

    // A waiter leaves with its whole unit (a squad member's cancel takes the squad out).
    public bool Remove(long characterId) {
        Waiter? waiter = waiting.FirstOrDefault(entry => entry.CharacterId == characterId);
        return waiter != null && waiting.RemoveAll(entry => entry.UnitId == waiter.UnitId) > 0;
    }

    // The matches the waiting characters make at this tick, by arena.
    public List<ArenaMatch> Pair(long tick) {
        var made = new List<ArenaMatch>();
        foreach (IGrouping<int, Waiter> arena in waiting.GroupBy(waiter => waiter.Arena).ToList()) {
            // Whole units in the order they came: a unit's members all go into one match. A match takes units while they
            // fit its count (a unit that would go past it waits for the next match), and holds at least two units.
            List<List<Waiter>> order = arena.GroupBy(waiter => waiter.UnitId).Select(unit => unit.ToList())
                .OrderBy(unit => unit.Min(waiter => waiter.Since)).ToList();
            int size = order[0][0].PlayerCount;
            int fewest = order[0][0].MinPlayers;
            while (order.Count >= 2) {
                var units = new List<List<Waiter>>();
                int count = 0;
                foreach (List<Waiter> unit in order) {
                    if (count + unit.Count > size) {
                        continue;
                    }
                    units.Add(unit);
                    count += unit.Count;
                    if (count == size) {
                        break;
                    }
                }
                bool gathered = count >= fewest && tick - order[0].Min(waiter => waiter.Since) >= GatherMs;
                if (units.Count < 2 || (count < size && !gathered)) {
                    break;
                }
                List<Waiter> taken = units.SelectMany(unit => unit).ToList();
                order.RemoveAll(units.Contains);
                waiting.RemoveAll(taken.Contains);
                var match = new ArenaMatch {
                    Id = nextMatchId++, Arena = arena.Key, Characters = taken.Select(waiter => waiter.CharacterId).ToList(), CreatedTick = tick,
                    Units = taken.ToDictionary(waiter => waiter.CharacterId, waiter => waiter.UnitId),
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

    // A character of a made match says no (the survival match box's refuse): it leaves the match at once, the match
    // goes on for the others. Its arena, or 0 when it was in no match.
    public int Decline(long characterId) {
        if (MatchOf(characterId) is not { } match || match.Joined.Contains(characterId) || !match.Released.Add(characterId)) {
            return 0;
        }
        matchOf.Remove(characterId);
        return match.Arena;
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
