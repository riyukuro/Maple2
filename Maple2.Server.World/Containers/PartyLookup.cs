using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Grpc.Core;
using Maple2.Database.Storage;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Party;
using Maple2.Model.Metadata;
using Maple2.Server.World.Service;
using Serilog;

namespace Maple2.Server.World.Containers;

public class PartyLookup : IDisposable {
    #region Autofac Autowired
    private readonly ChannelClientLookup channelClients;
    private readonly PlayerInfoLookup playerLookup;
    private readonly PartySearchLookup partySearchLookup;
    private readonly ServerTableMetadataStorage serverTableMetadata;
    #endregion

    private readonly ConcurrentDictionary<int, PartyManager> parties;
    // The Party 32/33 searches. Every matchmaker step (a search, a cancel, a party join, leave, kick or disband)
    // runs as a DungeonMatchStep under DungeonMatchLock, so a join and the room made for the group it fills do not
    // interleave. The channel calls a step makes (broadcasts, the room) are collected and made after the lock is
    // released, so a slow channel does not hold up every other step; they are made one at a time in step order.
    public DungeonMatchQueue DungeonMatch { get; }
    // The RoomDungeon 15/16 recruits (r63), used inside matchmaker steps as the queue is. Init: the tests' clock.
    public DungeonRecruitBoard DungeonRecruits { get; protected init; } = new();
    private readonly LockedSteps steps = new();
    private readonly ILogger logger = Log.ForContext<PartyLookup>();
    // The last id given; Create takes the next one (Interlocked: a party is made both inside and outside a step).
    private int nextPartyId;

    public PartyLookup(ChannelClientLookup channelClients, PlayerInfoLookup playerLookup, PartySearchLookup partySearchLookup, ServerTableMetadataStorage serverTableMetadata) {
        this.channelClients = channelClients;
        this.playerLookup = playerLookup;
        this.partySearchLookup = partySearchLookup;
        this.serverTableMetadata = serverTableMetadata;

        parties = new ConcurrentDictionary<int, PartyManager>();
        DungeonMatch = new DungeonMatchQueue(partyId => TryGet(partyId, out PartyManager? manager) ? manager.Party.Capacity : 0);
        steps.OnEffectError = ex => logger.Error(ex, "Dungeon match channel call failed");
    }

    public void Dispose() {
        foreach (PartyManager manager in parties.Values) {
            manager.Dispose();
            // Each party's own listing (it was the listing of the last party id given only, r60).
            if (partySearchLookup.TryGetByPartyId(manager.Party.Id, out PartySearchManager? partySearch)) {
                partySearch.Dispose();
            }
        }
    }

    public bool TryGet(int partyId, [NotNullWhen(true)] out PartyManager? party) {
        return parties.TryGetValue(partyId, out party);
    }

    public bool TryGetByCharacter(long characterId, [NotNullWhen(true)] out PartyManager? party) {
        party = null;
        foreach (PartyManager manager in parties.Values) {
            if (manager.Party.Members.TryGetValue(characterId, out PartyMember? member)) {
                party = manager;
                return true;
            }
        }
        return false;
    }

    // leaderInfo: the leader's info fetched beforehand (a dungeon match step, which must not wait for storage).
    public PartyError Create(long leaderId, out int partyId, PlayerInfo? leaderInfo = null) {
        partyId = Interlocked.Increment(ref nextPartyId);

        leaderInfo ??= playerLookup.GetPlayerInfo(leaderId);
        if (leaderInfo == null) {
            return PartyError.s_party_err_not_found;
        }

        var party = new Party(partyId, leaderInfo.AccountId, leaderInfo.CharacterId, leaderInfo.Name);
        var manager = new PartyManager(party) {
            ChannelClients = channelClients,
            PartyLookup = this,
            ServerTableMetadata = serverTableMetadata
        };

        if (!parties.TryAdd(partyId, manager)) {
            return PartyError.s_party_err_not_found;
        }

        return manager.Join(leaderInfo);
    }

    // notify: the members are told even when the party has one member (a search party of one).
    public PartyError Disband(int partyId, bool notify = false) => DungeonMatchStep(() => {
        if (!parties.TryRemove(partyId, out PartyManager? manager)) {
            return PartyError.s_party_err_not_found;
        }
        DungeonMatchResult removed = DungeonMatch.Remove(partyId);
        DungeonRecruits.Forget(partyId);
        if (removed.Outcome == DungeonMatchOutcome.Stopped) {
            // The search goes with the party: the members are told it stopped first (0x1e), as for a cancel.
            BroadcastDungeonMatch(manager, removed.Kind, false);
        }
        RemovePartyListing(partyId);
        manager.Dispose(notify);

        return PartyError.none;
    });

    // The party's Party Finder listing goes with the party (a disband, or a dungeon match move, rv42 MINOR 9); the
    // listing's removal message is a channel call, made after the step.
    private void RemovePartyListing(int partyId) {
        if (partySearchLookup.TryGetByPartyId(partyId, out PartySearchManager? listing)) {
            long listingId = listing.PartySearch.Id;
            Defer(() => partySearchLookup.Remove(listingId));
        }
    }

    // A dungeon match merge or join: the members of from move into to, whose group the queue already counts them
    // in. from goes without a Disband (its members load to in place of it); to's members are told of each mover.
    // Virtual: the tests' seam for a move that fails.
    public virtual PartyError MoveMembers(PartyManager from, PartyManager to) => DungeonMatchStep(() => {
        PartyMember[] movers = from.Party.Members.Values.ToArray();
        if (to.Party.Members.Count + movers.Length > to.Party.Capacity) {
            return PartyError.s_party_err_full_limit_player;
        }
        if (!parties.TryRemove(from.Party.Id, out _)) {
            return PartyError.s_party_err_not_found;
        }
        DungeonMatch.Remove(from.Party.Id);
        DungeonRecruits.Forget(from.Party.Id);
        RemovePartyListing(from.Party.Id);
        to.AddMoved(movers);
        return PartyError.none;
    });

    // A party that may take or be taken in a dungeon match merge: it exists and has no dungeon room set (rv42 MINOR 8).
    public bool MayMerge(int partyId) => TryGet(partyId, out PartyManager? manager) && !manager.Party.DungeonSet && !DungeonMatch.IsMatching(partyId);

    // The new group takes in the waiting smaller groups (rv42 MAJOR 2), and their parties move into it as a merge
    // does. A pulled party that cannot move searches on its own again, and the group keeps the others (MINOR 5).
    public DungeonMatchResult PullDungeonMatch(PartyManager manager) => DungeonMatchStep(() => {
        DungeonMatchPull pull = DungeonMatch.Pull(manager.Party.Id, MayMerge);
        var moved = new List<DungeonMatchGroup>();
        var failed = new List<DungeonMatchGroup>();
        foreach (DungeonMatchGroup pulled in pull.Pulled) {
            bool ok = TryGet(pulled.PartyId, out PartyManager? from) && MoveMembers(from, manager) == PartyError.none;
            (ok ? moved : failed).Add(pulled);
        }
        if (failed.Count == 0) {
            return pull.Result;
        }
        foreach (DungeonMatchGroup group in failed) {
            if (TryGet(group.PartyId, out PartyManager? stayed)) {
                // The party searches on its own again; a restore that finds it full or stopped is completed (rv44 MINOR 6).
                DungeonMatchResult restored = DungeonMatch.Restore(group);
                if (restored.Outcome != DungeonMatchOutcome.Searching) {
                    CompleteDungeonMatch(stayed, restored);
                }
            }
        }
        return DungeonMatch.Restore(pull.Before!, moved);
    });

    // A Party 32 search (moved here from WorldService, r62): the searchers join a group that holds them, else start their own.
    public PartyResponse SearchDungeonMatch(long requestorId, PlayerInfo requestor, byte kind, List<DungeonMatchRoom> rooms) => DungeonMatchStep(() => {
        RefreshDungeonMatch();
        PlayerInfo? info = null;
        long[] ids;
        if (TryGetByCharacter(requestorId, out PartyManager? manager)) {
            if (manager.Party.LeaderCharacterId != requestorId) {
                return new PartyResponse { Error = (int) PartyError.s_dungeonMatch_error_isNotChief };
            }
            if (manager.Party.Members.Values.Any(member => !member.Info.Online)) {
                return new PartyResponse { Error = (int) PartyError.s_dungeonMatch_error_hasOfflineUser };
            }
            if (DungeonMatch.IsSearching(manager.Party.Id)) {
                return new PartyResponse { Error = (int) PartyError.s_party_err_dungeon_match_another };
            }
            if (manager.Party.DungeonSet || DungeonMatch.IsMatching(manager.Party.Id)) {
                // A party with a dungeon room set (rv42 MINOR 8), or matched with its room being made (MINOR 6),
                // does not search; the client has no error of its own for it, 43 is the nearest.
                return new PartyResponse { Error = (int) PartyError.s_party_err_dungeon_match_another };
            }
            // A party that starts a search no longer recruits (r63).
            DungeonRecruits.Close(manager.Party.Id);
            ids = manager.MemberIds();
        } else {
            info = requestor;
            ids = [requestorId];
        }

        DungeonMatchResult? joined = DungeonMatch.Join(kind, rooms, ids, out DungeonMatchGroup? before, MayMerge);
        if (joined != null) {
            if (!TryGet(joined.Value.PartyId, out PartyManager? target)) {
                DungeonMatch.Remove(joined.Value.PartyId);
                DungeonMatch.EndMatching(joined.Value.PartyId);
            } else {
                PartyError error = manager != null ? MoveMembers(manager, target) : JoinSolo(target, info!);
                if (error == PartyError.none) {
                    if (joined.Value.Outcome == DungeonMatchOutcome.Matched) {
                        CompleteDungeonMatch(target, joined.Value);
                    } else {
                        BroadcastDungeonMatch(target, kind, true);
                    }
                    return new PartyResponse { PartyId = target.Party.Id };
                }
                // The group is put back as it was, still searching, and no room is made (rv42 MINOR 5); the
                // searcher then starts a search of their own below (rv44 MINOR 6).
                CompleteDungeonMatch(target, DungeonMatch.Restore(before!));
            }
        }

        bool made = false;
        if (manager == null) {
            PartyError createError = Create(requestorId, out int partyId, requestor);
            if (createError != PartyError.none) {
                return new PartyResponse { Error = (int) createError };
            }
            if (!TryGet(partyId, out manager)) {
                return new PartyResponse { Error = (int) PartyError.s_party_err_not_found };
            }
            made = true;
        }
        DungeonMatchResult own = DungeonMatch.Start(manager.Party.Id, kind, rooms, ids);
        switch (own.Outcome) {
            case DungeonMatchOutcome.Stopped:
                // No room holds the party: nothing was registered.
                if (made) {
                    Disband(manager.Party.Id);
                }
                return new PartyResponse();
            case DungeonMatchOutcome.AlreadySearching:
                return new PartyResponse { Error = (int) PartyError.s_party_err_dungeon_match_another };
        }
        if (made) {
            // The solo searcher's party of one, loaded before the search is announced.
            manager.SendLoad(manager.Party.Members.Values);
        }
        if (own.Outcome == DungeonMatchOutcome.Searching) {
            own = PullDungeonMatch(manager);
        }
        if (own.Outcome == DungeonMatchOutcome.Matched) {
            CompleteDungeonMatch(manager, own);
        } else {
            BroadcastDungeonMatch(manager, kind, true);
        }
        return new PartyResponse { PartyId = manager.Party.Id };
    });

    // RoomDungeon 15/16 (r63): the leader, or a player with no party (who gets a party of one), recruits for a room.
    // The notice goes to the party's members and to the online players at the room's level outside a full or
    // matching party. A party, and a requester in any party, sends one notice per cooldown; inside it the
    // requester gets the larger time left. The requester is held to the room's level and gear score as a joiner.
    public PartyResponse StartDungeonRecruit(PlayerInfo requestor, DungeonRecruitKind kind, DungeonRecruitRoom room, IReadOnlyCollection<PlayerInfo> online) => DungeonMatchStep(() => {
        PartyError own = DungeonRecruitRules.RefusesJoin(requestor, room, 0);
        if (own != PartyError.none) {
            return new PartyResponse { Error = (int) own };
        }
        bool made = false;
        if (TryGetByCharacter(requestor.CharacterId, out PartyManager? manager)) {
            int partyId = manager.Party.Id;
            if (manager.Party.LeaderCharacterId != requestor.CharacterId) {
                return new PartyResponse { Error = (int) PartyError.s_party_err_not_chief };
            }
            if (DungeonMatch.IsSearching(partyId) || DungeonMatch.IsMatching(partyId)) {
                return new PartyResponse { Error = (int) PartyError.s_party_err_dungeon_match_another };
            }
            // The party has another room set: a joiner would be held to one room and join for the other.
            if (manager.Party.DungeonSet && manager.Party.DungeonId != room.Id) {
                return new PartyResponse { Error = (int) PartyError.s_party_err_dungeon_match_another };
            }
            if (room.MaxUsers > 0 && manager.Party.Members.Count >= room.MaxUsers) {
                return new PartyResponse { Error = (int) PartyError.s_party_err_full_limit_player };
            }
            TimeSpan left = DungeonRecruits.CooldownLeft(partyId, requestor.CharacterId);
            if (left > TimeSpan.Zero) {
                return Cooldown(left);
            }
        } else {
            TimeSpan left = DungeonRecruits.CooldownLeftFor(requestor.CharacterId);
            if (left > TimeSpan.Zero) {
                return Cooldown(left);
            }
            PartyError createError = Create(requestor.CharacterId, out int partyId, requestor);
            if (createError != PartyError.none) {
                return new PartyResponse { Error = (int) createError };
            }
            if (!TryGet(partyId, out manager)) {
                return new PartyResponse { Error = (int) PartyError.s_party_err_not_found };
            }
            made = true;
        }

        DungeonRecruit recruit = DungeonRecruits.Start(manager.Party.Id, requestor.CharacterId, kind, room);
        if (made) {
            manager.SendLoad(manager.Party.Members.Values);
        }
        SendDungeonRecruit(manager, recruit, DungeonRecruitTargets(manager, room, online));
        return new PartyResponse { PartyId = manager.Party.Id };
    });

    private static PartyResponse Cooldown(TimeSpan left) =>
        new() { CooldownMilliseconds = Math.Max(1, (int) Math.Ceiling(left.TotalMilliseconds)) };

    // Who gets a party's notice: its members, and the players DungeonRecruitRules.Receives takes.
    public List<PlayerInfo> DungeonRecruitTargets(PartyManager manager, DungeonRecruitRoom room, IEnumerable<PlayerInfo> online) {
        var partyOf = new Dictionary<long, PartyManager>();
        foreach (PartyManager party in parties.Values) {
            foreach (long characterId in party.Party.Members.Keys) {
                partyOf[characterId] = party;
            }
        }
        List<PlayerInfo> targets = manager.Party.Members.Values.Select(member => member.Info).ToList();
        foreach (PlayerInfo player in online) {
            if (manager.Party.Members.ContainsKey(player.CharacterId)) {
                continue;
            }
            bool full = false, matching = false;
            if (partyOf.TryGetValue(player.CharacterId, out PartyManager? other)) {
                full = other.Party.Members.Count >= other.Party.Capacity;
                // Matching: in the matchmaker, searching or in a found match (the client's own gate, CanAskHelp).
                matching = DungeonMatch.IsSearching(other.Party.Id) || DungeonMatch.IsMatching(other.Party.Id);
            }
            if (DungeonRecruitRules.Receives(player, room, full, matching)) {
                targets.Add(player);
            }
        }
        return targets;
    }

    // Virtual: the tests' seam for the notice's channel calls.
    protected virtual void SendDungeonRecruit(PartyManager manager, DungeonRecruit recruit, List<PlayerInfo> targets) {
        PlayerInfo? leader = manager.Party.Members.TryGetValue(manager.Party.LeaderCharacterId, out PartyMember? member) ? member.Info : null;
        int members = manager.Party.Members.Count;
        manager.SendTo(new Channel.Service.PartyRequest {
            PartyId = manager.Party.Id,
            DungeonRecruit = new Channel.Service.PartyRequest.Types.DungeonRecruit {
                Kind = (int) recruit.Kind,
                LeaderName = leader?.Name ?? manager.Party.LeaderName,
                LeaderAccountId = leader?.AccountId ?? manager.Party.LeaderAccountId,
                LeaderId = manager.Party.LeaderCharacterId,
                RoomId = recruit.Room.Id,
                Members = members,
                NeedHelpers = 0,
                NeedMembers = DungeonRecruitRules.NeedMembers(recruit.Room, members),
            },
        }, targets);
    }

    // Party 39/43 (r63): a player with no party joins through a notice's chat link, as an ordinary member.
    public PartyError JoinDungeonRecruit(PlayerInfo joiner, int partyId, int roomId) => DungeonMatchStep(() => {
        if (TryGetByCharacter(joiner.CharacterId, out _)) {
            return PartyError.s_party_err_already;
        }
        if (!TryGet(partyId, out PartyManager? manager)) {
            return PartyError.s_party_err_not_found;
        }
        DungeonRecruit? recruit = DungeonRecruits.Find(partyId, roomId);
        if (recruit == null) {
            return PartyError.s_party_err_not_found;
        }
        if (DungeonMatch.IsSearching(partyId) || DungeonMatch.IsMatching(partyId)) {
            return PartyError.s_party_err_dungeon_match_another;
        }
        PartyError refused = DungeonRecruitRules.RefusesJoin(joiner, recruit.Room, manager.Party.Members.Count);
        return refused != PartyError.none ? refused : manager.Join(joiner);
    });

    // A join filled the party: its recruit closes.
    public void CheckDungeonRecruit(PartyManager manager) => DungeonMatchStep(() => {
        DungeonRecruit? recruit = DungeonRecruits.Get(manager.Party.Id);
        if (recruit == null) {
            return;
        }
        int members = manager.Party.Members.Count;
        if (members >= manager.Party.Capacity || (recruit.Room.MaxUsers > 0 && members >= recruit.Room.MaxUsers)) {
            DungeonRecruits.Close(manager.Party.Id);
        }
    });

    // The party's room was unset or set to another room than its recruit's: the recruit closes.
    public void DungeonRecruitRoomSet(int partyId, bool set, int dungeonId) => DungeonMatchStep(() => {
        DungeonRecruit? recruit = DungeonRecruits.Get(partyId);
        if (recruit != null && (!set || dungeonId != recruit.Room.Id)) {
            DungeonRecruits.Close(partyId);
        }
    });

    // A player with no party takes the slot the queue gave them in target's group.
    // Virtual: the tests' seam for a solo join that fails.
    protected virtual PartyError JoinSolo(PartyManager target, PlayerInfo info) {
        // The queue has counted the searcher and may have marked the party matched already.
        PartyError error = target.Join(info, matchmaker: true);
        if (error == PartyError.none && target.Party.Members.TryGetValue(info.CharacterId, out PartyMember? member)) {
            target.SendLoad([member]);
        }
        return error;
    }

    // Runs body under the matchmaker lock; the channel calls it defers (Defer) are made after the lock is released.
    public T DungeonMatchStep<T>(Func<T> body) => steps.Run(body);
    public void DungeonMatchStep(Action body) => steps.Run(body);
    public static void Defer(Action effect) => LockedSteps.Defer(effect);

    // Runs expire as a step once a party vote's time is up (PartyVoteReadyDurationSeconds).
    protected internal virtual void AfterVoteTime(PartyManager manager, Action expire) {
        int seconds = manager.VoteSeconds;
        Task.Factory.StartNew(() => {
            Thread.Sleep(TimeSpan.FromSeconds(seconds));
            DungeonMatchStep(expire);
        });
    }

    // Before a search: each searching group takes its party's members now; a group whose party is gone stops. A
    // leader who went offline without a logout reaching World hands the party over to an online member and the
    // search goes on (rv42 MINOR 7); with no member online the search stops.
    public void RefreshDungeonMatch() => DungeonMatchStep(() => {
        foreach (int partyId in DungeonMatch.SearchingParties()) {
            if (!TryGet(partyId, out PartyManager? manager)) {
                DungeonMatch.Remove(partyId);
                continue;
            }
            bool leaderOnline = manager.Party.Members.TryGetValue(manager.Party.LeaderCharacterId, out PartyMember? leader) && leader.Info.Online;
            if (!leaderOnline) {
                if (manager.OnlineMembers().Length == 0) {
                    CompleteDungeonMatch(manager, DungeonMatch.Remove(partyId));
                    continue;
                }
                manager.FindNewLeader(manager.Party.LeaderCharacterId);
                if (!TryGet(partyId, out _)) {
                    continue;
                }
            }
            DungeonMatchResize(manager);
        }
    });

    // A member joined or left a party: a searching party's group takes the new count. A group still searching after
    // it is put against the others as a new search is (rv44 MINOR 5): it joins a group at least its size that now
    // holds it, else it pulls the smaller groups that now fit.
    public void DungeonMatchResize(PartyManager manager, bool regroup = true) => DungeonMatchStep(() => {
        DungeonMatchResult result = DungeonMatch.Resize(manager.Party.Id, manager.MemberIds());
        if (regroup && result.Outcome == DungeonMatchOutcome.Searching) {
            DungeonMatchResult? own = RegroupDungeonMatch(manager);
            if (own == null) {
                return;
            }
            result = own.Value;
        }
        if (result.Outcome != DungeonMatchOutcome.NotSearching) {
            CompleteDungeonMatch(manager, result);
        }
    });

    // Null: the party moved into another group's party, which has been completed.
    private DungeonMatchResult? RegroupDungeonMatch(PartyManager manager) {
        DungeonMatchGroup? own = DungeonMatch.Snapshot(manager.Party.Id);
        if (own == null) {
            return new DungeonMatchResult(DungeonMatchOutcome.NotSearching, manager.Party.Id);
        }
        DungeonMatchResult? joined = DungeonMatch.Join(own.Kind, own.Rooms, own.Members, out DungeonMatchGroup? before, MayMerge);
        if (joined != null) {
            if (!TryGet(joined.Value.PartyId, out PartyManager? target)) {
                DungeonMatch.Remove(joined.Value.PartyId);
                DungeonMatch.EndMatching(joined.Value.PartyId);
            } else if (MoveMembers(manager, target) == PartyError.none) {
                if (joined.Value.Outcome == DungeonMatchOutcome.Matched) {
                    CompleteDungeonMatch(target, joined.Value);
                } else {
                    BroadcastDungeonMatch(target, own.Kind, true);
                }
                return null;
            } else {
                // The target's group is put back; this party searches on as it is.
                CompleteDungeonMatch(target, DungeonMatch.Restore(before!));
            }
        }
        return PullDungeonMatch(manager);
    }

    // What a queue step leaves to the party: a matched group gets its room (made on the first channel, as the
    // leader's CreateDungeon would) and the enter box; a stopped one is told the search ended. Searching: nothing.
    // The room is made after the step's lock is released; the party stays marked matching until then (it cannot
    // search again), and a party gone meanwhile has its room destroyed (rv42 MINOR 6). The room calls run on a task
    // of their own, not on the effect drain, so a slow channel holds no other party's sends (rv44 MINOR 1).
    public void CompleteDungeonMatch(PartyManager manager, DungeonMatchResult result) => DungeonMatchStep(() => {
        switch (result.Outcome) {
            case DungeonMatchOutcome.Matched:
                BroadcastDungeonMatch(manager, result.Kind, false);
                int size = manager.Party.Members.Count;
                int partyId = manager.Party.Id;
                long leaderId = manager.Party.LeaderCharacterId;
                Defer(() => RunRoomTask(() => {
                    int lobbyRoomId = 0;
                    try {
                        lobbyRoomId = CreateMatchedRoom(leaderId, partyId, result.RoomId, size);
                    } catch (Exception ex) {
                        logger.Error(ex, "Dungeon match room for party {PartyId} failed", partyId);
                    }
                    // Always: the matching mark ends whatever the room call did (rv44 MINOR 2).
                    DungeonMatchStep(() => {
                        DungeonMatch.EndMatching(partyId);
                        if (!TryGet(partyId, out PartyManager? current)) {
                            if (lobbyRoomId != 0) {
                                Defer(() => RunRoomTask(() => DestroyMatchedRoom(lobbyRoomId)));
                            }
                            return;
                        }
                        if (lobbyRoomId != 0) {
                            current.SetDungeon(current.Party.LeaderCharacterId, result.RoomId, true, lobbyRoomId, matched: true, maxUsers: result.MaxUsers);
                        } else {
                            EndSearchParty(current);
                        }
                    });
                }));
                break;
            case DungeonMatchOutcome.Stopped:
                BroadcastDungeonMatch(manager, result.Kind, false);
                EndSearchParty(manager);
                break;
        }
    });

    private void EndSearchParty(PartyManager manager) => DungeonMatchStep(() => {
        if (DungeonMatchRules.DisbandsWhenSearchEnds(manager.Party.Members.Count) && TryGet(manager.Party.Id, out _)) {
            Disband(manager.Party.Id, notify: true);
        }
    });

    public static void BroadcastDungeonMatch(PartyManager manager, byte kind, bool registered) {
        manager.Broadcast(new Channel.Service.PartyRequest {
            DungeonMatch = new Channel.Service.PartyRequest.Types.DungeonMatch {
                Kind = kind,
                Registered = registered,
            },
        });
    }

    // Virtual: the tests run the room work inline.
    protected virtual void RunRoomTask(Action work) => Task.Run(work);

    // A room call that gets no answer in this time counts as failed (chosen here).
    public const int RoomCallDeadlineSeconds = 10;

    // Virtual: the tests' seam for the channel call.
    protected virtual int CreateMatchedRoom(long leaderId, int partyId, int dungeonId, int size) {
        if (!channelClients.TryGetClient(channelClients.FirstChannel(), out Channel.Service.Channel.ChannelClient? client)) {
            return 0;
        }
        try {
            FieldResponse response = client.Field(new FieldRequest {
                RequesterId = leaderId,
                CreateDungeon = new FieldRequest.Types.CreateDungeon {
                    DungeonId = dungeonId,
                    Size = size,
                    PartyId = partyId,
                },
            }, deadline: DateTime.UtcNow.AddSeconds(RoomCallDeadlineSeconds));
            return response.Error == (int) MigrationError.ok ? response.RoomId : 0;
        } catch (Exception ex) {
            logger.Error(ex, "Dungeon match room for party {PartyId} not made", partyId);
            return 0;
        }
    }

    // The room made for a party that went before it was set: it is destroyed on the channel that made it.
    protected virtual void DestroyMatchedRoom(int lobbyRoomId) {
        if (!channelClients.TryGetClient(channelClients.FirstChannel(), out Channel.Service.Channel.ChannelClient? client)) {
            return;
        }
        try {
            client.Field(new FieldRequest {
                DestroyDungeon = new FieldRequest.Types.DestroyDungeon { RoomId = lobbyRoomId },
            }, deadline: DateTime.UtcNow.AddSeconds(RoomCallDeadlineSeconds));
        } catch (Exception ex) {
            logger.Error(ex, "Dungeon match room {RoomId} not destroyed", lobbyRoomId);
        }
    }
}
