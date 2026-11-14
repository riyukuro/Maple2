using System.Collections.Concurrent;
using Grpc.Core;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Club;
using Maple2.Model.Metadata;
using Maple2.Server.Channel.Service;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;
using ClubResponse = Maple2.Server.Channel.Service.ClubResponse;


namespace Maple2.Server.World.Containers;

public class ClubManager : IDisposable {
    public required GameStorage GameStorage { get; init; }
    public required ChannelClientLookup ChannelClients { get; init; }

    public readonly Club Club;

    private readonly List<long> acceptedInvites = []; // for tracking invites accepted to create club.
    private readonly ConcurrentDictionary<long, (string, DateTime)> pendingInvites;

    // The members, leader and name change under this lock: two members leaving a club of three at once would each see
    // three members and each only leave, keeping a club of one, and a join could pass the member cap the same way.
    private readonly object clubLock = new();
    // A rename tests the name against every club, so renames run one at a time: two clubs cannot take one name.
    private static readonly object RenameLock = new();
    private bool disbanded;


    public ClubManager(Club club) {
        Club = club;
        pendingInvites = new ConcurrentDictionary<long, (string, DateTime)>();
    }

    public ClubError NewClubInvite(long characterId, Model.Enum.ClubResponse reply) {
        lock (clubLock) {
            return NewClubInviteLocked(characterId, reply);
        }
    }

    // acceptedInvites is a plain list: two accepts at once would each add and could both miss the last count.
    private ClubError NewClubInviteLocked(long characterId, Model.Enum.ClubResponse reply) {
        if (Club.State == ClubState.Established) {
            return ClubError.s_club_err_unknown;
        }
        if (!Club.Members.TryGetValue(characterId, out ClubMember? member)) {
            return ClubError.s_club_err_unknown;
        }

        if (reply == Model.Enum.ClubResponse.Accept) {
            if (acceptedInvites.Contains(characterId)) {
                return ClubError.s_club_err_unknown;
            }

            acceptedInvites.Add(characterId);
            // All members have accepted the invite
            if (acceptedInvites.Count == Club.Members.Count) {
                Broadcast(new ClubRequest {
                    Establish = new ClubRequest.Types.Establish(),
                    ClubId = Club.Id,
                });

                using GameStorage.Request db = GameStorage.Context();
                Club.State = ClubState.Established;
                db.SaveClub(Club);
            }
            return ClubError.none;
        }

        Broadcast(new ClubRequest {
            ClubId = Club.Id,
            StagedClubInviteReply = new ClubRequest.Types.StagedClubInviteReply {
                CharacterId = characterId,
                Reply = (int) reply,
                Name = member.Name,
            },
        });

        // Any rejected invite will delete the club.
        Broadcast(new ClubRequest {
            ClubId = Club.Id,
            StagedClubFail = new ClubRequest.Types.StagedClubFail {
                Reply = (int) Model.Enum.ClubResponse.Fail,
            },
        });
        return ClubError.none;
    }

    public ClubError Invite(long requestorId, PlayerInfo info) {
        if (!Club.Members.TryGetValue(requestorId, out ClubMember? requestor)) {
            return ClubError.s_club_err_null_member;
        }

        if (requestorId != Club.LeaderId) {
            return ClubError.s_club_err_no_master;
        }

        if (Club.Members.Count >= Constant.MaxClubMembers) {
            return ClubError.s_club_err_full_member;
        }

        if (Club.Members.Values.Any(member => string.Equals(member.Name, info.Name, StringComparison.CurrentCultureIgnoreCase))) {
            return ClubError.s_club_err_already_exist;
        }

        if (info.ClubIds.Count >= Constant.ClubMaxCount) {
            return ClubError.s_club_err_full_club_member;
        }

        // An offline player (channel -1): the client's "player not found" text (11, s_club_err_null_user, shown as 0x372,
        // clubcli.cpp:1430-1500), not 1 (s_club_err_unknown).
        if (!info.Online) {
            return ClubError.s_club_err_null_user;
        }
        if (!ChannelClients.TryGetClient(info.Channel, out ChannelClient? client)) {
            return ClubError.s_club_err_unknown;
        }

        try {
            pendingInvites[info.CharacterId] = (requestor.Name, DateTime.Now.AddSeconds(30));
            var request = new ClubRequest {
                ClubId = Club.Id,
                ReceiverIds = {
                    info.CharacterId,
                },
                Invite = new ClubRequest.Types.Invite {
                    ClubName = Club.Name,
                    SenderId = requestorId,
                    SenderName = requestor.Name,
                },
            };

            ClubResponse response = client.Club(request);
            return (ClubError) response.Error;
        } catch (RpcException) {
            return ClubError.s_club_err_unknown;
        }
    }

    // The invited player logged out with the invite still open: the members get Club 9 with code 2, the client's
    // "logged out" text (CClubClientManager mode 9, clubcli.cpp:1143-1160 and 1411, s_club_join_reject_logout).
    public bool InviteeLoggedOut(long characterId, string name) {
        if (!pendingInvites.TryRemove(characterId, out (string, DateTime) invite) || invite.Item2 < DateTime.Now) {
            return false;
        }

        Broadcast(new ClubRequest {
            InviteReply = new ClubRequest.Types.InviteReply {
                RequestorName = name,
                Accept = false,
                Code = 2, // 0x34f; 3 is 0x34e (timeout), any other code 0x34d (declined)
            },
            ClubId = Club.Id,
        });
        return true;
    }

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

    // beforeNotice (the accepted invite reply, Club 9) goes to the members once the member is stored, before the join
    // notice: a join refused here (full, already a member, the row not written) tells no one the invite was accepted.
    public ClubError Join(string requestorName, PlayerInfo info, ClubRequest? beforeNotice = null) {
        lock (clubLock) {
            return JoinLocked(requestorName, info, beforeNotice);
        }
    }

    private ClubError JoinLocked(string requestorName, PlayerInfo info, ClubRequest? beforeNotice) {
        if (disbanded) {
            return ClubError.s_club_err_null_club;
        }
        if (Club.Members.Count >= Constant.MaxClubMembers) {
            return ClubError.s_club_err_full_member;
        }

        if (Club.Members.ContainsKey(info.CharacterId)) {
            return ClubError.s_club_err_already_exist;
        }

        using GameStorage.Request db = GameStorage.Context();
        ClubMember? member = db.CreateClubMember(Club.Id, info);
        if (member == null) {
            return ClubError.s_club_err_null_invite_member;
        }

        if (beforeNotice != null) {
            Broadcast(beforeNotice);
        }
        // Broadcast before adding this new member.
        Broadcast(new ClubRequest {
            ClubId = Club.Id,
            AddMember = new ClubRequest.Types.AddMember {
                CharacterId = member.CharacterId,
                RequestorName = requestorName,
                JoinTime = member.JoinTime,
            },
        });
        Club.Members.TryAdd(member.CharacterId, member);
        return ClubError.none;
    }

    // Club 6 (leave, clubcli.cpp) and a deleted character: a club of two, and a staged club for a deletion, is
    // disbanded (disband: ClubLookup.Disband), a bigger one loses the member. Only a member leaves: a stale
    // request from no member changes nothing.
    public ClubError LeaveOrDisband(long characterId, bool deleted, Func<ClubError> disband) {
        lock (clubLock) {
            if (disbanded) {
                return ClubError.s_club_err_null_club;
            }
            if (!Club.Members.ContainsKey(characterId)) {
                return ClubError.s_club_err_null_member;
            }
            if (Club.Members.Count <= 2 || (deleted && Club.State == ClubState.Staged)) {
                return disband();
            }
            return LeaveLocked(characterId, deleted);
        }
    }

    public ClubError Leave(long characterId, bool deleted = false) {
        lock (clubLock) {
            return LeaveLocked(characterId, deleted);
        }
    }

    private ClubError LeaveLocked(long characterId, bool deleted) {
        if (!Club.Members.TryGetValue(characterId, out ClubMember? member)) {
            return ClubError.s_club_err_null_member;
        }

        // The database first: a failed write leaves the member, the leader and every channel as they were.
        long oldLeaderId = Club.LeaderId;
        ClubMember oldLeader = Club.Leader;
        ClubMember? newLeader = characterId == oldLeaderId
            ? Club.Members.Values.FirstOrDefault(other => other.CharacterId != characterId)
            : null;
        if (!Club.Members.TryRemove(member.CharacterId, out _)) {
            return ClubError.s_club_err_unknown;
        }
        if (newLeader != null) {
            Club.Leader = newLeader;
            Club.LeaderId = newLeader.CharacterId;
        }

        bool saved;
        using (GameStorage.Request db = GameStorage.Context()) {
            // The leader left: one SaveClub stores the new leader and drops the member row in one transaction, so the
            // club row never names a character who is no member (loading the club picks the leader among the members).
            saved = newLeader != null ? db.SaveClub(Club) : db.DeleteClubMember(Club.Id, member.CharacterId);
        }
        if (!saved) {
            Club.Leader = oldLeader;
            Club.LeaderId = oldLeaderId;
            Club.Members.TryAdd(member.CharacterId, member);
            return ClubError.s_club_err_unknown;
        }

        if (newLeader != null) {
            BroadcastLeader(newLeader);
        }
        // The member who left gets it too: their channel drops the club from the session.
        Broadcast(new ClubRequest {
            RemoveMember = new ClubRequest.Types.RemoveMember {
                CharacterId = characterId,
                Deleted = deleted,
            },
            ClubId = Club.Id,
        }, member);
        return ClubError.none;
    }

    private void BroadcastLeader(ClubMember newLeader) {
        Broadcast(new ClubRequest {
            UpdateLeader = new ClubRequest.Types.UpdateLeader {
                CharacterId = newLeader.CharacterId,
            },
            ClubId = Club.Id,
        });
    }

    public ClubError ChangeLeader(long requesterId, string name) {
        lock (clubLock) {
            return ChangeLeaderLocked(requesterId, name);
        }
    }

    // The new leader is stored before anyone is told; a failed write keeps the old leader.
    private ClubError ChangeLeaderLocked(long requesterId, string name) {
        ClubError error = ClubLeaderRule.Pick(Club.LeaderId, requesterId,
            Club.Members.Values.Select(member => (member.CharacterId, member.Name)), name, out long newLeaderId);
        if (error != ClubError.none) {
            return error;
        }
        if (!Club.Members.TryGetValue(newLeaderId, out ClubMember? newLeader)) {
            return ClubError.s_club_err_null_member;
        }

        ClubMember oldLeader = Club.Leader;
        long oldLeaderId = Club.LeaderId;
        Club.Leader = newLeader;
        Club.LeaderId = newLeader.CharacterId;
        bool saved;
        using (GameStorage.Request db = GameStorage.Context()) {
            saved = db.SaveClub(Club);
        }
        if (!saved) {
            Club.Leader = oldLeader;
            Club.LeaderId = oldLeaderId;
            return ClubError.s_club_err_unknown;
        }
        Broadcast(new ClubRequest {
            UpdateLeader = new ClubRequest.Types.UpdateLeader {
                CharacterId = newLeader.CharacterId,
            },
            ClubId = Club.Id,
        });
        return ClubError.none;
    }

    // The game server tested the buff against table/clubbuff.xml and the caller the leader.
    public void SetBuff(int buffId, int buffLevel) {
        Club.BuffId = buffId;
        Club.BuffLevel = buffLevel;
        using (GameStorage.Request db = GameStorage.Context()) {
            db.SaveClub(Club);
        }
        Broadcast(new ClubRequest {
            UpdateBuff = new ClubRequest.Types.UpdateBuff {
                BuffId = buffId,
                BuffLevel = buffLevel,
            },
            ClubId = Club.Id,
        });
    }

    public ClubError Rename(string newName) {
        lock (RenameLock) {
            lock (clubLock) {
                return RenameLocked(newName);
            }
        }
    }

    // The name and its cooldown are stored before the members are told; a failed write keeps the old name.
    private ClubError RenameLocked(string newName) {
        if (string.Equals(Club.Name, newName, StringComparison.CurrentCultureIgnoreCase)) {
            return ClubError.s_club_err_same_club_name;
        }

        if (Club.NameChangeCooldown.FromEpochSeconds() > DateTime.Now) {
            return ClubError.s_club_err_remain_time;
        }

        if (newName.Contains(' ')) {
            return ClubError.s_club_err_clubname_has_blank;
        }

        using GameStorage.Request db = GameStorage.Context();
        if (db.ClubExists(newName)) {
            return ClubError.s_club_err_name_exist;
        }

        string oldName = Club.Name;
        long oldCooldown = Club.NameChangeCooldown;
        Club.Name = newName;
        Club.NameChangeCooldown = DateTime.Now.AddHours(1).ToEpochSeconds();
        if (!db.SaveClub(Club)) {
            Club.Name = oldName;
            Club.NameChangeCooldown = oldCooldown;
            return ClubError.s_club_err_unknown;
        }
        Broadcast(new ClubRequest {
            Rename = new ClubRequest.Types.Rename {
                Name = newName,
                ChangedTime = Club.NameChangeCooldown,
            },
            ClubId = Club.Id,
        });

        return ClubError.none;
    }

    // The club rows are already deleted (ClubLookup.Disband), so nothing is saved.
    public void Disband() {
        lock (clubLock) {
            disbanded = true;
        }
        Broadcast(new ClubRequest {
            ClubId = Club.Id,
            Disband = new ClubRequest.Types.Disband(),
        });
    }

    public void Broadcast(ClubRequest request, ClubMember? also = null) {
        if (request.ClubId > 0 && request.ClubId != Club.Id) {
            throw new InvalidOperationException($"Broadcasting {request.ClubCase} for incorrect guild: {request.ClubId} => {Club.Id}");
        }

        IEnumerable<ClubMember> receivers = also == null ? Club.Members.Values : Club.Members.Values.Append(also);
        foreach (IGrouping<short, ClubMember> group in receivers.GroupBy(member => member.Info.Channel)) {
            if (!ChannelClients.TryGetClient(group.Key, out ChannelClient? client)) {
                continue;
            }

            request.ReceiverIds.Clear();
            request.ReceiverIds.AddRange(group.Select(member => member.Info.CharacterId));

            try {
                client.Club(request);
            } catch (Exception ex) {
                Serilog.Log.Warning(ex, "Club {ClubId} {Case} did not reach channel {Channel}", Club.Id, request.ClubCase, group.Key);
            }
        }
    }

    public void Dispose() {
        using GameStorage.Request db = GameStorage.Context();
        db.SaveClub(Club);
    }
}
