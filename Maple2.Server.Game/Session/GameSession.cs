using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using Autofac;
using Grpc.Core;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Validators;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Network;
using Maple2.Server.Core.Packets;
using Maple2.Server.Core.Sync;
using Maple2.Server.Game.Commands;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;
using Maple2.Server.Game.Util.Sync;
using Maple2.Server.World.Service;
using Maple2.Tools.Extensions;
using Maple2.Tools.Scheduler;
using MigrationType = Maple2.Model.Enum.MigrationType;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Game.Session;

public sealed partial class GameSession : Core.Network.Session {
    protected override PatchType Type => PatchType.Ignore;
    public const int FIELD_KEY = 0x1234;

    // gameDisposeState: 0 = active, 1 = disposing, 2 = disposed
    private int gameDisposeState;
    private readonly GameServer server;

    public readonly CommandRouter CommandHandler;
    public readonly EventQueue Scheduler;

    public int ServerTick;
    public int ClientTick;

    public int Latency;
    public string PlayerName => FieldEntryRules.LogName(Player?.Value.Character.Name);
    public Guid MachineId { get; private set; }
    private bool preMigrationSaved;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    // ReSharper disable UnusedAutoPropertyAccessor.Global
    public required GameStorage GameStorage { get; init; }
    public required WorldClient World { get; init; }
    public required ItemMetadataStorage ItemMetadata { get; init; }
    public required SkillMetadataStorage SkillMetadata { get; init; }
    public required TableMetadataStorage TableMetadata { get; init; }
    public required ServerTableMetadataStorage ServerTableMetadata { get; init; }
    public required MapMetadataStorage MapMetadata { get; init; }

    // MoveEventField: the last answer of the World server about the fields with a running event.
    public HashSet<int> EventFields = [];
    public DateTime EventFieldsAskedAt;
    // Environment.TickCount64 of the last minimap ping passed on; 0 when there was none.
    public long MinimapPingTick;
    public required NpcMetadataStorage NpcMetadata { get; init; }
    public required AchievementMetadataStorage AchievementMetadata { get; init; }
    public required QuestMetadataStorage QuestMetadata { get; init; }
    public required ScriptMetadataStorage ScriptMetadata { get; init; }
    public required FunctionCubeMetadataStorage FunctionCubeMetadata { get; init; }
    public required RideMetadataStorage RideMetadata { get; init; }
    public required FieldManager.Factory FieldFactory { get; init; }
    public required ItemStatsCalculator ItemStatsCalc { private get; init; }
    public required PlayerInfoStorage PlayerInfo { get; init; }
    // ReSharper restore All
    // ReSharper restore UnusedAutoPropertyAccessor.Global
    #endregion

    // Home invites this player sent that are waiting for an answer (memory only).
    public HomeInviteTracker HomeInvites { get; } = new();
    public ConfigManager Config { get; set; } = null!;
    public readonly Maple2.Model.Validators.BuddyEmoteInvites BuddyEmoteInvites = new();
    // 1 once GlobalFactor (0x82) has been answered on this connection.
    public int GlobalFactorSent;
    public MailManager Mail { get; set; } = null!;
    public GuildManager Guild { get; set; } = null!;
    public SeatReleaseEcho SeatRelease { get; } = new();
    public BuddyManager Buddy { get; set; } = null!;
    public ItemManager Item { get; set; } = null!;
    public HousingManager Housing { get; set; } = null!;
    public CurrencyManager Currency { get; set; } = null!;
    public MasteryManager Mastery { get; set; } = null!;
    public StatsManager Stats { get; set; } = null!;
    public BuffManager Buffs { get; set; } = null!;
    public ItemEnchantManager ItemEnchant { get; set; } = null!;
    public ItemMergeManager ItemMerge { get; set; } = null!;
    public ItemBoxManager ItemBox { get; set; } = null!;
    public BeautyManager Beauty { get; set; } = null!;
    public GameEventManager GameEvent { get; set; } = null!;
    public ExperienceManager Exp { get; set; } = null!;
    public AchievementManager Achievement { get; set; } = null!;
    public QuestManager Quest { get; set; } = null!;
    public ShopManager Shop { get; set; } = null!;
    public UgcMarketManager UgcMarket { get; set; } = null!;
    public BlackMarketManager BlackMarket { get; set; } = null!;
    public FieldManager? Field { get; set; }
    public FieldPlayer Player { get; private set; } = null!;
    public PartyManager Party { get; set; } = null!;
    // The object names of the last InteractObject start packet; used by the session's own packet thread only.
    public readonly HashSet<string> InteractStarted = [];
    // The farming and breeding cube ids of the last FunctionCube begin packet; same thread rule.
    public readonly HashSet<string> CubeStarted = [];
    // The recall offered to this player per RecallScrollType (party, guild, wedding); lock the array to use it.
    public readonly PendingRecall?[] Recalls = new PendingRecall?[3];
    // The recall invite (s2c RecallUser 4) this player has not answered yet.
    public volatile PendingUserRecall? UserRecall;
    // Set once this client picked an NPC with the debug camera (SetDebugMode 4, select), which opens its NPC
    // control window. The client sends nothing when that window closes, so this stays set for the session.
    public volatile bool NpcControlPicked;
    public ConcurrentDictionary<int, GroupChatManager> GroupChats { get; set; }
    public ConcurrentDictionary<long, ClubManager> Clubs { get; set; }
    public SurvivalManager Survival { get; set; } = null!;
    public MarriageManager Marriage { get; set; } = null!;
    public FishingManager Fishing { get; set; } = null!;
    public DungeonManager Dungeon { get; set; } = null!;
    public AnimationManager Animation { get; set; } = null!;
    public RideManager Ride { get; set; } = null!;
    public MentoringManager Mentoring { get; set; } = null!;

    public GameSession(TcpClient tcpClient, GameServer server, IComponentContext context) : base(tcpClient) {
        this.server = server;
        State = SessionState.ChangeMap;
        CommandHandler = context.Resolve<CommandRouter>(new NamedParameter("session", this));
        Scheduler = new EventQueue(Logger);
        Scheduler.ScheduleRepeated(() => Send(TimeSyncPacket.Request()), TimeSpan.FromSeconds(1));
        Scheduler.ScheduleRepeated(TstPollCommands, TimeSpan.FromSeconds(1)); // test hook

        OnLoop += Scheduler.InvokeAll;
        GroupChats = new ConcurrentDictionary<int, GroupChatManager>();
        Clubs = new ConcurrentDictionary<long, ClubManager>();
    }

    public bool FindSession(long characterId, [NotNullWhen(true)] out GameSession? other) {
        return server.GetSession(characterId, out other);
    }

    // The session of an account on this channel, whichever character it plays.
    public GameSession? FindSessionByAccountId(long accountId) {
        return server.GetSessionByAccountId(accountId);
    }

    public bool FindField(int mapId, [NotNullWhen(true)] out FieldManager? field) {
        field = server.GetField(mapId);
        return field is not null;
    }

    public bool EnterServer(long accountId, Guid machineId, MigrateInResponse migrateResponse) {
        long characterId = migrateResponse.CharacterId;
        resummonPetUid = migrateResponse.PetUid;
        int channel = migrateResponse.Channel;
        int mapId = migrateResponse.MapId;
        int portalId = migrateResponse.PortalId;
        long ownerId = migrateResponse.OwnerId;
        int roomId = migrateResponse.RoomId;
        var migrationType = (MigrationType) migrateResponse.Type;
        var position = new Vector3(migrateResponse.PositionX, migrateResponse.PositionY, migrateResponse.PositionZ);

        AccountId = accountId;
        CharacterId = characterId;
        MachineId = machineId;

        State = SessionState.ChangeMap;
        server.OnConnected(this);

        using GameStorage.Request db = GameStorage.Context();
        db.BeginTransaction();
        int objectId = FieldManager.NextGlobalId();
        Player? player;
        try {
            AcquireLock(AccountId, 5);
            player = db.LoadPlayer(AccountId, CharacterId, objectId, GameServer.GetChannel());
            db.Commit();
        } finally {
            ReleaseLock(AccountId);
        }
        if (player == null) {
            Logger.Warning("Failed to load player from database: {AccountId}, {CharacterId}", AccountId, CharacterId);
            Send(MigrationPacket.MoveResult(MigrationError.s_move_err_default));
            return false;
        }

        Player = new FieldPlayer(this, player);
        Animation = new AnimationManager(this);
        Currency = new CurrencyManager(this);
        Mastery = new MasteryManager(this);
        Stats = new StatsManager(Player, ServerTableMetadata.UserStatTable);
        Config = new ConfigManager(db, this);
        Housing = new HousingManager(this, TableMetadata);
        Mail = new MailManager(this);
        ItemEnchant = new ItemEnchantManager(this);
        ItemMerge = new ItemMergeManager(this);
        ItemBox = new ItemBoxManager(this);
        Beauty = new BeautyManager(this);
        GameEvent = new GameEventManager(this);
        Exp = new ExperienceManager(this);
        Achievement = new AchievementManager(this);
        Quest = new QuestManager(this);
        Shop = new ShopManager(this);
        Guild = new GuildManager(this);
        Buddy = new BuddyManager(db, this);
        Item = new ItemManager(db, this, ItemStatsCalc);
        Buffs = new BuffManager(Player);
        UgcMarket = new UgcMarketManager(this);
        BlackMarket = new BlackMarketManager(this);
        Survival = new SurvivalManager(this);
        Marriage = new MarriageManager(this);
        Fishing = new FishingManager(this, TableMetadata, ServerTableMetadata);
        Dungeon = new DungeonManager(this);
        Ride = new RideManager(this);
        Mentoring = new MentoringManager(this);
        CommandHandler.RegisterCommands(); // Refresh commands with proper permissions
        GroupChatInfoResponse groupChatInfoRequest = World.GroupChatInfo(new GroupChatInfoRequest {
            CharacterId = CharacterId,
        });

        foreach (GroupChatInfo groupChatInfo in groupChatInfoRequest.Infos) {
            var manager = new GroupChatManager(groupChatInfo, this);
            GroupChats.TryAdd(groupChatInfo.Id, manager);
        }

        int fieldId = mapId == 0 ? player.Character.MapId : mapId;
        // A dungeon arrival whose room was closed in the meantime: enter the return field as a plain arrival.
        if (FieldEntryRules.ArrivesInReturnField(migrationType, server.HasDungeon(roomId))) {
            Logger.Warning("Character:{CharacterId} arrives for dungeon Room:{RoomId} which is gone, enters the return field", characterId, roomId);
            migrationType = MigrationType.Normal;
            fieldId = player.Character.ReturnMaps.Peek();
            position = player.Character.ReturnPosition;
            ownerId = 0;
            roomId = 0;
            portalId = -1;
        }
        // Arriving in another account's home (a channel change or a relog inside it): a blocked account
        // goes to its return field instead.
        if (ownerId != 0 && ownerId != AccountId
            && ServerTableMetadata.InstanceFieldTable.Entries.TryGetValue(fieldId, out InstanceFieldMetadata? instanceField)
            && instanceField.Type == InstanceType.ugcMap
            && db.IsHomeBlocked(ownerId, AccountId)) {
            fieldId = player.Character.ReturnMaps.Peek();
            position = player.Character.ReturnPosition;
            if (!MapMetadata.TryGet(fieldId, out _)) {
                fieldId = Constant.DefaultReturnMapId;
                position = default;
            }
            ownerId = 0;
            roomId = 0;
            portalId = -1;
        }
        // Arriving on a map the played client does not have (saved there before the map was marked, or sent
        // from another channel): enter the return field, else the default one, as a plain arrival.
        if (MapMetadata.TryGet(fieldId, out MapMetadata? arrival) && arrival.Property.NotInPlayedClient) {
            int returnId = player.Character.ReturnMaps.Peek();
            int arrivalId = FieldEntryRules.ArrivalMap(fieldId, true, returnId, Constant.DefaultReturnMapId,
                id => MapMetadata.TryGet(id, out MapMetadata? fallback) && !fallback.Property.NotInPlayedClient);
            if (arrivalId == 0) {
                Send(MigrationPacket.MoveResult(MigrationError.s_move_err_default));
                return false;
            }
            Logger.Warning("Character:{CharacterId} enters Map:{ArrivalId}, Map:{MapId} is not in the played client", characterId, arrivalId, fieldId);
            position = arrivalId == returnId ? player.Character.ReturnPosition : default;
            fieldId = arrivalId;
            migrationType = MigrationType.Normal;
            ownerId = 0;
            roomId = 0;
            portalId = -1;
        }
        // A login (a relog after the client closed in a dungeon) on a map of a room the character entered and that
        // still runs: back into that room's field, with its clock and missions, as a dungeon arrival.
        if (migrationType == MigrationType.Normal && roomId == 0 && ownerId == 0
            && server.FindDungeonField(characterId, fieldId) is DungeonFieldManager rejoined) {
            Logger.Information("Character:{CharacterId} enters Map:{MapId} of its running dungeon Room:{RoomId}", characterId, fieldId, rejoined.RoomId);
            migrationType = MigrationType.Dungeon;
            roomId = rejoined.RoomId;
            portalId = -1;
        }
        if (!PrepareField(fieldId, out FieldManager? fieldManager, portalId: portalId, ownerId: ownerId, roomId: roomId, position: position)) {
            // The saved field cannot be made (for one, its map has no navmesh file). A plain entry goes to the
            // return field, then to the default one, so the character is not locked out of the game.
            if (migrationType == MigrationType.Normal) {
                foreach (int fallbackId in FieldEntryRules.Fallbacks(fieldId, player.Character.ReturnMaps.Peek(), Constant.DefaultReturnMapId)) {
                    if (!MapMetadata.TryGet(fallbackId, out MapMetadata? fallbackMap) || fallbackMap.Property.NotInPlayedClient) {
                        continue;
                    }
                    Vector3 fallbackPosition = fallbackId == player.Character.ReturnMaps.Peek() ? player.Character.ReturnPosition : default;
                    if (PrepareField(fallbackId, out fieldManager, position: fallbackPosition)) {
                        Logger.Warning("Character:{CharacterId} enters Map:{FallbackId}, Map:{MapId} could not be made", characterId, fallbackId, fieldId);
                        break;
                    }
                }
            }
            if (fieldManager == null) {
                Send(MigrationPacket.MoveResult(MigrationError.s_move_err_default));
                return false;
            }
        }
        switch (migrationType) {
            case MigrationType.DecorPlanner:
            case MigrationType.ModelHome:
            case MigrationType.BlueprintDesigner:
                player.Home.EnterPlanner((PlotMode) migrationType);
                Housing.GetFieldPlot()?.SetPlannerMode((PlotMode) migrationType);
                break;
            case MigrationType.Dungeon:
                if (fieldManager is not DungeonFieldManager dungeonField) {
                    Logger.Error("Failed to load dungeon field for dungeon migration");
                    Send(MigrationPacket.MoveResult(MigrationError.s_move_err_default));
                    return false;
                }
                Dungeon.SetDungeon(dungeonField.Lobby ?? dungeonField);
                break;
            case MigrationType.Normal:
            default:
                break;
        }

        var playerUpdate = new PlayerUpdateRequest {
            AccountId = accountId,
            CharacterId = characterId,
            LastOnlineTime = Player.Value.Character.LastOnlineTime,
        };
        playerUpdate.SetFields(UpdateField.All, player);
        playerUpdate.Health = new HealthUpdate {
            CurrentHp = Stats.Values[BasicAttribute.Health].Current,
            TotalHp = Stats.Values[BasicAttribute.Health].Total,
        };
        PlayerInfo.SendUpdate(playerUpdate);

        //session.Send(Packet.Of(SendOp.REQUEST_SYSTEM_INFO));
        Send(MigrationPacket.MoveResult(MigrationError.ok));

        Survival.Load();
        // MeretMarket
        // UserConditionEvent
        // PCBangBonus
        Guild.Load();
        foreach ((int id, GroupChatManager groupChat) in GroupChats) {
            groupChat.Load();
        }

        ClubInfoResponse clubInfoResponse = World.ClubInfo(new ClubInfoRequest {
            CharacterId = CharacterId,
        });

        foreach (ClubInfo clubInfo in clubInfoResponse.Clubs) {
            var manager = ClubManager.Create(clubInfo, this);
            if (manager == null) {
                Logger.Error("Failed to create club manager for club {ClubId}", clubInfo.Id);
                continue;
            }
            if (Clubs.TryAdd(clubInfo.Id, manager)) {
                manager.Load();
            }
        }

        Buddy.Load();
        Send(HomeBlockPacket.Load(db.GetHomeBlocks(AccountId)));
        IList<Maid> maids = db.GetMaids(AccountId);
        Send(UserMaidPacket.List(maids));
        // The crafts of those maids, keyed by the maid uid (CManufactureManager).
        Send(MaidCraftItemPacket.List(maids.Where(MaidRules.IsCrafting).ToList()));

        try {
            PlayerConfigResponse configResponse = World.PlayerConfig(new PlayerConfigRequest {
                Get = new PlayerConfigRequest.Types.Get(),
                RequesterId = CharacterId,
            });
            long currentTick = Environment.TickCount64;
            Buffs.SetCacheBuffs(configResponse.Buffs, currentTick);
            Config.SetCacheSkillCooldowns(configResponse.SkillCooldowns, currentTick);
            Config.SetDeathPenalty(configResponse.DeathInfo, currentTick);

        } catch (RpcException ex) {
            Logger.Warning(ex, "Failed to load cache player config");
        }

        Send(SurvivalPacket.UpdateStats(player.Account));

        Send(TimeSyncPacket.Reset(DateTimeOffset.UtcNow));
        // The play start of this login; the attendance gift's play seconds are counted from it on both sides.
        DateTime playStart = DateTime.Now;
        GameEvent.StartPlayTime(playStart);
        Send(TimeSyncPacket.Set(playStart));

        Stats.Refresh();

        Send(RequestPacket.TickSync(Environment.TickCount));

        try {
            ChannelsResponse response = World.Channels(new ChannelsRequest());
            Send(ChannelPacket.Load(response.Channels));
        } catch (RpcException ex) {
            Logger.Warning(ex, "Failed to populate channels");
        }
        Send(ServerEnterPacket.Request(Player));

        // Ugc
        // Cash
        // Gvg
        // Pvp
        Send(StateSyncPacket.SyncNumber(0));
        // SyncWorld
        Send(PrestigePacket.Load(player.Account));
        Send(PrestigePacket.LoadMissions(player.Account));
        Item.Inventory.Load();
        Item.Furnishing.Load();
        // Load Quests
        Quest.Load();
        Quest.LoadAllianceMissions();
        // Send(QuestPacket.LoadQuests(Array.Empty<int>()));
        Achievement.Load();
        // MaidCraftItem
        // UserMaid
        // UserEnv
        Send(UserEnvPacket.LoadTitles(Player.Value.Unlock.Titles));
        Send(UserEnvPacket.InteractedObjects(Player.Value.Unlock.InteractedObjects));
        Send(UserEnvPacket.GatheringCounts(Config.GatheringCounts));
        Send(UserEnvPacket.LoadClaimedRewards(Player.Value.Unlock.MasteryRewardsClaimed));
        Send(UserEnvPacket.FamePoints(Player.Value.Unlock.FamePoints));
        Send(FishingPacket.LoadAlbum(Player.Value.Unlock.FishAlbum.Values));
        Pet?.Load();
        Send(PetPacket.LoadCollection(Player.Value.Unlock.Pets));
        Send(LegionBattlePacket.Load(ServerTableMetadata.TimeEventTable.WorldBoss));
        try {
            TimeEventResponse bossResponse = World.TimeEvent(new TimeEventRequest {
                GetActiveWorldBosses = new TimeEventRequest.Types.GetActiveWorldBosses(),
            });
            if (bossResponse.ActiveWorldBosses.Count > 0) {
                var bossGroups = new List<ICollection<MapWorldBoss>>();
                foreach (TimeEventResponse.Types.ActiveWorldBoss active in bossResponse.ActiveWorldBosses) {
                    if (!ServerTableMetadata.TimeEventTable.WorldBoss.TryGetValue(active.MetadataId, out WorldBossMetadata? bossMetadata)) {
                        continue;
                    }
                    int bossNpcId = bossMetadata.NpcIds.Length > 0 ? bossMetadata.NpcIds[0] : 0;
                    int bossMapId = bossMetadata.TargetMapIds.Length > 0 ? bossMetadata.TargetMapIds[0] : 0;
                    bossGroups.Add(active.AliveChannels.Select(ch => new MapWorldBoss(bossNpcId, bossMapId, (short) ch, active.SpawnTimestamp)).ToList());
                }
                Send(WorldMapPacket.Load(bossGroups, []));
            }
        } catch (RpcException ex) {
            Logger.Warning(ex, "Failed to fetch active field bosses");
        }
        Config.LoadCharacterAbilities();
        Config.LoadKeyTable();
        Send(GuideRecordPacket.Load(Config.GuideRecords));
        // No item starts an attendance event here, so the list is empty; KMS2 sends it after the guide records.
        Send(AttendancePacket.StartedEvents([]));
        // DailyWonder*
        GameEvent.Load();
        Send(GameEventPacket.Load(server.GetEvents().Where(SendsEvent).ToArray()));
        Send(BannerListPacket.Load(server.GetSystemBanners()));
        Dungeon.Load();
        Send(InGameRankPacket.Load());
        // The pet auto flag, on as the client starts with it; it is not stored here.
        Send(PetPacket.AutoChanged(true));
        Send(FieldEnterPacket.Request(Player));
        Party = new PartyManager(World, this);
        Send(HomeCommandPacket.LoadHome(AccountId, db.GetLastHomeCommend(AccountId)));
        // ResponseCube
        // Mentor
        Send(MentorPacket.MyList());
        Send(MentorPacket.Unknown12());
        Config.LoadChatStickers();
        // Mail
        Mail.Notify(true);
        // BypassKey
        // AH
        Config.LoadWardrobe();

        // Online Notifications

        return true;
    }

    private long resummonPetUid;
    // The client was told the pet is away (a field that allows no pet); sent once until the pet is back.
    private bool resummonUnSummonSent;
    // The pet to bring back after a move to another channel or game server (MigrateOutRequest.pet_uid).
    public long SummonedPetUid => Pet?.Pet.Uid ?? resummonPetUid;

    private void LeaveField() {
        InteractStarted.Clear();
        CubeStarted.Clear();
        Array.Clear(ItemLockStaging);
        Array.Clear(DismantleStaging);
        DismantleOpened = false;
        Trade?.Dispose();
        Storage?.Dispose();
        // A summoned pet leaves the field with its owner and is summoned again in the next one (EnterField).
        if (Pet != null) {
            resummonPetUid = Pet.Pet.Uid;
            resummonUnSummonSent = false;
        }
        Pet?.Dispose(fieldChange: true);
        Instrument = null;
        GuideObject = null;
        HeldCube = null;
        HeldLiftup = null;
        NpcScript = null;
        MiniGameRecord = null;

        Buffs?.LeaveField();
        Marriage?.ClearProposal();

        if (Field != null) {
            Scheduler.Stop();
            Housing?.LeaveBuildField();
            Field.RemovePlayer(Player.ObjectId, out _);
        }
    }

    // The field this session left for the one it enters (map 0: none, the session arrived from elsewhere), and
    // the owner when it was a home.
    private (int MapId, long OwnerId) leftField;

    public bool PrepareField(int mapId, int portalId = -1, long ownerId = 0, int roomId = 0, in Vector3 position = default, in Vector3 rotation = default) {
        return PrepareFieldInternal(mapId, out _, portalId, ownerId, roomId, position, rotation);
    }

    public bool PrepareField(int mapId, [NotNullWhen(true)] out FieldManager? newField, int portalId = -1, long ownerId = 0, int roomId = 0, in Vector3 position = default, in Vector3 rotation = default) {
        return PrepareFieldInternal(mapId, out newField, portalId, ownerId, roomId, position, rotation);
    }

    private bool PrepareFieldInternal(int mapId, out FieldManager? newField, int portalId, long ownerId, int roomId, in Vector3 position, in Vector3 rotation) {
        // If entering home without instanceKey set, default to own home.
        if (mapId == Player.Value.Home.Indoor.MapId && ownerId == 0) {
            ownerId = AccountId;
        }

        // A field with enterAdminLevel above 0 takes GM accounts only, whatever the way in (portal, taxi,
        // command, login into a saved field). The client only tests it for the taxi, in fielddatacli.cpp.
        if (MapMetadata.TryGet(mapId, out MapMetadata? target)
            && !FieldEntryRules.AdminFieldAllowed(target.Limit.EnterAdminLevel, GmCheck.IsGm(Player.Value.Account.AdminPermissions))) {
            Send(NoticePacket.MessageBox(StringCode.s_field_limit_admin));
            newField = null;
            return false;
        }

        // A map the played client does not have is closed on every way in: the client hangs on it. The
        // client has no text for this, so the player gets a chat line naming the map and stays in the field.
        if (target is { Property.NotInPlayedClient: true }) {
            Send(NoticePacket.Message(FieldEntryRules.NotInPlayedClientNotice(target.Id, target.Name)));
            newField = null;
            return false;
        }

        if (Field is DungeonFieldManager dungeonField) {
            if (dungeonField.Lobby!.RoomFields.TryGetValue(mapId, out DungeonFieldManager? nextDungeonField)) {
                newField = nextDungeonField;
            } else if (mapId == dungeonField.Lobby.MapId) {
                newField = dungeonField.Lobby;
            } else {
                Migrate(mapId);
                newField = null;
                return false;
            }
        } else {
            newField = FieldFactory.Get(mapId, ownerId: ownerId, roomId: roomId);
            if (newField == null) {
                return false;
            }
        }

        State = SessionState.ChangeMap;
        leftField = (Field?.MapId ?? 0, Field is HomeFieldManager leftHome ? leftHome.OwnerId : 0);
        bool leftDead = Player.IsDead;
        LeaveField();

        Field = newField;
        Player.Dispose();
        Player = Field.SpawnPlayer(this, Player, portalId, position, rotation);
        Stat health = Stats.Values[BasicAttribute.Health];
        if (RevivalRules.FillsHpOnFieldEntry(leftDead, health.Current)) {
            health.Current = health.Total;
            Player.DeathState = DeathState.Alive;
        }
        Config.Skill.UpdatePassiveBuffs();
        Player.Buffs.LoadFieldBuffs();
        Player.CheckRegen();

        return true;
    }

    public bool EnterField() {
        if (Field == null) return false;

        Config.LoadHotBars();
        Field.OnAddPlayer(Player);
        Scheduler.Start();
        State = SessionState.Connected;
        // A field that allows no pet keeps the uid for the next field that does.
        if (resummonPetUid != 0 && Field.Metadata.Limit.Pet) {
            // Before the stats packets below: the summon rebuilds the owner's stats with the pet's.
            long petUid = resummonPetUid;
            resummonPetUid = 0;
            resummonUnSummonSent = false;
            PacketHandlers.PetHandler.SummonPet(this, petUid);
        } else if (resummonPetUid != 0 && !resummonUnSummonSent) {
            resummonUnSummonSent = true;
            // The quiet unsummon in LeaveField left the client's summon uid set; clear it now that this
            // field keeps the pet away (the client prints the dismiss line here).
            Send(PetPacket.UnSummon(Player.ObjectId, resummonPetUid));
        }
        Housing.EnterBuildField();

        PlayerInfo.SendUpdate(new PlayerUpdateRequest {
            AccountId = AccountId,
            CharacterId = CharacterId,
            MapId = Field.MapId,
            RoomId = Field.RoomId,
            Async = true,
        });

        Send(StatsPacket.Init(Player));
        Field.Broadcast(StatsPacket.Update(Player), Player.Session);

        // The first visit of a map pays exp. It comes after the player is added to the field: the client
        // has no local player before its own FieldAddUser, so it shows no exp text for an earlier ExpUp, and
        // a level-up is sent to the field's players, which the entering player is not before OnAddPlayer.
        bool firstVisit = !Player.Value.Unlock.Maps.Contains(Player.Value.Character.MapId);
        ExpType? exploreExp = FieldEntryRules.ExploreExp(firstVisit, Field.Metadata.Property.ExploreType);
        if (exploreExp != null) {
            Exp.AddExp(exploreExp.Value);
        }
        // A stamp quest whose completion failed before (QuestManager.CompleteStampQuests) is tried again.
        Quest.CompleteStampQuests();
        if (firstVisit) {
            ConditionUpdate(ConditionType.explore_continent, codeLong: (int) Field.Metadata.Property.Continent);
            ConditionUpdate(ConditionType.continent, codeLong: (int) Field.Metadata.Property.Continent);
            ConditionUpdate(ConditionType.explore, codeLong: Field.MapId);
        }
        Player.Value.Unlock.Maps.Add(Player.Value.Character.MapId);

        Send(AdminPacket.Level(AdminLevel.Of(Player.AdminPermissions)));

        //TODO: Save current hp/sp/ep in memory. This will help determine if player is dead upon login.
        var pWriter = Packet.Of(SendOp.UserState);
        pWriter.WriteInt(Player.ObjectId);
        pWriter.Write<ActorState>(ActorState.Fall);
        Send(pWriter);

        Send(EmotePacket.Load(Player.Value.Unlock.Emotes.Select(id => new Emote(id)).ToList()));
        Config.InitMacros();
        Config.LoadSkillCooldowns();
        Dungeon.LoadField();
        Marriage.Load();

        Send(CubePacket.DesignRankReward(Player.Value.Home));
        Send(CubePacket.UpdateProfile(Player, true));
        Send(CubePacket.ReturnMap(Player.Value.Character.ReturnMaps.Peek()));

        Config.LoadLapenshard();
        Config.LoadRevival();
        Config.LoadStatAttributes();
        Config.LoadSkillPoints();
        Player.Buffs.LoadFieldBuffs();

        TimeEventResponse globalEventResponse = World.TimeEvent(new TimeEventRequest {
            GetGlobalPortal = new TimeEventRequest.Types.GetGlobalPortal(),
        });

        if (globalEventResponse.Error != 0 && globalEventResponse.GlobalPortalInfo != null) {
            if (ServerTableMetadata.TimeEventTable.GlobalPortal.TryGetValue(globalEventResponse.GlobalPortalInfo.MetadataId, out GlobalPortalMetadata? portal)) {
                Send(GlobalPortalPacket.Announce(portal, globalEventResponse.GlobalPortalInfo.EventId));
            }
        }

        Send(PremiumCubPacket.Activate(Player.ObjectId, Player.Value.Account.PremiumTime));
        Send(PremiumCubPacket.LoadItems(Player.Value.Account.PremiumRewardsClaimed));
        ConditionUpdate(ConditionType.map, codeLong: Player.Value.Character.MapId);
        ConditionUpdate(ConditionType.job_change, codeLong: (int) Player.Value.Character.Job.Code());
        if (Field is HomeFieldManager home
            && FieldEntryRules.VisitsOtherHome(home.OwnerId, home.MapId, AccountId, leftField.MapId, leftField.OwnerId)) {
            ConditionUpdate(ConditionType.enter_otherhouse);
        }

        SessionSave();

        // Update the client with the latest channel list.
        ChannelsResponse response = World.Channels(new ChannelsRequest());
        Send(ChannelPacket.Load(response.Channels));
        Send(ServerListPacket.Load(Target.SERVER_NAME, [new IPEndPoint(Target.LoginIp, Target.LoginPort)], response.Channels));
        return true;
    }

    // The saved return field, or the default one when the saved field is unknown or closed to this account.
    public int ReturnMapId() {
        int mapId = Player.Value.Character.ReturnMaps.Peek();
        bool known = MapMetadata.TryGet(mapId, out MapMetadata? metadata);
        return FieldEntryRules.ReturnMap(mapId, known, metadata?.Limit.EnterAdminLevel ?? 0,
            GmCheck.IsGm(Player.Value.Account.AdminPermissions), Constant.DefaultReturnMapId,
            metadata?.Property.NotInPlayedClient ?? false);
    }

    public void ReturnField() {
        Player player = Player.Value;
        if (player.Home.IsHomeSetup && !Player.Field.Plots.IsEmpty && (Player.Session.Housing.GetFieldPlot()?.IsPlanner ?? false)) {
            MigrateToPlanner(PlotMode.Normal);
            return;
        }

        Character character = player.Character;
        int mapId = character.ReturnMaps.Peek();
        Vector3 position = character.ReturnPosition;

        if (ReturnMapId() != mapId) {
            mapId = Constant.DefaultReturnMapId;
            position = default;
        }

        character.ReturnPosition = default;

        if (character.MapId is Constant.DefaultHomeMapId) {
            Migrate(mapId);
            return;
        }

        if (Guild.Guild is not null) {
            TableMetadata.GuildTable.Houses.TryGetValue(Guild.Guild.HouseRank, out IReadOnlyDictionary<int, GuildTable.House>? houseRank);

            if (houseRank is not null) {
                houseRank.TryGetValue(Guild.Guild.HouseTheme, out GuildTable.House? house);

                if (house?.MapId == character.MapId) {
                    Migrate(mapId);
                    return;
                }
            }
        }

        // If returning to a map, pass in the spawn point.
        Send(PrepareField(mapId, position: position)
            ? FieldEnterPacket.Request(Player)
            : FieldEnterPacket.Error(MigrationError.s_move_err_default));
    }

    // Environment.TickCount64 of the last minute counted for play time (ConditionType.playtime); the session's start
    // at first.
    public long PlayTimeTick = Environment.TickCount64;

    /// <summary>
    /// Updates game condition values for achievement and quest.
    /// </summary>
    /// <param name="conditionType">Condition Type to update</param>
    /// <param name="counter">Condition value to progress by. Default is 1.</param>
    /// <param name="targetString">condition target parameter in string.</param>
    /// <param name="targetLong">condition target parameter in long.</param>
    /// <param name="codeString">condition code parameter in string.</param>
    /// <param name="codeLong">condition code parameter in long.</param>
    public void ConditionUpdate(ConditionType conditionType, long counter = 1, string targetString = "", long targetLong = 0, string codeString = "", long codeLong = 0) {
        Achievement.Update(conditionType, counter, targetString, targetLong, codeString, codeLong);
        Quest.Update(conditionType, counter, targetString, targetLong, codeString, codeLong);
    }

    public IList<GameEvent> FindEvent(GameEventType type) => server.FindEvent(type);

    public GameEvent? FindEvent(int id) => server.FindEvent(id);
    public IEnumerable<GameEvent> Events => server.GetEvents();

    // See ReactorRules.SendsEvent: a Reactor event no reactor row names is kept from the client.
    public bool SendsEvent(GameEvent gameEvent) => ReactorRules.SendsEvent(gameEvent.Metadata.Type, gameEvent.Id, TableMetadata.ReactorTable);

    public IEnumerable<PremiumMarketItem> GetPremiumMarketItems(params int[] tabIds) => server.GetPremiumMarketItems(tabIds);

    public PremiumMarketItem? GetPremiumMarketItem(int id, int subId = 0) => server.GetPremiumMarketItem(id, subId);

    public RewardRecord GetRewardContent(int id) {
        if (!TableMetadata.RewardContentTable.BaseEntries.TryGetValue(id, out RewardContentTable.Base? baseMetadata)) {
            return new RewardRecord();
        }

        long exp = 0;
        if (baseMetadata.ExpTableId > 0) {
            if (baseMetadata.ExpTableId > 100000 && TableMetadata.RewardContentTable.ExpStaticEntries.TryGetValue(baseMetadata.ExpTableId, out exp)) {
                // Static exp entry found
            } else if (TableMetadata.ExpTable.ExpBase.TryGetValue(baseMetadata.ExpTableId, out IReadOnlyDictionary<int, long>? expTable)) {
                exp = expTable[Player.Value.Character.Level];
            }
            if (baseMetadata.ExpFactor > 0f) {
                exp = (long) (exp * baseMetadata.ExpFactor);
            }
            Exp.AddExp(exp);
        }

        long meso = 0;
        if (baseMetadata.MesoTableId > 0) {
            if (baseMetadata.MesoTableId > 100000 && TableMetadata.RewardContentTable.MesoStaticEntries.TryGetValue(baseMetadata.MesoTableId, out meso)) {
                // Static meso entry found
            } else if (TableMetadata.RewardContentTable.MesoEntries.TryGetValue(baseMetadata.MesoTableId, out Dictionary<int, long>? mesoTable)) {
                if (!mesoTable.TryGetValue(Player.Value.Character.Level, out meso)) {
                    Logger.Error("Failed to find meso for level {Level} in table {TableId}", Player.Value.Character.Level, baseMetadata.MesoTableId);
                }
            }
            if (baseMetadata.MesoFactor > 0f) {
                meso = (long) (meso * baseMetadata.MesoFactor);
            }
            Currency.Meso += meso;
        }

        long prestigeExp = 0;
        if (baseMetadata.PrestigeExpTableId > 0 && ServerTableMetadata.PrestigeIdExpTable.Entries.TryGetValue(baseMetadata.PrestigeExpTableId, out PrestigeIdExpTable.Entry? prestigeExpTable)) {
            // TODO: Prestige exp given this way is fixed. Need to revise how exp is given.
        }

        List<Item> items = [];
        if (baseMetadata.ItemTableId > 0 && TableMetadata.RewardContentTable.ItemEntries.TryGetValue(baseMetadata.ItemTableId, out RewardContentTable.Item? itemMetadata)) {
            foreach (RewardContentTable.Item.Data data in itemMetadata.ItemData) {
                if ((data.MinLevel > 0 && Player.Value.Character.Level < data.MinLevel) ||
                    (data.MaxLevel > 0 && Player.Value.Character.Level > data.MaxLevel)) {
                    continue;
                }
                foreach (RewardItem rewardItem in data.RewardItems) {
                    Item? item = Field?.ItemDrop.CreateItem(rewardItem.ItemId, rewardItem.Rarity, rewardItem.Amount);
                    if (item != null) {
                        items.Add(item);
                    }
                }
            }
            foreach (Item item in items) {
                if (!Item.Inventory.Add(item, true)) {
                    Item.MailItem(item);
                }
            }
        }
        return new RewardRecord(items, exp, prestigeExp, meso);
    }

    public void ChannelBroadcast(ByteWriter packet) {
        server.Broadcast(packet);
    }

    public void DailyReset() {
        // The new day's play time starts now, as KMS2 sends it at the reset; the client counts no play seconds
        // from a start on an earlier day.
        DateTime playStart = DateTime.Now;
        GameEvent.ResetPlayTime(playStart);
        Send(TimeSyncPacket.Set(playStart));
        // Gathering counts reset
        Config.GatheringCounts.Clear();
        Send(UserEnvPacket.GatheringCounts(Config.GatheringCounts));
        // Death Counter
        Config.AddInstantReviveCount(-1);
        // Premium Rewards Claimed
        Player.Value.Account.PremiumRewardsClaimed.Clear();
        Send(PremiumCubPacket.LoadItems(Player.Value.Account.PremiumRewardsClaimed));
        // Prestige
        Player.Value.Account.PrestigeExp = Player.Value.Account.PrestigeCurrentExp;
        Player.Value.Account.PrestigeLevelsGained = 0;
        Send(PrestigePacket.Load(Player.Value.Account));
        // Home
        Player.Value.Home.DecorationRewardTimestamp = 0;
        Send(CubePacket.DesignRankReward(Player.Value.Home));
        // Meso Market
        Player.Value.Account.MesoMarketListed = 0;
        Send(MesoMarketPacket.Quota(Player.Value.Account.MesoMarketListed, Player.Value.Account.MesoMarketPurchased));
        // Shop restock
        Shop.DailyReset();
        // Dungeon daily clears
        Dungeon.ResetDailyClears();
        // Alliance missions: the reset drops held quests, so it runs on the session's own queue, where the packets
        // that read the quests are handled, with the time of the reset.
        DateTime resetTime = DateTime.Now;
        Scheduler.Schedule(() => Quest.ResetAllianceMissions(resetTime));
        // Long-term attendance: the day's attend point, on the session's own queue like the handler's grant.
        Scheduler.Schedule(() => GameEvent.GrantLongTermAttendPoints());
        // Pet fusion count
        Player.Value.Character.PetFusionCount = 0;
        Player.Value.Character.PetFusionTime = DateTime.Now.ToEpochSeconds();
        Send(PetPacket.FusionCount(0));
    }

    public void WeeklyReset() {
        // Prestige Rewards
        Player.Value.Account.PrestigeRewardsClaimed.Clear();
        Send(PrestigePacket.Load(Player.Value.Account));
        // Dungeon enter limits
        Dungeon.UpdateDungeonEnterLimit();
        // Dungeon weekly clears
        Dungeon.ResetWeeklyClears();
        // Shop restock
        Shop.WeeklyReset();
    }

    public void MonthlyReset() {
        // Meso Market
        Player.Value.Account.MesoMarketPurchased = 0;
        Send(MesoMarketPacket.Quota(Player.Value.Account.MesoMarketListed, Player.Value.Account.MesoMarketPurchased));
    }

    public void MigrateToPlanner(PlotMode plotMode) {
        try {
            var request = new MigrateOutRequest {
                AccountId = AccountId,
                CharacterId = CharacterId,
                MachineId = MachineId.ToString(),
                Server = Server.World.Service.Server.Game,
                MapId = Constant.DefaultHomeMapId,
                OwnerId = AccountId,
                InstancedContent = true,
                Type = (World.Service.MigrationType) plotMode,
                PetUid = SummonedPetUid,
                RoomId = plotMode is not PlotMode.Normal ? -1 : 0,
            };

            MigrateOutResponse response = World.MigrateOut(request);
            if (!PlayerWarpUtil.TryEndpoint(response.IpAddress, response.Port, out IPEndPoint? endpoint)) {
                Logger.Error("{Where}: the World server answered with the address '{Address}' port {Port}", "Home planner", response.IpAddress, response.Port);
                Send(MigrationPacket.GameToGameError(MigrationError.s_move_err_default));
                return;
            }
            Send(MigrationPacket.GameToGame(endpoint, response.Token, Constant.DefaultHomeMapId));
            State = SessionState.ChangeMap;
            MigrationSave();
        } catch (RpcException ex) {
            Send(MigrationPacket.GameToGameError(MigrationError.s_move_err_default));
            Send(NoticePacket.Disconnect(new InterfaceText(ex.Message)));
        } finally {
            Disconnect();
        }
    }

    // beforeMove runs right before the migrate packet, only when the move is made (not when the map is refused or
    // the World server gives no address).
    public void Migrate(int mapId, long ownerId = 0, Action? beforeMove = null) {
        // Not to a map the played client does not have (see PrepareFieldInternal).
        if (MapMetadata.TryGet(mapId, out MapMetadata? target) && target.Property.NotInPlayedClient) {
            Send(NoticePacket.Message(FieldEntryRules.NotInPlayedClientNotice(target.Id, target.Name)));
            return;
        }

        bool isInstanced = ServerTableMetadata.InstanceFieldTable.Entries.ContainsKey(mapId);

        try {
            var request = new MigrateOutRequest {
                AccountId = AccountId,
                CharacterId = CharacterId,
                MachineId = MachineId.ToString(),
                Server = Server.World.Service.Server.Game,
                MapId = mapId,
                OwnerId = ownerId,
                InstancedContent = isInstanced,
                PetUid = SummonedPetUid,
            };

            MigrateOutResponse response = World.MigrateOut(request);
            if (!PlayerWarpUtil.TryEndpoint(response.IpAddress, response.Port, out IPEndPoint? endpoint)) {
                Logger.Error("{Where}: the World server answered with the address '{Address}' port {Port}", "Field migration", response.IpAddress, response.Port);
                Send(MigrationPacket.GameToGameError(MigrationError.s_move_err_default));
                return;
            }
            beforeMove?.Invoke();
            Send(MigrationPacket.GameToGame(endpoint, response.Token, mapId));

            if (isInstanced) {
                Player.Value.Character.ReturnChannel = Player.Value.Character.Channel;
            } else {
                Player.Value.Character.MapId = mapId;
                Player.Value.Character.ReturnChannel = 0;
            }

            State = SessionState.ChangeMap;
            MigrationSave();
        } catch (RpcException ex) {
            Send(MigrationPacket.GameToGameError(MigrationError.s_move_err_default));
            Send(NoticePacket.Disconnect(new InterfaceText(ex.Message)));
        } finally {
            Disconnect();
        }
    }

    private void AcquireLock(long accountId, int maxRetries = 3) {
        int retryCount = 0;
        const int backoffMs = 500;

        while (retryCount < maxRetries) {
            LockResponse? response;
            try {
                response = World.AcquireLock(new LockRequest {
                    AccountId = accountId,
                });
            } catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable) {
                // A world that is down (stopped before this server) holds no locks and grants none. The save goes on
                // without the lock, as after the retries below; a throw here would skip the save.
                Logger.Warning("World unavailable; lock for account {AccountId} not taken", accountId);
                return;
            }

            if (string.IsNullOrEmpty(response.Error)) {
                return;
            }

            retryCount++;
            Thread.Sleep(backoffMs);
        }

        Logger.Error("Failed to acquire lock for account {AccountId} after {MaxRetries} retries", accountId, maxRetries);
    }

    private void ReleaseLock(long accountId) {
        try {
            LockResponse response = World.ReleaseLock(new LockRequest {
                AccountId = accountId,
            });
            if (!string.IsNullOrEmpty(response.Error)) {
                Logger.Warning("Failed to release lock for account {AccountId}: {ErrorMessage}", accountId, response.Error);
            }
        } catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable) {
            // The world keeps its locks in memory: a world that is down (stopped before this server) holds none.
            Logger.Warning("World unavailable; lock for account {AccountId} not released", accountId);
        } catch (RpcException ex) {
            Logger.Error(ex, "Failed to release lock for account {AccountId}", accountId);
        }
    }

    #region Dispose
    ~GameSession() => Dispose(false);

    protected override void Dispose(bool disposing) {
        // Ensure dispose is only run once
        if (Interlocked.CompareExchange(ref gameDisposeState, 1, 0) != 0) return;

        // Snapshot values needed after teardown
        long fieldTickSnapshot = Field?.FieldTick ?? Environment.TickCount64;

        if (State == SessionState.Connected) {
            PlayerInfo.SendUpdate(new PlayerUpdateRequest {
                AccountId = AccountId,
                CharacterId = CharacterId,
                LastOnlineTime = DateTime.UtcNow.ToEpochSeconds(),
                MapId = 0,
                Channel = -1,
                Async = false,
            });

            Party.CheckDisband();
        }

        // The character was never loaded (an arrival with a used or unknown token): there is nothing to save.
        if (Player == null) {
            try {
                Scheduler.Stop();
                if (OnLoop != null) OnLoop -= Scheduler.InvokeAll;
                server.OnDisconnected(this);
                State = SessionState.Disconnected;
                base.Dispose(disposing);
            } catch (Exception ex) {
                Logger.Error(ex, "Error during cleanup of a session without a character");
            } finally {
                Interlocked.Exchange(ref gameDisposeState, 2);
            }
            return;
        }

        try {
            UserRecallUtil.DropPending(this);
        } catch (Exception ex) {
            Logger.Error(ex, "Error dropping the recall invite of {Player}", PlayerName);
        }

        try {
            Scheduler.Stop();
            if (OnLoop != null) OnLoop -= Scheduler.InvokeAll;
            server.OnDisconnected(this);
            LeaveField();
            Player.Value.Character.Channel = -1;
            Player.Value.Account.Online = false;
            State = SessionState.Disconnected;

            // Early base dispose to stop further network sends
            base.Dispose(disposing);

            // Cache config & persistence
            SaveCacheConfig();
            MigrationSave();
        } catch (Exception ex) {
            Logger.Error(ex, "Error during session cleanup for {Player}", PlayerName);
        } finally {
            SafeDispose(Guild);
            SafeDispose(Buddy);
            SafeDispose(Party);
            foreach ((int groupChatId, GroupChatManager groupChat) in GroupChats) {
                try { groupChat.CheckDisband(); } catch (Exception ex) { Logger.Error(ex, "Error disbanding group chat {Id} for {Player}", groupChatId, PlayerName); }
            }
            foreach ((long clubId, ClubManager club) in Clubs) {
                SafeDispose(club);
            }
            try { Player.Dispose(); } catch (Exception ex) { Logger.Error(ex, "Error disposing player for {Player}", PlayerName); }
            Interlocked.Exchange(ref gameDisposeState, 2);
        }
        return;

        void SafeDispose(IDisposable? disp) {
            if (disp == null) return;
            try { disp.Dispose(); } catch (Exception ex) { Logger.Error(ex, "Error disposing component for {Player}", PlayerName); }
        }

        void SaveCacheConfig() {
            List<Buff> buffs = Buffs.GetSaveCacheBuffs();
            IList<SkillCooldown> skillCooldowns = Config.GetCurrentSkillCooldowns();

            long stopTime = DateTime.Now.ToEpochSeconds();
            long fieldTick = fieldTickSnapshot;

            try {
                PlayerConfigResponse _ = World.PlayerConfig(new PlayerConfigRequest {
                    Save = new PlayerConfigRequest.Types.Save {
                        Buffs = {
                            buffs
                                .Where(buff => buff.EndTick - fieldTick > 0)
                                .Select(buff => new BuffInfo {
                                    Id = buff.Id,
                                    Level = buff.Level,
                                    MsRemaining = (buff.EndTick - fieldTick).Truncate32(),
                                    Stacks = buff.Stacks,
                                    Enabled = buff.Enabled,
                                    StopTime = stopTime,
                                }),
                        },
                        SkillCooldowns = {
                            skillCooldowns
                                .Where(cooldown => cooldown.EndTick - fieldTick > 0)
                                .Select(cooldown => new SkillCooldownInfo {
                                    SkillId = cooldown.SkillId,
                                    SkillLevel = cooldown.Level,
                                    GroupId = cooldown.GroupId,
                                    MsRemaining = (cooldown.EndTick - fieldTick).Truncate32(),
                                    StopTime = stopTime,
                                    Charges = cooldown.Charges,
                                }),
                        },
                        DeathInfo = new DeathInfo {
                            Count = Config.DeathCount,
                            MsRemaining = (int) (Config.DeathPenaltyEndTick - fieldTick),
                            StopTime = stopTime,
                        },
                    },
                    RequesterId = CharacterId,
                });
            } catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable) {
                // The world keeps the buffs and cooldowns in memory: a world that is down has none left to keep them in.
                Logger.Warning("World unavailable; buffs of {Player} not kept", PlayerName);
            } catch (Exception ex) { Logger.Error(ex, "Error saving buffs for {Player}", PlayerName); }
        }
    }
    #endregion

    public override string ToString() {
        return $"GameSession: {Player?.Value?.Character?.Name} ({CharacterId}) [{State}] // Account: {AccountId}, Machine: {MachineId}, Channel: {Player?.Value?.Character?.Channel}, Map: {Player?.Value?.Character?.MapId}]";
    }

    public void MigrationSave() {
        if (preMigrationSaved) return;

        try {
            AcquireLock(AccountId, 5);
            Save();
            preMigrationSaved = true;
        } catch (Exception ex) {
            Logger.Error(ex, "MigrationSave failed AccountId={AccountId} CharacterId={CharacterId}", AccountId, CharacterId);
        } finally {
            ReleaseLock(AccountId);
        }
    }

    public void SessionSave() {
        try {
            AcquireLock(AccountId, 5);
            Save();
        } catch (Exception ex) {
            Logger.Error(ex, "SessionSave failed AccountId={AccountId} CharacterId={CharacterId}", AccountId, CharacterId);
        } finally {
            ReleaseLock(AccountId);
        }
    }

    /// <summary>
    /// Stores the currency right after a purchase, together with any rows of the same purchase,
    /// under the account lock. Only the currency and the given rows are written, not the whole session.
    /// </summary>
    public void SavePurchase(Action<GameStorage.Request>? alsoSave = null) {
        try {
            AcquireLock(AccountId);
            using GameStorage.Request db = GameStorage.Context();
            db.BeginTransaction();
            if (!db.SaveCurrency(Player)) {
                Logger.Error("SavePurchase: currency not saved AccountId={AccountId} CharacterId={CharacterId}", AccountId, CharacterId);
            }
            alsoSave?.Invoke(db);
            db.Commit();
        } catch (Exception ex) {
            Logger.Error(ex, "SavePurchase failed AccountId={AccountId} CharacterId={CharacterId}", AccountId, CharacterId);
        } finally {
            ReleaseLock(AccountId);
        }
    }

    // Don't call this directly, since it does not acquire the account lock.
    private void Save() {
        using GameStorage.Request db = GameStorage.Context();
        db.BeginTransaction();
        db.SavePlayer(Player);
        TrySaveComponent(UgcMarket.Save);
        TrySaveComponent(Config.Save);
        TrySaveComponent(Shop.Save);
        TrySaveComponent(Item.Save);
        TrySaveComponent(Survival.Save);
        TrySaveComponent(Housing.Save);
        TrySaveComponent(GameEvent.Save);
        TrySaveComponent(Achievement.Save);
        TrySaveComponent(Quest.Save);
        TrySaveComponent(Dungeon.Save);
        db.Commit();
        db.SaveChanges();
        return;

        void TrySaveComponent(Action<GameStorage.Request> action) {
            try { action(db); } catch (Exception ex) { Logger.Error(ex, "Error saving component for {Player}", PlayerName); }
        }
    }
}
