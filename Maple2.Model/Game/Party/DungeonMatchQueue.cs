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

// MaxUsers: a matched room's maxUserCount (the most the party may then hold, rv44 MINOR 3).
public readonly record struct DungeonMatchResult(DungeonMatchOutcome Outcome, int PartyId = 0, byte Kind = 0, int RoomId = 0, int MaxUsers = 0);

// A copy of a searching group, to put it back (Restore) when the party side of a join or pull fails.
public sealed record DungeonMatchGroup(int PartyId, byte Kind, IReadOnlyList<DungeonMatchRoom> Rooms, IReadOnlyList<long> Members, IReadOnlyList<long> FoundIds, long Order) {
    public int Users => Members.Count;
}

// A new group's pull (DungeonMatchQueue.Pull): the groups it took in, in the order taken, the group as it was
// before, and its result (Matched when the pulls filled it).
public sealed record DungeonMatchPull(DungeonMatchResult Result, DungeonMatchGroup? Before, IReadOnlyList<DungeonMatchGroup> Pulled);

// The searching groups, one per party, under one lock. A group holds the character ids that count for it (every
// member of the party, online or not: an offline member keeps a slot, rv42 MAJOR 1) and the ones the matchmaker brought in (the "members found so far" of rule 2). The
// caller does the party side (the member move, the room) with what a call returns; when the party side fails the
// caller puts the groups back with Restore (rv42 MINOR 5).
//
// The merge rule (rv42 MAJOR 2): the smaller goes into the larger, on equal size the later into the earlier, under
// the room's cap and the target party's capacity, at any time a group starts: a new searcher joins the largest
// fitting group of at least its size (Join), and a group that starts pulls in the fitting smaller groups (Pull).
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
    private readonly Func<int, int> capacityOf;
    // Parties matched whose room is not made yet (rv42 MINOR 6): a new search is refused until EndMatching.
    private readonly HashSet<int> matching = new();
    private long nextOrder;

    // capacityOf: the most members a party may hold (Party.Capacity); a merge never makes a party larger.
    public DungeonMatchQueue(Func<int, int>? capacityOf = null) {
        this.capacityOf = capacityOf ?? (_ => int.MaxValue);
    }

    private static DungeonMatchGroup SnapshotOf(Group group) =>
        new(group.PartyId, group.Kind, group.Rooms.ToList(), group.Members.ToList(), group.FoundIds.ToList(), group.Order);

    public DungeonMatchGroup? Snapshot(int partyId) {
        lock (mutex) {
            return groups.TryGetValue(partyId, out Group? group) ? SnapshotOf(group) : null;
        }
    }

    // Puts a group back as it was (before a join or pull whose party side failed), with the groups in kept taken in
    // again (the pulls whose move went through), and checks it as Start does.
    public DungeonMatchResult Restore(DungeonMatchGroup before, IEnumerable<DungeonMatchGroup>? kept = null) {
        lock (mutex) {
            matching.Remove(before.PartyId);
            var group = new Group {
                PartyId = before.PartyId, Kind = before.Kind, Rooms = before.Rooms.ToList(),
                Members = before.Members.ToHashSet(), FoundIds = before.FoundIds.ToHashSet(), Order = before.Order,
            };
            foreach (DungeonMatchGroup pulled in kept ?? []) {
                var pulledRooms = pulled.Rooms.Select(room => room.Id).ToHashSet();
                group.Members.UnionWith(pulled.Members);
                group.FoundIds.UnionWith(pulled.Members);
                group.Rooms = group.Rooms.Where(room => pulledRooms.Contains(room.Id)).ToList();
            }
            group.Rooms = group.Rooms.Where(room => group.Users <= DungeonMatchRules.MaxUsers(room)).ToList();
            if (group.Users == 0 || group.Rooms.Count == 0) {
                groups.Remove(group.PartyId);
                return new DungeonMatchResult(DungeonMatchOutcome.Stopped, group.PartyId, group.Kind);
            }
            groups[group.PartyId] = group;
            return CheckFull(group);
        }
    }

    private bool Fits(Group target, int joining) => target.Users + joining <= capacityOf(target.PartyId);

    public bool IsSearching(int partyId) {
        lock (mutex) {
            return groups.ContainsKey(partyId);
        }
    }

    // The party was matched and its room is being made.
    public bool IsMatching(int partyId) {
        lock (mutex) {
            return matching.Contains(partyId);
        }
    }

    // The matched party's room is set, or could not be made: it may search again.
    public void EndMatching(int partyId) {
        lock (mutex) {
            matching.Remove(partyId);
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
            if (matching.Contains(partyId)) {
                return new DungeonMatchResult(DungeonMatchOutcome.AlreadySearching, partyId);
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

    public DungeonMatchResult? Join(byte kind, IReadOnlyList<DungeonMatchRoom> rooms, IReadOnlyCollection<long> members) =>
        Join(kind, rooms, members, out _);

    // Before a search starts: the searchers (a player with no party, or a party's members) go into the
    // largest searching group of the same kind, at least their size, that shares a room with them and still holds
    // them all in that room (the room's maxUserCount is the cap) and in its party, the one that started first on a
    // tie (rule 3 and the merge rule). canTake: the parties that may take a merge (not one with a dungeon room set).
    // The group then searches only those shared rooms and has the searchers as found members. before: the group
    // as it was, for Restore. Null: no group fits; the caller starts its own.
    public DungeonMatchResult? Join(byte kind, IReadOnlyList<DungeonMatchRoom> rooms, IReadOnlyCollection<long> members,
                                    out DungeonMatchGroup? before, Func<int, bool>? canTake = null) {
        before = null;
        lock (mutex) {
            var ids = members.ToHashSet();
            if (ids.Count == 0) {
                return null;
            }
            var wanted = rooms.Select(room => room.Id).ToHashSet();
            Group? best = null;
            List<DungeonMatchRoom>? bestRooms = null;
            foreach (Group group in groups.Values) {
                if (group.Kind != kind || group.Members.Overlaps(ids) || group.Users < ids.Count || !Fits(group, ids.Count)
                    || (canTake != null && !canTake(group.PartyId))) {
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

            before = SnapshotOf(best);
            best.Rooms = bestRooms!;
            best.Members.UnionWith(ids);
            best.FoundIds.UnionWith(ids);
            DungeonMatchResult result = CheckFull(best);
            return result.Outcome == DungeonMatchOutcome.Matched ? result : result with { Outcome = DungeonMatchOutcome.Searching };
        }
    }

    // A group that just started takes in the waiting smaller groups of its kind that share a room with it, the
    // largest first, then the earliest, while they fit the room's cap and its party (the merge rule's other half:
    // a party that waited is not left out because a larger one came later). canTake: the parties that may be
    // merged (not one with a dungeon room set). A pull that fills the cap makes the room (Matched).
    public DungeonMatchPull Pull(int partyId, Func<int, bool>? canTake = null) {
        lock (mutex) {
            if (!groups.TryGetValue(partyId, out Group? group)) {
                return new DungeonMatchPull(new DungeonMatchResult(DungeonMatchOutcome.NotSearching, partyId), null, []);
            }
            DungeonMatchGroup before = SnapshotOf(group);
            var pulled = new List<DungeonMatchGroup>();
            List<Group> candidates = groups.Values
                .Where(other => other != group && other.Kind == group.Kind && other.Users < group.Users)
                .OrderByDescending(other => other.Users)
                .ThenBy(other => other.Order)
                .ToList();
            foreach (Group other in candidates) {
                if (other.Users >= group.Users || other.Members.Overlaps(group.Members) || !Fits(group, other.Users)
                    || (canTake != null && (!canTake(other.PartyId) || !canTake(group.PartyId)))) {
                    continue;
                }
                var otherRooms = other.Rooms.Select(room => room.Id).ToHashSet();
                List<DungeonMatchRoom> shared = group.Rooms
                    .Where(room => otherRooms.Contains(room.Id) && DungeonMatchRules.HasSpace(room, group.Users, other.Users))
                    .ToList();
                if (shared.Count == 0) {
                    continue;
                }
                pulled.Add(SnapshotOf(other));
                groups.Remove(other.PartyId);
                group.Rooms = shared;
                group.Members.UnionWith(other.Members);
                group.FoundIds.UnionWith(other.Members);
                DungeonMatchResult checkedFull = CheckFull(group);
                if (checkedFull.Outcome == DungeonMatchOutcome.Matched) {
                    return new DungeonMatchPull(checkedFull, before, pulled);
                }
            }
            return new DungeonMatchPull(new DungeonMatchResult(DungeonMatchOutcome.Searching, partyId, group.Kind), before, pulled);
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
            if (room == null) {
                return new DungeonMatchResult(DungeonMatchOutcome.Stopped, partyId, group.Kind);
            }
            matching.Add(partyId);
            return new DungeonMatchResult(DungeonMatchOutcome.Matched, partyId, group.Kind, room.Value.Id, DungeonMatchRules.MaxUsers(room.Value));
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

    // Called under the lock: when the group is full for a room it leaves the queue matched to the lowest such room,
    // marked matching until its room is set.
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
        matching.Add(group.PartyId);
        return new DungeonMatchResult(DungeonMatchOutcome.Matched, group.PartyId, group.Kind, full.Value.Id, DungeonMatchRules.MaxUsers(full.Value));
    }
}
