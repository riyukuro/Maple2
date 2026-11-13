using System.Text.Json;
using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Server.World.Containers;
using Maple2.Database.Storage;
using Maple2.Model.Validators;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;
using ChannelGuildRequest = Maple2.Server.Channel.Service.GuildRequest;

namespace Maple2.Server.World.Service;

public partial class WorldService {
    public override Task<GuildInfoResponse> GuildInfo(GuildInfoRequest request, ServerCallContext context) {
        if (!guildLookup.TryGet(request.GuildId, out GuildManager? manager)) {
            return Task.FromResult(new GuildInfoResponse());
        }

        return Task.FromResult(new GuildInfoResponse { Guild = ToGuildInfo(manager) });
    }

    public override Task<GuildResponse> Guild(GuildRequest request, ServerCallContext context) {
        switch (request.GuildCase) {
            case GuildRequest.GuildOneofCase.Create:
                return Task.FromResult(CreateGuild(request.RequestorId, request.Create));
            case GuildRequest.GuildOneofCase.Disband:
                return Task.FromResult(DisbandGuild(request.RequestorId, request.Disband));
            case GuildRequest.GuildOneofCase.Invite:
                return Task.FromResult(InviteGuild(request.RequestorId, request.Invite));
            case GuildRequest.GuildOneofCase.RespondInvite:
                return Task.FromResult(RespondInviteGuild(request.RequestorId, request.RespondInvite));
            case GuildRequest.GuildOneofCase.Leave:
                return Task.FromResult(LeaveGuild(request.RequestorId, request.Leave));
            case GuildRequest.GuildOneofCase.Expel:
                return Task.FromResult(ExpelGuild(request.RequestorId, request.Expel));
            case GuildRequest.GuildOneofCase.UpdateMember:
                return Task.FromResult(UpdateGuildMember(request.RequestorId, request.UpdateMember));
            case GuildRequest.GuildOneofCase.CheckIn:
                return Task.FromResult(CheckInGuild(request.RequestorId, request.CheckIn));
            case GuildRequest.GuildOneofCase.UpdateLeader:
                return Task.FromResult(UpdateGuildLeader(request.RequestorId, request.UpdateLeader));
            case GuildRequest.GuildOneofCase.UpdateNotice:
                return Task.FromResult(UpdateGuildNotice(request.RequestorId, request.UpdateNotice));
            case GuildRequest.GuildOneofCase.UpdateEmblem:
                return Task.FromResult(UpdateGuildEmblem(request.RequestorId, request.UpdateEmblem));
            case GuildRequest.GuildOneofCase.Rename:
                return Task.FromResult(RenameGuild(request.RequestorId, request.Rename));
            case GuildRequest.GuildOneofCase.UpdatePoster:
                return Task.FromResult(UpdateGuildPoster(request.RequestorId, request.UpdatePoster));
            case GuildRequest.GuildOneofCase.UpdateRank:
                return Task.FromResult(UpdateGuildRank(request.RequestorId, request.UpdateRank));
            case GuildRequest.GuildOneofCase.UpdateFocus:
                return Task.FromResult(UpdateGuildFocus(request.RequestorId, request.UpdateFocus));
            case GuildRequest.GuildOneofCase.Apply:
                return Task.FromResult(ApplyGuild(request.RequestorId, request.Apply));
            case GuildRequest.GuildOneofCase.CancelApplication:
                return Task.FromResult(CancelGuildApplication(request.RequestorId, request.CancelApplication));
            case GuildRequest.GuildOneofCase.RespondApplication:
                return Task.FromResult(RespondGuildApplication(request.RequestorId, request.RespondApplication));
            case GuildRequest.GuildOneofCase.UpgradeBuff:
                return Task.FromResult(UpgradeGuildBuff(request.RequestorId, request.UpgradeBuff));
            case GuildRequest.GuildOneofCase.TrophyNotice:
                return Task.FromResult(GuildTrophyNotice(request.RequestorId, request.TrophyNotice));
            case GuildRequest.GuildOneofCase.RaidClear:
                return Task.FromResult(GuildRaidClear(request.RequestorId, request.RaidClear));
            case GuildRequest.GuildOneofCase.AddContribution:
                return Task.FromResult(GuildAddContribution(request.RequestorId, request.AddContribution));
            case GuildRequest.GuildOneofCase.VsGameRegister:
                return Task.FromResult(GuildVsGameRegister(request.RequestorId, request.VsGameRegister));
            case GuildRequest.GuildOneofCase.VsGameUnregister:
                return Task.FromResult(GuildVsGameUnregister(request.RequestorId, request.VsGameUnregister));
            case GuildRequest.GuildOneofCase.VsGameJoin:
                return Task.FromResult(GuildVsGameJoin(request.RequestorId, request.VsGameJoin));
            case GuildRequest.GuildOneofCase.VsGameEnd:
                guildVsGameLookup.End(request.VsGameEnd.MatchId);
                return Task.FromResult(new GuildResponse());
            case GuildRequest.GuildOneofCase.PvpRegister:
                return Task.FromResult(GuildPvpRegister(request.RequestorId, request.PvpRegister));
            case GuildRequest.GuildOneofCase.PvpUnregister:
                return Task.FromResult(GuildPvpUnregister(request.RequestorId, request.PvpUnregister));
            case GuildRequest.GuildOneofCase.PvpJoin:
                return Task.FromResult(GuildPvpJoin(request.RequestorId, request.PvpJoin));
            case GuildRequest.GuildOneofCase.PvpEnd:
                return Task.FromResult(GuildPvpEnd(request.PvpEnd));
            case GuildRequest.GuildOneofCase.UpgradeNpc:
                return Task.FromResult(UpgradeGuildNpc(request.RequestorId, request.UpgradeNpc));
            case GuildRequest.GuildOneofCase.UpgradeHouse:
                return Task.FromResult(UpgradeGuildHouse(request.RequestorId, request.UpgradeHouse));
            case GuildRequest.GuildOneofCase.ChangeHouseTheme:
                return Task.FromResult(ChangeGuildHouseTheme(request.RequestorId, request.ChangeHouseTheme));
            case GuildRequest.GuildOneofCase.UseBuff:
                return Task.FromResult(UseGuildBuff(request.RequestorId, request.UseBuff));
            case GuildRequest.GuildOneofCase.OpenEvent:
                return Task.FromResult(OpenGuildEvent(request.RequestorId, request.OpenEvent));
            case GuildRequest.GuildOneofCase.Recall:
                return Task.FromResult(RecallGuild(request.RequestorId, request.Recall));
            case GuildRequest.GuildOneofCase.AddExpFund:
                return Task.FromResult(AddGuildExpFund(request.RequestorId, request.AddExpFund));
            case GuildRequest.GuildOneofCase.SpendFund:
                return Task.FromResult(SpendGuildFund(request.RequestorId, request.SpendFund));
            case GuildRequest.GuildOneofCase.StorageGift:
                return Task.FromResult(GiftGuildStorage(request.RequestorId, request.StorageGift));
            case GuildRequest.GuildOneofCase.TakeMailTurn:
                return Task.FromResult(TakeGuildMailTurn(request.RequestorId, request.TakeMailTurn));
            case GuildRequest.GuildOneofCase.Donate:
                return Task.FromResult(DonateGuild(request.RequestorId, request.Donate));
            default:
                return Task.FromResult(new GuildResponse { Error = (int) GuildError.s_guild_err_none });
        }
    }

    // The open join requests of a character who is about to become a member; the storage removes them with
    // the member insert, and NotifyApplicationsRemoved tells the guilds that held them (46).
    private IList<GuildApplicationRow> OpenApplicationsOf(long characterId) {
        using GameStorage.Request db = guildLookup.Storage();
        return db.GetGuildApplicationsOf(characterId);
    }

    private void NotifyApplicationsRemoved(IEnumerable<GuildApplicationRow> rows, long exceptGuildId = 0) {
        foreach (GuildApplicationRow row in rows) {
            if (row.GuildId == exceptGuildId || !guildLookup.TryGet(row.GuildId, out GuildManager? manager)) {
                continue;
            }
            manager.BroadcastToApplicationHandlers(new ChannelGuildRequest {
                ApplicationRemoved = new ChannelGuildRequest.Types.ApplicationRemoved {
                    ApplicationId = row.Id,
                },
            });
        }
    }

    private GuildResponse CreateGuild(long requestorId, GuildRequest.Types.Create create) {
        IList<GuildApplicationRow> open = OpenApplicationsOf(requestorId);
        GuildError error = guildLookup.Create(create.GuildName, requestorId, out long guildId);
        if (error != GuildError.none) {
            return new GuildResponse { Error = (int) error };
        }
        NotifyApplicationsRemoved(open);
        if (!guildLookup.TryGet(guildId, out GuildManager? manager)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        }

        return new GuildResponse { Guild = ToGuildInfo(manager) };
    }

    // TODO: Send reject mail to any applicants
    private GuildResponse DisbandGuild(long requestorId, GuildRequest.Types.Disband disband) {
        GuildError error = guildLookup.Disband(requestorId, disband.GuildId);
        if (error != GuildError.none) {
            return new GuildResponse { Error = (int) error };
        }

        return new GuildResponse { GuildId = disband.GuildId };
    }

    private GuildResponse InviteGuild(long requestorId, GuildRequest.Types.Invite invite) {
        if (!guildLookup.TryGet(invite.GuildId, out GuildManager? manager)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        }
        if (!playerLookup.TryGet(invite.ReceiverId, out PlayerInfo? info)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_user };
        }

        GuildError error = manager.Invite(requestorId, info);
        if (error != GuildError.none) {
            return new GuildResponse { Error = (int) error };
        }

        return new GuildResponse { GuildId = invite.GuildId };
    }

    private GuildResponse RespondInviteGuild(long requestorId, GuildRequest.Types.RespondInvite respond) {
        if (!guildLookup.TryGet(respond.GuildId, out GuildManager? manager)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_invalid_guild };
        }
        string requestorName = manager.ConsumeInvite(requestorId);
        if (string.IsNullOrEmpty(requestorName)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_invite_member };
        }
        if (!playerLookup.TryGet(requestorId, out PlayerInfo? info)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_none };
        }

        if (respond.Accepted) {
            IList<GuildApplicationRow> open = OpenApplicationsOf(requestorId);
            // KMS2 order for the members: the invite answer (Guild 6), then the join notice (18) and the member data (32).
            GuildError error = manager.Join(requestorName, info, new ChannelGuildRequest {
                InviteReply = new ChannelGuildRequest.Types.InviteReply {
                    Name = info.Name,
                    Reply = (int) GuildInvite.Response.Accept,
                },
            });
            if (error != GuildError.none) {
                return new GuildResponse { Error = (int) error };
            }
            NotifyApplicationsRemoved(open);

            return new GuildResponse { Guild = ToGuildInfo(manager) };
        }

        manager.Broadcast(new ChannelGuildRequest {
            InviteReply = new ChannelGuildRequest.Types.InviteReply {
                Name = info.Name,
                Reply = (int) GuildInvite.Response.RejectInvite,
            },
        });

        return new GuildResponse { GuildId = manager.Guild.Id };
    }

    private GuildResponse LeaveGuild(long requestorId, GuildRequest.Types.Leave leave) {
        if (!guildLookup.TryGet(leave.GuildId, out GuildManager? manager)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        }

        GuildError error = manager.Leave(requestorId);
        if (error != GuildError.none) {
            return new GuildResponse { Error = (int) error };
        }

        return new GuildResponse { GuildId = leave.GuildId };
    }

    private GuildResponse ExpelGuild(long requestorId, GuildRequest.Types.Expel expel) {
        if (!guildLookup.TryGet(expel.GuildId, out GuildManager? manager)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        }

        GuildError error = manager.Expel(requestorId, expel.ReceiverId);
        if (error != GuildError.none) {
            return new GuildResponse { Error = (int) error };
        }

        return new GuildResponse { GuildId = expel.GuildId };
    }

    private GuildResponse UpdateGuildMember(long requestorId, GuildRequest.Types.UpdateMember update) {
        if (!guildLookup.TryGet(update.GuildId, out GuildManager? manager)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        }

        byte? rankId = update.HasRank ? (byte) update.Rank : null;
        string? message = update.HasMessage ? update.Message : null;
        GuildError error = manager.UpdateMember(requestorId, update.CharacterId, rankId, message);
        if (error != GuildError.none) {
            return new GuildResponse { Error = (int) error };
        }

        return new GuildResponse { GuildId = update.GuildId };
    }

    private GuildResponse CheckInGuild(long requestorId, GuildRequest.Types.CheckIn checkIn) {
        if (!guildLookup.TryGet(checkIn.GuildId, out GuildManager? manager)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        }

        GuildError error = manager.CheckIn(requestorId);
        if (error != GuildError.none) {
            return new GuildResponse { Error = (int) error };
        }

        return new GuildResponse { GuildId = checkIn.GuildId };
    }

    private GuildResponse UpdateGuildLeader(long requestorId, GuildRequest.Types.UpdateLeader leader) {
        if (!guildLookup.TryGet(leader.GuildId, out GuildManager? manager)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        }

        GuildError error = manager.UpdateLeader(requestorId, leader.LeaderId);
        if (error != GuildError.none) {
            return new GuildResponse { Error = (int) error };
        }

        return new GuildResponse { GuildId = leader.GuildId };
    }

    private GuildResponse UpdateGuildNotice(long requestorId, GuildRequest.Types.UpdateNotice notice) {
        if (!guildLookup.TryGet(notice.GuildId, out GuildManager? manager)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        }

        GuildError error = manager.UpdateNotice(requestorId, notice.Message);
        if (error != GuildError.none) {
            return new GuildResponse { Error = (int) error };
        }

        return new GuildResponse { GuildId = notice.GuildId };
    }

    private GuildResponse UpdateGuildRank(long requestorId, GuildRequest.Types.UpdateRank rank) {
        if (!guildLookup.TryGet(rank.GuildId, out GuildManager? manager)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        }

        GuildError error = manager.UpdateRank(requestorId, rank.RankId, rank.Name, (GuildPermission) rank.Permission);
        if (error != GuildError.none) {
            return new GuildResponse { Error = (int) error };
        }

        return new GuildResponse { GuildId = rank.GuildId };
    }

    private GuildResponse UpdateGuildFocus(long requestorId, GuildRequest.Types.UpdateFocus focus) {
        if (!guildLookup.TryGet(focus.GuildId, out GuildManager? manager)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        }

        GuildError error = manager.UpdateFocus(requestorId, focus.Listed, focus.Focus);
        if (error != GuildError.none) {
            return new GuildResponse { Error = (int) error };
        }

        return new GuildResponse { GuildId = focus.GuildId };
    }

    private GuildResponse RenameGuild(long requestorId, GuildRequest.Types.Rename rename) {
        if (!guildLookup.TryGet(rename.GuildId, out GuildManager? manager)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        }

        GuildError error = manager.Rename(requestorId, rename.Name);
        if (error != GuildError.none) {
            return new GuildResponse { Error = (int) error };
        }

        return new GuildResponse { GuildId = rename.GuildId };
    }

    private GuildResponse UpdateGuildEmblem(long requestorId, GuildRequest.Types.UpdateEmblem emblem) {
        if (!guildLookup.TryGet(emblem.GuildId, out GuildManager? manager)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        }

        GuildError error = manager.UpdateEmblem(requestorId, emblem.Emblem);
        if (error != GuildError.none) {
            return new GuildResponse { Error = (int) error };
        }

        return new GuildResponse { GuildId = emblem.GuildId };
    }

    private GuildResponse UpdateGuildPoster(long requestorId, GuildRequest.Types.UpdatePoster poster) {
        if (!guildLookup.TryGet(poster.GuildId, out GuildManager? manager)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        }

        GuildError error = manager.AddOrUpdatePoster(poster.Id, poster.Picture, poster.OwnerId, poster.OwnerName, poster.ResourceId);
        if (error != GuildError.none) {
            return new GuildResponse { Error = (int) error };
        }

        return new GuildResponse { GuildId = poster.GuildId };
    }

    // Guild 82: a player without a guild asks to join a listed guild.
    private GuildResponse ApplyGuild(long requestorId, GuildRequest.Types.Apply apply) {
        if (!guildLookup.TryGet(apply.GuildId, out GuildManager? manager)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        }
        if (!playerLookup.TryGet(requestorId, out PlayerInfo? _)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_user };
        }

        using GameStorage.Request db = guildLookup.Storage();
        GuildError error = GuildRules.CheckApply(manager.Guild.Listed, db.GetGuildId(requestorId),
            db.GetGuildApplicationsOf(requestorId), apply.GuildId);
        if (error != GuildError.none) {
            return new GuildResponse { Error = (int) error };
        }

        GuildApplicationRow? created = db.CreateGuildApplication(apply.GuildId, requestorId);
        if (created == null) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_unknown };
        }

        manager.BroadcastToApplicationHandlers(new ChannelGuildRequest {
            ApplicationAdded = new ChannelGuildRequest.Types.ApplicationAdded {
                ApplicationId = created.Id,
                ApplicantId = created.ApplicantId,
                CreationTime = created.CreationTime,
            },
        });

        return new GuildResponse {
            GuildId = manager.Guild.Id,
            ApplicationId = created.Id,
            GuildName = manager.Guild.Name,
        };
    }

    // Guild 83: the applicant takes a request back.
    private GuildResponse CancelGuildApplication(long requestorId, GuildRequest.Types.CancelApplication cancel) {
        using GameStorage.Request db = guildLookup.Storage();
        GuildApplicationRow? row = GuildRules.FindOwnApplication(db.GetGuildApplicationsOf(requestorId), cancel.Id);
        if (row == null) {
            return new GuildResponse { Error = (int) GuildError.s_guild_search_null_join_guild_request };
        }
        if (!db.DeleteGuildApplication(row.Id)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_unknown };
        }

        string guildName = string.Empty;
        if (guildLookup.TryGet(row.GuildId, out GuildManager? manager)) {
            guildName = manager.Guild.Name;
        }
        NotifyApplicationsRemoved([row]);

        return new GuildResponse {
            GuildId = row.GuildId,
            ApplicationId = row.Id,
            GuildName = guildName,
        };
    }

    // Guild 84: a member with the invite right accepts or rejects the request of a character.
    private GuildResponse RespondGuildApplication(long requestorId, GuildRequest.Types.RespondApplication respond) {
        if (!guildLookup.TryGet(respond.GuildId, out GuildManager? manager)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        }

        using GameStorage.Request db = guildLookup.Storage();
        bool isMember = manager.Guild.Members.TryGetValue(requestorId, out GuildMember? requestor);
        GuildApplicationRow? row = GuildRules.FindGuildApplication(db.GetGuildApplications(respond.GuildId), respond.Id);
        long applicantId = row?.ApplicantId ?? 0;
        GuildError error = GuildRules.CheckRespond(isMember, manager.HasPermission(requestorId, GuildPermission.InviteMembers),
            respond.GuildId, applicantId, row, respond.Accepted, row == null ? 0 : db.GetGuildId(applicantId));
        if (error == GuildError.s_guild_err_has_guild && row != null) {
            // The applicant joined a guild in another way: the request is over and leaves the list.
            if (db.DeleteGuildApplication(row.Id)) {
                NotifyApplicationsRemoved([row]);
            }
        }
        if (error != GuildError.none || requestor == null || row == null) {
            return new GuildResponse { Error = (int) (error != GuildError.none ? error : GuildError.s_guild_err_unknown) };
        }
        if (!playerLookup.TryGet(applicantId, out PlayerInfo? applicant)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_user };
        }

        if (respond.Accepted) {
            // The member insert removes all requests of the new member in the same save.
            IList<GuildApplicationRow> open = db.GetGuildApplicationsOf(applicantId);
            error = manager.Join(requestor.Name, applicant);
            if (error != GuildError.none) {
                return new GuildResponse { Error = (int) error };
            }
            NotifyApplicationsRemoved(open, exceptGuildId: respond.GuildId);
        } else if (!db.DeleteGuildApplication(row.Id)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_unknown };
        } else {
            Mail? mail = db.CreateMail(GuildRules.RejectedApplicationMail(applicantId, manager.Guild.Name));
            if (mail == null) {
                logger.Error("Guild {GuildId} reject mail to {CharacterId} could not be written", respond.GuildId, applicantId);
            } else if (MailNotifyRules.Notify(applicant) && channelClients.TryGetClient(applicant.Channel, out ChannelClient? mailClient)) {
                try {
                    mailClient.MailNotification(new MailNotificationRequest {
                        CharacterId = applicantId,
                        MailId = mail.Id,
                    });
                } catch (RpcException) { /* the mail is listed at the next look */ }
            }
        }

        manager.BroadcastToApplicationHandlers(new ChannelGuildRequest {
            ApplicationAnswered = new ChannelGuildRequest.Types.ApplicationAnswered {
                ApplicationId = row.Id,
                RequestorName = requestor.Name,
                ApplicantName = applicant.Name,
                Accepted = respond.Accepted,
            },
        }, exceptCharacterId: applicant.CharacterId);

        if (applicant.Online && channelClients.TryGetClient(applicant.Channel, out ChannelClient? client)) {
            try {
                client.Guild(new ChannelGuildRequest {
                    GuildId = manager.Guild.Id,
                    ReceiverIds = { applicant.CharacterId },
                    ApplicationResult = new ChannelGuildRequest.Types.ApplicationResult {
                        ApplicationId = row.Id,
                        GuildName = manager.Guild.Name,
                        Joined = respond.Accepted,
                    },
                });
            } catch (RpcException) { /* the applicant sees the result at the next login */ }
        }

        return new GuildResponse {
            GuildId = manager.Guild.Id,
            ApplicationId = row.Id,
            ApplicantName = applicant.Name,
        };
    }

    private GuildResponse GuildTrophyNotice(long requestorId, GuildRequest.Types.TrophyNotice notice) {
        if (!guildLookup.TryGet(notice.GuildId, out GuildManager? manager)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        }

        GuildError error = manager.TrophyNotice(requestorId, notice.TrophyId, notice.Grade, notice.LastGrade);
        if (error != GuildError.none) {
            return new GuildResponse { Error = (int) error };
        }

        return new GuildResponse { GuildId = notice.GuildId };
    }

    private GuildResponse GuildRaidClear(long requestorId, GuildRequest.Types.RaidClear clear) {
        if (!guildLookup.TryGet(clear.GuildId, out GuildManager? manager)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        }

        GuildError error = manager.RaidClear(requestorId, clear.DungeonRoomId);
        if (error != GuildError.none) {
            return new GuildResponse { Error = (int) error };
        }

        return new GuildResponse { GuildId = clear.GuildId };
    }

    private GuildResponse GuildAddContribution(long requestorId, GuildRequest.Types.AddContribution add) {
        if (!guildLookup.TryGet(add.GuildId, out GuildManager? manager)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        }

        GuildError error = manager.AddContribution(requestorId, add.Contribution);
        if (error != GuildError.none) {
            return new GuildResponse { Error = (int) error };
        }

        return new GuildResponse { GuildId = add.GuildId };
    }

    // Guild 115: only a member with the mini game right takes the guild's ticket.
    private GuildResponse GuildVsGameRegister(long requestorId, GuildRequest.Types.VsGameRegister register) {
        if (!guildLookup.TryGet(register.GuildId, out GuildManager? manager)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        }
        if (!manager.HasPermission(requestorId, GuildPermission.StartMiniGame)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_no_authority };
        }
        return new GuildResponse { Error = (int) guildVsGameLookup.Register(register.GuildId), GuildId = register.GuildId };
    }

    private GuildResponse GuildVsGameUnregister(long requestorId, GuildRequest.Types.VsGameUnregister unregister) {
        if (!guildLookup.TryGet(unregister.GuildId, out GuildManager? manager)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        }
        if (!manager.HasPermission(requestorId, GuildPermission.StartMiniGame)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_no_authority };
        }
        return new GuildResponse { Error = (int) guildVsGameLookup.Unregister(unregister.GuildId), GuildId = unregister.GuildId };
    }

    // Guild 120: any member of a matched guild enters its match field.
    private GuildResponse GuildVsGameJoin(long requestorId, GuildRequest.Types.VsGameJoin join) {
        if (!guildLookup.TryGet(join.GuildId, out GuildManager? manager)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        }
        if (!manager.Guild.Members.ContainsKey(requestorId)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_member };
        }
        GuildError error = guildVsGameLookup.Join(join.GuildId, out int channel, out int mapId, out int roomId);
        return new GuildResponse { Error = (int) error, GuildId = join.GuildId, Channel = channel, MapId = mapId, RoomId = roomId };
    }

    // Pvp 1: a member with the championship right (GuildPermission.StartPvp, the client's 0x100) registers the
    // guild. Chosen here: at least GuildPvpMinOnline members online (s_guild_ui_championship_condition: "more than 3
    // guild members"; its level is not checked).
    public const int GuildPvpMinOnline = 3;

    private GuildResponse GuildPvpRegister(long requestorId, GuildRequest.Types.PvpRegister register) {
        if (!guildLookup.TryGet(register.GuildId, out GuildManager? manager)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        }
        if (!manager.Guild.Members.TryGetValue(requestorId, out GuildMember? requestor)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_member };
        }
        if (!manager.HasPermission(requestorId, GuildPermission.StartPvp)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_no_authority };
        }
        if (!guildPvpLookup.IsOpen) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_isNotVsGameTime };
        }
        if (manager.OnlineCount() < GuildPvpMinOnline) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_requireOnlineUserCount, ErrorArg = GuildPvpMinOnline };
        }
        manager.RollPvp(DateTime.Now);
        GuildError error = guildPvpLookup.Register(register.GuildId, manager.PvpSnapshot().Score, requestor.Name);
        return new GuildResponse { Error = (int) error, GuildId = register.GuildId };
    }

    private GuildResponse GuildPvpUnregister(long requestorId, GuildRequest.Types.PvpUnregister unregister) {
        if (!guildLookup.TryGet(unregister.GuildId, out GuildManager? manager)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        }
        if (!manager.Guild.Members.TryGetValue(requestorId, out GuildMember? requestor)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_member };
        }
        if (!manager.HasPermission(requestorId, GuildPermission.StartPvp)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_no_authority };
        }
        return new GuildResponse { Error = (int) guildPvpLookup.Unregister(unregister.GuildId, requestor.Name), GuildId = unregister.GuildId };
    }

    // Pvp 3: any member of a matched guild enters its match field.
    private GuildResponse GuildPvpJoin(long requestorId, GuildRequest.Types.PvpJoin join) {
        if (!guildLookup.TryGet(join.GuildId, out GuildManager? manager)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        }
        if (!manager.Guild.Members.ContainsKey(requestorId)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_member };
        }
        GuildError error = guildPvpLookup.Join(join.GuildId, out int channel, out int mapId, out int roomId);
        return new GuildResponse { Error = (int) error, GuildId = join.GuildId, Channel = channel, MapId = mapId, RoomId = roomId };
    }

    // The match field ended the match. With a winner, both guilds' records move (the winner by guildPVPWinPoint,
    // the loser by guildPVPLosePoint) and each keeps the match in its history from its own side; a draw changes
    // nothing. The answer has both guilds' new scores, the winner first (Pvp 'u').
    private GuildResponse GuildPvpEnd(GuildRequest.Types.PvpEnd end) {
        GuildPvpLookup.Match? match = guildPvpLookup.End(end.MatchId);
        if (match == null) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_unknown };
        }
        if (end.WinnerGuildId != match.A && end.WinnerGuildId != match.B) {
            return new GuildResponse();
        }
        if (!guildLookup.TryGet(match.A, out GuildManager? managerA) || !guildLookup.TryGet(match.B, out GuildManager? managerB)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        }

        long time = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        DateTime now = DateTime.Now;
        managerA.RollPvp(now);
        managerB.RollPvp(now);
        var constants = managerA.ServerTableMetadata.ConstantsTable;
        long[] fighters = end.Fighters.ToArray();

        (int Score, int Delta) Apply(GuildManager self, GuildManager other, bool selfIsA) {
            bool won = end.WinnerGuildId == self.Guild.Id;
            var rounds = end.Rounds.Take(5).ToList();
            var history = new GuildPvpMatch {
                GuildA = self.Guild.Id,
                GuildB = other.Guild.Id,
                NameA = self.Guild.Name,
                NameB = other.Guild.Name,
                PlayersA = rounds.Select(round => selfIsA ? round.PlayersA : round.PlayersB).Concat(Enumerable.Repeat(string.Empty, 5)).Take(5).ToArray(),
                PlayersB = rounds.Select(round => selfIsA ? round.PlayersB : round.PlayersA).Concat(Enumerable.Repeat(string.Empty, 5)).Take(5).ToArray(),
                Results = rounds.Select(round => round.Winner == 0 ? (byte) 0 : (round.Winner == 1) == selfIsA ? (byte) 'W' : (byte) 'L')
                    .Concat(Enumerable.Repeat((byte) 0, 5)).Take(5).ToArray(),
                Time = time,
            };
            return self.EndPvp(history, won, won ? constants.guildPVPWinPoint : constants.guildPVPLosePoint, fighters);
        }

        (int scoreA, int deltaA) = Apply(managerA, managerB, true);
        (int scoreB, int deltaB) = Apply(managerB, managerA, false);
        var scoreRowA = new GuildResponse.Types.PvpScore {
            GuildId = match.A,
            Name = managerA.Guild.Name,
            Emblem = managerA.Guild.Emblem,
            Score = scoreA,
            Delta = deltaA,
        };
        var scoreRowB = new GuildResponse.Types.PvpScore {
            GuildId = match.B,
            Name = managerB.Guild.Name,
            Emblem = managerB.Guild.Emblem,
            Score = scoreB,
            Delta = deltaB,
        };
        var response = new GuildResponse();
        if (end.WinnerGuildId == match.A) {
            response.PvpScores.Add(scoreRowA);
            response.PvpScores.Add(scoreRowB);
        } else {
            response.PvpScores.Add(scoreRowB);
            response.PvpScores.Add(scoreRowA);
        }
        return response;
    }

    private GuildResponse UpgradeGuildBuff(long requestorId, GuildRequest.Types.UpgradeBuff upgrade) {
        if (!guildLookup.TryGet(upgrade.GuildId, out GuildManager? manager)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        }

        GuildError error = manager.UpgradeBuff(requestorId, upgrade.BuffId);
        if (error != GuildError.none) {
            return new GuildResponse { Error = (int) error };
        }

        return new GuildResponse { GuildId = upgrade.GuildId };
    }

    private GuildResponse UpgradeGuildNpc(long requestorId, GuildRequest.Types.UpgradeNpc upgrade) {
        if (!guildLookup.TryGet(upgrade.GuildId, out GuildManager? manager)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        }

        GuildError error = manager.UpgradeNpc(requestorId, (GuildNpcType) upgrade.Type);
        if (error != GuildError.none) {
            return new GuildResponse { Error = (int) error };
        }

        return new GuildResponse { GuildId = upgrade.GuildId };
    }

    private GuildResponse UpgradeGuildHouse(long requestorId, GuildRequest.Types.UpgradeHouse upgrade) {
        if (!guildLookup.TryGet(upgrade.GuildId, out GuildManager? manager)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        }

        GuildError error = manager.UpgradeHouse(requestorId, upgrade.Theme);
        if (error != GuildError.none) {
            return new GuildResponse { Error = (int) error };
        }

        return new GuildResponse { GuildId = upgrade.GuildId };
    }

    private GuildResponse ChangeGuildHouseTheme(long requestorId, GuildRequest.Types.ChangeHouseTheme change) {
        if (!guildLookup.TryGet(change.GuildId, out GuildManager? manager)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        }

        GuildError error = manager.ChangeHouseTheme(requestorId, change.Theme);
        if (error != GuildError.none) {
            return new GuildResponse { Error = (int) error };
        }

        return new GuildResponse { GuildId = change.GuildId };
    }

    private GuildResponse UseGuildBuff(long requestorId, GuildRequest.Types.UseBuff use) {
        if (!guildLookup.TryGet(use.GuildId, out GuildManager? manager)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        }

        GuildError error = manager.UseBuff(requestorId, use.BuffId);
        if (error != GuildError.none) {
            return new GuildResponse { Error = (int) error };
        }

        return new GuildResponse { GuildId = use.GuildId };
    }

    private GuildResponse OpenGuildEvent(long requestorId, GuildRequest.Types.OpenEvent open) {
        if (!guildLookup.TryGet(open.GuildId, out GuildManager? manager)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        }

        GuildError error = manager.OpenEvent(requestorId, open.EventId);
        if (error != GuildError.none) {
            return new GuildResponse { Error = (int) error };
        }

        return new GuildResponse { GuildId = open.GuildId };
    }

    private GuildResponse RecallGuild(long requestorId, GuildRequest.Types.Recall recall) {
        if (!guildLookup.TryGet(recall.GuildId, out GuildManager? manager)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        }

        GuildError error = manager.Recall(requestorId, recall.MapId, recall.EndTime);
        if (error != GuildError.none) {
            return new GuildResponse { Error = (int) error };
        }

        return new GuildResponse { GuildId = recall.GuildId };
    }

    private GuildResponse AddGuildExpFund(long requestorId, GuildRequest.Types.AddExpFund add) {
        if (!guildLookup.TryGet(add.GuildId, out GuildManager? manager)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        }

        GuildError error = manager.AddExpFund(requestorId, add.Exp, add.Fund, out int addedExp, out int addedFund);
        if (error != GuildError.none) {
            return new GuildResponse { Error = (int) error };
        }

        return new GuildResponse { GuildId = add.GuildId, AddedExp = addedExp, AddedFund = addedFund };
    }

    private GuildResponse SpendGuildFund(long requestorId, GuildRequest.Types.SpendFund spend) {
        if (!guildLookup.TryGet(spend.GuildId, out GuildManager? manager)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        }

        GuildError error = manager.SpendFund(requestorId, spend.Amount,
            new RewardItem(spend.ItemId, (short) spend.Rarity, spend.ItemAmount));
        if (error != GuildError.none) {
            return new GuildResponse { Error = (int) error };
        }

        return new GuildResponse { GuildId = spend.GuildId };
    }

    private GuildResponse GiftGuildStorage(long requestorId, GuildRequest.Types.StorageGift gift) {
        if (!guildLookup.TryGet(gift.GuildId, out GuildManager? manager)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        }

        GuildError error = manager.StorageGift(requestorId, gift.ItemId, (short) gift.Rarity, gift.Amount, gift.ReceiverName,
            gift.Rolls.ToByteArray(), out GuildMember? receiver);
        if (error != GuildError.none) {
            return new GuildResponse { Error = (int) error };
        }

        return new GuildResponse { GuildId = gift.GuildId, ReceiverName = receiver!.Name };
    }

    private GuildResponse TakeGuildMailTurn(long requestorId, GuildRequest.Types.TakeMailTurn take) {
        if (!guildLookup.TryGet(take.GuildId, out GuildManager? manager)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        }

        GuildError error = manager.TakeMailTurn(requestorId);
        if (error != GuildError.none) {
            return new GuildResponse { Error = (int) error };
        }

        return new GuildResponse { GuildId = take.GuildId };
    }

    // Guild 113: a member gives meso to the guild fund.
    private GuildResponse DonateGuild(long requestorId, GuildRequest.Types.Donate donate) {
        if (!guildLookup.TryGet(donate.GuildId, out GuildManager? manager)) {
            return new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        }

        GuildError error = manager.Donate(requestorId, donate.Count, donate.Fund, out GuildMember? member);
        if (error != GuildError.none || member == null) {
            return new GuildResponse { Error = (int) (error != GuildError.none ? error : GuildError.s_guild_err_unknown) };
        }

        return new GuildResponse {
            GuildId = donate.GuildId,
            DonationCount = member.DailyDonationCount,
            DonationTime = member.DonationTime,
        };
    }

    private static GuildInfo ToGuildInfo(GuildManager manager) {
        Guild guild = manager.Guild;
        return new GuildInfo {
            Id = guild.Id,
            Name = guild.Name,
            Emblem = guild.Emblem,
            Notice = guild.Notice,
            CreationTime = guild.CreationTime,
            Focus = (int) guild.Focus,
            Listed = guild.Listed,
            Experience = guild.Experience,
            Funds = guild.Funds,
            HouseRank = guild.HouseRank,
            HouseTheme = guild.HouseTheme,
            Members = {
                guild.Members.Values.Select(member => new GuildInfo.Types.Member {
                    CharacterId = member.CharacterId,
                    Message = member.Message,
                    Rank = member.Rank,
                    JoinTime = member.JoinTime,
                    CheckinTime = member.CheckinTime,
                    DonationTime = member.DonationTime,
                    WeeklyContribution = member.WeeklyContribution,
                    TotalContribution = member.TotalContribution,
                    DailyDonationCount = member.DailyDonationCount,
                    Raids = { member.Raids },
                    PvpSeasonCount = member.PvpSeasonCount,
                    PvpTotalCount = member.PvpTotalCount,
                }),
            },
            Ranks = {
                guild.Ranks.Select(rank => new GuildInfo.Types.Rank {
                    Name = rank.Name,
                    Permission = (int) rank.Permission,
                }),
            },
            Buffs = {
                guild.Buffs.Select(buff => new GuildInfo.Types.Buff {
                    Id = buff.Id,
                    Level = buff.Level,
                    ExpiryTime = buff.ExpiryTime,
                }),
            },
            Events = {
                manager.OpenedEvents().Select(opened => new GuildInfo.Types.Event {
                    Id = opened.Id,
                    Time = opened.Value,
                }),
            },
            Posters = {
                guild.Posters.Select(poster => new GuildInfo.Types.Poster {
                    Id = poster.Id,
                    Picture = poster.Picture,
                    OwnerId = poster.OwnerId,
                    OwnerName = poster.OwnerName,
                    ResourceId = poster.ResourceId,
                }),
            },
            Npcs = {
                guild.Npcs.Select(npc => new GuildInfo.Types.Npc {
                    Type = (int) npc.Type,
                    Level = npc.Level,
                }),
            },
            Pvp = JsonSerializer.Serialize(manager.PvpSnapshot()),
            PvpState = (int) guild.PvpState,
            Storage = {
                guild.StorageSnapshot().Select(item => new GuildInfo.Types.StorageItem {
                    ItemId = item.ItemId,
                    Rarity = item.Rarity,
                    Amount = item.Amount,
                }),
            },
        };
    }
}
