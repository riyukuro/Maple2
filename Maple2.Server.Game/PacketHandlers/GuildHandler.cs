using Grpc.Core;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Server.World.Service;
using Maple2.Tools.Extensions;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Game.PacketHandlers;

public class GuildHandler : PacketHandler<GameSession> {
    public override RecvOp OpCode => RecvOp.Guild;

    private enum Command : byte {
        Create = 1,
        Disband = 2,
        Invite = 3,
        RespondInvite = 5,
        Leave = 7,
        Expel = 8,
        UpdateMemberRank = 10,
        UpdateMemberMessage = 13,
        CheckIn = 15,
        // From here on the numbers are the client's CGuildManager senders; they are 2 (up to UpgradeBuff) and
        // 3 (from StartArcade) above the older GMS numbering.
        UpdateLeader = 63,
        UpdateNotice = 64,
        UpdateEmblem = 65,
        IncreaseCapacity = 66, // no client sender found; numbered by the shift of its neighbours
        UpdateRank = 67,
        UpdateFocus = 68,
        SendMail = 71,
        PvpHistory = 74,
        PvpSeason = 75,
        SendApplication = 82,
        CancelApplication = 83,
        RespondApplication = 84,
        ListApplications = 85,
        ListAppliedGuilds = 86,
        SearchGuilds = 87,
        SearchGuildName = 88,
        UseBuff = 90,
        UsePersonalBuff = 91,
        UpgradeBuff = 92,
        StartArcade = 99,
        EnterArcade = 100,
        UpgradeHouseRank = 101,
        UpgradeHouseTheme = 102,
        EnterHouse = 103,
        SendGift = 109,
        UpdateGiftLog = 112,
        Donate = 113,
        UpgradeNpc = 114,
        CreateGuildEvent = 115,
        StartGuildEvent = 116,
        JoinGuildEvent = 120,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required WorldClient World { private get; init; }
    public required TableMetadataStorage TableMetadata { private get; init; }
    public required BanWordStorage BanWordStorage { private get; init; }
    public required ServerTableMetadataStorage ServerTableMetadata { private get; init; }
    private ConstantsTable Constants => ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    // Guild handling is on. A field, not a constant, so the check below stays compiled either way.
    private static readonly bool HandlingOn = true;

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();

        if (!HandlingOn) {
            Logger.Debug("Guild command {Command} from {CharacterId} ignored: guild handling is off", command, session.CharacterId);
            return;
        }
        switch (command) {
            case Command.Create:
                HandleCreate(session, packet);
                return;
            case Command.Disband:
                HandleDisband(session);
                return;
            case Command.Invite:
                HandleInvite(session, packet);
                return;
            case Command.RespondInvite:
                HandleRespondInvite(session, packet);
                return;
            case Command.Leave:
                HandleLeave(session);
                return;
            case Command.Expel:
                HandleExpel(session, packet);
                return;
            case Command.UpdateMemberRank:
                HandleUpdateMemberRank(session, packet);
                return;
            case Command.UpdateMemberMessage:
                HandleUpdateMemberMessage(session, packet);
                return;
            case Command.CheckIn:
                HandleCheckIn(session);
                return;
            case Command.UpdateLeader:
                HandleUpdateLeader(session, packet);
                return;
            case Command.UpdateNotice:
                HandleUpdateNotice(session, packet);
                return;
            case Command.IncreaseCapacity:
                HandleIncreaseCapacity(session);
                return;
            case Command.UpdateRank:
                HandleUpdateRank(session, packet);
                return;
            case Command.UpdateFocus:
                HandleUpdateFocus(session, packet);
                return;
            case Command.SendMail:
                HandleSendMail(session, packet);
                return;
            case Command.PvpHistory:
                HandlePvpHistory(session, packet);
                return;
            case Command.PvpSeason:
                HandlePvpSeason(session);
                return;
            case Command.SendApplication:
                HandleSendApplication(session, packet);
                return;
            case Command.CancelApplication:
                HandleCancelApplication(session, packet);
                return;
            case Command.RespondApplication:
                HandleRespondApplication(session, packet);
                return;
            case Command.ListApplications:
                HandleListApplications(session);
                return;
            case Command.ListAppliedGuilds:
                HandleListAppliedGuilds(session);
                return;
            case Command.SearchGuilds:
                HandleSearchGuilds(session, packet);
                return;
            case Command.SearchGuildName:
                HandleSearchGuildName(session, packet);
                return;
            case Command.UseBuff:
                HandleUseBuff(session, packet);
                return;
            case Command.UsePersonalBuff:
                HandleUsePersonalBuff(session, packet);
                return;
            case Command.UpgradeBuff:
                HandleUpgradeBuff(session, packet);
                return;
            case Command.StartArcade:
                HandleStartArcade(session, packet);
                return;
            case Command.EnterArcade:
                HandleEnterArcade(session, packet);
                return;
            case Command.UpgradeHouseRank:
                HandleUpgradeHouseRank(session, packet);
                return;
            case Command.UpgradeHouseTheme:
                HandleUpgradeHouseTheme(session, packet);
                return;
            case Command.EnterHouse:
                HandleEnterHouse(session);
                return;
            case Command.SendGift:
                HandleSendGift(session, packet);
                return;
            case Command.UpdateGiftLog:
                HandleUpdateGiftLog(session);
                return;
            case Command.Donate:
                HandleDonate(session, packet);
                return;
            case Command.UpgradeNpc:
                HandleUpgradeNpc(session, packet);
                return;
            case Command.CreateGuildEvent:
                HandleCreateGuildEvent(session);
                return;
            case Command.StartGuildEvent:
                HandleStartGuildEvent(session);
                return;
            case Command.JoinGuildEvent:
                HandleJoinGuildEvent(session);
                return;
        }
    }

    private void HandleCreate(GameSession session, IByteReader packet) {
        string guildName = packet.ReadUnicodeString();
        if (session.Guild.Guild != null) {
            return; // Already in a guild.
        }

        if (string.IsNullOrWhiteSpace(guildName)) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_name_value));
            return;
        }
        if (guildName.Length < Constants.GuildNameLengthMin || guildName.Length > Constants.GuildNameLengthMax) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_name_value));
            return;
        }

        if (BanWordStorage.ContainsBannedWord(guildName)) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_name_value));
            return;
        }

        if (!NameValidator.ValidName(guildName)) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_name_value));
            return;
        }

        if (session.Player.Value.Character.Level < Constants.GuildCreateMinLevel) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_not_enough_level));
            return;
        }
        if (session.Currency.CanAddMeso(-Constants.GuildCreatePrice) != -Constants.GuildCreatePrice) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_no_money));
            return;
        }

        try {
            var request = new GuildRequest {
                RequestorId = session.CharacterId,
                Create = new GuildRequest.Types.Create {
                    GuildName = guildName,
                },
            };
            GuildResponse response = World.Guild(request);
            var error = (GuildError) response.Error;
            if (error != GuildError.none) {
                session.Send(GuildPacket.Error(error));
                return;
            }
            if (response.InfoCase != GuildResponse.InfoOneofCase.Guild) {
                session.Send(GuildPacket.Error(GuildError.s_guild_err_null_guild));
                return;
            }

            session.Guild.SetGuild(response.Guild);
            session.Currency.Meso -= Constants.GuildCreatePrice;

            session.Guild.Load();
            session.Send(GuildPacket.Created(guildName));
        } catch (RpcException ex) {
            Logger.Error(ex, "Failed to create guild: {Name}", guildName);
            session.Send(GuildPacket.Error(GuildError.s_guild_err_unknown));
        }
    }

    private void HandleDisband(GameSession session) {
        if (session.Guild.Guild == null) {
            return; // Not in a guild.
        }

        if (session.Guild.Guild.LeaderCharacterId != session.CharacterId) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_no_master));
            return;
        }

        try {
            var request = new GuildRequest {
                RequestorId = session.CharacterId,
                Disband = new GuildRequest.Types.Disband {
                    GuildId = session.Guild.Id,
                },
            };
            GuildResponse response = World.Guild(request);
            var error = (GuildError) response.Error;
            if (error != GuildError.none) {
                session.Send(GuildPacket.Error(error));
                return;
            }

            session.Send(GuildPacket.Disbanded());
            session.Guild.RemoveGuild();
        } catch (RpcException ex) {
            Logger.Error(ex, "Failed to disband guild");
            session.Send(GuildPacket.Error(GuildError.s_guild_err_unknown));
        }
    }

    private void HandleInvite(GameSession session, IByteReader packet) {
        if (session.Guild.Guild == null) {
            return; // Not in a guild.
        }

        string playerName = packet.ReadUnicodeString();
        using GameStorage.Request db = session.GameStorage.Context();
        long characterId = db.GetCharacterId(playerName);
        if (characterId == 0) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_wait_inviting));
            return;
        }

        try {
            var request = new GuildRequest {
                RequestorId = session.CharacterId,
                Invite = new GuildRequest.Types.Invite {
                    GuildId = session.Guild.Id,
                    ReceiverId = characterId,
                },
            };
            GuildResponse response = World.Guild(request);
            var error = (GuildError) response.Error;
            if (error != GuildError.none) {
                session.Send(GuildPacket.Error(error));
                return;
            }

            session.Send(GuildPacket.Invited(playerName));
        } catch (RpcException ex) {
            Logger.Error(ex, "Failed to invite {Name} to guild", playerName);
            session.Send(GuildPacket.Error(GuildError.s_guild_err_unknown));
        }
    }

    private void HandleRespondInvite(GameSession session, IByteReader packet) {
        var invite = packet.ReadClass<GuildInvite>();
        bool accepted = packet.ReadBool();

        // An invite stays open for a while; one accepted after joining another guild must not join a second one.
        if (accepted && session.Guild.Guild != null) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_already_exist));
            return;
        }

        try {
            var request = new GuildRequest {
                RequestorId = session.CharacterId,
                RespondInvite = new GuildRequest.Types.RespondInvite {
                    GuildId = invite.GuildId,
                    Accepted = accepted,
                },
            };
            GuildResponse response = World.Guild(request);
            var error = (GuildError) response.Error;
            if (error != GuildError.none) {
                session.Send(GuildPacket.Error(error));
                return;
            }

            session.Send(GuildPacket.InviteReply(invite, accepted));
            switch (response.InfoCase) {
                case GuildResponse.InfoOneofCase.GuildId: // Reject
                    break;
                case GuildResponse.InfoOneofCase.Guild: // Accept
                    session.Guild.SetGuild(response.Guild);
                    session.Guild.Load();
                    break;
            }
        } catch (RpcException ex) {
            Logger.Error(ex, "Failed to respond to guild invite");
            session.Send(GuildPacket.Error(GuildError.s_guild_err_unknown));
        }
    }

    private void HandleLeave(GameSession session) {
        if (session.Guild.Guild == null) {
            return; // Not in a guild.
        }

        try {
            var request = new GuildRequest {
                RequestorId = session.CharacterId,
                Leave = new GuildRequest.Types.Leave {
                    GuildId = session.Guild.Id,
                },
            };
            GuildResponse response = World.Guild(request);
            var error = (GuildError) response.Error;
            if (error != GuildError.none) {
                session.Send(GuildPacket.Error(error));
                return;
            }

            session.Send(GuildPacket.Leave());
            session.Guild.RemoveGuild();
        } catch (RpcException ex) {
            Logger.Error(ex, "Failed to leave guild");
            session.Send(GuildPacket.Error(GuildError.s_guild_err_unknown));
        }
    }

    private void HandleExpel(GameSession session, IByteReader packet) {
        if (session.Guild.Guild == null) {
            return; // Not in a guild.
        }

        string playerName = packet.ReadUnicodeString();
        GuildMember? member = session.Guild.GetMember(playerName);
        if (member == null) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_null_member));
            return;
        }

        try {
            var request = new GuildRequest {
                RequestorId = session.CharacterId,
                Expel = new GuildRequest.Types.Expel {
                    GuildId = session.Guild.Id,
                    ReceiverId = member.CharacterId,
                },
            };
            GuildResponse response = World.Guild(request);
            var error = (GuildError) response.Error;
            if (error != GuildError.none) {
                session.Send(GuildPacket.Error(error));
                return;
            }

            session.Send(GuildPacket.Expelled(playerName));
        } catch (RpcException) { /* ignored */ }
    }

    private void HandleUpdateMemberRank(GameSession session, IByteReader packet) {
        if (session.Guild.Guild == null) {
            return; // Not in a guild.
        }

        string playerName = packet.ReadUnicodeString();
        byte rankId = packet.ReadByte();

        if (!session.Guild.HasPermission(session.CharacterId, GuildPermission.EditRank)) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_no_authority));
            return;
        }

        // CGuildManager::SendGuildMode10 sends a rank from 1 to the last index; rank 0 is the master's.
        if (!GuildRules.IsAssignableRank(rankId, session.Guild.Guild.Ranks.Count)) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_invalid_grade_index));
            return;
        }

        GuildMember? member = session.Guild.GetMember(playerName);
        if (member == null) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_null_member));
            return;
        }
        if (!GuildRules.CanChangeRankOf(member.Rank)) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_set_grade_failed));
            return;
        }

        try {
            var request = new GuildRequest {
                RequestorId = session.CharacterId,
                UpdateMember = new GuildRequest.Types.UpdateMember {
                    GuildId = session.Guild.Id,
                    CharacterId = member.CharacterId,
                    Rank = rankId,
                },
            };

            GuildResponse response = World.Guild(request);
            var error = (GuildError) response.Error;
            if (error != GuildError.none) {
                session.Send(GuildPacket.Error(error));
                return;
            }

            session.Send(GuildPacket.UpdateMemberRank(playerName, rankId));
        } catch (RpcException) { /* ignored */ }
    }

    private void HandleUpdateMemberMessage(GameSession session, IByteReader packet) {
        if (session.Guild.Guild == null) {
            return; // Not in a guild.
        }

        string message = packet.ReadUnicodeString();
        try {
            var request = new GuildRequest {
                RequestorId = session.CharacterId,
                UpdateMember = new GuildRequest.Types.UpdateMember {
                    GuildId = session.Guild.Id,
                    CharacterId = session.CharacterId,
                    Message = message,
                },
            };

            GuildResponse response = World.Guild(request);
            var error = (GuildError) response.Error;
            if (error != GuildError.none) {
                session.Send(GuildPacket.Error(error));
                return;
            }

            session.Send(GuildPacket.UpdateMemberMessage(message));
        } catch (RpcException) { /* ignored */ }
    }

    private void HandleCheckIn(GameSession session) {
        if (session.Guild.Guild == null) {
            return; // Not in a guild.
        }

        GuildMember? self = session.Guild.GetMember(session.CharacterId);
        if (self == null) {
            return;
        }

        // Check that player has not already checked in today.
        DateTimeOffset today = DateTimeOffset.UtcNow.Date;
        if (self.CheckinTime >= today.ToUnixTimeSeconds()) {
            return;
        }

        try {
            var request = new GuildRequest {
                RequestorId = session.CharacterId,
                CheckIn = new GuildRequest.Types.CheckIn {
                    GuildId = session.Guild.Id,
                },
            };

            GuildResponse response = World.Guild(request);
            var error = (GuildError) response.Error;
            if (error != GuildError.none) {
                session.Send(GuildPacket.Error(error));
                return;
            }

            session.Send(GuildPacket.CheckedIn());
            session.Exp.AddExp(ExpType.guildUserExp, session.Guild.Properties.CheckInPlayerExpRate);
            Item? guildCoin = session.Field?.ItemDrop.CreateItem(Constant.GuildCoinId, Constant.GuildCoinRarity, session.Guild.Properties.CheckInCoin);
            if (guildCoin != null) {
                session.Item.Inventory.Add(guildCoin, true);
            }
        } catch (RpcException) { /* ignored */ }
    }

    private void HandleUpdateLeader(GameSession session, IByteReader packet) {
        string leaderName = packet.ReadUnicodeString();
        // Only leader is allowed to change leader
        if (session.CharacterId != session.Guild.LeaderId) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_no_master));
            return;
        }

        GuildMember? newLeader = session.Guild.GetMember(leaderName);
        if (newLeader == null) {
            return;
        }

        try {
            var request = new GuildRequest {
                RequestorId = session.CharacterId,
                UpdateLeader = new GuildRequest.Types.UpdateLeader {
                    GuildId = session.Guild.Id,
                    LeaderId = newLeader.CharacterId,
                },
            };

            GuildResponse response = World.Guild(request);
            var error = (GuildError) response.Error;
            if (error != GuildError.none) {
                session.Send(GuildPacket.Error(error));
                return;
            }

            session.Send(GuildPacket.UpdateLeader(leaderName));
        } catch (RpcException) { /* ignored */ }
    }

    private void HandleUpdateNotice(GameSession session, IByteReader packet) {
        // CGuildManager::FUN_1413b7c70: the kind of text, then the text.
        byte kind = packet.ReadByte();
        string notice = packet.ReadUnicodeString();
        if (!GuildRules.IsNotice(kind, notice)) {
            return;
        }

        if (!session.Guild.HasPermission(session.CharacterId, GuildPermission.EditNotice)) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_no_authority));
            return;
        }

        try {
            var request = new GuildRequest {
                RequestorId = session.CharacterId,
                UpdateNotice = new GuildRequest.Types.UpdateNotice {
                    GuildId = session.Guild.Id,
                    Message = notice,
                },
            };

            GuildResponse response = World.Guild(request);
            var error = (GuildError) response.Error;
            if (error != GuildError.none) {
                session.Send(GuildPacket.Error(error));
                return;
            }

            session.Send(GuildPacket.UpdateNotice(notice));
        } catch (RpcException) { /* ignored */ }
    }

    private void HandleIncreaseCapacity(GameSession session) {

    }

    // CGuildManager::SendGuildMode67: the rank index, then the whole rank with the same index. The client sends
    // it only from the master, for a rank it lists, with a name of at most 15 characters (the m_gradeName input).
    // Its checks: index 0..5 and CGuildGrade::IsValid (rights 0..4095). It also lets an index one above the
    // highest rank through, but its only caller (UIGuildDialog OnChangeGrade) skips every index the guild has no
    // rank for, and the members' reader of the update (OnPacketGradeUpdated) drops an unknown index, so the
    // server takes only ranks that exist.
    private void HandleUpdateRank(GameSession session, IByteReader packet) {
        byte rankId = packet.ReadByte();
        var rank = packet.ReadClass<GuildRank>();

        if (session.Guild.Guild == null) {
            return;
        }
        if (rank.Id != rankId || rankId >= MaxRanks || (int) rank.Permission is < 0 or > MaxRankPermission
            || rank.Name.Length is 0 or > MaxRankNameLength) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_invalid_grade_data));
            return;
        }

        try {
            GuildResponse response = World.Guild(new GuildRequest {
                RequestorId = session.CharacterId,
                UpdateRank = new GuildRequest.Types.UpdateRank {
                    GuildId = session.Guild.Id,
                    RankId = rankId,
                    Name = rank.Name,
                    Permission = (int) rank.Permission,
                },
            });
            var error = (GuildError) response.Error;
            if (error != GuildError.none) {
                session.Send(GuildPacket.Error(error));
                return;
            }

            // CGuildManager::OnPacketGradeChanged: the master's "rank changed" box.
            session.Send(GuildPacket.UpdateRank(rank));
        } catch (RpcException ex) {
            Logger.Error(ex, "Failed to update guild rank {RankId} of guild {GuildId}", rankId, session.Guild.Id);
            session.Send(GuildPacket.Error(GuildError.s_guild_err_unknown));
        }
    }

    private const int MaxRanks = 6;
    private const int MaxRankPermission = 4095;
    private const int MaxRankNameLength = 15;

    // CGuildManager::SendGuildMode68: a byte, listed in the guild search or not, then the focus bits. The client
    // sends it from a member with the invite right (2) when one of the two changed.
    private void HandleUpdateFocus(GameSession session, IByteReader packet) {
        byte listed = packet.ReadByte();
        int focus = packet.ReadInt();
        if (session.Guild.Guild == null || !GuildRules.IsFocus(listed, focus)) {
            return;
        }

        if (!session.Guild.HasPermission(session.CharacterId, GuildPermission.InviteMembers)) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_no_authority));
            return;
        }

        try {
            var request = new GuildRequest {
                RequestorId = session.CharacterId,
                UpdateFocus = new GuildRequest.Types.UpdateFocus {
                    GuildId = session.Guild.Id,
                    Listed = listed != 0,
                    Focus = focus,
                },
            };

            GuildResponse response = World.Guild(request);
            var error = (GuildError) response.Error;
            if (error != GuildError.none) {
                session.Send(GuildPacket.Error(error));
                return;
            }

            session.Send(GuildPacket.UpdateFocus(listed != 0, (GuildFocus) focus));
        } catch (RpcException) { /* ignored */ }
    }

    // CGuildManager::SendGuildMail: a title of 1 to 100 characters and a text of 1 to 2000, sent by a member
    // with the mail right (128). Every other member gets the mail; the reply (71, no body) prints "mail sent".
    private void HandleSendMail(GameSession session, IByteReader packet) {
        string title = packet.ReadUnicodeString();
        string content = packet.ReadUnicodeString();
        if (session.Guild.Guild == null) {
            return; // Not in a guild.
        }
        if (!session.Guild.HasPermission(session.CharacterId, GuildPermission.SendMail)) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_no_authority));
            return;
        }
        if (!MailRules.CanSend(title, content)) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_unknown));
            return;
        }
        // Server rule: one guild mail per sender in GuildRules.MailCooldownSeconds. World keeps the time, so
        // the limit holds on every channel and over a new login.
        try {
            GuildResponse response = World.Guild(new GuildRequest {
                RequestorId = session.CharacterId,
                TakeMailTurn = new GuildRequest.Types.TakeMailTurn {
                    GuildId = session.Guild.Id,
                },
            });
            var error = (GuildError) response.Error;
            if (error != GuildError.none) {
                session.Send(GuildPacket.Error(error));
                return;
            }
        } catch (RpcException ex) {
            Logger.Error(ex, "Failed to take the guild mail turn");
            session.Send(GuildPacket.Error(GuildError.s_guild_err_unknown));
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        foreach (long receiverId in GuildRules.MailReceivers(session.Guild.Guild.Members.Keys, session.CharacterId)) {
            Mail? mail = db.CreateMail(new Mail {
                SenderId = session.CharacterId,
                ReceiverId = receiverId,
                Type = MailType.Player,
                SenderName = session.PlayerName,
                Title = title,
                Content = content,
            });
            if (mail == null) {
                continue;
            }

            try {
                World.MailNotification(new MailNotificationRequest {
                    CharacterId = receiverId,
                    MailId = mail.Id,
                });
            } catch (RpcException) { /* the member sees the mail at the next login */ }
        }

        session.Send(GuildPacket.SendMail());
    }

    // CGuildManager::RequestPvpHistoryAndSeason (guildmanager.cpp:1144): sent when the guild PvP tab opens,
    // with the last history id the client holds. The server runs no guild PvP: the answer has no rows.
    private void HandlePvpHistory(GameSession session, IByteReader packet) {
        packet.ReadLong(); // the id of the newest row the client holds; the rows after it are asked for
        if (session.Guild.Guild == null) {
            return; // Not in a guild.
        }

        session.Send(GuildPacket.PvpHistory());
    }

    // CGuildManager::RequestPvpHistoryAndSeason (guildmanager.cpp:1152): no body; sent once per login, after
    // the history request. Until the answer comes the client does not draw the season part of the tab.
    private void HandlePvpSeason(GameSession session) {
        if (session.Guild.Guild == null) {
            return; // Not in a guild.
        }

        session.Send(GuildPacket.PvpSeason());
    }

    // CGuildManager::SendGuildMode82: the guild to ask, sent only by a player without a guild. The reply makes
    // the client print the "applied" line and ask for its own request list (86).
    private void HandleSendApplication(GameSession session, IByteReader packet) {
        long guildId = packet.ReadLong();
        if (session.Guild.Guild != null) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_has_guild));
            return;
        }
        if (guildId <= 0) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_null_guild));
            return;
        }

        try {
            GuildResponse response = World.Guild(new GuildRequest {
                RequestorId = session.CharacterId,
                Apply = new GuildRequest.Types.Apply { GuildId = guildId },
            });
            var error = (GuildError) response.Error;
            if (error != GuildError.none) {
                session.Send(GuildPacket.Error(error));
                return;
            }

            session.Send(GuildPacket.SendApplication(response.GuildId, response.GuildName));
        } catch (RpcException ex) {
            Logger.Error(ex, "Failed to send guild application");
            session.Send(GuildPacket.Error(GuildError.s_guild_err_unknown));
        }
    }

    // CGuildManager::SendGuildMode83: one id of the request to take back (see GuildRules.FindOwnApplication),
    // sent only by a player without a guild. The reply prints the "cancelled" line and the client asks for 86.
    private void HandleCancelApplication(GameSession session, IByteReader packet) {
        long id = packet.ReadLong();
        if (session.Guild.Guild != null || id <= 0) {
            session.Send(GuildPacket.Error(GuildError.s_guild_search_null_join_guild_request));
            return;
        }

        try {
            GuildResponse response = World.Guild(new GuildRequest {
                RequestorId = session.CharacterId,
                CancelApplication = new GuildRequest.Types.CancelApplication { Id = id },
            });
            var error = (GuildError) response.Error;
            if (error != GuildError.none) {
                session.Send(GuildPacket.Error(error));
                return;
            }

            session.Send(GuildPacket.CancelApplication(response.GuildId, response.GuildName));
        } catch (RpcException ex) {
            Logger.Error(ex, "Failed to cancel guild application");
            session.Send(GuildPacket.Error(GuildError.s_guild_err_unknown));
        }
    }

    // CGuildManager::SendGuildMode84: the applicant's character id (the "charSn" of the accept page row) and
    // accept or reject. The sender needs the invite right, as for the list (85).
    private void HandleRespondApplication(GameSession session, IByteReader packet) {
        long characterId = packet.ReadLong();
        bool accepted = packet.ReadBool();
        if (session.Guild.Guild == null) {
            return; // Not in a guild.
        }
        if (!session.Guild.HasPermission(session.CharacterId, GuildPermission.InviteMembers)) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_no_authority));
            return;
        }

        try {
            GuildResponse response = World.Guild(new GuildRequest {
                RequestorId = session.CharacterId,
                RespondApplication = new GuildRequest.Types.RespondApplication {
                    GuildId = session.Guild.Id,
                    ApplicantId = characterId,
                    Accepted = accepted,
                },
            });
            var error = (GuildError) response.Error;
            if (error != GuildError.none) {
                session.Send(GuildPacket.Error(error));
                return;
            }

            session.Send(GuildPacket.RespondApplication(characterId, response.ApplicantName, accepted));
        } catch (RpcException ex) {
            Logger.Error(ex, "Failed to respond to guild application");
            session.Send(GuildPacket.Error(GuildError.s_guild_err_unknown));
        }
    }

    // CGuildManager::SendGuildMode85: no body. A member with the invite right asks for the requests sent to
    // the guild; the client pages them by 7.
    private void HandleListApplications(GameSession session) {
        Guild? guild = session.Guild.Guild;
        if (guild == null || !session.Guild.HasPermission(session.CharacterId, GuildPermission.InviteMembers)) {
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        var applications = new List<GuildApplication>();
        foreach (GuildApplicationRow row in db.GetGuildApplications(guild.Id)) {
            if (!session.PlayerInfo.GetOrFetch(row.ApplicantId, out PlayerInfo? info)) {
                continue;
            }
            applications.Add(new GuildApplication {
                Id = row.Id,
                Guild = guild,
                Applicant = info,
                CreationTime = row.CreationTime,
            });
        }

        session.Send(GuildPacket.ListApplications(applications));
    }

    // CGuildManager::SendGuildMode86: no body. A player without a guild asks for the requests they sent.
    private void HandleListAppliedGuilds(GameSession session) {
        if (session.Guild.Guild != null || !session.PlayerInfo.GetOrFetch(session.CharacterId, out PlayerInfo? self)) {
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        var applications = new List<(GuildApplication, int)>();
        foreach (GuildApplicationRow row in db.GetGuildApplicationsOf(session.CharacterId)) {
            Guild? guild = db.GetGuild(row.GuildId);
            if (guild == null) {
                continue;
            }
            applications.Add((new GuildApplication {
                Id = row.Id,
                Guild = guild,
                Applicant = self,
                CreationTime = row.CreationTime,
            }, db.GetGuildMemberCount(row.GuildId)));
        }

        session.Send(GuildPacket.ListAppliedGuilds(applications));
    }

    // CGuildManager::SendGuildMode87 (CGuildJoinRequestSchCond): the focus bits, all 31 when none is ticked,
    // then an int the client always sets to 1. Sent only by a player without a guild.
    private void HandleSearchGuilds(GameSession session, IByteReader packet) {
        int focus = packet.ReadInt();
        packet.ReadInt(); // 1
        if (session.Guild.Guild != null || !GuildRules.IsSearchFocus(focus)) {
            return;
        }

        SendSearchResult(session, focus == GuildRules.SearchAnyFocus ? 0 : focus, string.Empty);
    }

    // CGuildManager::SendGuildMode88: a name of 2 characters or more, from a player without a guild.
    private void HandleSearchGuildName(GameSession session, IByteReader packet) {
        string guildName = packet.ReadUnicodeString();
        if (session.Guild.Guild != null || !GuildRules.IsSearchName(guildName)) {
            return;
        }

        SendSearchResult(session, 0, guildName);
    }

    private void SendSearchResult(GameSession session, int focusMask, string name) {
        using GameStorage.Request db = session.GameStorage.Context();
        IList<GuildSearchEntry> found = db.SearchGuilds(focusMask, name, GuildRules.MaxSearchResults);
        session.Send(GuildPacket.ListGuilds(found.Select(entry => (entry, CapacityAt(entry.Experience))).ToList()));
    }

    // The capacity of the guild level the experience gives, as GuildManager.UpdateProperties picks it.
    private int CapacityAt(int experience) {
        return TableMetadata.GuildTable.Properties
            .OrderBy(entry => entry.Value.Experience)
            .MinBy(entry => entry.Value.Experience > experience).Value.Capacity;
    }

    // CGuildManager::FUN_1413b8fe0: the id of the guild skill the master uses. The World server checks and
    // pays; every member gets the new fund (51) and the skill (54), the master the answer (90).
    private void HandleUseBuff(GameSession session, IByteReader packet) {
        int buffId = packet.ReadInt();
        if (session.Guild.Guild == null) {
            return; // Not in a guild.
        }

        try {
            GuildResponse response = World.Guild(new GuildRequest {
                RequestorId = session.CharacterId,
                UseBuff = new GuildRequest.Types.UseBuff {
                    GuildId = session.Guild.Id,
                    BuffId = buffId,
                },
            });
            var error = (GuildError) response.Error;
            if (error != GuildError.none) {
                session.Send(GuildPacket.Error(error));
                return;
            }

            session.Send(GuildPacket.UseBuff(buffId));
        } catch (RpcException ex) {
            Logger.Error(ex, "Failed to use guild skill");
            session.Send(GuildPacket.Error(GuildError.s_guild_err_unknown));
        }
    }

    // CGuildManager::FUN_1413b96e0: the id of the personal guild skill the member buys with meso. The answer
    // (91) makes the client mark the effect as bought.
    private void HandleUsePersonalBuff(GameSession session, IByteReader packet) {
        int buffId = packet.ReadInt();
        if (session.Guild.Guild == null) {
            return; // Not in a guild.
        }

        GuildError error = session.Guild.UsePersonalBuff(buffId);
        if (error != GuildError.none) {
            session.Send(GuildPacket.Error(error));
        }
    }

    // CGuildManager::FUN_1413b9ac0: the id of the guild skill the master raises by one level. The World
    // server checks and pays; every member gets the new fund (51) and the skill (55).
    private void HandleUpgradeBuff(GameSession session, IByteReader packet) {
        int buffId = packet.ReadInt();
        if (session.Guild.Guild == null) {
            return; // Not in a guild.
        }

        try {
            GuildResponse response = World.Guild(new GuildRequest {
                RequestorId = session.CharacterId,
                UpgradeBuff = new GuildRequest.Types.UpgradeBuff {
                    GuildId = session.Guild.Id,
                    BuffId = buffId,
                },
            });
            var error = (GuildError) response.Error;
            if (error != GuildError.none) {
                session.Send(GuildPacket.Error(error));
            }
        } catch (RpcException ex) {
            Logger.Error(ex, "Failed to upgrade guild skill");
            session.Send(GuildPacket.Error(GuildError.s_guild_err_unknown));
        }
    }

    // CGuildManager::FUN_1413ba080 (guildmanager.cpp:857): the id of the guild event a member with the mini
    // game right opens. The World server checks and keeps the time; every member gets the event (56), the
    // sender the answer (99).
    private void HandleStartArcade(GameSession session, IByteReader packet) {
        int eventId = packet.ReadInt();
        if (session.Guild.Guild == null) {
            return; // Not in a guild.
        }

        try {
            GuildResponse response = World.Guild(new GuildRequest {
                RequestorId = session.CharacterId,
                OpenEvent = new GuildRequest.Types.OpenEvent {
                    GuildId = session.Guild.Id,
                    EventId = eventId,
                },
            });
            var error = (GuildError) response.Error;
            if (error != GuildError.none) {
                session.Send(GuildPacket.Error(error));
                return;
            }

            session.Send(GuildPacket.OpenEvent(eventId));
        } catch (RpcException ex) {
            Logger.Error(ex, "Failed to open guild event");
            session.Send(GuildPacket.Error(GuildError.s_guild_err_unknown));
        }
    }

    // CGuildManager::SendGuildMode100 (guildmanager.cpp:881): the id of an open guild event the member
    // enters. The client reads no answer; the member goes to the event's map, in the guild's own copy of
    // it, as with the guild house.
    private void HandleEnterArcade(GameSession session, IByteReader packet) {
        int eventId = packet.ReadInt();
        Guild? guild = session.Guild.Guild;
        if (guild == null || session.Field == null) {
            return; // Not in a guild.
        }

        GuildTable.Event? row = null;
        TableMetadata.GuildTable.Events?.TryGetValue(eventId, out row);
        int openTime = guild.Events.FirstOrDefault(entry => entry.Id == eventId).Value;
        if (!GuildRules.CanEnterEvent(row?.MapId ?? 0, openTime, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), session.Field.MapId)) {
            return;
        }

        session.Migrate(row!.MapId, guild.Id);
    }

    // CGuildManager::FUN_1413ba820: the body is the theme chosen for the next house level, not the level;
    // the level is always the guild's own plus 1. The World server checks, pays and saves; every member
    // gets the new fund (51) and the house (57).
    private void HandleUpgradeHouseRank(GameSession session, IByteReader packet) {
        int theme = packet.ReadInt();
        if (session.Guild.Guild == null) {
            return; // Not in a guild.
        }

        try {
            GuildResponse response = World.Guild(new GuildRequest {
                RequestorId = session.CharacterId,
                UpgradeHouse = new GuildRequest.Types.UpgradeHouse {
                    GuildId = session.Guild.Id,
                    Theme = theme,
                },
            });
            var error = (GuildError) response.Error;
            if (error != GuildError.none) {
                session.Send(GuildPacket.Error(error));
            }
        } catch (RpcException ex) {
            Logger.Error(ex, "Failed to upgrade guild house");
            session.Send(GuildPacket.Error(GuildError.s_guild_err_unknown));
        }
    }

    // CGuildManager::FUN_1413babf0: the new theme of the house at its level. The World server checks, pays
    // and saves; every member gets the new fund (51) and the house (57).
    private void HandleUpgradeHouseTheme(GameSession session, IByteReader packet) {
        int theme = packet.ReadInt();
        if (session.Guild.Guild == null) {
            return; // Not in a guild.
        }

        try {
            GuildResponse response = World.Guild(new GuildRequest {
                RequestorId = session.CharacterId,
                ChangeHouseTheme = new GuildRequest.Types.ChangeHouseTheme {
                    GuildId = session.Guild.Id,
                    Theme = theme,
                },
            });
            var error = (GuildError) response.Error;
            if (error != GuildError.none) {
                session.Send(GuildPacket.Error(error));
            }
        } catch (RpcException ex) {
            Logger.Error(ex, "Failed to change guild house theme");
            session.Send(GuildPacket.Error(GuildError.s_guild_err_unknown));
        }
    }

    private void HandleEnterHouse(GameSession session) {
        if (session.Guild.Guild is null) {
            return;
        }
        TableMetadata.GuildTable.Houses.TryGetValue(session.Guild.Guild.HouseRank, out IReadOnlyDictionary<int, GuildTable.House>? houseRank);

        if (houseRank is null) {
            return;
        }

        houseRank.TryGetValue(session.Guild.Guild.HouseTheme, out GuildTable.House? house);

        if (house is null) {
            return;
        }

        session.Migrate(house.MapId, session.Guild.Id);
    }

    // Guild 109 (CGuildManager::FUN_1413bb490, guildmanager.cpp:1046): an item of the guild storage for a
    // member. Body: the item (ItemEntity::Write, commonstruct_wa.cpp:84: int id, short rank, int count,
    // 4 bools), then the receiver's name. The server keeps no guild storage, so the storage holds 0 of
    // every item and the gift is refused with the result byte (OnPacketStorageGiftResult, :2963).
    private void HandleSendGift(GameSession session, IByteReader packet) {
        var gift = packet.Read<RewardItem>();
        string playerName = packet.ReadUnicodeString();
        Guild? guild = session.Guild.Guild;
        if (guild == null) {
            return; // The client's reader drops 109 outside a guild.
        }

        bool receiverIsMember = guild.Members.Values.Any(member => member.Name == playerName);
        const int stored = 0;
        GuildError error = GuildRules.CheckStorageGift(gift.Amount, stored, receiverIsMember);
        if (error == GuildError.none) {
            error = GuildError.s_guild_err_unknown; // not reached while nothing is stored
        }
        session.Send(GuildPacket.StorageGift(error));
    }

    // CGuildManager::SendGuildMode112: sent once when the storage log is opened, with no body; the client
    // asks again until it gets an answer. No guild storage gift is handled (Guild 109), so the log is empty.
    private void HandleUpdateGiftLog(GameSession session) {
        if (session.Guild.Guild == null) {
            return; // Not in a guild.
        }

        session.Send(GuildPacket.StorageHistory());
    }

    // CGuildManager::FUN_1413b8540: the count of donations. One donation costs the guildDonateMeso constant
    // and adds as much to the guild fund. The reply (113) carries the member's count of today and the time.
    private void HandleDonate(GameSession session, IByteReader packet) {
        int count = packet.ReadInt();
        if (session.Guild.Guild == null) {
            return; // Not in a guild.
        }
        GuildMember? self = session.Guild.GetMember(session.CharacterId);
        if (self == null || count <= 0) {
            return;
        }
        if (count > session.Guild.Properties.DonateMax) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_unknown));
            return;
        }

        long cost = (long) Constants.guildDonateMeso * count;
        if (cost <= 0 || session.Currency.Meso < cost) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_no_money));
            return;
        }

        // Order: the meso is taken first, World adds and saves the fund, and only a refusal that World
        // itself answered gives the meso back. An RPC error leaves it taken, because World may have added
        // the fund before the call failed: the guild can not get fund without meso.
        session.Currency.Meso -= cost;
        try {
            GuildResponse response = World.Guild(new GuildRequest {
                RequestorId = session.CharacterId,
                Donate = new GuildRequest.Types.Donate {
                    GuildId = session.Guild.Id,
                    Count = count,
                    Fund = cost,
                },
            });
            var error = (GuildError) response.Error;
            if (error != GuildError.none) {
                session.Currency.Meso += cost;
                session.Send(GuildPacket.Error(error));
                return;
            }

            self.DailyDonationCount = response.DonationCount;
            self.DonationTime = response.DonationTime;
            session.Send(GuildPacket.Donated(self));
        } catch (RpcException ex) {
            Logger.Error(ex, "Failed to donate to guild; {Cost} meso of character {CharacterId} stay taken", cost, session.CharacterId);
            session.Send(GuildPacket.Error(GuildError.s_guild_err_unknown));
        }
    }

    // CGuildManager::FUN_1413bb000: the kind of guild NPC the master raises by one level. The World server
    // checks, pays and saves; every member gets the new fund (51) and the NPC (59).
    private void HandleUpgradeNpc(GameSession session, IByteReader packet) {
        int type = packet.ReadInt();
        if (session.Guild.Guild == null) {
            return; // Not in a guild.
        }

        try {
            GuildResponse response = World.Guild(new GuildRequest {
                RequestorId = session.CharacterId,
                UpgradeNpc = new GuildRequest.Types.UpgradeNpc {
                    GuildId = session.Guild.Id,
                    Type = type,
                },
            });
            var error = (GuildError) response.Error;
            if (error != GuildError.none) {
                session.Send(GuildPacket.Error(error));
            }
        } catch (RpcException ex) {
            Logger.Error(ex, "Failed to upgrade guild NPC");
            session.Send(GuildPacket.Error(GuildError.s_guild_err_unknown));
        }
    }

    // Guild 115 (CGuildManager::SendGuildMode115, guildmanager.cpp:1425): no body; the guild asks for a
    // ticket of the guild vs-game. The client tests the mini game right (0x400) itself. The server runs no
    // vs-game, so no time takes applications and the answer is error 47.
    private void HandleCreateGuildEvent(GameSession session) {
        if (session.Guild.Guild == null) {
            return;
        }

        bool hasRight = session.Guild.HasPermission(session.CharacterId, GuildPermission.StartMiniGame);
        GuildError error = GuildRules.CheckVsGameRegister(hasRight, isVsGameTime: false);
        if (error == GuildError.none) {
            error = GuildError.s_guild_err_isNotVsGameTime; // not reached: no vs-game time
        }
        session.Send(GuildPacket.Error(error));
    }

    // Guild 116 (CGuildManager::SendGuildMode116, guildmanager.cpp:1444): no body; the guild's vs-game
    // ticket is withdrawn. The client sends it only while its ticket flag is set, and the server never sets
    // it (115 is refused). The answer puts the flag back to the server's state: no ticket, without a line.
    private void HandleStartGuildEvent(GameSession session) {
        if (session.Guild.Guild == null) {
            return;
        }
        if (!session.Guild.HasPermission(session.CharacterId, GuildPermission.StartMiniGame)) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_no_authority));
            return;
        }

        session.Send(GuildPacket.VsGameTicket(registered: false, notify: false));
    }

    // Guild 120 (CGuildManager::SendGuildMode120, guildmanager.cpp:3386): no body; the member takes the
    // entry prompt of 119 (FUN_1413c45e0, :2679). The server opens no vs-game and sends no prompt, so there
    // is nothing to enter: the client's open game is cleared (119 with the id 0 shows no prompt).
    private void HandleJoinGuildEvent(GameSession session) {
        if (session.Guild.Guild == null) {
            return;
        }

        session.Send(GuildPacket.VsGameOpen(0));
    }
}
