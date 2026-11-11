using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Grpc.Core;
using Maple2.Database.Storage;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Party;
using Maple2.Model.Metadata;
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
    public PartyError MoveMembers(PartyManager from, PartyManager to) => DungeonMatchStep(() => {
        PartyMember[] movers = from.Party.Members.Values.ToArray();
        if (to.Party.Members.Count + movers.Length > to.Party.Capacity) {
            return PartyError.s_party_err_full_limit_player;
        }
        if (!parties.TryRemove(from.Party.Id, out _)) {
            return PartyError.s_party_err_not_found;
        }
        DungeonMatch.Remove(from.Party.Id);
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
            if (TryGet(group.PartyId, out _)) {
                DungeonMatch.Restore(group);
            }
        }
        return DungeonMatch.Restore(pull.Before!, moved);
    });

    // Runs body under the matchmaker lock; the channel calls it defers (Defer) are made after the lock is released.
    public T DungeonMatchStep<T>(Func<T> body) => steps.Run(body);
    public void DungeonMatchStep(Action body) => steps.Run(body);
    public static void Defer(Action effect) => LockedSteps.Defer(effect);

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

    // A member joined or left a party: a searching party's group takes the new count.
    public void DungeonMatchResize(PartyManager manager) => DungeonMatchStep(() => {
        DungeonMatchResult result = DungeonMatch.Resize(manager.Party.Id, manager.MemberIds());
        if (result.Outcome != DungeonMatchOutcome.NotSearching) {
            CompleteDungeonMatch(manager, result);
        }
    });

    // What a queue step leaves to the party: a matched group gets its room (made on the first channel, as the
    // leader's CreateDungeon would) and the enter box; a stopped one is told the search ended. Searching: nothing.
    // The room is made after the step's lock is released; the party stays marked matching until then (it cannot
    // search again), and a party gone meanwhile has its room destroyed (rv42 MINOR 6).
    public void CompleteDungeonMatch(PartyManager manager, DungeonMatchResult result) => DungeonMatchStep(() => {
        switch (result.Outcome) {
            case DungeonMatchOutcome.Matched:
                BroadcastDungeonMatch(manager, result.Kind, false);
                int size = manager.Party.Members.Count;
                int partyId = manager.Party.Id;
                long leaderId = manager.Party.LeaderCharacterId;
                Defer(() => {
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
                                Defer(() => DestroyMatchedRoom(lobbyRoomId));
                            }
                            return;
                        }
                        if (lobbyRoomId != 0) {
                            current.SetDungeon(current.Party.LeaderCharacterId, result.RoomId, true, lobbyRoomId, matched: true, maxUsers: result.MaxUsers);
                        } else {
                            EndSearchParty(current);
                        }
                    });
                });
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
            });
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
            });
        } catch (Exception ex) {
            logger.Error(ex, "Dungeon match room {RoomId} not destroyed", lobbyRoomId);
        }
    }
}
