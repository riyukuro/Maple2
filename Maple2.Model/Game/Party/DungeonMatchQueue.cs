namespace Maple2.Model.Game;

public enum DungeonMatchOutcome {
    // The party searches; nothing is full yet.
    Searching,
    // The group is complete for RoomId: the room is made and the party leaves the queue.
    Matched,
    // The search ended without a room (the leader cancelled under the minimum, or the group emptied).
    Stopped,
    // The party is not searching.
    NotSearching,
    // The party searches already (the client's s_party_err_dungeon_match_another).
    AlreadySearching,
}

public readonly record struct DungeonMatchResult(DungeonMatchOutcome Outcome, int PartyId = 0, byte Kind = 0, int RoomId = 0);

// The searching groups, one per party, under one lock. The caller does the party side (join, create, the room)
// with what a call returns; a slot taken by Join that the party then refuses is given back with Leave.
public sealed class DungeonMatchQueue {
    private sealed class Group {
        public required int PartyId;
        public required byte Kind;
        public required List<DungeonMatchRoom> Rooms;
        public required int Users;
        // Searchers Join put in this group: the "members found so far" of rule 2.
        public int Found;
        public required long Order;
    }

    private readonly object mutex = new();
    private readonly Dictionary<int, Group> groups = new();
    private long nextOrder;

    public bool IsSearching(int partyId) {
        lock (mutex) {
            return groups.ContainsKey(partyId);
        }
    }

    public byte KindOf(int partyId) {
        lock (mutex) {
            return groups.TryGetValue(partyId, out Group? group) ? group.Kind : (byte) 0;
        }
    }

    // A party starts its own search for these rooms (a party, or a searcher no group had room for). A group that
    // is already full for one of its rooms is matched at once.
    public DungeonMatchResult Start(int partyId, byte kind, IReadOnlyList<DungeonMatchRoom> rooms, int users) {
        lock (mutex) {
            if (groups.ContainsKey(partyId)) {
                return new DungeonMatchResult(DungeonMatchOutcome.AlreadySearching, partyId, groups[partyId].Kind);
            }
            List<DungeonMatchRoom> usable = rooms
                .Where(room => users <= DungeonMatchRules.MaxUsers(room, users))
                .DistinctBy(room => room.Id)
                .ToList();
            if (usable.Count == 0 || users <= 0) {
                return new DungeonMatchResult(DungeonMatchOutcome.Stopped, partyId, kind);
            }

            var group = new Group { PartyId = partyId, Kind = kind, Rooms = usable, Users = users, Order = nextOrder++ };
            groups[partyId] = group;
            return CheckFull(group);
        }
    }

    // Rule 3: a searcher with no party takes a slot in the largest searching group of the same kind that shares a
    // room with them and has room for one more (the one that started first on a tie). The group then searches
    // only the shared rooms with space. Null: no group fits; the caller starts a new party with Start.
    public DungeonMatchResult? Join(byte kind, IReadOnlyList<DungeonMatchRoom> rooms) {
        lock (mutex) {
            var wanted = rooms.Select(room => room.Id).ToHashSet();
            Group? best = null;
            List<DungeonMatchRoom>? bestRooms = null;
            foreach (Group group in groups.Values) {
                if (group.Kind != kind) {
                    continue;
                }
                List<DungeonMatchRoom> shared = group.Rooms
                    .Where(room => wanted.Contains(room.Id) && DungeonMatchRules.HasSpace(room, group.Users))
                    .ToList();
                if (shared.Count == 0) {
                    continue;
                }
                if (best == null || group.Users > best.Users || (group.Users == best.Users && group.Order < best.Order)) {
                    best = group;
                    bestRooms = shared;
                }
            }
            if (best == null) {
                return null;
            }

            best.Rooms = bestRooms!;
            best.Users++;
            best.Found++;
            DungeonMatchResult result = CheckFull(best);
            return result.Outcome == DungeonMatchOutcome.Matched ? result : result with { Outcome = DungeonMatchOutcome.Searching };
        }
    }

    // Party 34 by the leader (rule 2): when the search found someone and the group has at least the minimum of one
    // of its rooms, the party goes with the members found so far; otherwise the search stops and the party stays
    // as it is (a cancel with nobody found is a plain cancel).
    public DungeonMatchResult Cancel(int partyId) {
        lock (mutex) {
            if (!groups.Remove(partyId, out Group? group)) {
                return new DungeonMatchResult(DungeonMatchOutcome.NotSearching, partyId);
            }
            DungeonMatchRoom? room = group.Rooms
                .Where(candidate => group.Found > 0 && DungeonMatchRules.CanStart(candidate, group.Users))
                .OrderBy(candidate => candidate.Id)
                .Cast<DungeonMatchRoom?>()
                .FirstOrDefault();
            return room == null
                ? new DungeonMatchResult(DungeonMatchOutcome.Stopped, partyId, group.Kind)
                : new DungeonMatchResult(DungeonMatchOutcome.Matched, partyId, group.Kind, room.Value.Id);
        }
    }

    // A member left a searching party (or a Join slot was refused): the group shrinks; an empty group stops.
    public DungeonMatchResult Leave(int partyId) {
        lock (mutex) {
            if (!groups.TryGetValue(partyId, out Group? group)) {
                return new DungeonMatchResult(DungeonMatchOutcome.NotSearching, partyId);
            }
            group.Users--;
            if (group.Found > 0) {
                group.Found--;
            }
            if (group.Users > 0) {
                return new DungeonMatchResult(DungeonMatchOutcome.Searching, partyId, group.Kind);
            }
            groups.Remove(partyId);
            return new DungeonMatchResult(DungeonMatchOutcome.Stopped, partyId, group.Kind);
        }
    }

    // The party's member count changed outside the matchmaker (an invite, a leave, a kick): the group takes the
    // party's count, keeps the rooms that still hold it, and is matched when it is now full for one of them.
    public DungeonMatchResult Resize(int partyId, int users) {
        lock (mutex) {
            if (!groups.TryGetValue(partyId, out Group? group)) {
                return new DungeonMatchResult(DungeonMatchOutcome.NotSearching, partyId);
            }
            group.Users = users;
            group.Rooms = group.Rooms.Where(room => users <= DungeonMatchRules.MaxUsers(room, users)).ToList();
            if (users <= 0 || group.Rooms.Count == 0) {
                groups.Remove(partyId);
                return new DungeonMatchResult(DungeonMatchOutcome.Stopped, partyId, group.Kind);
            }
            return CheckFull(group);
        }
    }

    // The party is gone (disbanded): its search ends.
    public DungeonMatchResult Remove(int partyId) {
        lock (mutex) {
            return groups.Remove(partyId, out Group? group)
                ? new DungeonMatchResult(DungeonMatchOutcome.Stopped, partyId, group.Kind)
                : new DungeonMatchResult(DungeonMatchOutcome.NotSearching, partyId);
        }
    }

    // Called under the lock: when the group is full for a room it leaves the queue matched to the lowest such room.
    private DungeonMatchResult CheckFull(Group group) {
        DungeonMatchRoom? full = group.Rooms
            .Where(room => DungeonMatchRules.IsFull(room, group.Users))
            .OrderBy(room => room.Id)
            .Cast<DungeonMatchRoom?>()
            .FirstOrDefault();
        if (full == null) {
            return new DungeonMatchResult(DungeonMatchOutcome.Searching, group.PartyId, group.Kind);
        }
        groups.Remove(group.PartyId);
        return new DungeonMatchResult(DungeonMatchOutcome.Matched, group.PartyId, group.Kind, full.Value.Id);
    }
}
