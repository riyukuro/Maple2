using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Google.Protobuf;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Core.Sync;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util.Sync;
using Maple2.Server.World.Service;
using Maple2.Tools.Extensions;
using Serilog;

namespace Maple2.Server.Game.Manager;

public class GuildManager : IDisposable {
    private readonly GameSession session;

    public Guild? Guild { get; private set; }
    public long Id => Guild?.Id ?? 0;
    public long LeaderId => Guild?.LeaderCharacterId ?? 0;

    public GuildTable.Property Properties { get; private set; }
    private readonly CancellationTokenSource tokenSource;
    private List<(int Id, short Level)>? leftEffects;

    private readonly ILogger logger = Log.Logger.ForContext<GuildManager>();

    public GuildManager(GameSession session) {
        this.session = session;
        UpdateProperties();
        tokenSource = new CancellationTokenSource();

        if (session.Player.Value.Character.GuildId == 0) {
            return;
        }

        GuildInfoResponse response = session.World.GuildInfo(new GuildInfoRequest {
            GuildId = session.Player.Value.Character.GuildId,
        });
        if (response.Guild != null) {
            SetGuild(response.Guild, false);
        }
    }

    public void Dispose() {
        session.Dispose();
        tokenSource.Dispose();

        if (Guild != null) {
            foreach (GuildMember member in Guild.Members.Values) {
                member.Dispose();
            }
        }
    }

    public void Load() {
        if (Guild == null) {
            return;
        }

        session.Send(GuildPacket.Load(Guild));
    }

    // The expels the world kept while the player was offline; sent at login with or without a guild, then deleted.
    public void LoadExpelNotices() {
        using Maple2.Database.Storage.GameStorage.Request db = session.GameStorage.Context();
        IList<GuildExpelNotice> notices = db.TakeGuildExpelNotices(session.CharacterId);
        if (notices.Count > 0) {
            session.Send(GuildPacket.ExpelledOffline(notices));
        }
    }

    public bool SetGuild(GuildInfo info, bool addTag = true) {
        if (Guild != null) {
            return false;
        }

        GuildMember[] members = info.Members.Select(member => {
            if (!session.PlayerInfo.GetOrFetch(member.CharacterId, out PlayerInfo? playerInfo)) {
                return null;
            }

            var result = new GuildMember {
                GuildId = info.Id,
                Info = playerInfo.Clone(),
                Message = member.Message,
                Rank = (byte) member.Rank,
                JoinTime = member.JoinTime,
                CheckinTime = member.CheckinTime,
                DonationTime = member.DonationTime,
                WeeklyContribution = member.WeeklyContribution,
                TotalContribution = member.TotalContribution,
                DailyDonationCount = member.DailyDonationCount,
                PvpSeasonCount = member.PvpSeasonCount,
                PvpTotalCount = member.PvpTotalCount,
            };
            foreach ((int raidId, int value) in member.Raids) {
                result.Raids[raidId] = value;
            }
            return result;
        }).WhereNotNull().ToArray();

        GuildMember? leader = members.SingleOrDefault(member => member.Rank == 0);
        if (leader == null) {
            logger.Error("Guild {GuildId} does not have a valid leader", info.Id);
            session.Send(GuildPacket.Error(GuildError.s_guild_err_unknown));
            return false;
        }

        var guild = new Guild(info.Id, info.Name, leader) {
            Emblem = info.Emblem,
            Notice = info.Notice,
            CreationTime = info.CreationTime,
            Focus = (GuildFocus) info.Focus,
            Listed = info.Listed,
            Experience = info.Experience,
            Funds = info.Funds,
            HouseRank = info.HouseRank,
            HouseTheme = info.HouseTheme,
            Pvp = string.IsNullOrEmpty(info.Pvp) ? new GuildPvp() : JsonSerializer.Deserialize<GuildPvp>(info.Pvp) ?? new GuildPvp(),
            PvpState = (GuildPvpState) info.PvpState,
            Ranks = info.Ranks.Select((rank, i) => new GuildRank {
                Id = (byte) i,
                Name = rank.Name,
                Permission = (GuildPermission) rank.Permission,
            }).ToArray(),
            Buffs = info.Buffs.Select(buff => new GuildBuff {
                Id = buff.Id,
                Level = buff.Level,
                ExpiryTime = buff.ExpiryTime,
            }).ToList(),
            Posters = info.Posters.Select(poster => new GuildPoster {
                Id = poster.Id,
                Picture = poster.Picture,
                OwnerId = poster.OwnerId,
                OwnerName = poster.OwnerName,
                ResourceId = poster.ResourceId,
            }).ToList(),
            Npcs = info.Npcs.Select(npc => new GuildNpc {
                Type = (GuildNpcType) npc.Type,
                Level = npc.Level,
            }).ToList(),
        };
        // The client only asks to open an event its guild lists (CGuildInfo::FUN_1404d27b0), so every row
        // of the event table is listed, with the time of its last opening or 0.
        foreach (int eventId in session.TableMetadata.GuildTable.Events?.Keys.Order() ?? Enumerable.Empty<int>()) {
            int time = info.Events.FirstOrDefault(opened => opened.Id == eventId)?.Time ?? 0;
            guild.Events.Add(new GuildEvent(eventId, time));
        }
        foreach (GuildInfo.Types.StorageItem item in info.Storage) {
            guild.SetStored(item.ItemId, (short) item.Rarity, item.Amount);
        }
        foreach (GuildMember member in members) {
            if (guild.Members.TryAdd(member.CharacterId, member)) {
                BeginListen(member);
            }
        }

        Guild = guild;
        UpdateProperties();

        session.Player.Value.Character.GuildId = Guild.Id;
        session.Player.Value.Character.GuildName = Guild.Name;
        SendGuildUpdate();
        if (addTag) {
            session.Field?.Broadcast(GuildPacket.AddTag(session.PlayerName, Guild.Name));
        }

        session.ConditionUpdate(ConditionType.guild_join_req);
        return true;
    }

    // The skill effects of the guild this member left, not yet removed from the member. Field thread only.
    private void RemoveLeftEffects() {
        List<(int Id, short Level)>? effects = Interlocked.Exchange(ref leftEffects, null);
        if (effects == null) {
            return;
        }
        foreach ((int id, short _) in effects) {
            session.Player.Buffs.Remove(id, session.Player.ObjectId);
        }
    }

    public void RemoveGuild() {
        if (Guild == null) {
            return;
        }

        // The member who leaves, is expelled or whose guild is disbanded keeps none of the guild's skill
        // effects. The client has no step of its own for this: the effects it shows are the server's.
        // This can run on a World call's thread, so the removal is a task of the member's field; a member
        // who changes field before it runs loses the effects on the next field entry (LoadSkillBuffs).
        leftEffects = GuildRules.SkillEffects(Guild.Buffs, session.TableMetadata.GuildTable.Buffs);
        FieldManager? field = session.Field;
        if (field != null && leftEffects.Count > 0) {
            field.Scheduler.Schedule(() => {
                if (session.Field == field) {
                    RemoveLeftEffects();
                }
            });
        }

        Guild = null;
        session.Player.Value.Character.GuildId = 0;
        // A guild recall can no longer be answered out of the guild.
        session.CloseRecall(RecallScrollType.Guild);
        session.Player.Value.Character.GuildName = string.Empty;
        SendGuildUpdate();
        session.Field?.Broadcast(GuildPacket.RemoveTag(session.PlayerName));
    }

    // The World's copy of the player carries the guild to the party and other players' views; without this a
    // member who left or whose guild was disbanded kept the old guild there until the next login.
    private void SendGuildUpdate() {
        session.PlayerInfo.SendUpdate(new PlayerUpdateRequest {
            AccountId = session.AccountId,
            CharacterId = session.CharacterId,
            Async = true,
            GuildId = session.Player.Value.Character.GuildId,
            GuildName = session.Player.Value.Character.GuildName,
        });
    }

    public bool AddMember(string requestorName, GuildMember member) {
        if (Guild == null) {
            return false;
        }
        if (!Guild.Members.TryAdd(member.CharacterId, member)) {
            return false;
        }

        BeginListen(member);
        Guild.AchievementInfo += member.Info.AchievementInfo;
        session.Send(GuildPacket.Joined(requestorName, member));
        // KMS2 follows the join notice with the new member's data (Guild 32).
        session.Send(GuildPacket.UpdateMember(member.Info));
        return true;
    }

    public bool RemoveMember(long characterId, string requestorName = "") {
        if (Guild == null) {
            return false;
        }
        if (!Guild.Members.TryRemove(characterId, out GuildMember? member)) {
            return false;
        }
        Guild.AchievementInfo -= member.Info.AchievementInfo;
        EndListen(member);

        session.Send(string.IsNullOrEmpty(requestorName)
            ? GuildPacket.NotifyLeave(member.Name)
            : GuildPacket.NotifyExpelMember(requestorName, member.Name));
        // Nor one from a caller who left the guild.
        session.CloseRecall(RecallScrollType.Guild, characterId);
        return true;
    }

    public bool UpdateMemberRank(long requestorId, long characterId, byte rankId) {
        GuildMember? requestor = GetMember(requestorId);
        GuildMember? member = GetMember(characterId);
        if (requestor == null || member == null) {
            return false;
        }

        member.Rank = rankId;
        session.Send(GuildPacket.NotifyUpdateMemberRank(requestor.Name, member.Name, rankId));
        return true;
    }

    public bool UpdateMemberMessage(long characterId, string message) {
        GuildMember? member = GetMember(characterId);
        if (member == null) {
            return false;
        }

        member.Message = message;
        session.Send(GuildPacket.NotifyUpdateMemberMessage(member));
        return true;
    }

    // The weekly reset: this copy's members go to 0 and lose their guild raid records, and the client is told with
    // Guild 61 (which clears the client's raid records too).
    public void WeeklyReset() {
        if (Guild == null) {
            return;
        }

        GuildRules.ResetWeeklyContribution(Guild.Members.Values);
        GuildRules.ResetRaids(Guild.Members.Values);
        session.Send(GuildPacket.WeeklyContributionReset());
    }

    // Guild 37: a member's new value of a guild raid, from World.
    public bool UpdateMemberRaid(string name, int raidId, int value) {
        GuildMember? member = GetMember(name);
        if (member == null) {
            return false;
        }

        member.Raids[raidId] = value;
        session.Send(GuildPacket.MemberRaid(member.Name, raidId, value));
        return true;
    }

    // A guild raid clear of this player goes to World, which keeps the member's value and tells the guild. The
    // call runs off the caller's thread (the dungeon field's), so a slow World does not hold the field up.
    // Returns the call, null when the clear does not count.
    public Task? RaidClear(int dungeonRoomId) {
        if (Guild == null || session.TableMetadata.GuildTable.Raids?.ContainsKey(dungeonRoomId) != true) {
            return null;
        }

        var request = new GuildRequest {
            RequestorId = session.CharacterId,
            RaidClear = new GuildRequest.Types.RaidClear {
                GuildId = Guild.Id,
                DungeonRoomId = dungeonRoomId,
            },
        };
        return Task.Run(() => {
            try {
                session.World.Guild(request);
            } catch (Exception ex) {
                logger.Warning(ex, "Guild raid clear failed: {DungeonRoomId}", dungeonRoomId);
            }
        });
    }

    // Contribution of one kind (GuildContributionKind) this player earned outside the guild dialog (the guild vs game,
    // a guild quest) goes to World, which reads the kind's row, keeps the member's value and tells the guild; off the
    // caller's thread, as a raid clear. Null when nothing is added (no guild, or the kind has no row).
    public Task? AddContribution(string kind) {
        if (Guild == null || session.TableMetadata.GuildTable.ContributionOf(kind) <= 0) {
            return null;
        }

        var request = new GuildRequest {
            RequestorId = session.CharacterId,
            AddContribution = new GuildRequest.Types.AddContribution {
                GuildId = Guild.Id,
                Kind = kind,
            },
        };
        return Task.Run(() => {
            try {
                session.World.Guild(request);
            } catch (Exception ex) {
                logger.Warning(ex, "Guild contribution failed: {Kind}", kind);
            }
        });
    }

    public bool UpdateMemberContribution(long characterId, long checkInTime, int contribution) {
        GuildMember? member = GetMember(characterId);
        if (member == null) {
            return false;
        }

        member.WeeklyContribution += contribution;
        member.TotalContribution += contribution;

        session.Send(GuildPacket.GuildContribution(member, contribution));
        // Only a check-in sends its time; a contribution without one (the guild vs game's) leaves it.
        if (checkInTime > 0) {
            session.Send(GuildPacket.CheckInTime(member.Name, checkInTime));
        }
        return true;
    }

    public bool UpdateGuildExpFunds(long contributorId, int exp, int funds) {
        if (Guild == null) {
            return false;
        }

        long contributedExp = GuildRules.ContributedExp(session.CharacterId, contributorId, Guild.Experience, exp);
        int addExp = exp - Guild.Experience;
        int addFunds = funds - Guild.Funds;
        Guild.Experience = exp;
        Guild.Funds = funds;
        byte oldCapacity = Guild.Capacity;
        UpdateProperties();

        session.Send(GuildPacket.GuildExperience(Guild.Experience));
        session.Send(GuildPacket.GuildFunds(Guild.Funds));
        // A level up that raises the member cap: the client sets it, redraws the guild window and shows the new
        // cap (Guild 28, CGuildManager::FUN_1413c1840; it reads a name it does not use).
        if (Guild.Capacity > oldCapacity) {
            session.Send(GuildPacket.NotifyUpdateCapacity(string.Empty, Guild.Capacity));
        }
        if (session.CharacterId == contributorId) {
            session.Send(GuildPacket.AddContribution(addExp, addFunds));
        }
        if (contributedExp > 0) {
            session.ConditionUpdate(ConditionType.guild_exp, counter: contributedExp);
        }
        return true;
    }

    public bool UpdateLeader(long oldLeaderId, long newLeaderId) {
        if (Guild == null) {
            return false;
        }

        GuildMember? oldLeader = GetMember(oldLeaderId);
        if (oldLeader == null) {
            return false;
        }
        GuildMember? newLeader = GetMember(newLeaderId);
        if (newLeader == null) {
            return false;
        }

        oldLeader.Rank = 1; // Jr. Master
        newLeader.Rank = 0; // Master
        Guild.LeaderAccountId = newLeader.AccountId;
        Guild.LeaderCharacterId = newLeader.CharacterId;
        Guild.LeaderName = newLeader.Name;

        session.Send(GuildPacket.NotifyUpdateLeader(oldLeader.Name, newLeader.Name));
        return true;
    }

    // Guild 54 (CGuildManager::FUN_1413c3170): the client replaces the skill it holds with the one sent, whose
    // time is the time of use, and prints s_guild_notify_use_skill_user. The member gets the skill's effect
    // for its duration, on the thread of the field the member is in.
    public bool UseBuff(string requestorName, GuildBuff buff, int effectId, short effectLevel, int durationSeconds) {
        if (Guild == null) {
            return false;
        }

        for (int i = 0; i < Guild.Buffs.Count; i++) {
            if (Guild.Buffs[i].Id != buff.Id) {
                continue;
            }

            // The time of use is stored first: a member with no field now, or who leaves this field before
            // the task below runs, gets the effect from LoadSkillBuffs on the next field entry.
            Guild.Buffs[i] = buff;
            session.Send(GuildPacket.NotifyUseBuff(requestorName, buff));
            FieldManager? field = session.Field;
            if (field != null && effectId > 0) {
                field.Scheduler.Schedule(() => {
                    if (session.Field == field) {
                        session.Player.Buffs.AddBuff(session.Player, session.Player, effectId, effectLevel, field.FieldTick,
                            durationMs: (int) Math.Min(durationSeconds * 1000L, int.MaxValue));
                    }
                });
            }
            return true;
        }
        return false;
    }

    // Guild 91: a member buys a personal guild skill with meso and gets its effect for the row's duration.
    // The skill's level is the guild's. Nothing of the guild changes, so World is not asked.
    public GuildError UsePersonalBuff(int buffId) {
        FieldManager? field = session.Field;
        if (Guild == null || field == null) {
            return GuildError.s_guild_err_null_guild;
        }

        GuildTable.Buff? row = null;
        foreach (GuildBuff buff in Guild.Buffs) {
            if (buff.Id == buffId && session.TableMetadata.GuildTable.Buffs.TryGetValue(buffId, out IReadOnlyDictionary<short, GuildTable.Buff>? levels)) {
                levels.TryGetValue((short) buff.Level, out row);
                break;
            }
        }

        // An effect the server has no data for cannot be given: the row counts as missing, before any meso.
        if (row != null && (row.Id <= 0 || !field.SkillMetadata.TryGetEffect(row.Id, row.Level, out _))) {
            row = null;
        }

        UpdateProperties();
        bool hasEffect = row != null && session.Player.Buffs.HasBuff(row.Id, row.Level);
        GuildError error = GuildRules.PersonalSkillCharge(buffId, row, Properties.Level, session.Currency.Meso, hasEffect, out long charge);
        if (error != GuildError.none) {
            return error;
        }

        // Checked above against the balance, taken here in one step: a take on another thread in between
        // answers as the check would have.
        if (!session.Currency.TryTakeMeso(charge)) {
            return GuildError.s_guild_err_no_money;
        }
        session.Player.Buffs.AddBuff(session.Player, session.Player, row!.Id, row.Level, field.FieldTick,
            durationMs: (int) Math.Min(row.Duration * 60000L, int.MaxValue));
        session.Send(GuildPacket.UsePersonalBuff(buffId));
        return GuildError.none;
    }

    // The effects of the guild skills that are running, for the time they have left; called when the member
    // enters a field, so that a member who logs in, joins or changes field after the use has them too.
    public void LoadSkillBuffs() {
        RemoveLeftEffects();
        FieldManager? field = session.Field;
        if (Guild == null || field == null) {
            return;
        }

        bool isPvpMap = field.Metadata.Property.Type == MapType.Pvp;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach ((int id, short level, long remaining) in GuildRules.RunningSkillEffects(Guild.Buffs, session.TableMetadata.GuildTable.Buffs, now)) {
            if (!field.SkillMetadata.TryGetEffect(id, level, out AdditionalEffectMetadata? effect)
                || !GuildRules.GivesEffectOnEntry(isPvpMap, effect.Property.KeepOnEnterPvpZone, effect.Property.RemoveOnPvpZone)) {
                continue;
            }

            session.Player.Buffs.AddBuff(session.Player, session.Player, id, level, field.FieldTick,
                durationMs: (int) Math.Min(remaining * 1000L, int.MaxValue));
        }
    }

    // How long a guild recall offer stays open; as for the party recall, the server pack has no keep-time row for it.
    private const int RecallKeepSeconds = 300;

    /// <summary>
    /// The members a guild recall scroll calls, as the client counts them before it casts the recall
    /// (CGuildInfo::FUN_1404d3400, commonfiles/guilddef_1.cpp:2232): online and in a field that is not a tutorial.
    /// The caller is left out (the client takes one off its count).
    /// </summary>
    public static int CountRecallable(IEnumerable<GuildMember> members, long callerId, Func<int, bool> isTutorialMap) {
        return members.Count(member => member.CharacterId != callerId && member.Info.Online && !isTutorialMap(member.Info.MapId));
    }

    /// <summary>
    /// Offers a guild recall to the other members (s2c RecallScroll type 1 on their channels).
    /// False when there is no one to call, the field forbids recalls or the World refuses.
    /// </summary>
    public bool Recall() {
        if (Guild == null || session.Field == null || !session.Field.Metadata.CashCall.Recall) {
            return false;
        }
        bool IsTutorialMap(int mapId) => session.MapMetadata.TryGet(mapId, out MapMetadata? map) && map.Property.TutorialType != 0;
        if (CountRecallable(Guild.Members.Values, session.CharacterId, IsTutorialMap) <= 0) {
            return false;
        }

        try {
            GuildResponse response = session.World.Guild(new GuildRequest {
                RequestorId = session.CharacterId,
                Recall = new GuildRequest.Types.Recall {
                    GuildId = Guild.Id,
                    MapId = session.Field.MapId,
                    EndTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + RecallKeepSeconds,
                },
            });
            return response.Error == (int) GuildError.none;
        } catch (Grpc.Core.RpcException ex) {
            logger.Error(ex, "Guild recall by {CharacterId} failed", session.CharacterId);
            return false;
        }
    }

    /// <summary>
    /// A completed quest's guild reward (completeReward guildExp and guildFund, the guild quests 73000001 and on)
    /// for the guild; the World adds it and every member gets the new values (Guild 53), the player also the
    /// contribution notice (97). Nothing without a guild.
    /// </summary>
    // The guildquestrewardfactor.xml row of the guild's quest npc level (CGuildManager::GetNpcLevel(6) in
    // Unk_QuestRewardCalc), or null without a guild, a quest npc or a row for its level.
    public GuildTable.QuestRewardFactor? QuestRewardFactor() {
        if (Guild == null || session.TableMetadata.GuildTable.QuestRewardFactors == null) {
            return null;
        }
        int level = NpcLevel(GuildNpcType.Quest);
        return session.TableMetadata.GuildTable.QuestRewardFactors.TryGetValue((short) level, out GuildTable.QuestRewardFactor? factor) ? factor : null;
    }

    // CGuildManager::GetNpcLevel: the level of the guild's npc of that type, 0 without a guild or that npc.
    public int NpcLevel(GuildNpcType type) {
        if (Guild == null) {
            return 0;
        }
        foreach (GuildNpc npc in Guild.Npcs) {
            if (npc.Type == type) {
                return npc.Level;
            }
        }
        return 0;
    }

    // A guild shop buy paid from the guild fund: the world checks the master and the fund under the guild's lock,
    // and stores the product (amount above 0) in the guild storage in the same save.
    public GuildError SpendFund(int amount, RewardItem product = default) {
        if (Guild == null) {
            return GuildError.s_guild_err_null_guild;
        }

        try {
            GuildResponse response = session.World.Guild(new GuildRequest {
                RequestorId = session.CharacterId,
                SpendFund = new GuildRequest.Types.SpendFund {
                    GuildId = Guild.Id,
                    Amount = amount,
                    ItemId = product.ItemId,
                    Rarity = product.Rarity,
                    ItemAmount = product.Amount,
                },
            });
            return (GuildError) response.Error;
        } catch (Grpc.Core.RpcException ex) {
            logger.Error(ex, "Guild fund spend of {CharacterId} failed", session.CharacterId);
            return GuildError.s_guild_err_unknown;
        }
    }

    // Guild 109: the World checks and takes the stored item and sends it by mail. The error and, on success, the
    // receiver's name as the World knows it.
    public GuildError StorageGift(RewardItem item, string receiverName, out string receiver) {
        receiver = string.Empty;
        if (Guild == null) {
            return GuildError.s_guild_err_null_guild;
        }

        // Rolled here as a bag item is (stats, sockets, color); the World creates the mailed item with these rolls.
        Item? rolled = session.Field?.ItemDrop.CreateItem(item.ItemId, item.Rarity, item.Amount);
        byte[] rolls = rolled != null ? ItemRolls.Write(rolled) : [];
        try {
            GuildResponse response = session.World.Guild(new GuildRequest {
                RequestorId = session.CharacterId,
                StorageGift = new GuildRequest.Types.StorageGift {
                    GuildId = Guild.Id,
                    ItemId = item.ItemId,
                    Rarity = item.Rarity,
                    Amount = item.Amount,
                    ReceiverName = receiverName,
                    Rolls = ByteString.CopyFrom(rolls),
                },
            });
            receiver = response.ReceiverName;
            return (GuildError) response.Error;
        } catch (Grpc.Core.RpcException ex) {
            logger.Error(ex, "Guild storage gift of {CharacterId} failed", session.CharacterId);
            return GuildError.s_guild_err_unknown;
        }
    }

    // Exp and fund for the guild, added by the World: a quest's guild reward, a picked-up guild fund item.
    public void AddExpFund(long exp, long fund) {
        if (Guild == null || (exp <= 0 && fund <= 0)) {
            return;
        }

        try {
            session.World.Guild(new GuildRequest {
                RequestorId = session.CharacterId,
                AddExpFund = new GuildRequest.Types.AddExpFund {
                    GuildId = Guild.Id,
                    Exp = (int) Math.Clamp(exp, 0, int.MaxValue),
                    Fund = (int) Math.Clamp(fund, 0, int.MaxValue),
                },
            });
        } catch (Grpc.Core.RpcException ex) {
            logger.Error(ex, "Guild exp and fund of {CharacterId} failed", session.CharacterId);
        }
    }

    // The same off the caller's thread (a field's trigger): the exp and the fund the World added, null when it
    // refused or failed.
    public Task<(int Exp, int Fund)?>? AddExpFundAsync(long exp, long fund) {
        if (Guild == null || (exp <= 0 && fund <= 0)) {
            return null;
        }

        var request = new GuildRequest {
            RequestorId = session.CharacterId,
            AddExpFund = new GuildRequest.Types.AddExpFund {
                GuildId = Guild.Id,
                Exp = (int) Math.Clamp(exp, 0, int.MaxValue),
                Fund = (int) Math.Clamp(fund, 0, int.MaxValue),
            },
        };
        return Task.Run<(int Exp, int Fund)?>(() => {
            try {
                GuildResponse response = session.World.Guild(request);
                if (response.Error != 0) {
                    return null;
                }
                return (response.AddedExp, response.AddedFund);
            } catch (Exception ex) {
                logger.Warning(ex, "Guild exp and fund of {CharacterId} failed", session.CharacterId);
                return null;
            }
        });
    }

    // Guild 56 (CGuildManager::FUN_1413c3780, guildmanager.cpp:2533): the client copies the event over the
    // one it holds and prints s_guild_notify_open_event_user.
    public bool OpenEvent(string requestorName, GuildEvent opened) {
        if (Guild == null) {
            return false;
        }

        if (!Guild.SetEvent(opened)) {
            return false;
        }

        session.Send(GuildPacket.NotifyOpenEvent(requestorName, opened));
        return true;
    }

    // Guild 55 (CGuildManager::FUN_1413c3460): the client replaces the skill it holds with the one sent and
    // prints s_guild_notify_upgrade_skill.
    public bool UpgradeBuff(string requestorName, GuildBuff buff) {
        if (Guild == null) {
            return false;
        }

        for (int i = 0; i < Guild.Buffs.Count; i++) {
            if (Guild.Buffs[i].Id == buff.Id) {
                Guild.Buffs[i] = buff;
                session.Send(GuildPacket.UpgradeBuff(requestorName, buff));
                return true;
            }
        }
        return false;
    }

    // Guild 59 (CGuildManager::FUN_1413c3ef0): the client adds or replaces the NPC of that kind and prints
    // s_guild_notify_upgrade_npc.
    public bool UpgradeNpc(string requestorName, GuildNpc npc) {
        if (Guild == null) {
            return false;
        }

        bool found = false;
        for (int i = 0; i < Guild.Npcs.Count; i++) {
            if (Guild.Npcs[i].Type == npc.Type) {
                Guild.Npcs[i] = npc;
                found = true;
                break;
            }
        }
        if (!found) {
            Guild.Npcs.Add(npc);
        }
        session.Send(GuildPacket.UpgradeNpc(requestorName, npc));
        return true;
    }

    // Guild 57 (CGuildManager::FUN_1413c39e0): the client sets the house level and theme it holds and prints
    // s_guild_notify_house_upgrade.
    public bool UpdateHouse(string requestorName, int rank, int theme) {
        if (Guild == null) {
            return false;
        }

        Guild.HouseRank = rank;
        Guild.HouseTheme = theme;
        session.Send(GuildPacket.UpgradeHouse(requestorName, rank, theme));
        return true;
    }

    public bool UpdateNotice(string requestorName, string message) {
        if (Guild == null) {
            return false;
        }

        Guild.Notice = message;
        session.Send(GuildPacket.NotifyUpdateNotice(requestorName, 1, message));
        return true;
    }

    public bool UpdateRank(string requestorName, int rankId, string name, GuildPermission permission) {
        GuildRank? rank = Guild?.Ranks.FirstOrDefault(r => r.Id == rankId);
        if (rank == null) {
            return false;
        }

        rank.Name = name;
        rank.Permission = permission;
        // CGuildManager::OnPacketGradeUpdated: who changed it, then the rank; it refreshes the members of that rank.
        session.Send(GuildPacket.NotifyUpdateRank(new InterfaceText(requestorName), rank));
        return true;
    }

    public bool UpdateFocus(string requestorName, bool listed, GuildFocus focus) {
        if (Guild == null) {
            return false;
        }

        Guild.Listed = listed;
        Guild.Focus = focus;
        // CGuildManager::FUN_1413c1c50: who changed it, the listed byte, the focus. It only stores the two.
        session.Send(GuildPacket.NotifyUpdateFocus(requestorName, listed, focus));
        return true;
    }

    // The guild's new name (a rename ticket). Guild 34 (CGuildManager::OnPacketGuildRenamed) stores it, shows
    // s_guild_notify_change_name and renames the own character's tag; the field is sent this member's tag (Guild 77)
    // for everyone else, and the World's player info gets the name.
    public bool Rename(string name) {
        if (Guild == null) {
            return false;
        }

        Guild.Name = name;
        session.Player.Value.Character.GuildName = name;
        session.Send(GuildPacket.UpdateName(name));
        session.Field?.Broadcast(GuildPacket.AddTag(session.PlayerName, name), session);
        session.PlayerInfo.SendUpdate(new PlayerUpdateRequest {
            AccountId = session.AccountId,
            CharacterId = session.CharacterId,
            Async = true,
            GuildName = name,
        });
        return true;
    }

    public bool UpdateEmblem(string requestorName, string emblem) {
        if (Guild == null) {
            return false;
        }

        Guild.Emblem = emblem;
        session.Send(GuildPacket.UpdateEmblem(Guild.Emblem));
        session.Send(GuildPacket.NotifyUpdateEmblem(requestorName, emblem));
        return true;
    }

    public bool AddOrUpdatePoster(GuildPoster poster) {
        if (Guild == null) {
            return false;
        }

        GuildPoster? oldPoster = Guild.Posters.FirstOrDefault(p => p.Id == poster.Id);
        if (oldPoster is not null) {
            Guild.Posters.Remove(oldPoster);
        }

        Guild.Posters.Add(poster);
        session.Send(GuildPacket.UpdatePoster(poster));
        return true;
    }

    public bool HasPermission(long characterId, GuildPermission permission) {
        GuildRank? rank = GetRank(characterId);
        return rank != null && rank.Permission.HasFlag(permission);
    }

    public GuildRank? GetRank(long characterId) {
        if (Guild == null || !Guild.Members.TryGetValue(characterId, out GuildMember? member)) {
            return null;
        }

        return Guild.Ranks.ElementAtOrDefault(member.Rank);
    }

    public GuildMember? GetMember(string name) {
        return Guild?.Members.Values.FirstOrDefault(member => member.Name == name);
    }

    public GuildMember? GetMember(long characterId) {
        if (Guild?.Members.TryGetValue(characterId, out GuildMember? member) == true) {
            return member;
        }

        return null;
    }

    [MemberNotNull(nameof(Properties))]
    private void UpdateProperties() {
        int experience = Guild?.Experience ?? 0;
        Properties = GuildRules.PropertyAt(session.TableMetadata.GuildTable.Properties, experience)!;

        if (Guild != null) {
            Guild.Capacity = Properties.Capacity;
        }
    }

    #region PlayerInfo Events
    private void BeginListen(GuildMember member) {
        // Clean up previous token if necessary
        if (member.TokenSource != null) {
            logger.Warning("BeginListen called on Member {Id} that was already listening", member.CharacterId);
            EndListen(member);
        }

        member.TokenSource = CancellationTokenSource.CreateLinkedTokenSource(tokenSource.Token);
        CancellationToken token = member.TokenSource.Token;
        var listener = new PlayerInfoListener(UpdateField.Guild, (type, info) => SyncUpdate(token, member.CharacterId, type, info));
        session.PlayerInfo.Listen(member.Info.CharacterId, listener);
    }

    private void EndListen(GuildMember member) {
        member.TokenSource?.Cancel();
        member.TokenSource?.Dispose();
        member.TokenSource = null;
    }

    private bool SyncUpdate(CancellationToken cancel, long id, UpdateField type, IPlayerInfo info) {
        if (cancel.IsCancellationRequested || Guild == null || !Guild.Members.TryGetValue(id, out GuildMember? member)) {
            return true;
        }

        bool wasOnline = member.Info.Online;
        string name = member.Info.Name;
        member.Info.Update(type, info);

        if (name != member.Info.Name) {
            session.Send(GuildPacket.UpdateMemberName(name, member.Name));
        }

        // The world sends one notice for all that changed in its sync step, so a field change often comes with
        // fields the guild does not list (health): only the listed ones choose the packet.
        type &= UpdateField.Guild;
        if (type == UpdateField.Map) {
            // KMS2 sends a field change alone, by the member's name (Guild 31); the client redraws the member's row.
            session.Send(GuildPacket.UpdateMemberMap(member.Name, member.Info.MapId));
            logger.Debug("Guild {GuildId}: member {MemberId} map {MapId} sent to {CharacterId}", Guild.Id, id, member.Info.MapId, session.CharacterId);
        } else if (type == UpdateField.GearScore) {
            // KMS2 sends a gear score change alone, by the member's name.
            session.Send(GuildPacket.UpdateMemberGearScore(member.Name, member.Info.GearScore));
        } else {
            session.Send(GuildPacket.UpdateMember(member.Info));
        }

        if (member.Info.Online != wasOnline) {
            session.Send(member.Info.Online
                ? GuildPacket.NotifyLogin(member.Name)
                : GuildPacket.NotifyLogout(member.Name, member.Info.LastOnlineTime));
        }
        return false;
    }
    #endregion
}
