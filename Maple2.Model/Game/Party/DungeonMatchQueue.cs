namespace Maple2.Model.Game;

public enum DungeonMatchOutcome {
    // The party searches; nothing is full yet.
    Searching,
    // The group is complete for RoomId: the room is made and the party leaves the queue.
    Matched,
    // The search ended without a room (the leader cancelled under the minimum, or the group emptied).
    Stopped,
    // The party is not searching (for this kind).
    NotSearching,
    // The party searches already (the client's s_party_err_dungeon_match_another).
    AlreadySearching,
}

public readonly record struct DungeonMatchResult(DungeonMatchOutcome Outcome, int PartyId = 0, byte Kind = 0, int RoomId = 0);

// The searching groups, one per party, under one lock. A group holds the character ids that count for it (every
// member of the party, online or not: an offline member keeps a slot, rv42 MAJOR 1) and the ones the matchmaker brought in (the "members found so far" of rule 2). The
// caller does the party side (the member move, the room) with what a call returns; members Join counted that the
// party then refuses are given back with Leave.
public sealed class DungeonMatchQueue {
    private sealed class Group {
        public required int PartyId;
        public required byte Kind;
        public required List<DungeonMatchRoom> Rooms;
        public required HashSet<long> Members;
        public HashSet<long> FoundIds = [];
        public required long Order;

        public int Users => Members.Count;
        // Found members still counted: one who left the party is no longer found.
        public int Found => FoundIds.Count(Members.Contains);
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

    public int[] SearchingParties() {
        lock (mutex) {
            return groups.Keys.ToArray();
        }
    }

    // How many members count for the group and how many of them were found (0, 0 when not searching).
    public (int Users, int Found) CountOf(int partyId) {
        lock (mutex) {
            return groups.TryGetValue(partyId, out Group? group) ? (group.Users, group.Found) : (0, 0);
        }
    }

    // A party starts its own search for these rooms (a party no larger group took, or a searcher no group had
    // room for). A group that is already full for one of its rooms is matched at once.
    public DungeonMatchResult Start(int partyId, byte kind, IReadOnlyList<DungeonMatchRoom> rooms, IReadOnlyCollection<long> members) {
        lock (mutex) {
            if (groups.TryGetValue(partyId, out Group? searching)) {
                return new DungeonMatchResult(DungeonMatchOutcome.AlreadySearching, partyId, searching.Kind);
            }
            var ids = members.ToHashSet();
            List<DungeonMatchRoom> usable = rooms
                .Where(room => ids.Count <= DungeonMatchRules.MaxUsers(room))
                .DistinctBy(room => room.Id)
                .ToList();
            if (usable.Count == 0 || ids.Count == 0) {
                return new DungeonMatchResult(DungeonMatchOutcome.Stopped, partyId, kind);
            }

            var group = new Group { PartyId = partyId, Kind = kind, Rooms = usable, Members = ids, Order = nextOrder++ };
            groups[partyId] = group;
            return CheckFull(group);
        }
    }

    // Before a search starts: the searchers (a player with no party, or a party's members) go into the
    // largest searching group of the same kind that shares a room with them and still holds them all in that room
    // (the room's maxUserCount is the cap), the one that started first on a tie. A player alone takes any such
    // group (rule 3); a party only a larger one (the user's merge rule, 11-11). The group then searches only
    // those shared rooms and has the searchers as found members. Null: no group fits; the caller starts its own.
    public DungeonMatchResult? Join(byte kind, IReadOnlyList<DungeonMatchRoom> rooms, IReadOnlyCollection<long> members) {
        lock (mutex) {
            var ids = members.ToHashSet();
            if (ids.Count == 0) {
                return null;
            }
            var wanted = rooms.Select(room => room.Id).ToHashSet();
            Group? best = null;
            List<DungeonMatchRoom>? bestRooms = null;
            foreach (Group group in groups.Values) {
                if (group.Kind != kind || group.Members.Overlaps(ids) || (ids.Count > 1 && group.Users <= ids.Count)) {
                    continue;
                }
                List<DungeonMatchRoom> shared = group.Rooms
                    .Where(room => wanted.Contains(room.Id) && DungeonMatchRules.HasSpace(room, group.Users, ids.Count))
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
            best.Members.UnionWith(ids);
            best.FoundIds.UnionWith(ids);
            DungeonMatchResult result = CheckFull(best);
            return result.Outcome == DungeonMatchOutcome.Matched ? result : result with { Outcome = DungeonMatchOutcome.Searching };
        }
    }

    // Party 34 by the leader for this kind (rule 2): when the search found someone and the group has at least the
    // minimum of one of its rooms, the party goes with the members found so far; otherwise the search stops and the
    // party stays as it is (a cancel with nobody found is a plain cancel). A cancel of another kind is not this
    // party's search: NotSearching, and the search goes on.
    public DungeonMatchResult Cancel(int partyId, byte kind) {
        lock (mutex) {
            if (!groups.TryGetValue(partyId, out Group? group) || group.Kind != kind) {
                return new DungeonMatchResult(DungeonMatchOutcome.NotSearching, partyId);
            }
            groups.Remove(partyId);
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

    // Members Join counted that the party then could not take: they are taken out again; an empty group stops.
    public DungeonMatchResult Leave(int partyId, IReadOnlyCollection<long> members) {
        lock (mutex) {
            if (!groups.TryGetValue(partyId, out Group? group)) {
                return new DungeonMatchResult(DungeonMatchOutcome.NotSearching, partyId);
            }
            group.Members.ExceptWith(members);
            group.FoundIds.ExceptWith(members);
            if (group.Users > 0) {
                return new DungeonMatchResult(DungeonMatchOutcome.Searching, partyId, group.Kind);
            }
            groups.Remove(partyId);
            return new DungeonMatchResult(DungeonMatchOutcome.Stopped, partyId, group.Kind);
        }
    }

    // The party's members changed outside the matchmaker (an invite, a leave, a kick): the group
    // takes them, keeps the rooms that still hold them, and is matched when it is now full for one of them. A found
    // member who is no longer among them is no longer found (rule 2 counts the members found so far).
    public DungeonMatchResult Resize(int partyId, IReadOnlyCollection<long> online) {
        lock (mutex) {
            if (!groups.TryGetValue(partyId, out Group? group)) {
                return new DungeonMatchResult(DungeonMatchOutcome.NotSearching, partyId);
            }
            group.Members = online.ToHashSet();
            group.FoundIds.IntersectWith(group.Members);
            group.Rooms = group.Rooms.Where(room => group.Users <= DungeonMatchRules.MaxUsers(room)).ToList();
            if (group.Users <= 0 || group.Rooms.Count == 0) {
                groups.Remove(partyId);
                return new DungeonMatchResult(DungeonMatchOutcome.Stopped, partyId, group.Kind);
            }
            return CheckFull(group);
        }
    }

    // The party is gone (disbanded or merged): its search ends.
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
