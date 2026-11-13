using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Party;
using Maple2.Server.World.Containers;
using ChannelPartyRequest = Maple2.Server.Channel.Service.PartyRequest;

namespace Maple2.Server.World.Service;

public partial class WorldService {
    public override Task<PartyInfoResponse> PartyInfo(PartyInfoRequest request, ServerCallContext context) {
        if (!(request.PartyId != 0 && partyLookup.TryGet(request.PartyId, out PartyManager? manager)) && !(request.CharacterId != 0 && partyLookup.TryGetByCharacter(request.CharacterId, out manager))) {
            return Task.FromResult(new PartyInfoResponse());
        }

        return Task.FromResult(new PartyInfoResponse { Party = ToPartyInfo(manager.Party) });
    }

    public override Task<PartyResponse> Party(PartyRequest request, ServerCallContext context) {
        switch (request.PartyCase) {
            case PartyRequest.PartyOneofCase.Create:
                return Task.FromResult(CreateParty(request.RequestorId, request.Create));
            case PartyRequest.PartyOneofCase.Disband:
                // A Maple Survival squad of the party leaves the queue (every member told).
                pvpLookup.LeaveSquad(request.RequestorId);
                return Task.FromResult(DisbandParty(request.RequestorId, request.Disband));
            case PartyRequest.PartyOneofCase.Invite:
                return Task.FromResult(InviteParty(request.RequestorId, request.Invite));
            case PartyRequest.PartyOneofCase.RespondInvite:
                return Task.FromResult(RespondInviteParty(request.RequestorId, request.RespondInvite));
            case PartyRequest.PartyOneofCase.Leave:
                pvpLookup.LeaveSquad(request.RequestorId);
                return Task.FromResult(LeaveParty(request.RequestorId, request.Leave));
            case PartyRequest.PartyOneofCase.Kick:
                pvpLookup.LeaveSquad(request.Kick.ReceiverId);
                return Task.FromResult(KickParty(request.RequestorId, request.Kick));
            case PartyRequest.PartyOneofCase.UpdateLeader:
                return Task.FromResult(UpdatePartyLeader(request.RequestorId, request.UpdateLeader));
            case PartyRequest.PartyOneofCase.ReadyCheck:
                return Task.FromResult(StartReadyCheck(request.RequestorId, request.ReadyCheck));
            case PartyRequest.PartyOneofCase.VoteReply:
                return Task.FromResult(VoteReply(request.RequestorId, request.VoteReply));
            case PartyRequest.PartyOneofCase.VoteKick:
                return Task.FromResult(VoteKick(request.RequestorId, request.VoteKick));
            case PartyRequest.PartyOneofCase.JoinByPartySearch:
                return Task.FromResult(JoinByPartySearch(request.RequestorId, request.JoinByPartySearch));
            case PartyRequest.PartyOneofCase.SetDungeon:
                return Task.FromResult(SetDungeon(request.RequestorId, request.SetDungeon));
            case PartyRequest.PartyOneofCase.Recall:
                return Task.FromResult(Recall(request.RequestorId, request.Recall));
            case PartyRequest.PartyOneofCase.DungeonMatch:
                return Task.FromResult(DungeonMatch(request.RequestorId, request.DungeonMatch));
            case PartyRequest.PartyOneofCase.CancelDungeonMatch:
                return Task.FromResult(CancelDungeonMatch(request.RequestorId, request.CancelDungeonMatch));
            case PartyRequest.PartyOneofCase.DungeonRecruit:
                return Task.FromResult(DungeonRecruit(request.RequestorId, request.DungeonRecruit));
            case PartyRequest.PartyOneofCase.JoinDungeonRecruit:
                return Task.FromResult(JoinDungeonRecruit(request.RequestorId, request.JoinDungeonRecruit));
            default:
                return Task.FromResult(new PartyResponse { Error = (int) PartyError.none });
        }
    }

    private PartyResponse CreateParty(long requestorId, PartyRequest.Types.Create create) {
        PartyError error = partyLookup.Create(requestorId, out int partyId);
        if (error != PartyError.none) {
            return new PartyResponse { Error = (int) error };
        }
        if (!partyLookup.TryGet(partyId, out PartyManager? manager)) {
            return new PartyResponse { Error = (int) PartyError.s_party_err_not_found };
        }

        return new PartyResponse { Party = ToPartyInfo(manager.Party) };
    }

    private PartyResponse DisbandParty(long requestorId, PartyRequest.Types.Disband disband) {
        if (!partyLookup.TryGet(disband.PartyId, out PartyManager? manager)) {
            return new PartyResponse { Error = (int) PartyError.s_party_err_not_found };
        }

        manager.Disband(requestorId);
        return new PartyResponse { PartyId = disband.PartyId };
    }

    private PartyResponse InviteParty(long requestorId, PartyRequest.Types.Invite invite) {
        if (!partyLookup.TryGet(invite.PartyId, out PartyManager? manager)) {
            return new PartyResponse { Error = (int) PartyError.s_party_err_not_found };
        }
        if (!playerLookup.TryGet(invite.ReceiverId, out PlayerInfo? info)) {
            return new PartyResponse { Error = (int) PartyError.s_party_err_cannot_invite };
        }

        PartyError error = manager.Invite(requestorId, info);
        if (error != PartyError.none) {
            return new PartyResponse { Error = (int) error };
        }

        return new PartyResponse { PartyId = invite.PartyId };
    }

    private PartyResponse RespondInviteParty(long characterId, PartyRequest.Types.RespondInvite respond) {
        if (!partyLookup.TryGet(respond.PartyId, out PartyManager? manager)) {
            return new PartyResponse { Error = (int) PartyError.s_party_err_not_found };
        }
        string requestorName = manager.ConsumeInvite(characterId);
        if (string.IsNullOrEmpty(requestorName)) {
            return new PartyResponse { Error = (int) PartyError.s_party_err_cannot_invite };
        }
        if (!playerLookup.TryGet(characterId, out PlayerInfo? info)) {
            return new PartyResponse { Error = (int) PartyError.none };
        }

        if (respond.Reply == (int) PartyInviteResponse.Accept) {
            PartyError error = manager.Join(info);
            if (error != PartyError.none) {
                return new PartyResponse { Error = (int) error };
            }

            return new PartyResponse { Party = ToPartyInfo(manager.Party) };
        }

        manager.Broadcast(new ChannelPartyRequest {
            InviteReply = new ChannelPartyRequest.Types.InviteReply {
                Name = info.Name,
                Reply = respond.Reply,
            },
        });

        return new PartyResponse { PartyId = manager.Party.Id };
    }

    private PartyResponse LeaveParty(long requestorId, PartyRequest.Types.Leave leave) {
        if (!partyLookup.TryGet(leave.PartyId, out PartyManager? manager)) {
            return new PartyResponse { Error = (int) PartyError.s_party_err_not_found };
        }

        PartyError error = manager.Leave(requestorId);
        if (error != PartyError.none) {
            return new PartyResponse { Error = (int) error };
        }

        return new PartyResponse { PartyId = leave.PartyId };
    }

    private PartyResponse KickParty(long requestorId, PartyRequest.Types.Kick kick) {
        if (!partyLookup.TryGet(kick.PartyId, out PartyManager? manager)) {
            return new PartyResponse { Error = (int) PartyError.s_party_err_not_found };
        }

        PartyError error = manager.Kick(requestorId, kick.ReceiverId);
        if (error != PartyError.none) {
            return new PartyResponse { Error = (int) error };
        }

        return new PartyResponse { PartyId = kick.PartyId };
    }

    private PartyResponse UpdatePartyLeader(long requestorId, PartyRequest.Types.UpdateLeader update) {
        if (!partyLookup.TryGet(update.PartyId, out PartyManager? manager)) {
            return new PartyResponse { Error = (int) PartyError.s_party_err_not_found };
        }

        if (update.CharacterId == 0) {
            manager.FindNewLeader(requestorId);
            return new PartyResponse { PartyId = update.PartyId };
        }

        PartyError error = manager.UpdateLeader(requestorId, update.CharacterId);
        if (error != PartyError.none) {
            return new PartyResponse { Error = (int) error };
        }

        return new PartyResponse { PartyId = update.PartyId };
    }

    private PartyResponse StartReadyCheck(long requestorId, PartyRequest.Types.ReadyCheck readyCheck) {
        if (!partyLookup.TryGet(readyCheck.PartyId, out PartyManager? manager)) {
            return new PartyResponse { Error = (int) PartyError.s_party_err_not_found };
        }

        PartyError error = manager.StartReadyCheck(requestorId);
        return new PartyResponse {
            Error = (int) error,
        };
    }

    private PartyResponse VoteReply(long requestorId, PartyRequest.Types.VoteReply reply) {
        if (!partyLookup.TryGet(reply.PartyId, out PartyManager? manager)) {
            return new PartyResponse { Error = (int) PartyError.s_party_err_not_found };
        }

        PartyError error = manager.ReadyCheckReply(requestorId, reply.VoteId, reply.Reply);
        return new PartyResponse {
            Error = (int) error,
        };
    }

    private PartyResponse VoteKick(long requestorId, PartyRequest.Types.VoteKick voteKick) {
        if (!partyLookup.TryGet(voteKick.PartyId, out PartyManager? manager)) {
            return new PartyResponse { Error = (int) PartyError.s_party_err_not_found };
        }

        PartyError error = manager.VoteKick(requestorId, voteKick.TargetUserId);
        return new PartyResponse {
            Error = (int) error,
        };
    }

    private PartyResponse JoinByPartySearch(long requestorId, PartyRequest.Types.JoinByPartySearch joinByPartySearch) {
        if (!partyLookup.TryGet(joinByPartySearch.PartyId, out PartyManager? manager)) {
            return new PartyResponse { Error = (int) PartyError.s_party_err_not_found };
        }

        if (manager.Party.Search == null) {
            return new PartyResponse { Error = (int) PartyError.s_party_err_invalid_recruit };
        }

        if (!manager.Party.Search.NoApproval) {
            return new PartyResponse { Error = (int) PartyError.s_party_err_wrong_recruit };
        }

        if (manager.Party.Search.Id != joinByPartySearch.PartySearchId) {
            return new PartyResponse { Error = (int) PartyError.s_party_err_wrong_recruit };
        }

        if (manager.Party.Search.Size <= manager.Party.Members.Count) {
            return new PartyResponse { Error = (int) PartyError.s_party_err_full_limit_player };
        }

        if (!playerLookup.TryGet(requestorId, out PlayerInfo? info)) {
            return new PartyResponse { Error = (int) PartyError.none };
        }

        PartyError error = manager.Join(info);
        if (error != PartyError.none) {
            return new PartyResponse { Error = (int) error };
        }

        return new PartyResponse { Party = ToPartyInfo(manager.Party) };
    }

    private PartyResponse SetDungeon(long requestorId, PartyRequest.Types.SetDungeon setDungeon) {
        if (!partyLookup.TryGet(setDungeon.PartyId, out PartyManager? manager)) {
            return new PartyResponse { Error = (int) PartyError.s_party_err_not_found };
        }

        PartyError error = partyLookup.DungeonMatchStep(() => manager.SetDungeon(requestorId, setDungeon.DungeonId, setDungeon.Set, setDungeon.DungeonRoomId, maxUsers: setDungeon.MaxUsers));

        return new PartyResponse { Error = (int) error };
    }

    private PartyResponse Recall(long requestorId, PartyRequest.Types.Recall recall) {
        if (!partyLookup.TryGet(recall.PartyId, out PartyManager? manager)) {
            return new PartyResponse { Error = (int) PartyError.s_party_err_not_found };
        }

        return new PartyResponse { Error = (int) manager.Recall(requestorId, recall.MapId, recall.EndTime) };
    }

    // Party 32/33. A party searches as one group: the leader asks, with every member online. A player with no
    // party, or alone in one, first takes a slot in the largest searching group with room for them (rule 3); a
    // party first joins a larger same-kind group sharing a room when the merged size fits that room's maximum (the
    // user's merge rule). The joiners move into that group's party. Otherwise the party (or a party of one made
    // for the solo searcher) searches as its own group.
    private PartyResponse DungeonMatch(long requestorId, PartyRequest.Types.DungeonMatch match) {
        var kind = (byte) match.Kind;
        List<DungeonMatchRoom> rooms = match.Rooms
            .Select(room => new DungeonMatchRoom(room.Id, room.MinUsers, room.MaxUsers, room.ChangeMaxUsers))
            .ToList();
        if (rooms.Count == 0) {
            return new PartyResponse();
        }

        // Fetched before the step: a player not cached is read from storage, which the matchmaker lock must not wait for.
        if (!playerLookup.TryGet(requestorId, out PlayerInfo? requestor)) {
            return new PartyResponse { Error = (int) PartyError.s_party_err_not_found };
        }

        return partyLookup.SearchDungeonMatch(requestorId, requestor, kind, rooms);
    }

    // Party 34 by the leader: the party goes with the members found so far when that is enough for a room (rule 2),
    // else the search stops. Any member can send 34; only the leader's counts (the client's isNotChief error). A 34
    // of another kind than the party's search, or with no search, changes nothing (rv40 MINOR 10).
    private PartyResponse CancelDungeonMatch(long requestorId, PartyRequest.Types.CancelDungeonMatch cancel) {
        return partyLookup.DungeonMatchStep(() => {
            if (!partyLookup.TryGet(cancel.PartyId, out PartyManager? manager)) {
                return new PartyResponse { Error = (int) PartyError.s_party_err_not_found };
            }
            if (manager.Party.LeaderCharacterId != requestorId) {
                return new PartyResponse { Error = (int) PartyError.s_dungeonMatch_error_isNotChief };
            }

            DungeonMatchResult result = partyLookup.DungeonMatch.Cancel(manager.Party.Id, (byte) cancel.Kind);
            if (result.Outcome != DungeonMatchOutcome.NotSearching) {
                partyLookup.CompleteDungeonMatch(manager, result);
            }
            return new PartyResponse { PartyId = manager.Party.Id };
        });
    }

    // RoomDungeon 15/16 (r63): Game checked the room; the notice goes out from PartyLookup.StartDungeonRecruit.
    private PartyResponse DungeonRecruit(long requestorId, PartyRequest.Types.DungeonRecruit recruit) {
        if (recruit.Kind is not ((int) DungeonRecruitKind.Helper or (int) DungeonRecruitKind.Member)) {
            return new PartyResponse();
        }
        // Fetched before the step, as for DungeonMatch.
        if (!playerLookup.TryGet(requestorId, out PlayerInfo? requestor)) {
            return new PartyResponse { Error = (int) PartyError.s_party_err_not_found };
        }
        var room = new DungeonRecruitRoom(recruit.RoomId, recruit.MinLevel, recruit.GearScore, recruit.MaxUsers);
        return partyLookup.StartDungeonRecruit(requestor, (DungeonRecruitKind) recruit.Kind, room, playerLookup.GetOnlinePlayerInfos());
    }

    // Party 39/43 (r63): the notice's chat link.
    private PartyResponse JoinDungeonRecruit(long requestorId, PartyRequest.Types.JoinDungeonRecruit join) {
        if (!playerLookup.TryGet(requestorId, out PlayerInfo? joiner)) {
            return new PartyResponse { Error = (int) PartyError.s_party_err_not_found };
        }
        PartyError error = partyLookup.JoinDungeonRecruit(joiner, join.PartyId, join.RoomId);
        if (error != PartyError.none) {
            return new PartyResponse { Error = (int) error };
        }
        if (!partyLookup.TryGet(join.PartyId, out PartyManager? manager)) {
            return new PartyResponse { Error = (int) PartyError.s_party_err_not_found };
        }
        return new PartyResponse { Party = ToPartyInfo(manager.Party) };
    }

    private PartyInfo ToPartyInfo(Party party) {
        return new PartyInfo {
            DungeonMatchKind = partyLookup.DungeonMatch.KindOf(party.Id),
            Id = party.Id,
            CreationTime = party.CreationTime,
            LeaderAccountId = party.LeaderAccountId,
            LeaderCharacterId = party.LeaderCharacterId,
            LeaderName = party.LeaderName,
            DungeonId = party.DungeonId,
            DungeonRoomId = party.DungeonLobbyRoomId,
            DungeonSet = party.DungeonSet,
            Members = {
                party.Members.Values.Select(member => new PartyInfo.Types.Member {
                    CharacterId = member.CharacterId,
                    JoinTime = member.JoinTime,
                }),
            },
        };
    }
}
