using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Server.Channel.Service;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Service;

public partial class ChannelService {
    public override Task<GuildResponse> Guild(GuildRequest request, ServerCallContext context) {
        switch (request.GuildCase) {
            case GuildRequest.GuildOneofCase.Invite:
                return Task.FromResult(GuildInvite(request.GuildId, request.ReceiverIds, request.Invite));
            case GuildRequest.GuildOneofCase.InviteReply:
                return Task.FromResult(GuildInviteReply(request.ReceiverIds, request.InviteReply));
            case GuildRequest.GuildOneofCase.AddMember:
                return Task.FromResult(AddGuildMember(request.GuildId, request.ReceiverIds, request.AddMember));
            case GuildRequest.GuildOneofCase.RemoveMember:
                return Task.FromResult(RemoveGuildMember(request.ReceiverIds, request.RemoveMember));
            case GuildRequest.GuildOneofCase.UpdateMember:
                return Task.FromResult(UpdateGuildMember(request.ReceiverIds, request.UpdateMember));
            case GuildRequest.GuildOneofCase.UpdateContribution:
                return Task.FromResult(UpdateGuildContribution(request.ReceiverIds, request.UpdateContribution));
            case GuildRequest.GuildOneofCase.UpdateLeader:
                return Task.FromResult(UpdateGuildLeader(request.ReceiverIds, request.UpdateLeader));
            case GuildRequest.GuildOneofCase.UpdateNotice:
                return Task.FromResult(UpdateGuildNotice(request.ReceiverIds, request.UpdateNotice));
            case GuildRequest.GuildOneofCase.UpdateEmblem:
                return Task.FromResult(UpdateGuildEmblem(request.ReceiverIds, request.UpdateEmblem));
            case GuildRequest.GuildOneofCase.UpdatePoster:
                return Task.FromResult(UpdateGuildPoster(request.ReceiverIds, request.UpdatePoster));
            case GuildRequest.GuildOneofCase.UpdateRank:
                return Task.FromResult(UpdateGuildRank(request.ReceiverIds, request.UpdateRank));
            case GuildRequest.GuildOneofCase.UpdateFocus:
                return Task.FromResult(UpdateGuildFocus(request.ReceiverIds, request.UpdateFocus));
            case GuildRequest.GuildOneofCase.ApplicationAdded:
                return Task.FromResult(GuildApplicationAdded(request.ReceiverIds, request.ApplicationAdded));
            case GuildRequest.GuildOneofCase.ApplicationRemoved:
                return Task.FromResult(GuildApplicationRemoved(request.ReceiverIds, request.ApplicationRemoved));
            case GuildRequest.GuildOneofCase.ApplicationAnswered:
                return Task.FromResult(GuildApplicationAnswered(request.ReceiverIds, request.ApplicationAnswered));
            case GuildRequest.GuildOneofCase.ApplicationResult:
                return Task.FromResult(GuildApplicationResult(request.GuildId, request.ReceiverIds, request.ApplicationResult));
            default:
                return Task.FromResult(new GuildResponse { Error = (int) GuildError.s_guild_err_none });
        }
    }

    private GuildResponse GuildInvite(long guildId, IEnumerable<long> receiverIds, GuildRequest.Types.Invite invite) {
        // There really should only be 1 receiver here, but whatever.
        foreach (long receiverId in receiverIds) {
            if (!server.GetSession(receiverId, out GameSession? session)) {
                return new GuildResponse { Error = (int) GuildError.s_guild_err_wait_inviting };
            }
            if (session.Buddy.IsBlocked(invite.SenderId)) {
                return new GuildResponse { Error = (int) GuildError.s_guild_err_blocked };
            }
            if (session.Guild.Guild != null) {
                return new GuildResponse { Error = (int) GuildError.s_guild_err_has_guild };
            }

            var guildInvite = new GuildInvite {
                GuildId = guildId,
                GuildName = invite.GuildName,
                SenderName = invite.SenderName,
                ReceiverName = session.PlayerName,
            };
            session.Send(GuildPacket.InviteInfo(guildInvite));
        }

        return new GuildResponse();
    }

    private GuildResponse GuildInviteReply(IEnumerable<long> receiverIds, GuildRequest.Types.InviteReply reply) {
        foreach (long characterId in receiverIds) {
            if (!server.GetSession(characterId, out GameSession? session)) {
                continue;
            }

            session.Send(GuildPacket.NotifyInvite(reply.Name, (GuildInvite.Response) reply.Reply));
        }

        return new GuildResponse();
    }

    private GuildResponse AddGuildMember(long guildId, IEnumerable<long> receiverIds, GuildRequest.Types.AddMember add) {
        if (!playerInfos.GetOrFetch(add.CharacterId, out PlayerInfo? info)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_fail_addmember };
        }

        foreach (long characterId in receiverIds) {
            if (!server.GetSession(characterId, out GameSession? session)) {
                continue;
            }

            // Intentionally create a separate GuildMember instance for each session.
            session.Guild.AddMember(add.RequestorName, new GuildMember {
                GuildId = guildId,
                Info = info.Clone(),
                Rank = (byte) add.Rank,
                JoinTime = add.JoinTime,
            });
        }

        return new GuildResponse();
    }

    private GuildResponse RemoveGuildMember(IEnumerable<long> receiverIds, GuildRequest.Types.RemoveMember remove) {
        foreach (long characterId in receiverIds) {
            if (!server.GetSession(characterId, out GameSession? session)) {
                continue;
            }

            if (characterId == remove.CharacterId) {
                session.Send(GuildPacket.NotifyExpel(remove.RequestorName));
                session.Guild.RemoveGuild();
            } else {
                session.Guild.RemoveMember(remove.CharacterId, remove.RequestorName);
            }
        }

        return new GuildResponse();
    }

    private GuildResponse UpdateGuildMember(IEnumerable<long> receiverIds, GuildRequest.Types.UpdateMember update) {
        foreach (long characterId in receiverIds) {
            if (!server.GetSession(characterId, out GameSession? session)) {
                continue;
            }

            if (update.HasRank) {
                session.Guild.UpdateMemberRank(update.RequestorId, update.CharacterId, (byte) update.Rank);
            }
            if (update.HasMessage) {
                session.Guild.UpdateMemberMessage(update.CharacterId, update.Message);
            }
            if (update.HasContribution || update.HasCheckInTime) {
                session.Guild.UpdateMemberContribution(update.CharacterId, update.CheckInTime, update.Contribution);
            }
        }

        return new GuildResponse();
    }

    private GuildResponse UpdateGuildContribution(IEnumerable<long> receiverIds, GuildRequest.Types.UpdateContribution guild) {
        foreach (long characterId in receiverIds) {
            if (!server.GetSession(characterId, out GameSession? session)) {
                continue;
            }

            session.Guild.UpdateGuildExpFunds(guild.ContributorId, guild.GuildExp, guild.GuildFund);
        }

        return new GuildResponse();
    }

    private GuildResponse UpdateGuildLeader(IEnumerable<long> receiverIds, GuildRequest.Types.UpdateLeader update) {
        foreach (long characterId in receiverIds) {
            if (!server.GetSession(characterId, out GameSession? session)) {
                continue;
            }

            session.Guild.UpdateLeader(update.OldLeaderId, update.NewLeaderId);
        }

        return new GuildResponse();
    }

    private GuildResponse UpdateGuildNotice(IEnumerable<long> receiverIds, GuildRequest.Types.UpdateNotice update) {
        foreach (long characterId in receiverIds) {
            if (!server.GetSession(characterId, out GameSession? session)) {
                continue;
            }

            session.Guild.UpdateNotice(update.RequestorName, update.Message);
        }

        return new GuildResponse();
    }

    private GuildResponse UpdateGuildRank(IEnumerable<long> receiverIds, GuildRequest.Types.UpdateRank update) {
        foreach (long characterId in receiverIds) {
            if (!server.GetSession(characterId, out GameSession? session)) {
                continue;
            }

            session.Guild.UpdateRank(update.RequestorName, update.RankId, update.Name, (GuildPermission) update.Permission);
        }

        return new GuildResponse();
    }

    private GuildResponse UpdateGuildFocus(IEnumerable<long> receiverIds, GuildRequest.Types.UpdateFocus update) {
        foreach (long characterId in receiverIds) {
            if (!server.GetSession(characterId, out GameSession? session)) {
                continue;
            }

            session.Guild.UpdateFocus(update.RequestorName, update.Listed, (GuildFocus) update.Focus);
        }

        return new GuildResponse();
    }

    // Guild 45 (CGuildManager::OnPacketJoinRequestReceived): a new row in the join request list of the members
    // who may answer it.
    private GuildResponse GuildApplicationAdded(IEnumerable<long> receiverIds, GuildRequest.Types.ApplicationAdded added) {
        if (!playerInfos.GetOrFetch(added.ApplicantId, out PlayerInfo? info)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_user };
        }

        foreach (long characterId in receiverIds) {
            if (!server.GetSession(characterId, out GameSession? session) || session.Guild.Guild == null) {
                continue;
            }

            session.Send(GuildPacket.ReceiveApplication(new GuildApplication {
                Id = added.ApplicationId,
                Guild = session.Guild.Guild,
                Applicant = info,
                CreationTime = added.CreationTime,
            }));
        }

        return new GuildResponse();
    }

    // Guild 46: the row leaves the list.
    private GuildResponse GuildApplicationRemoved(IEnumerable<long> receiverIds, GuildRequest.Types.ApplicationRemoved removed) {
        foreach (long characterId in receiverIds) {
            if (!server.GetSession(characterId, out GameSession? session)) {
                continue;
            }

            session.Send(GuildPacket.WithdrawApplication(removed.ApplicationId));
        }

        return new GuildResponse();
    }

    // Guild 47: the row leaves the list and the chat names who answered whom.
    private GuildResponse GuildApplicationAnswered(IEnumerable<long> receiverIds, GuildRequest.Types.ApplicationAnswered answered) {
        foreach (long characterId in receiverIds) {
            if (!server.GetSession(characterId, out GameSession? session)) {
                continue;
            }

            session.Send(GuildPacket.NotifyApplication(answered.RequestorName, answered.ApplicantName, answered.ApplicationId, answered.Accepted));
        }

        return new GuildResponse();
    }

    // Guild 48 (CGuildManager::OnPacketMyJoinRequestRemoved), to the applicant. The client reads it only while
    // it has no guild, so it goes out before the guild is loaded.
    private GuildResponse GuildApplicationResult(long guildId, IEnumerable<long> receiverIds, GuildRequest.Types.ApplicationResult result) {
        foreach (long characterId in receiverIds) {
            if (!server.GetSession(characterId, out GameSession? session) || session.Guild.Guild != null) {
                continue;
            }

            session.Send(GuildPacket.NotifyApplicant(result.GuildName, result.ApplicationId, result.Joined));
            if (!result.Joined) {
                continue;
            }

            try {
                Maple2.Server.World.Service.GuildInfoResponse response = session.World.GuildInfo(new Maple2.Server.World.Service.GuildInfoRequest { GuildId = guildId });
                if (response.Guild != null && session.Guild.SetGuild(response.Guild)) {
                    session.Guild.Load();
                }
            } catch (RpcException) { /* the guild is loaded at the next login */ }
        }

        return new GuildResponse();
    }

    private GuildResponse UpdateGuildEmblem(IEnumerable<long> receiverIds, GuildRequest.Types.UpdateEmblem update) {
        foreach (long characterId in receiverIds) {
            if (!server.GetSession(characterId, out GameSession? session)) {
                continue;
            }

            session.Guild.UpdateEmblem(update.RequestorName, update.Emblem);
        }

        return new GuildResponse();
    }

    private GuildResponse UpdateGuildPoster(IEnumerable<long> receiverIds, GuildRequest.Types.UpdatePoster update) {
        foreach (long characterId in receiverIds) {
            if (!server.GetSession(characterId, out GameSession? session)) {
                continue;
            }

            session.Guild.AddOrUpdatePoster(new GuildPoster {
                Id = update.Id,
                Picture = update.Picture,
                OwnerId = update.OwnerId,
                OwnerName = update.OwnerName,
                ResourceId = update.ResourceId,
            });
        }

        return new GuildResponse();
    }
}
