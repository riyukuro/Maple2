using System.Collections.Concurrent;
using Grpc.Core;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Party;
using Maple2.Model.Metadata;
using Maple2.Server.Channel.Service;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;

namespace Maple2.Server.World.Containers;

public class PartyManager : IDisposable {
    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    // ReSharper disable UnusedAutoPropertyAccessor.Global
    public required ChannelClientLookup ChannelClients { get; init; }
    public required PartyLookup PartyLookup { get; init; }
    public required ServerTableMetadataStorage ServerTableMetadata { get; init; }
    private ConstantsTable Constants => ServerTableMetadata.ConstantsTable;
    internal int VoteSeconds => Constants.PartyVoteReadyDurationSeconds;
    // ReSharper restore All
    // ReSharper restore UnusedAutoPropertyAccessor.Global
    #endregion

    public readonly Party Party;
    private readonly ConcurrentDictionary<long, (string, DateTime)> pendingInvites;
    // Numbers the party's votes; the client answers a vote with its number (PartyVoteCli::SendParty).
    private int voteCount;

    public PartyManager(Party party) {
        Party = party;
        pendingInvites = new ConcurrentDictionary<long, (string, DateTime)>();
    }

    public void Dispose() => Dispose(false);

    public void Dispose(bool notify) {
        Broadcast(new PartyRequest {
            Disband = new PartyRequest.Types.Disband { Notify = notify },
        });
    }

    // Members moved in from another party (PartyLookup.MoveMembers): the members already here are told of each, the
    // movers load this party in place of theirs. The dungeon match group already counts them.
    public void AddMoved(IReadOnlyCollection<PartyMember> movers) {
        PartyMember[] receivers = Party.Members.Values.ToArray();
        var moved = new List<PartyMember>();
        foreach (PartyMember mover in movers) {
            var member = new PartyMember {
                PartyId = Party.Id,
                Info = mover.Info,
                JoinTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            };
            Send(new PartyRequest {
                PartyId = Party.Id,
                AddMember = new PartyRequest.Types.AddMember {
                    CharacterId = member.CharacterId,
                    JoinTime = member.JoinTime,
                },
            }, receivers);
            if (Party.Members.TryAdd(member.CharacterId, member)) {
                moved.Add(member);
                if (Party.Search != null) {
                    Party.Search.MemberCount++;
                }
            }
        }
        SendLoad(moved);
    }

    // The receivers load this party from World (a solo searcher's own party, or a move).
    public void SendLoad(IEnumerable<PartyMember> receivers) {
        Send(new PartyRequest {
            PartyId = Party.Id,
            LoadParty = new PartyRequest.Types.LoadParty(),
        }, receivers);
    }

    public void Broadcast(PartyRequest request) {
        if (request.PartyId > 0 && request.PartyId != Party.Id) {
            throw new InvalidOperationException($"Broadcasting {request.PartyCase} for incorrect party: {request.PartyId} => {Party.Id}");
        }

        request.PartyId = Party.Id;
        Send(request, Party.Members.Values);
    }

    // The receivers are taken now; the calls are made when the current matchmaker step releases its lock
    // (PartyLookup.Defer), so a member added or removed by the step after this call is not, or still is, told.
    private void Send(PartyRequest request, IEnumerable<PartyMember> receivers) => SendTo(request, receivers.Select(member => member.Info));

    // The same to players outside the party too (the recruiting notice).
    public void SendTo(PartyRequest request, IEnumerable<PlayerInfo> receivers) {
        (short Channel, long[] Ids)[] targets = receivers
            .GroupBy(info => info.Channel)
            .Select(group => (group.Key, group.Select(info => info.CharacterId).ToArray()))
            .ToArray();
        PartyLookup.Defer(() => {
            foreach ((short channel, long[] ids) in targets) {
                if (!ChannelClients.TryGetClient(channel, out ChannelClient? client)) {
                    continue;
                }

                request.ReceiverIds.Clear();
                request.ReceiverIds.AddRange(ids);

                try {
                    client.Party(request);
                } catch { }
            }
        });
    }

    /// <summary>
    /// Differs from Broadcast as it only broadcasts to voters.
    /// </summary>
    private void BroadcastVote(PartyRequest request) {
        if (Party.Vote == null) {
            return;
        }
        if (request.PartyId > 0 && request.PartyId != Party.Id) {
            throw new InvalidOperationException($"Broadcasting {request.PartyCase} for incorrect party: {request.PartyId} => {Party.Id}");
        }

        request.PartyId = Party.Id;
        // Through the step queue as every broadcast (rv44 MINOR 1).
        Send(request, Party.Members.Values.Where(member => Party.Vote.Voters.Contains(member.CharacterId)).ToArray());
    }

    // The members who count for a dungeon match search: every member, online or not (rv42 MAJOR 1), so the party
    // never outgrows the room's maxUserCount when an offline member comes back.
    public long[] MemberIds() => Party.Members.Keys.ToArray();

    // except: a member going offline (logout), counted offline whether or not their info has caught up.
    public long[] OnlineMembers(long except = 0) => Party.Members.Values
        .Where(member => member.Info.Online && member.CharacterId != except)
        .Select(member => member.CharacterId)
        .ToArray();

    public void FindNewLeader(long characterId) {
        PartyMember? newLeader = Party.Members.Values.FirstOrDefault(m => m.CharacterId != characterId && m.Info.Online);
        if (newLeader == null) {
            CheckForDisband(characterId);
            return;
        }
        UpdateLeader(characterId, newLeader.CharacterId);
    }

    // leavingId: the member leaving, kicked, or going offline.
    public bool CheckForDisband(long leavingId = 0) {
        int online = Party.Members.Values.Count(member => member.Info.Online);
        bool searching = PartyLookup.DungeonMatch.IsSearching(Party.Id) || PartyLookup.DungeonMatch.IsMatching(Party.Id);
        if (DungeonMatchRules.Disbands(Party.Members.Count, online, OnlineMembers(leavingId).Length, searching)) {
            PartyLookup.Disband(Party.Id);
            return true;
        }
        return false;
    }

    public PartyError Invite(long requestorId, PlayerInfo player) {
        if (!Party.Members.TryGetValue(requestorId, out PartyMember? requestor)) {
            return PartyError.s_party_err_not_exist;
        }
        bool isLeader = requestorId == Party.LeaderCharacterId;
        if (!isLeader) {
            return PartyError.s_party_err_not_chief;
        }
        if (Party.Members.Count >= Party.Capacity) {
            return PartyError.s_party_err_full;
        }
        if (Party.Members.ContainsKey(player.CharacterId)) {
            return PartyError.s_party_err_cannot_invite;
        }
        if (!ChannelClients.TryGetClient(player.Channel, out ChannelClient? client)) {
            return PartyError.s_party_err_cannot_invite;
        }

        try {
            pendingInvites[player.CharacterId] = (requestor.Name, DateTime.Now.AddSeconds(30));
            var request = new PartyRequest {
                PartyId = Party.Id,
                ReceiverIds = {
                    player.CharacterId,
                },
                Invite = new PartyRequest.Types.Invite {
                    SenderId = requestor.CharacterId,
                    SenderName = requestor.Name,
                },
            };

            PartyResponse response = client.Party(request);
            return (PartyError) response.Error;
        } catch (RpcException) {
            return PartyError.s_party_err_not_found;
        }
    }

    // matchmaker: the dungeon matchmaker's own join of a searcher, which the queue has counted already.
    public PartyError Join(PlayerInfo info, bool matchmaker = false) => PartyLookup.DungeonMatchStep(() => {
        if (Party.Members.Count >= Party.Capacity) {
            return PartyError.s_party_err_full_limit_player;
        }
        if (!matchmaker && DungeonMatchRules.RefusesJoin(PartyLookup.DungeonMatch.IsMatching(Party.Id), Party.DungeonSet, Party.Members.Count, DungeonMaxUsers)) {
            // The client has no error of its own for it; "the party is full" is the nearest.
            return PartyError.s_party_err_full_limit_player;
        }
        if (Party.Members.ContainsKey(info.CharacterId)) {
            return PartyError.s_party_err_alreadyInvite;
        }

        var member = new PartyMember {
            PartyId = Party.Id,
            Info = info,
            JoinTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };

        Broadcast(new PartyRequest {
            AddMember = new PartyRequest.Types.AddMember {
                CharacterId = member.CharacterId,
                JoinTime = member.JoinTime,
            },
        });
        if (Party.Members.TryAdd(info.CharacterId, member) && Party.Search != null) {
            Party.Search.MemberCount++;
        }
        PartyLookup.CheckDungeonRecruit(this);
        // The matchmaker's own join is placed by its caller; the group is not regrouped under it.
        PartyLookup.DungeonMatchResize(this, regroup: !matchmaker);
        return PartyError.none;
    });

    public PartyError Kick(long requestorId, long characterId) => PartyLookup.DungeonMatchStep(() => {
        if (!Party.Members.TryGetValue(requestorId, out PartyMember? _)) {
            return PartyError.s_party_err_not_found;
        }
        if (requestorId != Party.LeaderCharacterId) {
            return PartyError.s_party_err_not_chief;
        }
        if (characterId == Party.LeaderCharacterId) {
            return PartyError.none;
        }
        if (!Party.Members.TryGetValue(characterId, out PartyMember? member)) {
            return PartyError.s_party_err_not_exist;
        }
        if (CheckForDisband(characterId)) {
            return PartyError.none;
        }

        Broadcast(new PartyRequest {
            RemoveMember = new PartyRequest.Types.RemoveMember {
                CharacterId = member.CharacterId,
                IsKicked = true,
            },
        });
        if (Party.Members.TryRemove(member.CharacterId, out _) && Party.Search != null) {
            Party.Search.MemberCount--;
        }
        PartyLookup.DungeonMatchResize(this);
        return PartyError.none;
    });

    public PartyError Leave(long characterId) => PartyLookup.DungeonMatchStep(() => {
        if (!Party.Members.TryGetValue(characterId, out PartyMember? member)) {
            return PartyError.s_party_err_not_found;
        }

        if (characterId == Party.LeaderCharacterId) {
            FindNewLeader(characterId);
        }
        if (!PartyLookup.TryGet(Party.Id, out _)) {
            // FindNewLeader found no one online and the party went.
            return PartyError.none;
        }

        if (CheckForDisband(characterId)) {
            return PartyError.none;
        }

        Broadcast(new PartyRequest {
            RemoveMember = new PartyRequest.Types.RemoveMember {
                CharacterId = member.CharacterId,
            },
        });
        if (Party.Members.TryRemove(member.CharacterId, out _) && Party.Search != null) {
            Party.Search.MemberCount--;
        }
        PartyLookup.DungeonMatchResize(this);
        return PartyError.none;
    });

    // UpdateLeader, the ready check, the kick vote and the recall are steps too, so their sends keep step order
    // with the matchmaker's (rv44 MINOR 1: a leader hand-over no longer passes an AddMember still queued).
    public PartyError UpdateLeader(long requestorId, long characterId) => PartyLookup.DungeonMatchStep(() => {
        if (!Party.Members.TryGetValue(requestorId, out PartyMember? requestor)) {
            return PartyError.s_party_err_not_found;
        }
        if (!Party.Members.TryGetValue(characterId, out PartyMember? member)) {
            return PartyError.s_party_err_not_found;
        }

        if (requestorId != Party.LeaderCharacterId) {
            return PartyError.s_party_err_not_chief;
        }

        Party.LeaderCharacterId = member.Info.CharacterId;
        Party.LeaderAccountId = member.Info.AccountId;
        Party.LeaderName = member.Info.Name;
        Broadcast(new PartyRequest {
            UpdateLeader = new PartyRequest.Types.UpdateLeader {
                CharacterId = characterId,
            },
        });

        if (Party.Search != null) {
            Party.Search.LeaderCharacterId = member.Info.CharacterId;
            Party.Search.LeaderAccountId = member.Info.AccountId;
            Party.Search.LeaderName = member.Info.Name;
        }

        return PartyError.none;
    });

    // The requestor logged out (Game's CheckDisband). An offline member keeps a slot in a search (rv42 MAJOR 1): the
    // group is not resized. A leader who logs out hands the party over first and the search goes on with the new
    // leader (rv42 MINOR 7); with no member online the party goes, and Disband ends its search.
    public void Disband(long requestorId) => PartyLookup.DungeonMatchStep(() => {
        int onlineCount = OnlineMembers(requestorId).Length;
        if (onlineCount == 0) {
            PartyLookup.Disband(Party.Id);
            return;
        }
        if (requestorId == Party.LeaderCharacterId) {
            FindNewLeader(requestorId);
        }
    });

    public string ConsumeInvite(long characterId) {
        foreach ((long id, (string name, DateTime expiryTime)) in pendingInvites) {
            // Remove any expired entries while iterating.
            if (expiryTime < DateTime.Now) {
                pendingInvites.Remove(id, out _);
                continue;
            }

            if (id == characterId) {
                pendingInvites.Remove(id, out _);
                return name;
            }
        }

        return string.Empty;
    }

    public PartyError StartReadyCheck(long requestorId) => PartyLookup.DungeonMatchStep(() => {
        if (!Party.Members.TryGetValue(requestorId, out PartyMember? requestor)) {
            return PartyError.s_party_err_not_found;
        }
        if (requestorId != Party.LeaderCharacterId) {
            return PartyError.s_party_err_not_chief;
        }
        if (Party.Vote != null) {
            return PartyError.s_party_err_already_vote;
        }

        int voteId = ++voteCount;
        Party.Vote = new PartyVote(PartyVoteType.ReadyCheck, Party.Members.Keys, requestorId) {
            Id = voteId,
        };
        Broadcast(new PartyRequest {
            StartReadyCheck = new PartyRequest.Types.StartReadyCheck {
                CharacterId = requestorId,
                VoteId = voteId,
            },
        });

        PartyLookup.AfterVoteTime(this, () => ExpireVote(voteId));
        return PartyError.none;
    });

    // A vote's timer ends only its own vote: a newer vote started after this one ended keeps its full time.
    internal void ExpireVote(int voteId) {
        if (Party.Vote == null || Party.Vote.Id != voteId) {
            return;
        }
        if (Party.Vote.Type == PartyVoteType.ReadyCheck &&
            Party.Vote.Disapprovals.Count + Party.Vote.Approvals.Count >= Party.Vote.Voters.Count) {
            Party.Vote = null;
            return;
        }

        Broadcast(new PartyRequest {
            ExpiredVote = new PartyRequest.Types.ExpiredVote {
                PartyId = Party.Id,
            },
        });

        Party.Vote = null;
    }

    public PartyError ReadyCheckReply(long requestorId, int voteId, bool reply) => PartyLookup.DungeonMatchStep(() => {
        if (Party.Vote == null) {
            return PartyError.s_party_err_not_found;
        }
        // An answer to an earlier vote (the client sends the number it was given) is not counted in this one.
        if (voteId != 0 && voteId != Party.Vote.Id) {
            return PartyError.none;
        }

        if (!Party.Members.TryGetValue(requestorId, out PartyMember? requestor)) {
            return PartyError.s_party_err_not_found;
        }

        if (Party.Vote.Approvals.Contains(requestorId) || Party.Vote.Disapprovals.Contains(requestorId)) {
            return PartyError.s_party_err_already_vote;
        }

        if (reply) {
            Party.Vote.Approvals.Add(requestorId);
        } else {
            Party.Vote.Disapprovals.Add(requestorId);
        }

        Broadcast(new PartyRequest {
            VoteReply = new PartyRequest.Types.VoteReply {
                CharacterId = requestorId,
                Reply = reply,
                PartyId = Party.Id,
            },
        });


        CheckEndVote();

        return PartyError.none;
    });

    private void CheckEndVote() {
        if (Party.Vote == null) {
            return;
        }

        switch (Party.Vote.Type) {
            case PartyVoteType.ReadyCheck:
                int votes = Party.Vote.Approvals.Count + Party.Vote.Disapprovals.Count;
                if (votes >= Party.Vote.Voters.Count) {
                    Broadcast(new PartyRequest {
                        EndVote = new PartyRequest.Types.EndVote {
                            PartyId = Party.Id,
                        },
                    });
                    Party.Vote = null;
                }
                break;
            case PartyVoteType.Kick:
                if (Party.Vote.Type == PartyVoteType.Kick &&
                    Party.Vote.Approvals.Count >= Party.Vote.VotesNeeded) {
                    // Any member may start a kick vote; once it passes, the party removes the target as its leader would.
                    Kick(Party.LeaderCharacterId, Party.Vote.TargetMember!.CharacterId);

                    Broadcast(new PartyRequest {
                        EndVote = new PartyRequest.Types.EndVote {
                            PartyId = Party.Id,
                        },
                    });
                    // The vote is over: later answers must not kick again, and a new vote can start.
                    Party.Vote = null;
                }
                break;
        }
    }

    public PartyError VoteKick(long requestorId, long targetId) => PartyLookup.DungeonMatchStep(() => {
        if (!Party.Members.ContainsKey(requestorId)) {
            return PartyError.s_party_err_not_found;
        }

        if (!Party.Members.TryGetValue(targetId, out PartyMember? target)) {
            return PartyError.s_party_err_not_found;
        }

        if (Party.Vote != null) {
            return PartyError.s_party_err_already_vote;
        }

        int voteId = ++voteCount;
        ICollection<long> voters = Party.Members.Keys.Where(member => member != targetId).ToList();
        Party.Vote = new PartyVote(PartyVoteType.Kick, voters, requestorId) {
            Id = voteId,
            TargetMember = target,
        };

        BroadcastVote(new PartyRequest {
            PartyId = Party.Id,
            StartVoteKick = new PartyRequest.Types.StartVoteKick {
                CharacterId = requestorId,
                TargetId = targetId,
                ReceiverIds = { Party.Vote.Voters },
                VoteId = voteId,
            },
        });

        // The client does not settle the kick vote's length; the ready check's is kept (FORK.md).
        PartyLookup.AfterVoteTime(this, () => ExpireVote(voteId));

        return PartyError.none;
    });

    // A party recall scroll: every member's channel offers the recall to its members but the requestor.
    public PartyError Recall(long requestorId, int mapId, long endTime) => PartyLookup.DungeonMatchStep(() => {
        if (!Party.Members.TryGetValue(requestorId, out PartyMember? requestor)) {
            return PartyError.s_party_err_not_found;
        }

        Broadcast(new PartyRequest {
            Recall = new PartyRequest.Types.Recall {
                CharacterId = requestorId,
                Name = requestor.Info.Name,
                MapId = mapId,
                EndTime = endTime,
            },
        });
        return PartyError.none;
    });

    // The set room's maxUserCount (0: none set, or not known).
    public int DungeonMaxUsers { get; private set; }

    public PartyError SetDungeon(long requestorId, int dungeonId, bool set, int dungeonRoomId = 0, bool matched = false, int maxUsers = 0) {
        if (requestorId != Party.LeaderCharacterId) {
            return PartyError.s_party_err_not_chief;
        }
        // A room the leader makes is refused while the party searches or is matched: the match would overwrite it
        // and the searchers would skip the party (rv44 MINOR 4). Error 43, as for a search with a room set.
        if (set && !matched && (PartyLookup.DungeonMatch.IsSearching(Party.Id) || PartyLookup.DungeonMatch.IsMatching(Party.Id))) {
            return PartyError.s_party_err_dungeon_match_another;
        }

        DungeonMaxUsers = set ? maxUsers : 0;
        PartyLookup.DungeonRecruitRoomSet(Party.Id, set, dungeonId);
        Party.DungeonSet = set;
        Party.DungeonId = dungeonId;
        Party.DungeonLobbyRoomId = dungeonRoomId;
        Broadcast(new PartyRequest {
            SetDungeon = new PartyRequest.Types.SetDungeon {
                PartyId = Party.Id,
                DungeonId = dungeonId,
                DungeonRoomId = dungeonRoomId,
                Set = set,
                Matched = matched,
            },
        });

        return PartyError.none;
    }
}
