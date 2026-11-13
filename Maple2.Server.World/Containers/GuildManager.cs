using System.Collections.Concurrent;
using Grpc.Core;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Channel.Service;
using Maple2.Tools.Extensions;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;
using Maple2.Model.Validators;

namespace Maple2.Server.World.Containers;

public class GuildManager : IDisposable {
    public required GameStorage GameStorage { get; init; }
    public required ChannelClientLookup ChannelClients { get; init; }
    public required TableMetadataStorage TableMetadata { get; init; }

    public readonly Guild Guild;
    private readonly ConcurrentDictionary<long, (string, DateTime)> pendingInvites;
    // The lock of the guild's own values. One join at a time, by invite or by request (the capacity check
    // and the insert are one step), and every change of the fund or the experience.
    private readonly object guildLock = new();

    // Time of the last guild mail of each sender, for the server's limit. Old entries go on each insert.
    private readonly Dictionary<long, long> lastMail = new();

    public GuildManager(Guild guild) {
        Guild = guild;
        pendingInvites = new ConcurrentDictionary<long, (string, DateTime)>();
    }

    public void Dispose() {
        lock (guildLock) {
            using GameStorage.Request db = GameStorage.Context();
            db.SaveGuild(Guild);
        }
    }

    // The weekly reset of the members held here; the database row is reset after it, so a later save of the
    // guild keeps the 0.
    public void WeeklyReset() {
        GuildRules.ResetWeeklyContribution(Guild.Members.Values);
    }

    public bool HasPermission(long characterId, GuildPermission permission) {
        if (!Guild.Members.TryGetValue(characterId, out GuildMember? member)) {
            return false;
        }
        GuildRank? rank = Guild.Ranks.ElementAtOrDefault(member.Rank);
        return rank != null && rank.Permission.HasFlag(permission);
    }

    // The join request list is held by the members who may answer a request (the invite right), so the
    // request notices go to them only.
    public void BroadcastToApplicationHandlers(GuildRequest request, long exceptCharacterId = 0) {
        Broadcast(request, member => member.CharacterId != exceptCharacterId
                                     && HasPermission(member.CharacterId, GuildPermission.InviteMembers));
    }

    public void Broadcast(GuildRequest request, Func<GuildMember, bool>? filter = null) {
        if (request.GuildId > 0 && request.GuildId != Guild.Id) {
            throw new InvalidOperationException($"Broadcasting {request.GuildCase} for incorrect guild: {request.GuildId} => {Guild.Id}");
        }

        request.GuildId = Guild.Id;
        IEnumerable<GuildMember> receivers = filter == null ? Guild.Members.Values : Guild.Members.Values.Where(filter);
        foreach (IGrouping<short, GuildMember> group in receivers.GroupBy(member => member.Info.Channel)) {
            if (!ChannelClients.TryGetClient(group.Key, out ChannelClient? client)) {
                continue;
            }

            request.ReceiverIds.Clear();
            request.ReceiverIds.AddRange(group.Select(member => member.Info.CharacterId));

            try {
                client.Guild(request);
            } catch { /* ignored */ }
        }
    }

    public GuildError Invite(long requestorId, PlayerInfo player) {
        if (!Guild.Members.TryGetValue(requestorId, out GuildMember? requestor)) {
            return GuildError.s_guild_err_null_member;
        }
        GuildRank? rank = Guild.Ranks.ElementAtOrDefault(requestor.Rank);
        if (rank == null || !rank.Permission.HasFlag(GuildPermission.InviteMembers)) {
            return GuildError.s_guild_err_no_authority;
        }
        // The cap of the guild's level (guild.xml), not the stored default: 60 up to level 3, 100 at level 14.
        if (Guild.Members.Count >= GuildProperty().Capacity) {
            return GuildError.s_guild_err_full_member;
        }
        if (Guild.Members.ContainsKey(player.CharacterId)) {
            return GuildError.s_guild_err_already_exist;
        }
        if (!ChannelClients.TryGetClient(player.Channel, out ChannelClient? client)) {
            return GuildError.s_guild_err_wait_inviting;
        }

        try {
            pendingInvites[player.CharacterId] = (requestor.Name, DateTime.Now.AddSeconds(30));
            var request = new GuildRequest {
                GuildId = Guild.Id,
                ReceiverIds = { player.CharacterId },
                Invite = new GuildRequest.Types.Invite {
                    GuildName = Guild.Name,
                    SenderId = requestor.CharacterId,
                    SenderName = requestor.Name,
                },
            };

            GuildResponse response = client.Guild(request);
            return (GuildError) response.Error;
        } catch (RpcException) {
            return GuildError.s_guild_err_unknown;
        }
    }

    // beforeNotice goes to the members before the join notice: KMS2 sends the invite answer (Guild 6) first.
    public GuildError Join(string requestorName, PlayerInfo info, GuildRequest? beforeNotice = null) {
        lock (guildLock) {
            return JoinLocked(requestorName, info, beforeNotice);
        }
    }

    private GuildError JoinLocked(string requestorName, PlayerInfo info, GuildRequest? beforeNotice) {
        if (Guild.Members.Count >= GuildProperty().Capacity) {
            return GuildError.s_guild_err_full_member;
        }
        if (Guild.Members.ContainsKey(info.CharacterId)) {
            return GuildError.s_guild_err_already_exist;
        }

        using GameStorage.Request db = GameStorage.Context();
        GuildMember? member = db.CreateGuildMember(Guild.Id, info);
        if (member == null) {
            return GuildError.s_guild_err_fail_addmember;
        }

        // Broadcast before adding this new member.
        if (beforeNotice != null) {
            Broadcast(beforeNotice);
        }
        Broadcast(new GuildRequest {
            AddMember = new GuildRequest.Types.AddMember {
                CharacterId = member.CharacterId,
                RequestorName = requestorName,
                Rank = member.Rank,
                JoinTime = member.JoinTime,
            },
        });
        Guild.Members.TryAdd(member.CharacterId, member);
        return GuildError.none;
    }

    public GuildError Expel(long requestorId, long characterId) {
        if (!Guild.Members.TryGetValue(requestorId, out GuildMember? requestor)) {
            return GuildError.s_guild_err_null_member;
        }
        GuildRank? rank = Guild.Ranks.ElementAtOrDefault(requestor.Rank);
        if (rank == null || !rank.Permission.HasFlag(GuildPermission.ExpelMembers)) {
            return GuildError.s_guild_err_no_authority;
        }
        if (characterId == Guild.LeaderCharacterId) {
            return GuildError.s_guild_err_expel_target_master;
        }
        if (!Guild.Members.TryGetValue(characterId, out GuildMember? member)) {
            return GuildError.s_guild_err_not_join_member;
        }
        if (member.Rank < requestor.Rank) {
            return GuildError.s_guild_err_no_authority;
        }

        using GameStorage.Request db = GameStorage.Context();
        if (!db.DeleteGuildMember(Guild.Id, member.CharacterId)) {
            return GuildError.s_guild_err_unknown;
        }

        // An online member gets Guild 9 from the broadcast; an offline one is told at its next login (Guild 76).
        if (!member.Info.Online) {
            var notice = new GuildExpelNotice(requestor.Name, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            if (!db.CreateGuildExpelNotice(member.CharacterId, notice)) {
                Serilog.Log.Warning("Failed to keep guild expel notice for {CharacterId}", member.CharacterId);
            }
        }

        Broadcast(new GuildRequest {
            RemoveMember = new GuildRequest.Types.RemoveMember {
                CharacterId = member.CharacterId,
                RequestorName = requestor.Name,
            },
        });
        Guild.Members.TryRemove(member.CharacterId, out _);
        return GuildError.none;
    }

    public GuildError Leave(long characterId) {
        if (characterId == Guild.LeaderCharacterId) {
            return GuildError.s_guild_err_cannot_leave_master;
        }
        if (!Guild.Members.TryGetValue(characterId, out GuildMember? member)) {
            return GuildError.s_guild_err_not_join_member;
        }

        using GameStorage.Request db = GameStorage.Context();
        if (!db.DeleteGuildMember(Guild.Id, member.CharacterId)) {
            return GuildError.s_guild_err_unknown;
        }

        Guild.Members.TryRemove(member.CharacterId, out _);
        Broadcast(new GuildRequest {
            RemoveMember = new GuildRequest.Types.RemoveMember {
                CharacterId = member.CharacterId,
            },
        });
        return GuildError.none;
    }

    public GuildError UpdateMember(long requestorId, long characterId, byte? rankId, string? message) {
        if (!Guild.Members.TryGetValue(requestorId, out GuildMember? requestor)) {
            return GuildError.s_guild_err_null_member;
        }
        if (!Guild.Members.TryGetValue(characterId, out GuildMember? member)) {
            return GuildError.s_guild_err_null_member;
        }

        if (rankId != null) {
            if (member.Rank == rankId) {
                return GuildError.s_guild_err_set_grade_failed;
            }

            GuildRank? rank = Guild.Ranks.ElementAtOrDefault(requestor.Rank);
            if (rank == null || !rank.Permission.HasFlag(GuildPermission.EditRank)) {
                return GuildError.s_guild_err_no_authority;
            }
            // Rank 0 is the master's: it is not given here and the master does not leave it here (UpdateLeader does both).
            if (!GuildRules.IsAssignableRank((byte) rankId, Guild.Ranks.Count)) {
                return GuildError.s_guild_err_invalid_grade_index;
            }
            if (!GuildRules.CanChangeRankOf(member.Rank)) {
                return GuildError.s_guild_err_set_grade_failed;
            }

            member.Rank = (byte) rankId;
            Broadcast(new GuildRequest {
                UpdateMember = new GuildRequest.Types.UpdateMember {
                    RequestorId = requestor.CharacterId,
                    CharacterId = member.CharacterId,
                    Rank = member.Rank,
                },
            });
        }
        if (message != null) {
            if (message.Length > Constant.MaxMottoLength) {
                return GuildError.s_guild_err_none;
            }

            member.Message = message;
            Broadcast(new GuildRequest {
                UpdateMember = new GuildRequest.Types.UpdateMember {
                    RequestorId = requestor.CharacterId,
                    CharacterId = member.CharacterId,
                    Message = member.Message,
                },
            });
        }

        return GuildError.none;
    }

    public GuildError CheckIn(long requestorId) {
        const int contribution = 10;

        if (!Guild.Members.TryGetValue(requestorId, out GuildMember? requestor)) {
            return GuildError.s_guild_err_null_member;
        }

        // Tested and taken under the member's lock, as the donation: two check-ins that arrive together
        // (the channel tests its own copy of the member, which is behind until the update comes back) must
        // not both pay the guild and the member.
        lock (requestor) {
            long dayStart = GuildRules.DayStart();
            if (!GuildRules.CanCheckIn(requestor.JoinTime, requestor.CheckinTime, dayStart)) {
                return GuildError.s_guild_err_unknown;
            }
            requestor.CheckinTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            requestor.WeeklyContribution += contribution;
            requestor.TotalContribution += contribution;
        }

        // Under the guild lock, as every other change of the fund (donation, skill upgrade): an add made
        // outside it could be lost against one of those.
        int guildExp;
        int guildFund;
        lock (guildLock) {
            GuildTable.Property property = GuildProperty();
            Guild.Experience += property.CheckInExp;
            // The fund stops at the level's ceiling, as a donation is refused above it.
            Guild.Funds = GuildRules.AddFund(Guild.Funds, property.CheckInFund, property.FundMax);
            guildExp = Guild.Experience;
            guildFund = Guild.Funds;
            // Stored now: the guild search (and the guild list of a player outside it) reads the row, so a level
            // reached here showed the old level and member cap until the World server stopped. A failed save keeps
            // the gain here for the next save.
            TrySave();
        }

        Broadcast(new GuildRequest {
            UpdateContribution = new GuildRequest.Types.UpdateContribution {
                ContributorId = requestor.CharacterId,
                GuildExp = guildExp,
                GuildFund = guildFund,
            },
        });
        Broadcast(new GuildRequest {
            UpdateMember = new GuildRequest.Types.UpdateMember {
                RequestorId = requestor.CharacterId,
                CharacterId = requestor.CharacterId,
                Contribution = contribution,
                CheckInTime = requestor.CheckinTime,
            },
        });

        return GuildError.none;
    }

    // Guild 92: the skill goes up one level and the fund pays the upgrade cost of the new level.
    public GuildError UpgradeBuff(long requestorId, int buffId) {
        if (!Guild.Members.TryGetValue(requestorId, out GuildMember? requestor)) {
            return GuildError.s_guild_err_null_member;
        }

        GuildBuff upgraded;
        int guildExp;
        int guildFund;
        lock (guildLock) {
            int index = -1;
            for (int i = 0; i < Guild.Buffs.Count; i++) {
                if (Guild.Buffs[i].Id == buffId) {
                    index = i;
                    break;
                }
            }
            if (index < 0 || !TableMetadata.GuildTable.Buffs.TryGetValue(buffId, out IReadOnlyDictionary<short, GuildTable.Buff>? levels)) {
                return GuildError.s_guild_err_unknown;
            }

            GuildBuff buff = Guild.Buffs[index];
            levels.TryGetValue((short) buff.Level, out GuildTable.Buff? current);
            levels.TryGetValue((short) (buff.Level + 1), out GuildTable.Buff? next);
            GuildError error = GuildRules.CheckUpgradeSkill(requestorId == Guild.LeaderCharacterId, buffId, current, next,
                GuildProperty().Level, Guild.Funds);
            if (error != GuildError.none) {
                return error;
            }

            int oldFunds = Guild.Funds;
            Guild.Funds -= next!.UpgradeCost;
            upgraded = buff with { Level = buff.Level + 1 };
            Guild.Buffs[index] = upgraded;

            // Saved before the answer, as the donation and the NPC upgrade; a failed save undoes both values.
            bool saved;
            try {
                using GameStorage.Request db = GameStorage.Context();
                saved = db.SaveGuild(Guild);
            } catch (Exception) {
                saved = false;
            }
            if (!saved) {
                Guild.Funds = oldFunds;
                Guild.Buffs[index] = buff;
                return GuildError.s_guild_err_unknown;
            }
            guildExp = Guild.Experience;
            guildFund = Guild.Funds;
        }

        // No contributor: the members get the new fund (51) without a contribution line.
        Broadcast(new GuildRequest {
            UpdateContribution = new GuildRequest.Types.UpdateContribution {
                ContributorId = 0,
                GuildExp = guildExp,
                GuildFund = guildFund,
            },
        });
        Broadcast(new GuildRequest {
            UpgradeBuff = new GuildRequest.Types.UpgradeBuff {
                RequestorName = requestor.Name,
                Id = upgraded.Id,
                Level = upgraded.Level,
                ExpiryTime = upgraded.ExpiryTime,
            },
        });

        return GuildError.none;
    }

    // Guild 90: the fund pays the cost of the skill at its level and the skill gets the time of use, which
    // the client counts the duration from. Saved before the answer; a failed save undoes both values.
    public GuildError UseBuff(long requestorId, int buffId) {
        if (!Guild.Members.TryGetValue(requestorId, out GuildMember? requestor)) {
            return GuildError.s_guild_err_null_member;
        }

        GuildBuff used;
        GuildTable.Buff? row;
        int guildExp;
        int guildFund;
        lock (guildLock) {
            int index = -1;
            for (int i = 0; i < Guild.Buffs.Count; i++) {
                if (Guild.Buffs[i].Id == buffId) {
                    index = i;
                    break;
                }
            }
            if (index < 0 || !TableMetadata.GuildTable.Buffs.TryGetValue(buffId, out IReadOnlyDictionary<short, GuildTable.Buff>? levels)) {
                return GuildError.s_guild_err_unknown;
            }

            GuildBuff buff = Guild.Buffs[index];
            levels.TryGetValue((short) buff.Level, out row);
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            GuildError error = GuildRules.CheckUseSkill(requestorId == Guild.LeaderCharacterId, buffId, row,
                GuildProperty().Level, Guild.Funds, buff.ExpiryTime, now);
            if (error != GuildError.none) {
                return error;
            }

            int oldFunds = Guild.Funds;
            Guild.Funds -= row!.Cost;
            used = buff with { ExpiryTime = now };
            Guild.Buffs[index] = used;
            if (!TrySave()) {
                Guild.Funds = oldFunds;
                Guild.Buffs[index] = buff;
                return GuildError.s_guild_err_unknown;
            }
            guildExp = Guild.Experience;
            guildFund = Guild.Funds;
        }

        // No contributor: the members get the new fund (51) without a contribution line, then the skill (54).
        Broadcast(new GuildRequest {
            UpdateContribution = new GuildRequest.Types.UpdateContribution {
                ContributorId = 0,
                GuildExp = guildExp,
                GuildFund = guildFund,
            },
        });
        Broadcast(new GuildRequest {
            UseBuff = new GuildRequest.Types.UseBuff {
                RequestorName = requestor.Name,
                Id = used.Id,
                Level = used.Level,
                UseTime = used.ExpiryTime,
                EffectId = row.Id,
                EffectLevel = row.Level,
                Duration = row.Duration * 60,
            },
        });

        return GuildError.none;
    }

    // A guild quest's reward for the guild (completeReward guildExp and guildFund), from the member who completed it.
    // As a check-in's, it is added under the guild lock and saved with the guild; the fund stops at the level's
    // fundMax (GuildRules.AddFund), the ceiling the client's donation check keeps it under.
    public GuildError AddExpFund(long requestorId, int exp, int fund) {
        if (!Guild.Members.ContainsKey(requestorId)) {
            return GuildError.s_guild_err_null_member;
        }
        if (exp <= 0 && fund <= 0) {
            return GuildError.none;
        }

        int guildExp;
        int guildFund;
        lock (guildLock) {
            Guild.Experience += Math.Max(exp, 0);
            Guild.Funds = GuildRules.AddFund(Guild.Funds, fund, GuildProperty().FundMax);
            guildExp = Guild.Experience;
            guildFund = Guild.Funds;
            // Stored now, as the check-in.
            TrySave();
        }

        Broadcast(new GuildRequest {
            UpdateContribution = new GuildRequest.Types.UpdateContribution {
                ContributorId = requestorId,
                GuildExp = guildExp,
                GuildFund = guildFund,
            },
        });
        return GuildError.none;
    }

    // A guild shop buy paid from the guild fund (shop currency 7). The client offers it to the master only and the
    // fund must cover the price; the fund is saved before the answer and a failed save undoes it.
    public GuildError SpendFund(long requestorId, int amount) {
        if (!Guild.Members.ContainsKey(requestorId)) {
            return GuildError.s_guild_err_null_member;
        }

        int guildExp;
        int guildFund;
        lock (guildLock) {
            GuildError error = GuildRules.CheckSpendFund(requestorId == Guild.LeaderCharacterId, Guild.Funds, amount);
            if (error != GuildError.none) {
                return error;
            }

            int oldFunds = Guild.Funds;
            Guild.Funds -= amount;
            if (!TrySave()) {
                Guild.Funds = oldFunds;
                return GuildError.s_guild_err_unknown;
            }
            guildExp = Guild.Experience;
            guildFund = Guild.Funds;
        }

        Broadcast(new GuildRequest {
            UpdateContribution = new GuildRequest.Types.UpdateContribution {
                ContributorId = 0,
                GuildExp = guildExp,
                GuildFund = guildFund,
            },
        });
        return GuildError.none;
    }

    // A guild recall scroll: every member's channel offers the recall to its members but the requestor.
    public GuildError Recall(long requestorId, int mapId, long endTime) {
        if (!Guild.Members.TryGetValue(requestorId, out GuildMember? requestor)) {
            return GuildError.s_guild_err_null_member;
        }

        Broadcast(new GuildRequest {
            Recall = new GuildRequest.Types.Recall {
                CharacterId = requestorId,
                Name = requestor.Name,
                MapId = mapId,
                EndTime = endTime,
            },
        }, member => member.CharacterId != requestorId && member.Info.Online);
        return GuildError.none;
    }

    // Guild 99. The time of opening is kept in memory only: an event is open for 30 seconds
    // (GuildRules.EventOpenSeconds), so nothing is lost when World stops.
    public GuildError OpenEvent(long requestorId, int eventId) {
        if (!Guild.Members.TryGetValue(requestorId, out GuildMember? requestor)) {
            return GuildError.s_guild_err_null_member;
        }

        var opened = new GuildEvent(eventId, 0);
        lock (guildLock) {
            int index = -1;
            for (int i = 0; i < Guild.Events.Count; i++) {
                if (Guild.Events[i].Id == eventId) {
                    index = i;
                    break;
                }
            }
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            GuildError error = GuildRules.CheckOpenEvent(TableMetadata.GuildTable.Events?.ContainsKey(eventId) == true,
                Guild.CreationTime, HasPermission(requestorId, GuildPermission.StartMiniGame),
                index < 0 ? 0 : Guild.Events[index].Value, now);
            if (error != GuildError.none) {
                return error;
            }

            opened = new GuildEvent(eventId, (int) now);
            if (index < 0) {
                Guild.Events.Add(opened);
            } else {
                Guild.Events[index] = opened;
            }
        }

        Broadcast(new GuildRequest {
            OpenEvent = new GuildRequest.Types.OpenEvent {
                RequestorName = requestor.Name,
                Id = opened.Id,
                Time = opened.Value,
            },
        });

        return GuildError.none;
    }

    public GuildEvent[] OpenedEvents() {
        lock (guildLock) {
            return Guild.Events.ToArray();
        }
    }

    // Guild 71. The server's own limit (GuildRules.MailCooldownSeconds), kept here so that it holds on every
    // channel; the mail right is checked here too. The turn is taken before the channel writes the mails.
    public GuildError TakeMailTurn(long requestorId) {
        if (!Guild.Members.TryGetValue(requestorId, out GuildMember? _)) {
            return GuildError.s_guild_err_null_member;
        }
        if (!HasPermission(requestorId, GuildPermission.SendMail)) {
            return GuildError.s_guild_err_no_authority;
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        lock (lastMail) {
            if (!GuildRules.TakeMailTurn(lastMail, requestorId, now)) {
                return GuildRules.MailCooldownError;
            }
        }
        return GuildError.none;
    }

    // Guild 101. The fund and the house are saved before the answer; a failed save undoes them.
    public GuildError UpgradeHouse(long requestorId, int theme) {
        if (!Guild.Members.TryGetValue(requestorId, out GuildMember? requestor)) {
            return GuildError.s_guild_err_null_member;
        }

        int guildExp;
        int guildFund;
        int rank;
        lock (guildLock) {
            GuildTable.House? next = null;
            if (TableMetadata.GuildTable.Houses.TryGetValue(Guild.HouseRank + 1, out IReadOnlyDictionary<int, GuildTable.House>? themes)) {
                themes.TryGetValue(theme, out next);
            }
            GuildError error = GuildRules.CheckUpgradeHouse(requestorId == Guild.LeaderCharacterId, next,
                GuildProperty().Level, Guild.Funds);
            if (error != GuildError.none) {
                return error;
            }

            int oldFunds = Guild.Funds;
            int oldRank = Guild.HouseRank;
            int oldTheme = Guild.HouseTheme;
            Guild.Funds -= next!.UpgradeCost;
            Guild.HouseRank = oldRank + 1;
            Guild.HouseTheme = theme;
            if (!TrySave()) {
                Guild.Funds = oldFunds;
                Guild.HouseRank = oldRank;
                Guild.HouseTheme = oldTheme;
                return GuildError.s_guild_err_unknown;
            }
            guildExp = Guild.Experience;
            guildFund = Guild.Funds;
            rank = Guild.HouseRank;
        }

        BroadcastHouse(requestor.Name, guildExp, guildFund, rank, theme);
        return GuildError.none;
    }

    // Guild 102. The fund and the theme are saved before the answer; a failed save undoes them.
    public GuildError ChangeHouseTheme(long requestorId, int theme) {
        if (!Guild.Members.TryGetValue(requestorId, out GuildMember? requestor)) {
            return GuildError.s_guild_err_null_member;
        }

        int guildExp;
        int guildFund;
        int rank;
        lock (guildLock) {
            GuildTable.House? house = null;
            if (TableMetadata.GuildTable.Houses.TryGetValue(Guild.HouseRank, out IReadOnlyDictionary<int, GuildTable.House>? themes)) {
                themes.TryGetValue(theme, out house);
            }
            GuildError error = GuildRules.CheckChangeHouseTheme(requestorId == Guild.LeaderCharacterId, house,
                Guild.HouseTheme, theme, Guild.Funds);
            if (error != GuildError.none) {
                return error;
            }

            int oldFunds = Guild.Funds;
            int oldTheme = Guild.HouseTheme;
            Guild.Funds -= house!.ReThemeCost;
            Guild.HouseTheme = theme;
            if (!TrySave()) {
                Guild.Funds = oldFunds;
                Guild.HouseTheme = oldTheme;
                return GuildError.s_guild_err_unknown;
            }
            guildExp = Guild.Experience;
            guildFund = Guild.Funds;
            rank = Guild.HouseRank;
        }

        BroadcastHouse(requestor.Name, guildExp, guildFund, rank, theme);
        return GuildError.none;
    }

    private bool TrySave() {
        try {
            using GameStorage.Request db = GameStorage.Context();
            return db.SaveGuild(Guild);
        } catch (Exception) {
            return false;
        }
    }

    // No contributor: the members get the new fund (51) without a contribution line, then the house (57).
    private void BroadcastHouse(string requestorName, int guildExp, int guildFund, int rank, int theme) {
        Broadcast(new GuildRequest {
            UpdateContribution = new GuildRequest.Types.UpdateContribution {
                ContributorId = 0,
                GuildExp = guildExp,
                GuildFund = guildFund,
            },
        });
        Broadcast(new GuildRequest {
            UpdateHouse = new GuildRequest.Types.UpdateHouse {
                RequestorName = requestorName,
                Rank = rank,
                Theme = theme,
            },
        });
    }

    // Guild 114. The fund and the NPC are saved before the answer; a failed save undoes both.
    public GuildError UpgradeNpc(long requestorId, GuildNpcType type) {
        if (!Guild.Members.TryGetValue(requestorId, out GuildMember? requestor)) {
            return GuildError.s_guild_err_null_member;
        }

        GuildNpc upgraded;
        int guildExp;
        int guildFund;
        lock (guildLock) {
            int index = -1;
            for (int i = 0; i < Guild.Npcs.Count; i++) {
                if (Guild.Npcs[i].Type == type) {
                    index = i;
                    break;
                }
            }
            int level = index < 0 ? 0 : Guild.Npcs[index].Level;

            GuildTable.Npc? next = null;
            if (level >= 0 && level < short.MaxValue
                && TableMetadata.GuildTable.Npcs.TryGetValue(type, out IReadOnlyDictionary<short, GuildTable.Npc>? levels)) {
                levels.TryGetValue((short) (level + 1), out next);
            }
            GuildError error = GuildRules.CheckUpgradeNpc(requestorId == Guild.LeaderCharacterId, next,
                GuildProperty().Level, Guild.HouseRank, Guild.Funds);
            if (error != GuildError.none) {
                return error;
            }

            int oldFunds = Guild.Funds;
            upgraded = new GuildNpc(type, level + 1);
            Guild.Funds -= next!.UpgradeCost;
            if (index < 0) {
                Guild.Npcs.Add(upgraded);
            } else {
                Guild.Npcs[index] = upgraded;
            }

            bool saved;
            try {
                using GameStorage.Request db = GameStorage.Context();
                saved = db.SaveGuild(Guild);
            } catch (Exception) {
                saved = false;
            }
            if (!saved) {
                Guild.Funds = oldFunds;
                if (index < 0) {
                    Guild.Npcs.Remove(upgraded);
                } else {
                    Guild.Npcs[index] = new GuildNpc(type, level);
                }
                return GuildError.s_guild_err_unknown;
            }
            guildExp = Guild.Experience;
            guildFund = Guild.Funds;
        }

        // No contributor: the members get the new fund (51) without a contribution line.
        Broadcast(new GuildRequest {
            UpdateContribution = new GuildRequest.Types.UpdateContribution {
                ContributorId = 0,
                GuildExp = guildExp,
                GuildFund = guildFund,
            },
        });
        Broadcast(new GuildRequest {
            UpgradeNpc = new GuildRequest.Types.UpgradeNpc {
                RequestorName = requestor.Name,
                Type = (int) upgraded.Type,
                Level = upgraded.Level,
            },
        });

        return GuildError.none;
    }

    // Guild 113. The channel has taken the member's meso before this call and gives it back on a refusal.
    public GuildError Donate(long requestorId, int count, long fund, out GuildMember? member) {
        if (!Guild.Members.TryGetValue(requestorId, out member)) {
            return GuildError.s_guild_err_null_member;
        }

        GuildTable.Property property;
        lock (guildLock) {
            property = GuildProperty(); // reads the experience, which changes under this lock
        }
        int guildExp;
        int guildFund;
        lock (member) {
            long dayStart = GuildRules.DayStart();
            int today = GuildRules.DonationsToday(member.DailyDonationCount, member.DonationTime, dayStart);
            lock (guildLock) {
                if (!GuildRules.CanDonate(count, member.JoinTime, dayStart, today, property.DonateMax, Guild.Funds, fund,
                        Math.Min(property.FundMax, int.MaxValue))) {
                    return GuildError.s_guild_err_unknown;
                }

                // The donation is saved before it is answered: the channel has taken the meso by now, so
                // the fund may not be lost with the World process. A failed save puts the three values back.
                int oldFunds = Guild.Funds;
                int oldCount = member.DailyDonationCount;
                long oldTime = member.DonationTime;
                Guild.Funds += (int) fund;
                member.DailyDonationCount = today + count;
                member.DonationTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                bool saved;
                try {
                    using GameStorage.Request db = GameStorage.Context();
                    saved = db.SaveGuild(Guild);
                } catch (Exception) {
                    saved = false;
                }
                if (!saved) {
                    Guild.Funds = oldFunds;
                    member.DailyDonationCount = oldCount;
                    member.DonationTime = oldTime;
                    return GuildError.s_guild_err_unknown;
                }
                guildExp = Guild.Experience;
                guildFund = Guild.Funds;
            }
        }

        Broadcast(new GuildRequest {
            UpdateContribution = new GuildRequest.Types.UpdateContribution {
                ContributorId = member.CharacterId,
                GuildExp = guildExp,
                GuildFund = guildFund,
            },
        });

        return GuildError.none;
    }

    public GuildError UpdateLeader(long requestorId, long newLeaderId) {
        if (requestorId != Guild.LeaderCharacterId) {
            return GuildError.s_guild_err_no_master;
        }
        if (!Guild.Members.TryGetValue(requestorId, out GuildMember? oldLeader)) {
            return GuildError.s_guild_err_null_member;
        }
        if (!Guild.Members.TryGetValue(newLeaderId, out GuildMember? newLeader)) {
            return GuildError.s_guild_err_null_member;
        }

        oldLeader.Rank = 1; // Jr. Master
        newLeader.Rank = 0; // Master
        Guild.LeaderCharacterId = newLeader.CharacterId;
        Guild.LeaderAccountId = newLeader.AccountId;
        Guild.LeaderName = newLeader.Name;

        Broadcast(new GuildRequest {
            UpdateLeader = new GuildRequest.Types.UpdateLeader {
                OldLeaderId = oldLeader.CharacterId,
                NewLeaderId = newLeader.CharacterId,
            },
        });

        return GuildError.none;
    }

    // Guild 35: every member, the requestor too, is told of a trophy grade the requestor earned.
    public GuildError TrophyNotice(long requestorId, int trophyId, int grade, bool lastGrade) {
        if (!Guild.Members.TryGetValue(requestorId, out GuildMember? requestor)) {
            return GuildError.s_guild_err_null_member;
        }

        Broadcast(new GuildRequest {
            TrophyNotice = new GuildRequest.Types.TrophyNotice {
                Name = requestor.Name,
                TrophyId = trophyId,
                Grade = grade,
                LastGrade = lastGrade,
            },
        });

        return GuildError.none;
    }

    public GuildError UpdateNotice(long requestorId, string message) {
        if (!Guild.Members.TryGetValue(requestorId, out GuildMember? requestor)) {
            return GuildError.s_guild_err_null_member;
        }
        GuildRank? rank = Guild.Ranks.ElementAtOrDefault(requestor.Rank);
        if (rank == null || !rank.Permission.HasFlag(GuildPermission.EditNotice)) {
            return GuildError.s_guild_err_no_authority;
        }
        if (message.Length > GuildRules.MaxNoticeLength) {
            return GuildError.s_guild_err_unknown;
        }

        Guild.Notice = message;
        Broadcast(new GuildRequest {
            UpdateNotice = new GuildRequest.Types.UpdateNotice {
                RequestorName = requestor.Name,
                Message = Guild.Notice,
            },
        });

        return GuildError.none;
    }

    // CGuildManager::SendGuildMode67: only the master edits a rank, and only one that exists (the guild
    // dialog edits the ranks it lists). The rank's name and permissions change; its members keep it.
    public GuildError UpdateRank(long requestorId, int rankId, string name, GuildPermission permission) {
        if (!Guild.Members.TryGetValue(requestorId, out GuildMember? requestor)) {
            return GuildError.s_guild_err_null_member;
        }
        if (Guild.LeaderCharacterId != requestorId) {
            return GuildError.s_guild_err_no_authority;
        }
        // Ranks are matched by their id, the index the client sends, not by list position.
        GuildRank? rank = Guild.Ranks.FirstOrDefault(r => r.Id == rankId);
        if (rank == null) {
            return GuildError.s_guild_err_invalid_grade_index;
        }
        if ((int) permission is < 0 or > 4095) {
            return GuildError.s_guild_err_invalid_grade_data;
        }

        // Under the guild lock, as every save of the guild: a save may not run while a donation or an
        // upgrade is between its change and its own save.
        lock (guildLock) {
            rank.Name = name;
            rank.Permission = permission;

            using GameStorage.Request db = GameStorage.Context();
            db.SaveGuild(Guild);
        }

        Broadcast(new GuildRequest {
            UpdateRank = new GuildRequest.Types.UpdateRank {
                RequestorName = requestor.Name,
                RankId = rankId,
                Name = rank.Name,
                Permission = (int) rank.Permission,
            },
        });

        return GuildError.none;
    }

    // CGuildManager::SendGuildMode68: sent by a member with the invite right (2). The search reads the
    // database, so the change is written at once.
    public GuildError UpdateFocus(long requestorId, bool listed, int focus) {
        if (!Guild.Members.TryGetValue(requestorId, out GuildMember? requestor)) {
            return GuildError.s_guild_err_null_member;
        }
        GuildRank? rank = Guild.Ranks.ElementAtOrDefault(requestor.Rank);
        if (rank == null || !rank.Permission.HasFlag(GuildPermission.InviteMembers)) {
            return GuildError.s_guild_err_no_authority;
        }
        if ((focus & ~GuildRules.FocusMask) != 0) {
            return GuildError.s_guild_err_unknown;
        }

        lock (guildLock) {
            Guild.Listed = listed;
            Guild.Focus = (GuildFocus) focus;

            using GameStorage.Request db = GameStorage.Context();
            db.SaveGuild(Guild);
        }

        Broadcast(new GuildRequest {
            UpdateFocus = new GuildRequest.Types.UpdateFocus {
                RequestorName = requestor.Name,
                Listed = Guild.Listed,
                Focus = (int) Guild.Focus,
            },
        });

        return GuildError.none;
    }

    public GuildError UpdateEmblem(long requestorId, string emblem) {
        if (!Guild.Members.TryGetValue(requestorId, out GuildMember? requestor)) {
            return GuildError.s_guild_err_null_member;
        }
        GuildRank? rank = Guild.Ranks.ElementAtOrDefault(requestor.Rank);
        if (rank == null || !rank.Permission.HasFlag(GuildPermission.EditEmblem)) {
            return GuildError.s_guild_err_no_authority;
        }

        lock (guildLock) {
            Guild.Emblem = emblem;

            using GameStorage.Request db = GameStorage.Context();
            db.SaveGuild(Guild);
        }

        Broadcast(new GuildRequest {
            UpdateEmblem = new GuildRequest.Types.UpdateEmblem {
                RequestorName = requestor.Name,
                Emblem = Guild.Emblem,
            },
        });

        return GuildError.none;
    }

    // The guild rename ticket (item function ChangeGuildName): only the master may use it (the client checks this
    // too, ItemFunctionDispatch ChangeGuildName), and the name must not be taken by any guild.
    public GuildError Rename(long requestorId, string name) {
        if (!Guild.Members.ContainsKey(requestorId)) {
            return GuildError.s_guild_err_null_member;
        }
        if (Guild.LeaderCharacterId != requestorId) {
            return GuildError.s_guild_err_no_master;
        }

        lock (GuildLookup.NameLock) {
            using GameStorage.Request db = GameStorage.Context();
            if (db.GuildExists(guildName: name)) {
                return GuildError.s_guild_err_name_exist;
            }
            lock (guildLock) {
                string old = Guild.Name;
                Guild.Name = name;
                if (!db.SaveGuild(Guild)) {
                    Guild.Name = old;
                    return GuildError.s_guild_err_unknown;
                }
            }
        }

        Broadcast(new GuildRequest {
            Rename = new GuildRequest.Types.Rename {
                Name = name,
            },
        });

        return GuildError.none;
    }

    public GuildError AddOrUpdatePoster(int id, string picture, long ownerId, string ownerName, long resourceId) {
        // Any member: the client asks for no rank right before a poster upload, unlike the emblem (64).
        if (!Guild.Members.ContainsKey(ownerId)) {
            return GuildError.s_guild_err_null_member;
        }

        GuildPoster? oldPoster = Guild.Posters.FirstOrDefault(poster => poster.Id == id);
        if (oldPoster != null) {
            Guild.Posters.Remove(oldPoster);
        }

        Guild.Posters.Add(new GuildPoster {
            Id = id,
            Picture = picture,
            OwnerId = ownerId,
            OwnerName = ownerName,
            ResourceId = resourceId,
        });

        Broadcast(new GuildRequest {
            UpdatePoster = new GuildRequest.Types.UpdatePoster {
                Id = id,
                Picture = picture,
                OwnerId = ownerId,
                OwnerName = ownerName,
                ResourceId = resourceId,
            },
        });
        return GuildError.none;
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

    private GuildTable.Property GuildProperty() {
        return GuildRules.PropertyAt(TableMetadata.GuildTable.Properties, Guild.Experience)!;
    }
}
