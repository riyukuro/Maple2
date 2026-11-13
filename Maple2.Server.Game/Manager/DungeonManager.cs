using System.Net;
using Grpc.Core;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Game.Event;
using Maple2.Model.Game.Party;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Server.World.Service;
using Maple2.Tools.Extensions;
using Serilog;
using MigrationType = Maple2.Server.World.Service.MigrationType;

namespace Maple2.Server.Game.Manager;

public class DungeonManager {
    private readonly GameSession session;

    public IDictionary<int, DungeonRecord> Records { get; set; }
    private IDictionary<int, DungeonEnterLimit> EnterLimits => session.Player.Value.Character.DungeonEnterLimits;
    private IDictionary<int, DungeonRankReward> RankRewards => session.Player.Value.Unlock.DungeonRankRewards;

    public DungeonFieldManager? Lobby { get; private set; }
    public DungeonRoomMetadata? Metadata;
    public DungeonRoomRecord? DungeonRoomRecord => Lobby?.DungeonRoomRecord;
    public DungeonUserRecord? UserRecord;
    private int LobbyRoomId { get; set; }

    private Party? Party => session.Party.Party;

    private readonly ILogger logger = Log.Logger.ForContext<DungeonManager>();

    // When this session began: the client's kept open periods are the events it has seen since.
    private readonly DateTime loginTime = DateTime.Now;

    public DungeonManager(GameSession session) {
        this.session = session;
        Records = new Dictionary<int, DungeonRecord>();
        Init();
    }

    private void Init() {
        using GameStorage.Request db = session.GameStorage.Context();
        Records = db.GetDungeonRecords(session.CharacterId);

        foreach (DungeonRoomMetadata metadata in session.TableMetadata.DungeonRoomTable.Entries.Values) {
            if (!Records.TryGetValue(metadata.Id, out DungeonRecord? record)) {
                record = new DungeonRecord(metadata.Id);
                record = db.CreateDungeonRecord(record, session.CharacterId);
                if (record == null) {
                    logger.Error("Failed to create dungeon record for dungeonId {dungeonId}", metadata.Id);
                    continue;
                }
                Records.Add(metadata.Id, record);
            }
            // A period end written by an older rule is healed before the client or a script reads it.
            DungeonRoomRules.HealCooldown(record, metadata.CooldownType, metadata.CooldownValue, DateTime.Now);

            DungeonEnterLimit limit = GetEnterLimit(metadata);
            EnterLimits[metadata.Id] = limit;
        }
    }

    public void Load() {
        MailSeasonRewards();
        session.Send(DungeonRoomPacket.Load(Records));
        session.Send(DungeonRoomPacket.RankRewards(RankRewards));
        session.Send(FieldEntrancePacket.Load(EnterLimits));
    }

    public void ResetDailyClears() {
        foreach (DungeonRecord record in Records.Values) {
            record.UnionSubClears = 0;
        }
        using GameStorage.Request db = session.GameStorage.Context();
        db.SaveDungeonRecords(session.CharacterId, Records.Values.ToArray());
        session.Send(DungeonRoomPacket.Load(Records));
    }

    // The weekly season reward. When a season week has ended with scores (DungeonScoreRules.FinishedWeeks), the
    // week's total (SeasonTotal) picks the season's reward (DungeonSeason::GetReward), mailed with the season's
    // systemMailID; the week's scores are then set to 0 (the client shows CurrentRecord only before its expiry, so
    // nothing it shows changes). Run at login and at the weekly reset; a week that ended while the player was
    // offline is mailed at the next login. The season of a week is the one holding the second before its end.
    // A week whose reward item cannot be made keeps its scores and is tried again.
    public void MailSeasonRewards() {
        FieldManager? field = session.Field;
        if (field == null) {
            return;
        }
        int minLevel = session.ServerTableMetadata.ConstantsTable.DungeonSeasonRankMinLevel;
        IEnumerable<DungeonRecord> seasonRecords = Records.Values.Where(record =>
            session.TableMetadata.DungeonRoomTable.Entries.TryGetValue(record.DungeonId, out DungeonRoomMetadata? metadata)
            && DungeonScoreRules.IsSeasonRankRoom(metadata.GroupType, metadata.Limit.MinLevel, minLevel));
        List<IGrouping<long, DungeonRecord>> weeks = DungeonScoreRules.FinishedWeeks(seasonRecords, DateTime.Now.ToEpochSeconds()).ToList();
        if (weeks.Count == 0) {
            return;
        }

        bool changed = false;
        foreach (IGrouping<long, DungeonRecord> week in weeks) {
            DungeonSeasonTable.Entry? season = session.TableMetadata.DungeonSeasonTable.Current(week.Key.FromEpochSeconds().AddSeconds(-1));
            if (season != null) {
                int total = DungeonScoreRules.SeasonTotal(week.Select(record => record.CurrentRecord), season.MaxSeasonScore);
                DungeonSeasonTable.Reward? reward = season.GetReward(total);
                if (reward != null && reward.ItemId != 0 && !MailSeasonReward(field, season, reward)) {
                    continue;
                }
            }
            foreach (DungeonRecord record in week) {
                record.CurrentRecord = 0;
            }
            changed = true;
        }
        if (changed) {
            using GameStorage.Request db = session.GameStorage.Context();
            db.SaveDungeonRecords(session.CharacterId, Records.Values.ToArray());
        }
    }

    private bool MailSeasonReward(FieldManager field, DungeonSeasonTable.Entry season, DungeonSeasonTable.Reward reward) {
        Item? item = field.ItemDrop.CreateItem(reward.ItemId, reward.Grade, reward.Count);
        if (item == null) {
            logger.Warning("Dungeon season {SeasonId} reward item {ItemId} could not be made", season.SeasonId, reward.ItemId);
            return false;
        }
        using GameStorage.Request db = session.GameStorage.Context();
        Mail? mail = db.CreateMail(new Mail {
            Type = MailType.System,
            ReceiverId = session.CharacterId,
            Content = season.SystemMailId.ToString(), // an id of string/systemmailcontent
        });
        if (mail == null) {
            return false;
        }
        Item? added = db.CreateItem(mail.Id, item);
        if (added == null) {
            logger.Error("Dungeon season {SeasonId} reward item {ItemId} could not be added to mail {MailId}", season.SeasonId, reward.ItemId, mail.Id);
            return true; // the mail exists; a retry would send a second one
        }
        mail.Items.Add(added);
        try {
            session.World.MailNotification(new MailNotificationRequest {
                CharacterId = session.CharacterId,
                MailId = mail.Id,
            });
        } catch { /* ignored */ }
        return true;
    }

    public void ResetWeeklyClears() {
        MailSeasonRewards();
        // Each record by its own period (PeriodOver), not the world's weekly day: one still in its period keeps its
        // counts. The bought extra counts expire with the record's period (DungeonRecordCli::GetBonusCountNow).
        long now = DateTime.Now.ToEpochSeconds();
        foreach (DungeonRecord record in Records.Values.Where(record => DungeonRoomRules.PeriodOver(record, now))) {
            record.UnionClears = 0;
            record.ExtraSubClears = 0;
        }
        using GameStorage.Request db = session.GameStorage.Context();
        db.SaveDungeonRecords(session.CharacterId, Records.Values.ToArray());
        session.Send(DungeonRoomPacket.Load(Records));
    }

    public void LoadField() {
        if (Lobby == null || session.Field is not DungeonFieldManager) {
            return;
        }

        // The client keeps the count in a global that no other packet resets, so every dungeon field gets it.
        session.Send(RevivalPacket.DungeonReviveCount(UserRecord?.RevivalCount ?? 0));

        if (session.Field.FieldInstance.Type == InstanceType.DungeonLobby) {
            session.Send(DungeonWaitingPacket.Set(Lobby.DungeonId, Lobby.Size));
        } else if (Metadata != null) {
            // The client makes its mission list (RoomStageDungeonCli) only on RoomStageDungeon mode 0 and
            // registers the DungeonMission handler there. It drops them only on mode 1 (LeaveStage).
            session.Send(RoomStageDungeonPacket.Set(Metadata.Id));
            if (UserRecord != null && UserRecord.Missions.Count > 0) {
                session.Send(DungeonMissionPacket.Load(UserRecord.Missions));
            }
            // The new stage starts without the give-up flag and with stage 0; a player who enters after the
            // script set the flag, or after the result, gets them here.
            if (session.Field is DungeonFieldManager { GiveUpEnabled: true }) {
                session.Send(DungeonMissionPacket.SetAbandon(true));
            }
            if (Lobby.DungeonRoomRecord.State != DungeonState.None) {
                session.Send(DungeonMissionPacket.Stage(1));
            }
        }
    }

    // The client keeps the dungeon stage (mission list, the party window's give-up state, the quest guide's
    // field mission skip) until RoomStageDungeon mode 1; a field change alone does not clear it. KMS2 sends it
    // as the player leaves a dungeon room field. A move to another room field of the dungeon keeps the stage.
    public void LeaveStage() {
        if (session.Field is DungeonFieldManager field && field.FieldInstance.Type != InstanceType.DungeonLobby) {
            session.Send(RoomStageDungeonPacket.Clear());
        }
    }

    // The room's defaultRevivalLimitCount: 0 means no limit (UIRevivalDialog::GetFirstButtonLabel).
    public bool HasDefaultRevivalLeft() {
        if (session.Field is not DungeonFieldManager || Metadata == null || UserRecord == null) {
            return true;
        }
        return Metadata.Limit.MaxRevivalCount <= 0 || UserRecord.RevivalCount < Metadata.Limit.MaxRevivalCount;
    }

    public void CountDefaultRevival() {
        if (UserRecord == null) {
            return;
        }
        UserRecord.RevivalCount++;
        session.Send(RevivalPacket.DungeonReviveCount(UserRecord.RevivalCount));
    }

    public void UpdateDungeonEnterLimit() {
        bool updated = false;
        foreach (int dungeonId in EnterLimits.Keys) {
            if (!session.TableMetadata.DungeonRoomTable.Entries.TryGetValue(dungeonId, out DungeonRoomMetadata? metadata)) {
                continue;
            }

            DungeonEnterLimit newLimit = GetEnterLimit(metadata);
            if (EnterLimits[dungeonId] != newLimit) {
                EnterLimits[dungeonId] = newLimit;
                updated = true;
            }
        }

        if (updated) {
            session.PlayerInfo.SendUpdate(new PlayerUpdateRequest {
                AccountId = session.AccountId,
                CharacterId = session.CharacterId,
                DungeonEnterLimits = {
                    EnterLimits.Select(dungeon => new DungeonEnterLimitUpdate {
                        DungeonId = dungeon.Key,
                        Limit = (int) dungeon.Value,
                    })
                },
                Async = true,
            });
            session.Send(FieldEntrancePacket.Load(EnterLimits));
        }
    }

    private DungeonEnterLimit GetEnterLimit(DungeonRoomMetadata metadata) {
        DungeonRoomLimitMetadata limitMetadataMetadata = metadata.Limit;
        if (limitMetadataMetadata.MinLevel > session.Player.Value.Character.Level) {
            return DungeonEnterLimit.MinLevel;
        }

        if (limitMetadataMetadata.RequiredAchievementId > 0 && !session.Achievement.HasAchievement(limitMetadataMetadata.RequiredAchievementId)) {
            return DungeonEnterLimit.Achievement;
        }

        if (limitMetadataMetadata.VipOnly && !session.Config.IsPremiumClubActive()) {
            return DungeonEnterLimit.Vip;
        }

        if (limitMetadataMetadata.GearScore > 0 && session.Stats.Values.GearScore < limitMetadataMetadata.GearScore) {
            return DungeonEnterLimit.Gearscore;
        }

        if (limitMetadataMetadata.ClearDungeonIds.Length > 0) {
            foreach (int clearDungeonId in limitMetadataMetadata.ClearDungeonIds) {
                DungeonRecord clearDungeonRecord = GetRecord(clearDungeonId);
                if (clearDungeonRecord.ClearTimestamp == 0) {
                    return DungeonEnterLimit.DungeonClear;
                }
            }
        }

        if (limitMetadataMetadata.Buffs.Length > 0 && limitMetadataMetadata.Buffs.Any(buffId => !session.Buffs.HasBuff(buffId))) {
            return DungeonEnterLimit.Buff;
        }

        if (limitMetadataMetadata.EquippedRecommendedWeapon) {
            if (!session.Item.Equips.Gear.TryGetValue(EquipSlot.RH, out Item? item)) {
                return DungeonEnterLimit.RecommendedWeapon;
            }
            switch (session.Player.Value.Character.Job.Code()) {
                case JobCode.Newbie:
                    if (!item.Type.IsBludgeon) {
                        return DungeonEnterLimit.RecommendedWeapon;
                    }
                    break;
                case JobCode.Knight:
                    if (!item.Type.IsLongsword) {
                        return DungeonEnterLimit.RecommendedWeapon;
                    }
                    break;
                case JobCode.Berserker:
                    if (!item.Type.IsGreatsword) {
                        return DungeonEnterLimit.RecommendedWeapon;
                    }
                    break;
                case JobCode.Wizard:
                    if (!item.Type.IsStaff) {
                        return DungeonEnterLimit.RecommendedWeapon;
                    }
                    break;
                case JobCode.Priest:
                    if (!item.Type.IsScepter) {
                        return DungeonEnterLimit.RecommendedWeapon;
                    }
                    break;
                case JobCode.Archer:
                    if (!item.Type.IsBow) {
                        return DungeonEnterLimit.RecommendedWeapon;
                    }
                    break;
                case JobCode.HeavyGunner:
                    if (!item.Type.IsCannon) {
                        return DungeonEnterLimit.RecommendedWeapon;
                    }
                    break;
                case JobCode.Assassin:
                    if (!item.Type.IsThrowingStar) {
                        return DungeonEnterLimit.RecommendedWeapon;
                    }
                    break;
                case JobCode.Thief:
                    if (!item.Type.IsDagger) {
                        return DungeonEnterLimit.RecommendedWeapon;
                    }
                    break;
                case JobCode.RuneBlader:
                    if (!item.Type.IsBlade) {
                        return DungeonEnterLimit.RecommendedWeapon;
                    }
                    break;
                case JobCode.Striker:
                    if (!item.Type.IsKnuckle) {
                        return DungeonEnterLimit.RecommendedWeapon;
                    }
                    break;
                case JobCode.SoulBinder:
                    if (!item.Type.IsOrb) {
                        return DungeonEnterLimit.RecommendedWeapon;
                    }
                    break;
            }
        }

        if (GetRecord(metadata.Id).Flag.HasFlag(DungeonRecordFlag.Veteran)) {
            return DungeonEnterLimit.Veteran;
        }

        return DungeonEnterLimit.Rookie;
    }

    public void SetDungeon(DungeonFieldManager field) {
        Lobby = field;
        LobbyRoomId = field.RoomId;
        Metadata = field.DungeonMetadata;
        // One record per player and room: a player who leaves and enters the room again keeps the record,
        // and with it the revivals already used in this run.
        UserRecord = field.DungeonRoomRecord.UserResults.GetOrAdd(session.CharacterId,
            _ => new DungeonUserRecord(field.DungeonId, session.CharacterId));

        field.DungeonRoomRecord.SetFirstUser(session.CharacterId);
        FillMissions(UserRecord, Metadata.PartyMissions, Metadata.UserMissions, session.TableMetadata.DungeonMissionTable.Missions);
    }

    /// <summary>
    /// A new record gets the room's missions in the client's order (DungeonRoom::GetMissions: the party
    /// missions, then the user missions). Ids without a mission row are skipped, as the client does.
    /// </summary>
    public static void FillMissions(DungeonUserRecord record, int[] partyMissions, int[] userMissions, IReadOnlyDictionary<int, DungeonMissionMetadata> table) {
        if (record.Missions.Count > 0) {
            return;
        }
        foreach (int missionId in partyMissions.Concat(userMissions)) {
            if (table.TryGetValue(missionId, out DungeonMissionMetadata? mission)) {
                record.Missions.TryAdd(missionId, new DungeonMission(mission, userMissions.Contains(missionId)));
            }
        }
    }

    // The open hours the running DungeonEnterTimeLimit events give.
    private IEnumerable<DungeonEnterTime> EnterTimes() {
        return session.FindEvent(GameEventType.DungeonEnterTimeLimit)
            .SelectMany(gameEvent => gameEvent.Metadata.Data is DungeonEnterTimeLimit limit ? limit.Times : []);
    }

    // Why the room is closed now, null when it is open: its days of the week, its hours and its open period. Every
    // way into a new room asks this: the room request, the dungeon match and party search (the rooms searched and
    // the room the match makes).
    public DungeonRoomError? ClosedError(DungeonRoomMetadata metadata) {
        DateTime now = DateTime.Now;
        // The server's local day, as the cooldowns use it.
        if (!DungeonRoomRules.OpenOnDay(metadata.Limit.DayOfWeeks, now.DayOfWeek)) {
            return DungeonRoomError.s_room_dungeon_canEnterDayOfWeeks;
        }
        if (!DungeonRoomRules.OpenAtTime(metadata.Limit.IsLimitTime, metadata.Id, EnterTimes(), now.TimeOfDay)) {
            return DungeonRoomError.s_room_dungeon_NotAllowTime;
        }
        return DungeonRoomRules.OpenPeriodError(metadata.Limit.OpenPeriodDate, metadata.Limit.OpenPeriodTag,
            DungeonRoomRules.OpenPeriodTags(session.OpenPeriods, loginTime, now), now);
    }

    public void CreateDungeonRoom(int dungeonId, bool withParty, bool useTicket = false) {
        int size = 1;
        if (!session.TableMetadata.DungeonRoomTable.Entries.TryGetValue(dungeonId, out DungeonRoomMetadata? metadata)) {
            session.Send(DungeonRoomPacket.Error(DungeonRoomError.s_room_dungeon_notOpenTimeDungeon));
            return;
        }

        if (EnterLimits.TryGetValue(dungeonId, out DungeonEnterLimit limit) && limit != DungeonEnterLimit.Veteran && limit != DungeonEnterLimit.Rookie) {
            session.Send(FieldEntrancePacket.Error(dungeonId, limit));
            return;
        }

        DungeonRoomError? closedError = ClosedError(metadata);
        if (closedError != null) {
            session.Send(DungeonRoomPacket.Error(closedError.Value));
            return;
        }

        // A limitEnter room is entered only while its reward record has a count left. The client's OnError 4
        // reads the room id and shows that room's record expire (dungeonrecordcli.cpp:338).
        // With useTicket (UIDungeonRoomInfo::AskExtraTicketEnter, uidungeonroomdialog.cpp:1675: the confirmed box of
        // a room with an extra reward ticket and no count left) one extra reward count is bought first, as mode 8
        // buys it; a refused purchase gets that purchase's error and no room.
        if (metadata.LimitEnter) {
            (DungeonRoomMetadata rewardRoom, int rewardId) = RewardRoom(metadata);
            Records.TryGetValue(rewardId, out DungeonRecord? rewardRecord);
            if (!DungeonRoomRules.HasCountLeft(rewardRecord, rewardRoom.Reward.Count, rewardRoom.Reward.SubRewardCount, DateTime.Now.ToEpochSeconds(), ExtraRewardCount(rewardId))) {
                if (!useTicket) {
                    session.Send(DungeonRoomPacket.Error(DungeonRoomError.s_room_party_err_cooldown, metadata.Id));
                    return;
                }
                if (!BuyExtraRewardCount(rewardRoom, rewardId)) {
                    return;
                }
            }
        }

        if (withParty) {
            if (Party == null) {
                session.Send(DungeonRoomPacket.Error(DungeonRoomError.s_room_dungeon_error_invalidPartyOID));
                return;
            }
            if (Party.LeaderCharacterId != session.CharacterId) {
                session.Send(DungeonRoomPacket.Error(DungeonRoomError.s_room_party_err_not_chief));
                return;
            }
            size = Party.Members.Count;
            // Each member's enter limit, as the client's room window shows it; the reply names the room and limit.
            DungeonEnterLimit? blocking = DungeonRoomRules.PartyBlockingLimit(
                Party.Members.Values.OrderBy(member => member.JoinTime).Select(member => (IReadOnlyDictionary<int, DungeonEnterLimit>) member.Info.DungeonEnterLimits), dungeonId);
            if (blocking != null) {
                session.Send(FieldEntrancePacket.Error(dungeonId, blocking.Value));
                return;
            }
        }

        // The client's enter buttons: solo only without a minimum user count, party only up to the maximum.
        switch (DungeonRoomRules.CanCreate(withParty, size, metadata.Limit.MinUserCount, metadata.Limit.MaxUserCount, GmCheck.IsGm(session.Player.AdminPermissions))) {
            case DungeonRoomRules.CreateVerdict.UnderMinUserCount:
                session.Send(DungeonRoomPacket.Error(DungeonRoomError.s_room_dungeon_UnderMinUserCount, metadata.Limit.MinUserCount));
                return;
            case DungeonRoomRules.CreateVerdict.OverMaxUserCount:
                session.Send(DungeonRoomPacket.Error(DungeonRoomError.s_room_dungeon_OverMaxUserCount, metadata.Limit.MaxUserCount));
                return;
        }

        if (withParty && Party!.DungeonMatchKind != 0) {
            // The party searches (dungeon match): no room of the leader's own (rv44 MINOR 4); World refuses it too.
            session.Send(PartyPacket.Error(PartyError.s_party_err_dungeon_match_another));
            return;
        }

        try {
            FieldResponse response = session.World.Field(new FieldRequest {
                CreateDungeon = new FieldRequest.Types.CreateDungeon {
                    DungeonId = dungeonId,
                    Size = size,
                    PartyId = session.Party.Id,
                },
            });
            if ((MigrationError) response.Error != MigrationError.ok) {
                session.Send(MigrationPacket.GameToGameError((MigrationError) response.Error));
                return;
            }

            LobbyRoomId = response.RoomId;
        } catch (RpcException) {
            session.Send(MigrationPacket.GameToGameError(MigrationError.s_move_err_no_server));
            return;
        }

        if (withParty) {
            var request = new PartyRequest {
                RequestorId = session.CharacterId,
                SetDungeon = new PartyRequest.Types.SetDungeon {
                    PartyId = Party!.Id,
                    DungeonId = dungeonId,
                    DungeonRoomId = LobbyRoomId,
                    Set = true,
                    MaxUsers = metadata.Limit.MaxUserCount,
                },
            };

            PartyError error = PartyError.none;
            try {
                error = (PartyError) session.World.Party(request).Error;
            } catch (RpcException) { }
            if (error == PartyError.s_party_err_dungeon_match_another) {
                // World refused the room (the party searches or is matched): the room made for it goes.
                try {
                    session.World.Field(new FieldRequest {
                        DestroyDungeon = new FieldRequest.Types.DestroyDungeon { RoomId = LobbyRoomId },
                    });
                } catch (RpcException) { }
                LobbyRoomId = 0;
                session.Send(PartyPacket.Error(error));
                return;
            }
        }

        Metadata = metadata;
        MigrateToDungeon();
    }

    public void EnterLobby() {
        if (Party == null) {
            session.Send(MigrationPacket.GameToGameError(MigrationError.s_move_err_NotFoundParty));
            return;
        }

        // The room is closed some time after everyone left it, and the party panel keeps its Enter Dungeon button.
        // Ask before the move: once the client is told to move it leaves the field for the loading screen.
        MigrationError error;
        try {
            error = (MigrationError) session.World.Field(new FieldRequest {
                RequesterId = session.CharacterId,
                CheckDungeon = new FieldRequest.Types.CheckDungeon {
                    RoomId = LobbyRoomId,
                },
            }).Error;
        } catch (RpcException) {
            error = MigrationError.s_move_err_no_server;
        }
        if (error != MigrationError.ok) {
            if (error == MigrationError.s_move_err_dungeon_not_exist_2 && !FieldEntryRules.ClearsPartyDungeon(LobbyRoomId, Party.DungeonLobbyRoomId)) {
                // This session held an older room id; take over what the party has now.
                SetDungeon(Party.DungeonId, Party.DungeonLobbyRoomId, Party.DungeonSet);
            } else if (error == MigrationError.s_move_err_dungeon_not_exist_2) {
                try {
                    session.World.Party(new PartyRequest {
                        RequestorId = session.CharacterId,
                        SetDungeon = new PartyRequest.Types.SetDungeon {
                            PartyId = Party.Id,
                            DungeonId = 0,
                            DungeonRoomId = 0,
                            Set = false,
                        },
                    });
                } catch (RpcException) { }
                SetDungeon(0, 0, false);
            }
            session.Send(MigrationPacket.GameToGameError(error));
            return;
        }

        MigrateToDungeon();
    }

    public void EnterInitField() {
        if (Lobby == null || Metadata == null || Lobby.RoomFields.IsEmpty) {
            logger.Error("Field is null, cannot enter dungeon");
            return;
        }

        if (!Lobby.RoomFields.TryGetValue(Metadata.FieldIds[0], out DungeonFieldManager? firstField)) {
            logger.Error("First field is null, cannot enter dungeon");
            return;
        }

        if (firstField.DungeonRoomRecord.StartTick == 0) {
            // The clock every FieldTick reads; Lobby.FieldTick stops while the lobby is empty.
            firstField.DungeonRoomRecord.StartTick = Environment.TickCount64;
        }
        session.Send(session.PrepareField(firstField.MapId, roomId: firstField.RoomId) ? FieldEnterPacket.Request(session.Player) :
            FieldEnterPacket.Error(MigrationError.s_move_err_default));
    }

    public void SetDungeon(int dungeonId, int roomId, bool set) {
        if (set) {
            if (!session.TableMetadata.DungeonRoomTable.Entries.TryGetValue(dungeonId, out DungeonRoomMetadata? metadata)) {
                logger.Error("Dungeon metadata not found for dungeonId {dungeonId}", dungeonId);
                return;
            }

            Metadata = metadata;
            LobbyRoomId = roomId;
        } else {
            Metadata = null;
            LobbyRoomId = 0;
        }
    }

    // The room whose record counts helper rewards: the helper window's count is room 1003's weekly reward count
    // (UIDungeonHelper::GetHelperRewardCountText, uidungeonhelper.cpp:441, GetWeeklyRewardCountText(0x3eb)).
    public const int HelperRewardRoomId = 1003;

    // Whether this player's record of the room is a veteran's (RoomDungeon 17 sets it).
    public bool IsVeteran(int dungeonId) =>
        Records.TryGetValue(dungeonId, out DungeonRecord? record) && record.Flag.HasFlag(DungeonRecordFlag.Veteran);

    /// <param name="withRookie">Another player of the clear played the room as a rookie.</param>
    public void CompleteDungeon(long clearTimestamp, bool withRookie = false) {
        if (Lobby == null || Metadata == null || UserRecord == null) {
            return;
        }
        DungeonRoomRewardMetadata metadata = Metadata.Reward;
        UserRecord.IsDungeonSuccess = Lobby.DungeonRoomRecord.State == DungeonState.Clear;
        UserRecord.TotalSeconds = (int) (Lobby.DungeonRoomRecord.EndTick - Lobby.DungeonRoomRecord.StartTick) / 1000;

        // The order KMS2 sends a clear in: the room records, the reward notice, the result, and only then what the
        // result names (meso, exp with its prestige exp, items, a helper's items, then the weekly reward's meso, exp
        // and items). The client's reader (OnRewardPacket) only stores the result, so either order works; this one is KMS2's.
        List<Action> grants = [];
        CalculateRewards(clearTimestamp, withRookie, grants);
        if (UserRecord.IsDungeonSuccess) {
            UpdateRankScore(clearTimestamp);
        }
        if (UserRecord.Flag.HasFlag(DungeonBonusFlag.Clear)) {
            session.Send(DungeonRoomPacket.Modify(DungeonRoomModify.GiveReward, Lobby.DungeonId));
            // A guild raid room's clear counts for the member's guild raid record (Guild 37). Chosen here: only a
            // clear that gave the clear reward counts, so a limitReward room's clears past its reward count do not.
            if (Metadata.GroupType == DungeonGroupType.guildRaid) {
                session.Guild.RaidClear(Lobby.DungeonId);
            }
        }

        session.Send(DungeonRewardPacket.Dungeon(UserRecord));
        foreach (Action grant in grants) {
            grant();
        }
    }

    // A failed run's result: the reward window with the fail title, its time under the fail-time label and no
    // reward. A fail's closing photo opens that window when it closes (UIDungeonClosingPhotoDialog vfunc_67,
    // uidungeonclosingphotodialog.cpp:226, opens 20217 = UIDungeonRewardDialog); without this packet the window
    // keeps its defaults (the Clear Time text "time", 0 mesos and exp, the guild rows). OnRewardPacket
    // (uidungeonrewarddialog.cpp:557) reads the win flag first and shows s_dungeon_reward_title_fail and
    // s_dungeon_reward_fail_time when it is false.
    public void FailDungeon() {
        if (Lobby == null || Metadata == null || UserRecord == null) {
            return;
        }
        UserRecord.IsDungeonSuccess = false;
        UserRecord.TotalSeconds = (int) (Lobby.DungeonRoomRecord.EndTick - Lobby.DungeonRoomRecord.StartTick) / 1000;
        session.Send(DungeonRewardPacket.Dungeon(UserRecord));
    }

    // The rank score of a clear (DungeonScoreRules), on every clear of a room with a rank table, whether or not
    // the clear still had a reward count left: LifetimeRecord keeps the best score, and in a season rank room
    // CurrentRecord keeps the best score of the season week, its expiry in CooldownTimestamp (the long the client
    // reads as the expiry of that timed value, DungeonRecord m_68).
    private void UpdateRankScore(long clearTimeStamp) {
        if (Metadata == null || UserRecord == null) {
            return;
        }
        int rankTableId = DungeonScoreRules.RankTableId(Metadata.RankTableId, Metadata.GroupType);
        if (rankTableId == 0 || !session.TableMetadata.DungeonConfigTable.MissionRanks.TryGetValue(rankTableId, out DungeonMissionRankMetadata? rankTable)) {
            return;
        }

        // A clear of a room with a rank table: the room id is the code (93000174 "Clear the "Duel on the Deck"
        // Fortress Rumble", 24091005). The client groups the type with dungeon_rank (conditionutil.cpp 0xc1, 0x2a).
        session.ConditionUpdate(ConditionType.dungeon_rank_clear, codeLong: Metadata.Id);

        DungeonRecord record = GetRecord(Metadata.Id);
        short score = DungeonScoreRules.RunScore(UserRecord.Missions.Values.Select(mission => mission.Score), rankTable.MaxScore);
        // The result dialog's score fields: the best before this clear and the run's (new record when it is higher).
        UserRecord.HighestScore = record.LifetimeRecord;
        UserRecord.Score = score;
        DungeonScoreRules.ApplyLifetime(record, score);
        // The clear's rank counts for dungeon_rank: the room is the code and the rank (0 F to 5 S+) must reach the
        // target (22210001 "Earn S+ Rank" 5, 22210045 "Earn A rank" 3, 93000173 "Spar with" the captain 0).
        session.ConditionUpdate(ConditionType.dungeon_rank, targetLong: (long) DungeonScoreRules.Rank(rankTable.Scores, score),
            codeLong: Metadata.Id);

        if (DungeonScoreRules.IsSeasonRankRoom(Metadata.GroupType, Metadata.Limit.MinLevel, session.ServerTableMetadata.ConstantsTable.DungeonSeasonRankMinLevel)) {
            DateTime clearTime = clearTimeStamp.FromEpochSeconds();
            DungeonSeasonTable.Entry? season = session.TableMetadata.DungeonSeasonTable.Current(clearTime);
            if (season != null) {
                DateTime expiry = DungeonScoreRules.SeasonExpiry(clearTime, season.DayOfWeeks, season.EndTime);
                DungeonScoreRules.ApplySeason(record, score, clearTimeStamp, expiry.ToEpochSeconds());
            }
        }
        session.Send(DungeonRoomPacket.Update(record));
    }

    // grants: what gives the rewards the record names, run after the result is sent.
    private void CalculateRewards(long clearTimeStamp, bool withRookie, List<Action> grants) {
        if (Lobby == null || Metadata == null || UserRecord == null) {
            return;
        }
        DungeonRecord record = GetRecord(Metadata.Id);
        // Tested on the record the player entered with, before this clear counts.
        bool helper = withRookie && DungeonRoomRules.CanClaimHelperReward(record, Metadata.HelperRequireClearCount);
        List<int> rewardIds = [];

        if (Metadata.Reward.UnionRewardId > 0) {
            if (!session.TableMetadata.DungeonRoomTable.Entries.TryGetValue(Metadata.Reward.UnionRewardId, out DungeonRoomMetadata? unionMetadata)) {
                return;
            }
            DungeonRecord unionRecord = GetRecord(Metadata.Reward.UnionRewardId);
            int weeklyBefore = GetWeeklyClearCount(unionRecord);
            // A clear counts while the record has a count left, as the client tells (HasCountLeft): a room without a
            // subRewardCount has no daily limit (the Colosseum and most rooms).
            if (DungeonRoomRules.HasCountLeft(unionRecord, unionMetadata.Reward.Count, unionMetadata.Reward.SubRewardCount, clearTimeStamp,
                    ExtraRewardCount(Metadata.Reward.UnionRewardId))) {
                UpdateUnionRecord(unionRecord, unionMetadata, clearTimeStamp);
                UserRecord.Flag |= DungeonBonusFlag.Clear;
            }

            // Only a clear that counted gives its step: one past the cap left the count, and gave the step again.
            if (DungeonRoomRules.UnitedWeeklyStep(session.TableMetadata.DungeonConfigTable.UnitedWeeklyRewards, weeklyBefore,
                    GetWeeklyClearCount(unionRecord)) is { } rewardId) {
                UserRecord.Flag |= DungeonBonusFlag.UnitedWeekly;
                rewardIds.Add(rewardId);
            }

            UpdateDungeonRecord(record, Metadata, clearTimeStamp);

        } else {
            if (DungeonRoomRules.HasCountLeft(record, Metadata.Reward.Count, Metadata.Reward.SubRewardCount, clearTimeStamp, ExtraRewardCount(Metadata.Id))) {
                UpdateUnionRecord(record, Metadata, clearTimeStamp);
                UpdateDungeonRecord(record, Metadata, clearTimeStamp);
                UserRecord.Flag |= DungeonBonusFlag.Clear;
            }
        }

        List<Action> helperGrants = [];
        if (helper) {
            GiveHelperReward(clearTimeStamp, helperGrants);
        }

        if (UserRecord.Flag.HasFlag(DungeonBonusFlag.Clear)) {
            GetClearDungeonRewards();
            // "Collect a reward from any dungeon" (70700002): a clear that still had a reward count left. The dungeon room
            // goes along as the code; the rows that name a dungeon match it by room id / 1000 (ConditionUtil).
            session.ConditionUpdate(ConditionType.dungeon_reward, codeLong: Lobby.DungeonId);
            // The same reward by the room's group type (dungeon_reward_group: 70700006 code 1 normal, 89000215 code 2
            // raid, 70700061 code 9 item, 70700003 code 10 vip).
            session.ConditionUpdate(ConditionType.dungeon_reward_group, codeLong: (long) Metadata.GroupType);
        }

        // Chosen here: the helper boxes' items after the room's and before the weekly reward.
        grants.AddRange(helperGrants);

        foreach (int rewardId in rewardIds) {
            // The weekly step reward: its exp and meso carry the weekly message code (1146), as in KMS2.
            (RewardRecord rewardRecord, Action give) = session.PrepareRewardContent(rewardId, ExpMessageCode.dungeonUnitedWeekly);
            UserRecord.Add(rewardRecord);
            grants.Add(give);
        }

        return;

        void GetClearDungeonRewards() {
            DungeonRoomRewardMetadata rewardMetadata = Metadata!.Reward;
            int exp = (int) rewardMetadata.Exp;
            if (rewardMetadata.ExpRate > 0f) {
                exp = (int) (rewardMetadata.Exp * rewardMetadata.ExpRate);
            }
            int meso = (int) rewardMetadata.Meso;
            // The running DungeonBonusReward events for the room or its group: more exp and meso, the boxes
            // rolled again, the events' own boxes, and the result dialog's event line (DungeonBonusFlag.Event).
            DungeonBonusReward? bonus = EventSaleRules.DungeonBonus(
                session.FindEvent(GameEventType.DungeonBonusReward).Select(gameEvent => gameEvent.Metadata.Data).OfType<DungeonBonusReward>(),
                Metadata.Id, Metadata.GroupType);
            if (bonus != null) {
                exp = (int) Math.Min((long) exp + EventSaleRules.DungeonBonusAmount(exp, bonus.ExpRate), int.MaxValue);
                meso = (int) Math.Min((long) meso + EventSaleRules.DungeonBonusAmount(meso, bonus.MesoRate), int.MaxValue);
                UserRecord.Flag |= DungeonBonusFlag.Event;
            }
            UserRecord.Rewards[DungeonRewardType.Exp] = exp;
            UserRecord.Rewards[DungeonRewardType.Meso] = meso;
            // The clear's prestige exp: the dialog names the table's amount, as KMS2 sends it whatever the character
            // gets (less past the daily amount, none below the prestige start level). It is given with the exp,
            // which carries the dungeon message code (1085) and so the dungeonClear prestige type. KMS2 sends the
            // meso first, with the same code.
            session.ServerTableMetadata.PrestigeExpTable.Entries.TryGetValue(ExpType.dungeonClear, out long prestige);
            UserRecord.Rewards[DungeonRewardType.Prestige] = (int) Math.Min(prestige, int.MaxValue);
            grants.Add(() => {
                session.Currency.AddMeso(meso, ExpMessageCode.dungeonRelative);
                session.Exp.AddExp(exp, ExpMessageCode.dungeonRelative);
            });

            ICollection<Item> items = [];
            // Each box is rolled for the player as an opened item box is (drop count, smart drop by job, gender, level):
            // a room box such as 21001001 holds every job's gear, and gave all 55 entries at once unrolled.
            IEnumerable<int> boxIds = DungeonRoomRules.ClearDropBoxIds(rewardMetadata).ToArray();
            if (bonus != null) {
                // The room's boxes once more per roll of the bonus, then the bonus' own boxes once.
                boxIds = Enumerable.Repeat(boxIds, 1 + bonus.ItemRolls).SelectMany(ids => ids).Concat(bonus.DropBoxIds).ToArray();
            }
            foreach (int boxId in boxIds) {
                items = items.Concat(Lobby.ItemDrop.GetIndividualDropItems(session, session.Player.Value.Character.Level, boxId)).ToList();
                items = items.Concat(Lobby.ItemDrop.GetGlobalDropItems(boxId, Metadata.Level)).ToList();
            }

            foreach (Item item in items) {
                UserRecord.RewardItems.Add(item);
                grants.Add(() => GiveItem(item));
            }
        }
    }

    /// <summary>
    /// A dungeon reward pouch (item function OpenDungeonItemBox, @0x140d24e70): the room it names, when the room
    /// exists and its reward room's record has a count left as the client asks it (PouchHasCountLeft); else null.
    /// The client asks the same before its confirm box (s_msg_dungeon_item_open_dungeon_count when none is left).
    /// </summary>
    public DungeonRoomMetadata? RewardPouchRoom(string parameters) {
        if (!session.TableMetadata.DungeonRoomTable.Entries.TryGetValue(DungeonRoomRules.PouchRoomId(parameters), out DungeonRoomMetadata? metadata)) {
            return null;
        }
        (DungeonRoomMetadata rewardRoom, int rewardId) = RewardRoom(metadata);
        Records.TryGetValue(rewardId, out DungeonRecord? record);
        return DungeonRoomRules.PouchHasCountLeft(record, rewardRoom.Reward.Count, DateTime.Now.ToEpochSeconds()) ? metadata : null;
    }

    /// <summary>
    /// Opens the pouch of a room RewardPouchRoom gave: one reward count of its reward room is used (the confirm
    /// text s_msg_item_open_dungeon_item_box: one dungeon reward is used up, the items go to the mailbox when the
    /// bag is full) and the room's clear boxes are rolled for the player as a clear rolls them. Chosen here: the
    /// count is taken as a clear takes it (the period starts anew once over); the daily count (UnionSubClears, which
    /// decides a real clear's daily clear bonus) is left alone, as the client asks only the period count; and
    /// the pouch gives the boxes only, not the clear's exp and meso; the record goes out as after a clear.
    /// </summary>
    public ICollection<Item> OpenRewardPouch(DungeonRoomMetadata metadata, FieldManager field) {
        (DungeonRoomMetadata rewardRoom, int rewardId) = RewardRoom(metadata);
        DungeonRecord record = GetRecord(rewardId);
        DateTime now = DateTime.Now;
        long nowSeconds = now.ToEpochSeconds();
        if (nowSeconds >= record.UnionCooldownTimestamp) {
            record.UnionCooldownTimestamp = GetCooldownTime(rewardRoom.CooldownType, rewardRoom.CooldownValue, now);
            record.UnionClears = 0;
            record.ExtraSubClears = 0;
        }
        record.UnionClears++;
        session.Send(DungeonRoomPacket.Update(record));

        List<Item> items = [];
        foreach (int boxId in DungeonRoomRules.ClearDropBoxIds(metadata.Reward)) {
            items.AddRange(field.ItemDrop.GetIndividualDropItems(session, session.Player.Value.Character.Level, boxId));
            items.AddRange(field.ItemDrop.GetGlobalDropItems(boxId, metadata.Level));
        }
        foreach (Item item in items) {
            GiveItem(item);
        }
        return items;
    }

    private void GiveItem(Item item) {
        if (!session.Item.Inventory.Add(item, true)) {
            session.Item.MailItem(item);
        }
    }

    // The helper reward of a veteran who cleared with a rookie: one count of room 1003's record while it has one
    // left, the result dialog's helper line (DungeonBonusFlag.Helper, s_dungeon_reward_dungeon_helper), the items of
    // the room's helper boxes, and the reward notice (Modify GiveDungeonHelperReward, RewardNotice kind 2).
    private void GiveHelperReward(long clearTimeStamp, List<Action> grants) {
        if (Lobby == null || Metadata == null || UserRecord == null
            || !session.TableMetadata.DungeonRoomTable.Entries.TryGetValue(HelperRewardRoomId, out DungeonRoomMetadata? helperRoom)) {
            return;
        }
        Records.TryGetValue(HelperRewardRoomId, out DungeonRecord? existing);
        if (!DungeonRoomRules.HasCountLeft(existing, helperRoom.Reward.Count, helperRoom.Reward.SubRewardCount, clearTimeStamp)) {
            return;
        }

        DungeonRecord record = GetRecord(HelperRewardRoomId);
        DateTime clearTime = clearTimeStamp.FromEpochSeconds();
        if (clearTimeStamp >= record.UnionCooldownTimestamp) {
            record.UnionCooldownTimestamp = GetCooldownTime(helperRoom.CooldownType, helperRoom.CooldownValue, clearTime);
            record.UnionClears = 0;
            record.ExtraSubClears = 0;
        }
        if (clearTimeStamp >= record.UnionSubCooldownTimestamp) {
            record.UnionSubCooldownTimestamp = new DateTime(clearTime.Year, clearTime.Month, clearTime.Day).AddDays(1).ToEpochSeconds();
            record.UnionSubClears = 0;
        }
        record.UnionClears++;
        record.UnionSubClears++;
        UserRecord.Flag |= DungeonBonusFlag.Helper;

        // The boxes once, and once more per bonus of the running FindDungeonHelperBonus events.
        int times = EventSaleRules.HelperRewardTimes(session.FindEvent(GameEventType.FindDungeonHelperBonus)
            .Select(gameEvent => gameEvent.Metadata.Data is FindDungeonHelperBonus bonus ? bonus.Bonus : 0));
        foreach (int boxId in Enumerable.Repeat(Metadata.HelperRewardDropBoxIds ?? [], times).SelectMany(ids => ids)) {
            foreach (Item item in Lobby.ItemDrop.GetIndividualDropItems(session, session.Player.Value.Character.Level, boxId).Concat(Lobby.ItemDrop.GetGlobalDropItems(boxId, Metadata.Level))) {
                UserRecord.RewardItems.Add(item);
                grants.Add(() => GiveItem(item));
            }
        }
        session.Send(DungeonRoomPacket.Update(record));
        session.Send(DungeonRoomPacket.Modify(DungeonRoomModify.GiveDungeonHelperReward, Lobby.DungeonId));
    }

    // The room whose record and coupon count the rewards: the unionRewardID room if it is set (DungeonRoom::GetCoupon,
    // DungeonRoomInfo::GetRewardRoom), else the room itself.
    private (DungeonRoomMetadata Room, int Id) RewardRoom(DungeonRoomMetadata metadata) {
        int rewardId = metadata.Reward.UnionRewardId != 0 ? metadata.Reward.UnionRewardId : metadata.Id;
        return (session.TableMetadata.DungeonRoomTable.Entries.GetValueOrDefault(rewardId, metadata), rewardId);
    }

    /// <summary>
    /// RoomDungeon mode 8 (UIDungeonRoomInfo::OnUseExtraRewardItem, uidungeonroomdialog.cpp:1502): one more reward
    /// count of the dungeon's reward room, paid with its coupon's ticket items.
    /// </summary>
    public void AddExtraRewardCount(int dungeonId) {
        if (!session.TableMetadata.DungeonRoomTable.Entries.TryGetValue(dungeonId, out DungeonRoomMetadata? metadata)) {
            session.Send(DungeonRoomPacket.Error(DungeonRoomError.s_room_dungeon_reward_CantUseExtraReward, dungeonId));
            return;
        }
        (DungeonRoomMetadata rewardRoom, int rewardId) = RewardRoom(metadata);
        BuyExtraRewardCount(rewardRoom, rewardId);
    }

    // The purchase: the client's errors 5 (no ticket), 7 (at the maximum) and 6 (too few tickets; its nArg is the
    // room id, ShowLackExtraTicket) name the reward room. On success the record goes out (Update, mode 6) with the
    // reward notice for an added count (Modify AddExtraCount, RewardNotice mode 7 kind 3). A purchase in an expired
    // period starts the next one, as a clear would.
    private bool BuyExtraRewardCount(DungeonRoomMetadata rewardRoom, int rewardId) {
        DungeonExtraRewardTicket? ticket = rewardRoom.ExtraRewardTicket;
        Records.TryGetValue(rewardId, out DungeonRecord? existing);
        DateTime now = DateTime.Now;
        int bonus = DungeonRoomRules.BonusCountNow(existing, now.ToEpochSeconds());
        switch (DungeonRoomRules.CanAddExtraReward(ticket, bonus)) {
            case DungeonRoomRules.ExtraRewardVerdict.NoTicket:
                session.Send(DungeonRoomPacket.Error(DungeonRoomError.s_room_dungeon_reward_CantUseExtraReward, rewardId));
                return false;
            case DungeonRoomRules.ExtraRewardVerdict.AtMax:
                session.Send(DungeonRoomPacket.Error(DungeonRoomError.s_room_dungeon_max_reward_count, rewardId));
                return false;
        }

        int cost = DungeonRoomRules.ExtraTicketCost(ticket!, bonus);
        if (!session.Item.Inventory.Consume([new IngredientInfo(ticket!.TicketTag, cost)])) {
            session.Send(DungeonRoomPacket.Error(DungeonRoomError.s_dungeonRoom_lack_extra_ticket, rewardId));
            return false;
        }

        DungeonRecord record = GetRecord(rewardId);
        if (now.ToEpochSeconds() >= record.UnionCooldownTimestamp) {
            record.UnionCooldownTimestamp = GetCooldownTime(rewardRoom.CooldownType, rewardRoom.CooldownValue, now);
            record.UnionClears = 0;
            record.ExtraSubClears = 0;
        }
        record.ExtraSubClears++;
        session.Send(DungeonRoomPacket.Update(record));
        session.Send(DungeonRoomPacket.Modify(DungeonRoomModify.AddExtraCount, rewardId));
        return true;
    }

    private void UpdateUnionRecord(DungeonRecord record, DungeonRoomMetadata metadata, long clearTimeStamp) {
        if (UserRecord == null) {
            return;
        }
        DateTime clearTime = clearTimeStamp.FromEpochSeconds();
        if (record.ClearTimestamp == 0) {
            record.ClearTimestamp = clearTimeStamp;
        }
        DungeonRoomRules.HealCooldown(record, metadata.CooldownType, metadata.CooldownValue, clearTime);
        if (clearTimeStamp > record.UnionCooldownTimestamp) {
            record.UnionCooldownTimestamp = GetCooldownTime(metadata.CooldownType, metadata.CooldownValue, clearTime);
            record.UnionClears = 0;
            record.ExtraSubClears = 0;
        }

        if (clearTimeStamp > record.UnionSubCooldownTimestamp) {
            record.UnionSubCooldownTimestamp = new DateTime(clearTime.Year, clearTime.Month, clearTime.Day).AddDays(1).ToEpochSeconds();
            record.UnionSubClears = 0;
        }

        if (metadata.GroupType == DungeonGroupType.colosseum) {
            record.UnionClears = (byte) UserRecord.Round;
        } else if (record.UnionClears >= metadata.Reward.Count + DungeonRoomRules.BonusCountNow(record, clearTimeStamp)
                   && ExtraReward(record.DungeonId) is { } extra
                   && DungeonRoomRules.UseExtraCount(record, extra.Entry, clearTime, extra.End)) {
            // The room's reward count is used: the clear takes an event count, and the reward stays.
            record.UnionSubClears++;
        } else {
            record.UnionClears++;
            record.UnionSubClears++;
        }

        if (record.UnionSubClears <= metadata.Reward.SubRewardCount) {
            UserRecord.Flag |= DungeonBonusFlag.Clear;
        }
        session.Send(DungeonRoomPacket.Update(record));
    }

    private void UpdateDungeonRecord(DungeonRecord record, DungeonRoomMetadata metadata, long clearTimeStamp, bool updateUnion = false) {
        if (UserRecord == null) {
            return;
        }
        if (record.ClearTimestamp == 0) {
            record.ClearTimestamp = clearTimeStamp;
        }

        if (updateUnion) {
            UpdateUnionRecord(record, metadata, clearTimeStamp);
            return;
        }

        if (metadata.GroupType == DungeonGroupType.colosseum) {
            record.TotalClears = UserRecord.Round;
        } else {
            record.TotalClears++;
        }
        // CooldownTimestamp is not the room cooldown: the client reads it as the expiry of CurrentRecord, and
        // nothing on the server read the cooldown written here. UpdateRankScore sets it.
        session.Send(DungeonRoomPacket.Update(record));
        // KMS2 sends the dungeon_first_clear update right after the record update that shows 1 clear.
        if (TrophyReportRules.IsFirstClear(metadata.GroupType, record.TotalClears)) {
            session.ConditionUpdate(ConditionType.dungeon_first_clear, codeLong: record.DungeonId);
        }
    }

    private int GetWeeklyClearCount(DungeonRecord record) {
        if (!session.TableMetadata.DungeonRoomTable.Entries.TryGetValue(record.DungeonId, out DungeonRoomMetadata? metadata)) {
            return 0;
        }

        if (metadata.Reward.UnionRewardId > 0) {
            DungeonRecord unionRecord = GetRecord(metadata.Reward.UnionRewardId);

            if (DateTime.Now.ToEpochSeconds() > unionRecord.UnionCooldownTimestamp) {
                return 0;
            }
            return unionRecord.UnionClears;
        }

        return RewardCount(record, DateTime.Now.ToEpochSeconds());
    }

    /// <summary>
    /// The rewards a record has taken in its cooldown period (the client's DungeonRecord::GetBonusRewardCount):
    /// none once the period is over. A Colosseum record keeps its rounds cleared here.
    /// </summary>
    public static int RewardCount(DungeonRecord? record, long now) {
        if (record == null || now > record.UnionCooldownTimestamp) {
            return 0;
        }
        return record.UnionClears;
    }

    /// <summary>The rewards taken from a dungeon room in its period, 0 without a record.</summary>
    public int RewardCount(int dungeonId) {
        Records.TryGetValue(dungeonId, out DungeonRecord? record);
        return RewardCount(record, DateTime.Now.ToEpochSeconds());
    }

    /// <summary>
    /// receivedRewardDungeonID: a reward of the room was taken in its period. completeRewardDungeonID: all were
    /// (no count left by the client's DungeonRecordCli::HasCountLeft, its count against the room's rewardCount).
    /// </summary>
    public static bool RewardCondition(int rewardCount, int roomRewardCount, bool complete) {
        return complete ? rewardCount >= roomRewardCount : rewardCount > 0;
    }

    // The running DungeonExtraReward event count of a reward room, with the event's end.
    private (DungeonExtraReward.Entry Entry, DateTime End)? ExtraReward(int dungeonId) {
        IList<GameEvent> events = session.FindEvent(GameEventType.DungeonExtraReward);
        var found = DungeonRoomRules.ExtraRewardEntry(events
            .Where(gameEvent => gameEvent.Metadata.Data is DungeonExtraReward)
            .Select(gameEvent => (gameEvent.Id, (DungeonExtraReward) gameEvent.Metadata.Data)), dungeonId);
        if (found == null) {
            return null;
        }
        return (found.Value.Entry, events.First(gameEvent => gameEvent.Id == found.Value.EventId).Metadata.EndTime);
    }

    private int ExtraRewardCount(int dungeonId) => ExtraReward(dungeonId)?.Entry.Count ?? 0;

    private static long GetCooldownTime(DungeonCooldownType type, int cooldownValue, DateTime clearTime) {
        return DungeonRoomRules.CooldownEnd(type, cooldownValue, clearTime).ToEpochSeconds();
    }

    public void Reset() {
        if (session.Party.Party == null || session.Party.Party.LeaderCharacterId != session.CharacterId) {
            return;
        }

        try {
            FieldResponse response = session.World.Field(new FieldRequest {
                DestroyDungeon = new FieldRequest.Types.DestroyDungeon {
                    RoomId = LobbyRoomId,
                },
            });
            if ((MigrationError) response.Error != MigrationError.ok) {
                session.Send(MigrationPacket.GameToGameError((MigrationError) response.Error));
                return;
            }
        } catch (RpcException) {
            session.Send(MigrationPacket.GameToGameError(MigrationError.s_move_err_no_server));
            return;
        }

        var request = new PartyRequest {
            RequestorId = session.CharacterId,
            SetDungeon = new PartyRequest.Types.SetDungeon {
                PartyId = session.Party.Id,
                DungeonId = 0,
                DungeonRoomId = 0,
                Set = false,
            },
        };

        try {
            session.World.Party(request);
        } catch (RpcException) { }

    }

    private DungeonRecord GetRecord(int dungeonId) {
        if (!Records.TryGetValue(dungeonId, out DungeonRecord? record)) {
            Records[dungeonId] = record = new DungeonRecord(dungeonId);
        }
        return record;
    }

    private void MigrateToDungeon() {
        if (Metadata == null) {
            logger.Error("Dungeon metadata is null, cannot migrate to dungeon");
            return;
        }

        session.MigrationSave();
        try {
            var request = new MigrateOutRequest {
                AccountId = session.AccountId,
                CharacterId = session.CharacterId,
                MachineId = session.MachineId.ToString(),
                Server = Server.World.Service.Server.Game,
                MapId = Metadata.LobbyFieldId,
                RoomId = LobbyRoomId,
                Type = MigrationType.Dungeon,
                PetUid = session.SummonedPetUid,
            };

            MigrateOutResponse response = session.World.MigrateOut(request);
            if (!PlayerWarpUtil.TryEndpoint(response.IpAddress, response.Port, out IPEndPoint? endpoint)) {
                logger.Error("{Where}: the World server answered with the address '{Address}' port {Port}", "Dungeon lobby", response.IpAddress, response.Port);
                session.Send(MigrationPacket.GameToGameError(MigrationError.s_move_err_default));
                return;
            }
            session.Send(MigrationPacket.GameToGame(endpoint, response.Token, Metadata.LobbyFieldId));
            session.State = SessionState.ChangeMap;
        } catch (RpcException ex) {
            session.Send(MigrationPacket.GameToGameError(MigrationError.s_move_err_default));
            session.Send(NoticePacket.Disconnect(new InterfaceText(ex.Message)));
        } finally {
            session.Disconnect();
        }
    }

    public void Save(GameStorage.Request db) {
        db.SaveDungeonRecords(session.CharacterId, Records.Values.ToArray());
    }
}
