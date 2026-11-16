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
using Maple2.Model.Game.Pvp;
using Maple2.Model.Game.Event;
using Maple2.Model.Validators;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Helpers;
using Maple2.Server.Core.Network;
using Maple2.Server.Core.Packets;
using Maple2.Server.Core.Sync;
using Maple2.Server.Game.Commands;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.PacketHandlers;
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

    // The client echoes the key of the RequestFieldEnter it loaded in ResponseFieldEnter and RequestLoadUgcMap.
    public static bool FieldKeyMatches(int key) => key == FIELD_KEY;

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
    private volatile bool preMigrationSaved;
    // How long a move's save waits for a handler the receive thread runs to end (MigrationSave).
    internal static readonly TimeSpan InlineGateWait = TimeSpan.FromSeconds(5);
    // The session made the save of a move to another server or to the character select, and has not been refused it:
    // a packet handled after it would change state no save stores (FieldManager.ProcessPackets drops it).
    internal bool MigrationSaved => preMigrationSaved;
    // Every packet after that save is dropped, also those of handlers that run on the receive thread; and every
    // packet once the session closes (ClosePackets).
    public override bool DropsPackets => preMigrationSaved || packetsClosed;
    private volatile bool packetsClosed;
    // How long a closing session waits for its field's running packet batch to end (ClosePackets).
    internal static TimeSpan PacketBatchWait { get; set; } = TimeSpan.FromSeconds(10); // tests shorten it
    // How long the close waits in all for its LeaveField on the field's thread (CloseLeaveField) and for a
    // receive-thread handler before the last save (SaveBeforeLeaving): longer than a handler's own bounded waits (a
    // move's save waits up to ResetWait for a reset, on each of its two tries). Past it the thread is stuck (for every
    // player of the field), and the close goes on beside it, logged. A kick's wait for the close
    // (ChannelService.CloseWait) holds it.
    internal static TimeSpan CloseWait { get; set; } = TimeSpan.FromSeconds(130); // tests shorten it
    // The end of the close's CloseWait (Environment.TickCount64), set when the close first waits on it.
    private long closeWaitEnd;
    // The account's and character's item rows, held from an entry that took the account lock (see EnterServer).
    private IDisposable? heldRows;
    // The account's session generation this entry claimed (GameStorage.ClaimSession): a save writes only while the
    // account's is still this one. 0 before the load (a session without a load has nothing to save).
    private long sessionGen;
    // EnterServer finished: the World's cache config (buffs, cooldowns, death) was loaded and the session played, so
    // Dispose stores it back. A refused entry leaves the World's copy as it was.
    private bool entered;
    // The timed save while the player stays on one map (TimedSave): a part that cannot be stored is found within
    // this time, not only at the next field entry.
    private static readonly TimeSpan TimedSaveInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TimedSaveRetryDelay = TimeSpan.FromSeconds(2);
    // How long a save that cannot come back later waits for a reset being stored (GetResetStamps): a daily reset over
    // 50k characters holds its lock about 1 to 5 s.
    internal static TimeSpan ResetWait { get; set; } = TimeSpan.FromSeconds(60); // tests shorten it
    // A save slower than this is logged (one line), so a slow database shows before saves start to fail.
    private const long SlowSaveMs = 500;
    // The session's first save is logged (one line): its time and the item rows it wrote (rows of items unchanged
    // since their load are not written, see GameStorage.SaveItems).
    private bool firstSaveLogged;
    private int lastSaveItemRows;

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
    // When SkillMacro 0 was last answered (0 before any): an empty macro save is taken only some time after it.
    public long SkillMacroLoadTick;
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
    // The character's last save before this entry stamped it (epoch seconds, 0 for a character never saved): how
    // long it was away, which the ReturnUser season's mentee candidacy reads (MentoringManager.Load).
    public long EntryLastSave { get; set; }
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
    // The field tick of the last InteractObject start packet, null for none in this field; same thread rule.
    public long? InteractStartTick;
    // The highest running MasteryReduce rate at that start: the client works a gather's cast out as it starts.
    public int InteractStartMasteryReduce;
    // The farming and breeding cube ids of the last FunctionCube begin packet; same thread rule.
    public readonly HashSet<string> CubeStarted = [];
    // The field tick of the last StateReact packet, null for none in this field; same thread rule.
    public long? LastReactTick;
    // The attends at game start a reset being stored refused (AttendanceHandler), by event id, each with its try
    // again: run 2 s later on the field's queue, or at the next field entry once the session left that field.
    private readonly Dictionary<int, Action> attendRefused = [];
    // The recall offered to this player per RecallScrollType (party, guild, wedding); lock the array to use it.
    public readonly PendingRecall?[] Recalls = new PendingRecall?[3];

    // Closes the notice of a recall that can no longer be answered (RecallScroll mode 0); with a callerId other
    // than 0, only a recall from that caller. Sends nothing when no such recall is pending.
    public void CloseRecall(RecallScrollType type, long callerId = 0) {
        if (RecallSlots.Take(Recalls, type, callerId)) {
            Send(RecallScrollPacket.Close(type));
        }
    }

    // Stores an offered recall and shows its notice; a pending recall of that type it replaces is closed first.
    public void OfferRecall(RecallScrollType type, PendingRecall recall) {
        if (RecallSlots.Offer(Recalls, type, recall)) {
            Send(RecallScrollPacket.Close(type));
        }
        Send(RecallScrollPacket.Notify(type, recall.CallerName, recall.MapId, recall.EndTime));
    }

    // Closes the notices whose end time has passed (FieldPlayer.Update, once a second).
    public void CloseExpiredRecalls(long now) {
        foreach (RecallScrollType type in RecallSlots.TakeExpired(Recalls, now)) {
            Send(RecallScrollPacket.Close(type));
        }
    }
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
    public PcBangManager PcBang { get; set; } = null!;
    public MentoringManager Mentoring { get; set; } = null!;
    public AdventureProtectManager AdventureProtect { get; set; } = null!;

    public GameSession(TcpClient tcpClient, GameServer server, IComponentContext context) : base(tcpClient) {
        this.server = server;
        State = SessionState.ChangeMap;
        CommandHandler = context.Resolve<CommandRouter>(new NamedParameter("session", this));
        Scheduler = new EventQueue(Logger);
        Scheduler.ScheduleRepeated(() => Send(TimeSyncPacket.Request()), TimeSpan.FromSeconds(1));
        Scheduler.ScheduleRepeated(TstPollCommands, TimeSpan.FromSeconds(1)); // test hook
        Scheduler.ScheduleRepeated(TimedSave, TimedSaveInterval, skipFirst: true);

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

    // Everything the World server keeps about this player.
    private PlayerUpdateRequest WorldUpdate() {
        var playerUpdate = new PlayerUpdateRequest {
            AccountId = AccountId,
            CharacterId = CharacterId,
            LastOnlineTime = Player.Value.Character.LastOnlineTime,
        };
        playerUpdate.SetFields(UpdateField.All, Player.Value);
        playerUpdate.Health = new HealthUpdate {
            CurrentHp = Stats.Values[BasicAttribute.Health].Current,
            TotalHp = Stats.Values[BasicAttribute.Health].Total,
        };
        return playerUpdate;
    }

    // Reports this session to a World server that was restarted. False for a session that is not in the game yet
    // (its own login reports it). The party is asked for again: parties are not stored, so after a World restart
    // there is none and the member gets the disband answer (chosen here).
    public bool AnnounceToWorld(WorldClient world) {
        if (Player == null || Stats == null || Party == null || State != SessionState.Connected) {
            return false;
        }

        world.UpdatePlayer(WorldUpdate());
        if (Party.Id != 0) {
            PartyInfoResponse response = world.PartyInfo(new PartyInfoRequest { CharacterId = CharacterId });
            if (ChannelRegisterRules.PartyGone(Party.Id, response.Party?.Id ?? 0)) {
                // This runs on the World link's thread: the party is ended as a task of the player's field.
                int heldPartyId = Party.Id;
                FieldManager? field = Field;
                if (field == null) {
                    Party.Disband(notify: true);
                } else {
                    field.Scheduler.Schedule(() => {
                        if (Party != null && Party.Id == heldPartyId) {
                            Party.Disband(notify: true);
                        }
                    }, TimeSpan.Zero);
                }
            }
        }
        return true;
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
        bool locked = false;
        long lockToken = 0;
        try {
            // Taken: the channel this session came from has stored its save (its MigrationSave holds the lock), and
            // nothing else writes this account's and character's item rows while the session plays: the item loads
            // may keep the values they read (GameStorage.HoldOwners). Not taken (after the retries, or the world
            // down): another server may still write them, and no load of this session keeps any.
            locked = AcquireLock(AccountId, out lockToken, 5) == LockTake.Taken;
            // A running SendMail ReturnUser row mails a character that enters after its days away (KR 10100100:
            // 90), stored with the entry's stamp.
            IList<GameEvent> sendMail = FindEvent(GameEventType.SendMail);
            // A running ReturnUserYearRound row gives a returner its box (ReturnUserGift), stored in the same load.
            IList<GameEvent> yearRound = FindEvent(GameEventType.ReturnUserYearRound);
            // The resets the rows below hold: read first, with a share lock, in the load's transaction.
            resetsSeen = new ResetsSeen(db.GetResetStamps(ResetWait));
            // The claim before the load reads anything: a save of the session this one follows that still runs (its
            // MigrationSave without the lock, or past the lock's 30 s) is waited for, and one that comes later writes
            // nothing; the rows loaded below stay as read while this session plays.
            sessionGen = db.ClaimSession(AccountId);
            player = db.LoadPlayer(AccountId, CharacterId, objectId, GameServer.GetChannel(), lastSave => {
                EntryLastSave = lastSave;
                return EventMailRules.ReturnUser(sendMail.Select(gameEvent => gameEvent.Metadata), CharacterId, lastSave,
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            });
            if (player != null && !ReturnUserGift.Store(db, AccountId, CharacterId, yearRound.Select(gameEvent => gameEvent.Metadata),
                    EntryLastSave, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), reward => ItemDropManager.Create(ItemMetadata, ItemStatsCalc,
                        TableMetadata, ServerTableMetadata, reward.ItemId, reward.Rarity, reward.Amount))) {
                // None of the load's writes are kept: the entry fails.
                db.Rollback();
                Logger.Error("Returner gift of {CharacterId} not stored, entry refused", CharacterId);
                player = null;
            } else {
                db.Commit();
            }
        } finally {
            // Only a lock this load took: one not taken may be another holder's.
            if (locked) {
                ReleaseLock(AccountId, lockToken);
            }
        }
        if (player == null) {
            Logger.Warning("Failed to load player from database: {AccountId}, {CharacterId}", AccountId, CharacterId);
            Send(MigrationPacket.MoveResult(MigrationError.s_move_err_default));
            return false;
        }
        if (locked) {
            heldRows ??= GameStorage.HoldOwners(AccountId, CharacterId);
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
        // After the guild: an auto start quest's guildNpcLevel require reads the guild's quest npc.
        Quest.StartAutoQuests();
        Buddy = new BuddyManager(db, this);
        Item = new ItemManager(db, this, ItemStatsCalc);
        Buffs = new BuffManager(Player);
        UgcMarket = new UgcMarketManager(this);
        BlackMarket = new BlackMarketManager(this);
        Survival = new SurvivalManager(this);
        Marriage = new MarriageManager(this);
        Fishing = new FishingManager(this, TableMetadata, ServerTableMetadata);
        Dungeon = new DungeonManager(this);
        AdventureProtect = new AdventureProtectManager(this);
        Ride = new RideManager(this);
        PcBang = new PcBangManager(this);
        Mentoring = new MentoringManager(this);
        // The managers are made: a reset stored since the load is taken now (by the entry's save at the latest).
        resetsSeen!.Ready = true;
        // The entry's save is a gate: a character whose state cannot be stored does not play (see SessionSave). It
        // runs before anything of the entry is created at once (items, mails), so a refused entry leaves nothing.
        if (!EntrySave()) {
            Send(MigrationPacket.MoveResult(MigrationError.s_move_err_default));
            return false;
        }
        CommandHandler.RegisterCommands(); // Refresh commands with proper permissions
        GroupChatInfoResponse groupChatInfoRequest = World.GroupChatInfo(new GroupChatInfoRequest {
            CharacterId = CharacterId,
        });

        foreach (GroupChatInfo groupChatInfo in groupChatInfoRequest.Infos) {
            var manager = new GroupChatManager(groupChatInfo, this);
            GroupChats.TryAdd(groupChatInfo.Id, manager);
        }

        int fieldId = mapId == 0 ? player.Character.MapId : mapId;
        // A login on the return field arrives at its spot: the player logged out in a field it left to from there.
        position = FieldEntryRules.LoginPosition(position, mapId, migrationType, ownerId, roomId, fieldId, player.Character);
        // A dungeon arrival whose room was closed in the meantime: enter the return field as a plain arrival.
        // The same for a rank duel arrival whose match field is gone, and for a login on a rank duel map (the
        // character left the match by logging out): a rank duel field is entered only through its match.
        bool roomRuns = migrationType == MigrationType.PvpMatch ? server.HasPvpMatch(roomId) : server.HasDungeon(roomId);
        bool roomGone = FieldEntryRules.ArrivesInReturnField(migrationType, roomRuns);
        bool stadiumLogin = roomId == 0 && FieldEntryRules.UgcOxStadiumWithoutEvent(fieldId);
        if (roomGone || stadiumLogin || (roomId == 0 && FieldEntryRules.RankDuelMapWithoutMatch(fieldId, PvpMatchMaps.All(TableMetadata)))) {
            if (roomGone) {
                Logger.Warning("Character:{CharacterId} arrives for dungeon or match Room:{RoomId} which is gone, enters the return field", characterId, roomId);
            } else if (stadiumLogin) {
                Logger.Information("Character:{CharacterId} logs in on the OX quiz stadium without its event, enters the return field", characterId);
            } else {
                Logger.Information("Character:{CharacterId} logs in on rank duel map {MapId} without its match, enters the return field", characterId, fieldId);
            }
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
        } else if (migrationType == MigrationType.Normal && roomId == 0 && ownerId == 0
                   && DungeonRoomMaps.All(TableMetadata) is var roomMaps && FieldEntryRules.DungeonRoomMapWithoutRoom(fieldId, roomMaps)) {
            int returnId = player.Character.ReturnMaps.Peek();
            bool gm = GmCheck.IsGm(player.Account.AdminPermissions);
            int arrivalId = FieldEntryRules.DungeonLoginReturnField(returnId, roomMaps, Constant.DefaultReturnMapId,
                id => MapMetadata.TryGet(id, out MapMetadata? metadata) && !metadata.Property.NotInPlayedClient
                    && FieldEntryRules.AdminFieldAllowed(metadata.Limit.EnterAdminLevel, gm));
            Logger.Information("Character:{CharacterId} arrives on Map:{MapId} of a dungeon room that does not run, enters Map:{ArrivalId}",
                characterId, fieldId, arrivalId);
            position = arrivalId == returnId ? player.Character.ReturnPosition : default;
            fieldId = arrivalId;
            portalId = -1;
        }
        if (!PrepareField(fieldId, out FieldManager? fieldManager, portalId: portalId, ownerId: ownerId, roomId: roomId, position: position)) {
            // The saved field cannot be made (for one, its map has no navmesh file). A plain entry goes to the
            // return field, then to the default one, so the character is not locked out of the game. So does a
            // rank duel arrival whose match field went between the check above and here: it arrives as a plain entry.
            if (FieldEntryRules.TakesFallback(migrationType)) {
                migrationType = MigrationType.Normal;
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

        PlayerInfo.SendUpdate(WorldUpdate());

        //session.Send(Packet.Of(SendOp.REQUEST_SYSTEM_INFO));
        Send(MigrationPacket.MoveResult(MigrationError.ok));

        // The survival lobby and queue state go only to the restored client (MapleSurvivalRules.RestoredClient); the
        // KR client gets the record and medals as before, as does the restored client with SurvivalContents01 off.
        bool survivalLobby = MapleSurvivalRules.RestoredClient && SurvivalRules.On(ServerTableMetadata.FeatureTable);
        if (survivalLobby) {
            MapleSurvivalProgress.SendLobby(this);
        } else {
            Survival.Load();
        }
        RankDuelStanding.Send(this);
        if (survivalLobby) {
            MapleSurvivalHandler.SendState(this);
        }
        // MeretMarket
        // PCBangBonus: the grade and the day's record; the client clears its PC bang info at game start.
        PcBang.Load();
        Guild.Load();
        Guild.LoadExpelNotices();
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
        // KMS2 sends it at every login, after the guild, club and buddy lists and before the home blocks.
        // Its id is the preview row of the last user condition row noticed (GameEventManager.UserConditionPreview).
        Send(UserConditionEventPacket.Load(GameEvent.UserConditionPreview()));
        Send(HomeBlockPacket.Load(db.GetHomeBlocks(AccountId)));
        // The account's restrictions, at every server entry (Chosen here: where in the login sequence; the client
        // replaces its record of each kind, so a resend does no harm).
        AccountBlocks = db.GetAccountBlocks(AccountId).ToArray();
        Send(AdminBlockPacket.Load(AccountBlocks));
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
            Config.SetCachePvpHold(configResponse.PvpHeldCooldowns, configResponse.PvpStay, currentTick);
            Config.SetDeathPenalty(configResponse.DeathInfo, currentTick);

        } catch (RpcException ex) {
            Logger.Warning(ex, "Failed to load cache player config");
        }

        // The cast ticks are measured from the tick this first time sync carries (ClockRate.Start).
        Config.CastClock.Start(Environment.TickCount, Environment.TickCount64);
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
        // The cash shop flag KMS2 sends at every login (1, the client's own default).
        Send(CashPacket.Enabled(true));
        // Gvg: the content shutdown flags, cleared and set again at every game server entry (ContentShutdownConfig).
        Send(GvgPacket.ContentShutdown(ContentShutdownConfig.Contents));
        // Gvg kind 42: the rates of the united reward boost (DungeonRoomPacket.BoostState, sent with the dungeon records).
        Send(GvgPacket.DungeonRewardRates(DungeonRoomRules.UnitedRewardBoostExpRate, DungeonRoomRules.UnitedRewardBoostMesoRate));
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
        // The adventurer protection state, once the quests are known (Chosen here: no KMS2 order is known for it).
        AdventureProtect.Load();
        // The term tags (KMS2 sends them a few packets after the adventurer protection state).
        LoadTermTags();
        // An announced shutdown, so the client's countdown notices reach those who enter after it (Chosen here).
        if (ShutdownRules.SendAtEntry(Target.LOCALE, server.ShutdownTime, DateTimeOffset.UtcNow.ToUnixTimeSeconds())) {
            Send(ShutdownNoticePacket.Time(server.ShutdownTime));
        }
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
        Send(UserEnvPacket.FameLogs(Player.Value.Unlock.FameLogs));
        Send(FishingPacket.LoadAlbum(Player.Value.Unlock.FishAlbum.Values));
        Pet?.Load();
        Send(PetPacket.LoadCollection(Player.Value.Unlock.Pets));
        Send(LegionBattlePacket.Load(ServerTableMetadata.TimeEventTable.WorldBoss));
        // The world map's boss icons (WorldMap Load) are not sent here: KMS2 sends them only as the answer to the
        // map's own request, which the map makes each time it opens (UIWorldMapDialog::SendRequestWorldmapMode0,
        // uiworldmapdialog.cpp:388; WorldMapHandler).
        Config.LoadCharacterAbilities();
        Config.LoadKeyTable();
        Send(GuideRecordPacket.Load(Config.GuideRecords));
        // DailyWonder*
        // The new-user and returning-user marks first: the values sent next carry them.
        GameEvent.Mark(Player.Value.Character.CreationTime, EntryLastSave);
        GameEvent.Load();
        // The attendance events started with an item (CashAttendEvent), after the user values they are read from.
        Send(AttendancePacket.StartedEvents(GameEvent.ItemStartedEvents()));
        server.LoadEvents(this);
        Send(BannerListPacket.Load(server.GetSystemBanners()));
        Dungeon.Load();
        WorldChampionHandler.MailRewards(this);
        Send(InGameRankPacket.Load());
        // The pet auto flag, on as the client starts with it; it is not stored here.
        Send(PetPacket.AutoChanged(true));
        Send(FieldEnterPacket.Request(Player));
        // The design market's charge table, where KMS2 sends it.
        Send(UgcPacket.CostTable(UgcMarketRules.CostRows));
        Party = new PartyManager(World, this);
        Send(HomeCommandPacket.LoadHome(AccountId, db.GetLastHomeCommend(AccountId)));
        // ResponseCube
        // Mentor: the stored requests and pairs while a ReturnUser season runs, and the account's registered
        // characters (Mentor 12).
        Mentoring.Load();
        Config.LoadChatStickers();
        // Mail (the ReturnUser mails were stored by LoadPlayer)
        Mail.Notify(true);
        // BypassKey
        // AH
        Config.LoadWardrobe();

        // Online Notifications

        entered = true;
        return true;
    }

    private long resummonPetUid;
    // The client was told the pet is away (a field that allows no pet); sent once until the pet is back.
    private bool resummonUnSummonSent;
    private bool noticeBannerSent;
    // The pet to bring back after a move to another channel or game server (MigrateOutRequest.pet_uid).
    public long SummonedPetUid => Pet?.Pet.Uid ?? resummonPetUid;

    private void LeaveField() {
        InteractStarted.Clear();
        InteractStartTick = null;
        LastReactTick = null;
        InteractStartMasteryReduce = 0;
        CubeStarted.Clear();
        Array.Clear(ItemLockStaging);
        Array.Clear(DismantleStaging);
        DismantleOpened = false;
        DonationBoxUsed = false;
        Trade?.Dispose();
        Storage?.Dispose();
        // A summoned pet leaves the field with its owner and is summoned again in the next one (EnterField).
        if (Pet != null) {
            resummonPetUid = Pet.Pet.Uid;
            resummonUnSummonSent = false;
        }
        Pet?.Dispose(fieldChange: true);
        // A play still running ends with the field: the other players are told and later arrivals are not sent it.
        // A waiting ensemble member stops waiting (the leader's start finds the member's ready mark only in its field).
        if (Instrument != null && Field != null) {
            if (Instrument.Improvising) {
                Field.Broadcast(InstrumentPacket.StopImprovise(Instrument));
                Field.RemoveInstrument(Instrument.ObjectId, broadcast: false);
            } else {
                Field.RemoveInstrument(Instrument.ObjectId);
            }
        }
        Instrument = null;
        EnsembleReady = false;
        StagedInstrumentItem = null;
        StagedScoreItem = null;
        Fishing?.LeaveField();
        GuideObject = null;
        HeldCube = null;
        HeldLiftup = null;
        NpcScript = null;
        // The beauty dialogs close with the field and have no close packet: a shop kept past it took edits and fees in
        // any later field. A random pick still waiting keeps the new hair, as a new shop open does (HandleShop).
        BeautyShop = null;
        Beauty?.ClearPreviousHair();
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
    // The dungeon group of the field left by the last field change (DungeonAbilityRules.RebuildsOnEntry).
    private DungeonGroupType? leftDungeonGroup;

    public bool PrepareField(int mapId, int portalId = -1, long ownerId = 0, int roomId = 0, in Vector3 position = default, in Vector3 rotation = default) {
        return PrepareFieldInternal(mapId, out _, portalId, ownerId, roomId, position, rotation, null);
    }

    // A field change bought with a fare or tickets (the taxis): pay runs once the change went, after the move here
    // (PrepareField true) or, for a move to another server, before that move's save (Migrate's beforeMove), so the
    // payment is stored with it. A refused change never runs it. Returns PrepareField's answer, for SendFieldEnter.
    public bool PaidFieldChange(int mapId, Action pay, in Vector3 position = default, in Vector3 rotation = default) {
        bool prepared = PrepareFieldInternal(mapId, out _, -1, 0, 0, position, rotation, pay);
        if (prepared) {
            pay();
        }
        return prepared;
    }

    // Set when the field being entered was reached by a portal: EnterField then sends InvincibleEffect. Chosen from
    // the constant's name (UserPortalInvincibleTick): portals only, not a login, a revival, a GM or a trigger move,
    // and not a portal that migrates to another server.
    public bool PortalArrival { get; private set; }

    // The DPS meter window is open (DpsMode 1): only then is DpsStat sent. Chosen here: it lasts for the session, a
    // field change on this server included (the client sends it only when the window opens or closes); a new
    // session (a channel change) starts with it closed.
    public bool DpsMeterOpen;
    // The home OX quiz settings the host chose (HomeAction 10 sub 0), until the confirm box's yes (sub 3).
    public UgcOxRules.Settings? UgcOxPending;

    public bool PrepareFieldByPortal(int mapId, int portalId = -1, int roomId = 0) {
        bool prepared = PrepareFieldInternal(mapId, out _, portalId, 0, roomId, default, default, null);
        PortalArrival = prepared;
        return prepared;
    }

    // A portal that returns the player (target map 0) goes through ReturnField: it counts when a new field was
    // prepared on this server.
    public void ReturnFieldByPortal() {
        FieldManager? before = Field;
        ReturnField();
        if (Field != before) {
            PortalArrival = true;
        }
    }

    public bool PrepareField(int mapId, [NotNullWhen(true)] out FieldManager? newField, int portalId = -1, long ownerId = 0, int roomId = 0, in Vector3 position = default, in Vector3 rotation = default) {
        return PrepareFieldInternal(mapId, out newField, portalId, ownerId, roomId, position, rotation, null);
    }

    // Set by a field change that became a move to another server (a dungeon left for a map outside it): the move
    // is answered by GameToGame or its error box (CNetworkService::OnGameToGame), so no FieldEnter error follows.
    private bool fieldChangeMigrated;
    // The last PrepareField moved the player to another channel or server instead of a field here.
    public bool FieldChangeMigrated => fieldChangeMigrated;

    // The answer to a field change on this server: FieldEnter's request, or its error unless the change was handed
    // to the World server as a move and answered there.
    public void SendFieldEnter(bool prepared) {
        if (prepared) {
            Send(FieldEnterPacket.Request(Player));
        } else if (!fieldChangeMigrated) {
            Send(FieldEnterPacket.Error(MigrationError.s_move_err_default));
        }
    }

    private bool PrepareFieldInternal(int mapId, out FieldManager? newField, int portalId, long ownerId, int roomId, in Vector3 position, in Vector3 rotation,
                                      Action? beforeMove) {
        PortalArrival = false;
        fieldChangeMigrated = false;
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
                Dungeon.LeaveStage();
            } else {
                Migrate(mapId, beforeMove: beforeMove, position: position);
                fieldChangeMigrated = true;
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
        leftDungeonGroup = StatsManager.DungeonGroupOf(Field);
        bool leftDead = Player.IsDead;
        LeaveField();

        Field = newField;
        Player.Dispose();
        Player = Field.SpawnPlayer(this, Player, portalId, position, rotation);
        // The field was retired between the factory's answer and the spawn (another entry found the home's model
        // house applied since it was loaded): the factory now loads the home again, so the player goes there.
        // Before, the player entered the retired field, which the factory disposes. Should the new load fail, the
        // player stays in the retired field (logged).
        if (Field.Retired && Field is not DungeonFieldManager) {
            FieldManager? again = FieldFactory.Get(mapId, ownerId: ownerId, roomId: roomId);
            if (again != null && !again.Retired) {
                Field.RemovePlayer(Player.ObjectId, out _);
                // The spawn there pushed its return map (when it saves one); the spawn here pushes it again.
                if (FieldEntryRules.SavesReturnMap(Field.FieldInstance.Type, Field.FieldInstance.SaveField, false, Field.Metadata.Property.EnterReturnId)
                    && !Player.Value.Character.ReturnMaps.IsEmpty) {
                    Player.Value.Character.ReturnMaps.Pop();
                }
                Field = again;
                Player.Dispose();
                Player = Field.SpawnPlayer(this, Player, portalId, position, rotation);
            } else {
                Logger.Error("Field {MapId} of {OwnerId} was retired and not loaded again", mapId, ownerId);
            }
        }
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

    // The field enter's global portal announce: sent while the World server has a portal open, in the field types
    // the announce at the opening goes to.
    public void AnnounceOpenGlobalPortal() {
        if (Field == null) {
            return;
        }

        TimeEventResponse globalEventResponse = World.TimeEvent(new TimeEventRequest {
            GetGlobalPortal = new TimeEventRequest.Types.GetGlobalPortal(),
        });

        if (globalEventResponse.Error == 0 && globalEventResponse.GlobalPortalInfo != null
            && TimeEventRules.PortalAnnouncedIn(Field.Metadata.Property.Type) && GlobalPortalLevelAllowed()) {
            if (ServerTableMetadata.TimeEventTable.GlobalPortal.TryGetValue(globalEventResponse.GlobalPortalInfo.MetadataId, out GlobalPortalMetadata? portal)) {
                Send(GlobalPortalPacket.Announce(portal, globalEventResponse.GlobalPortalInfo.EventId));
            }
        }
    }

    // The client shows the global portal only from GlobalPortalMinLevel (GlobalPortalRules.LevelAllowed).
    public bool GlobalPortalLevelAllowed() =>
        GlobalPortalRules.LevelAllowed(Player.Value.Character.Level, ServerTableMetadata.ConstantsTable.GlobalPortalMinLevel);

    // The uid of the field war this player was last announced (FieldWarPacket.Announce): its SidePopup enter has no
    // id. 0 when none was.
    public long FieldWarUid;

    public bool FieldWarEligible() {
        ConstantsTable constants = ServerTableMetadata.ConstantsTable;
        return FieldWarRules.Eligible(Player.Value.Character.Level, constants.FieldWarRequireLevel, constants.FieldWarRequireAchieveID,
            id => Achievement.HasAchievement(id));
    }

    // A field war announce: to an eligible player in a field the global portal announce goes to, once a war.
    public void AnnounceFieldWar(int fieldWarId, long uid) {
        if (Field == null || FieldWarUid == uid || !TimeEventRules.PortalAnnouncedIn(Field.Metadata.Property.Type) || !FieldWarEligible()) {
            return;
        }
        FieldWarUid = uid;
        Send(FieldWarPacket.Announce(fieldWarId, uid));
    }

    // The field enter's field war announce, while the World server has a war open that this character did not join.
    public void AnnounceOpenFieldWar() {
        if (Field == null || !TimeEventRules.PortalAnnouncedIn(Field.Metadata.Property.Type)) {
            return;
        }
        TimeEventResponse response = World.TimeEvent(new TimeEventRequest {
            GetFieldWar = new TimeEventRequest.Types.GetFieldWar {
                CharacterId = CharacterId,
            },
        });
        if (response.Error == 0 && response.FieldWarInfo != null) {
            AnnounceFieldWar(response.FieldWarInfo.FieldWarId, response.FieldWarInfo.EventId);
        }
    }

    // The notice banner of the GM notice config, at a field entry, once per session, NoticeRules.BannerDelay later
    // (the client opens the notice dialog a few ticks after the entry).
    public TermTagState TermTags => server.TermTags;

    // The channel's finished and running terms (TermTagManagerCli load): the client registers the running ones.
    public void LoadTermTags() {
        (List<TermTagTable.Entry> finished, List<TermTagTable.Entry> running) =
            server.TermTags.Split(ServerTableMetadata.TermTagTable, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        Send(TermTagPacket.Load(finished, running));
    }

    // A packet to every player of this channel.
    public void BroadcastChannel(ByteWriter packet) => server.Broadcast(packet);

    public void SendNoticeBanner() {
        if (NoticeRules.BannerAtEntry(server.NoticeBanner, noticeBannerSent)) {
            noticeBannerSent = true;
            Scheduler.Schedule(ShowNoticeBanner, NoticeRules.BannerDelay);
        }
    }

    public void ShowNoticeBanner() {
        if (Field == null) return;
        Send(NoticeDialogPacket.Show());
    }

    // The Chaos Raid lines count only in their room and the massive event speed lines only in their game's field:
    // entering or leaving one rebuilds the stats with the new field. A rebuild sends the stats itself, so the entry
    // sends them once either way (a rebuild left for a battle ride's end sends them here).
    internal void SendEntryStats(DungeonGroupType? leftGroup, int leftMapId) {
        if (Field == null) return;
        if (DungeonAbilityRules.RebuildsOnEntry(leftGroup, StatsManager.DungeonGroupOf(Field))
            || MassiveAbilityRules.RebuildsOnEntry(leftMapId, Field.MapId)) {
            Stats.Refresh();
            if (!Stats.RefreshDeferred) {
                return;
            }
        }
        Send(StatsPacket.Init(Player));
        Field.Broadcast(StatsPacket.Update(Player), Player.Session);
    }

    // A map entered for the first time joins the visited maps. The client gets them in ServerEnter only, which this
    // server sends at login (KMS sends one before most field enters), so the new map also goes to the client with
    // FindFields (CSceneStateGame::OnFindFields, scenestategame.cpp:6497): the world map draws a tile's icons,
    // world bosses included, only on a visited ("not sealed") tile, so a map first entered this session stayed dark
    // until the next login.
    internal bool MarkVisited(int mapId) {
        if (!Player.Value.Unlock.Maps.Add(mapId)) {
            return false;
        }
        Send(FindFieldsPacket.Add([mapId]));
        return true;
    }

    // The conditions of a map's first visit: its continent (explore_continent, continent) and the map (explore).
    internal void FirstVisitConditions() {
        ConditionUpdate(ConditionType.explore_continent, codeLong: (int) Field!.Metadata.Property.Continent);
        ConditionUpdate(ConditionType.continent, codeLong: (int) Field.Metadata.Property.Continent);
        ConditionUpdate(ConditionType.explore, codeLong: Field.MapId);
    }

    // The conditions of every field entry: the map, the job held (job_change), and another account's home.
    internal void EntryConditions() {
        ConditionUpdate(ConditionType.map, codeLong: Player.Value.Character.MapId);
        ConditionUpdate(ConditionType.job_change, codeLong: (int) Player.Value.Character.Job.Code());
        if (Field is HomeFieldManager home
            && FieldEntryRules.VisitsOtherHome(home.OwnerId, home.MapId, AccountId, leftField.MapId, leftField.OwnerId)) {
            ConditionUpdate(ConditionType.enter_otherhouse);
        }
    }

    public bool EnterField() {
        if (Field == null) return false;

        Config.LoadHotBars();
        Field.OnAddPlayer(Player);
        Scheduler.Start();
        State = SessionState.Connected;
        RunPlayerActionsAtEntry();
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
        // Survival match items do not outlive the match (SurvivalItemRules, Chosen here).
        Item.Inventory.ClearSurvivalItems(Field.Metadata.Property.Survival, Field.MapId, Field.Survival?.InRunningGame(CharacterId) == true);

        PlayerInfo.SendUpdate(new PlayerUpdateRequest {
            AccountId = AccountId,
            CharacterId = CharacterId,
            MapId = Field.MapId,
            RoomId = Field.RoomId,
            Async = true,
        });

        SendEntryStats(leftDungeonGroup, leftField.MapId);
        leftDungeonGroup = null;

        // The first visit of a map pays exp. It comes after the player is added to the field: the client
        // has no local player before its own FieldAddUser, so it shows no exp text for an earlier ExpUp, and
        // a level-up is sent to the field's players, which the entering player is not before OnAddPlayer.
        bool firstVisit = !Player.Value.Unlock.Maps.Contains(Player.Value.Character.MapId);
        ExpType? exploreExp = FieldEntryRules.ExploreExp(firstVisit, Field.Metadata.Property.ExploreType);
        if (exploreExp != null) {
            Exp.AddExp(exploreExp.Value);
        }
        // A stamp quest or a field mission whose completion failed before (QuestManager.CompleteStampQuests,
        // CompleteFieldMissions) is tried again.
        Quest.CompleteStampQuests();
        Quest.CompleteFieldMissions();
        AttendAgainAtFieldEntry();
        // The mentor coins the mentor's mentees earned while it was away (MentoringManager.TakeMentorCoins).
        Mentoring.TakeMentorCoins();
        if (firstVisit) {
            FirstVisitConditions();
        }
        MarkVisited(Player.Value.Character.MapId);

        Send(AdminPacket.Level(AdminLevel.Of(Player.AdminPermissions)));

        //TODO: Save current hp/sp/ep in memory. This will help determine if player is dead upon login.
        var pWriter = Packet.Of(SendOp.UserState);
        pWriter.WriteInt(Player.ObjectId);
        pWriter.Write<ActorState>(ActorState.Fall);
        Send(pWriter);

        Send(EmotePacket.Load(Player.Value.Unlock.Emotes.Select(Player.Value.Unlock.GetEmote).ToList()));
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
        // The burning levelup's jumping item, given in the field the tutorial ends in.
        GameEvent.BurningLevelupFieldEnter();

        AnnounceOpenGlobalPortal();
        AnnounceOpenFieldWar();
        SendNoticeBanner();

        Send(PremiumCubPacket.Activate(Player.ObjectId, Player.Value.Account.PremiumTime));
        Send(PremiumCubPacket.LoadItems(Player.Value.Account.PremiumRewardsClaimed));
        // Merat payback: an earlier month is paid, and the dialog gets this month's status and products.
        PaybackService.EnterField(this);
        // The quest "map" condition: one count per field entry, whatever the way in (a portal, a warp, a login). Chosen
        // here: the client does not show when the server counts it.
        EntryConditions();

        // A save that cannot be stored ends the session: nothing more is done on it.
        if (!SessionSave()) {
            return false;
        }

        // Update the client with the latest channel list.
        ChannelsResponse response = World.Channels(new ChannelsRequest());
        Send(ChannelPacket.Load(response.Channels));
        int characters;
        using (GameStorage.Request db = GameStorage.Context()) {
            characters = db.CountCharacters(AccountId);
        }
        Send(ServerListPacket.Load(Target.SERVER_NAME, [new IPEndPoint(Target.LoginIp, Target.LoginPort)], response.Channels, characters, response.Online));
        if (PortalArrival) {
            PortalArrival = false;
            Player.PortalInvincible();
        }
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

        // The OX quiz stadium's way out (its portals, the host's leave, the script's MoveAllUser) is the host's home,
        // where the event's portal led in from.
        if (Field?.UgcOx is { } ox) {
            SendFieldEnter(PrepareField(ox.HomeMapId, ownerId: ox.HostAccountId));
            return;
        }

        Character character = player.Character;
        int mapId = character.ReturnMaps.Peek();
        Vector3 position = character.ReturnPosition;

        if (ReturnMapId() != mapId) {
            mapId = Constant.DefaultReturnMapId;
            position = default;
        }

        // The spot is used up only by a move that went: a refused one (no server, a field that cannot be made)
        // keeps it for the next way out. For a move to another server it is cleared before that move's save.
        void UseSpot() => character.ReturnPosition = default;

        if (character.MapId is Constant.DefaultHomeMapId) {
            Migrate(mapId, beforeMove: UseSpot, position: position);
            return;
        }

        if (Guild.Guild is not null) {
            TableMetadata.GuildTable.Houses.TryGetValue(Guild.Guild.HouseRank, out IReadOnlyDictionary<int, GuildTable.House>? houseRank);

            if (houseRank is not null) {
                houseRank.TryGetValue(Guild.Guild.HouseTheme, out GuildTable.House? house);

                if (house?.MapId == character.MapId) {
                    Migrate(mapId, beforeMove: UseSpot, position: position);
                    return;
                }
            }
        }

        // If returning to a map, pass in the spawn point. PaidFieldChange runs UseSpot only once the change went.
        SendFieldEnter(PaidFieldChange(mapId, UseSpot, position));
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
    // fieldMapId: the field a field mission counts in when it is not the character's map now (QuestManager.Update).
    public void ConditionUpdate(ConditionType conditionType, long counter = 1, string targetString = "", long targetLong = 0, string codeString = "", long codeLong = 0, int fieldMapId = 0) {
        Achievement.Update(conditionType, counter, targetString, targetLong, codeString, codeLong);
        Quest.Update(conditionType, counter, targetString, targetLong, codeString, codeLong, fieldMapId);
        GameEvent.SnowmanCondition(conditionType);
        GameEvent.PromotionCondition(conditionType, targetString, targetLong, codeString, codeLong);
        GameEvent.UserConditionChanged(conditionType);
    }

    public IList<GameEvent> FindEvent(GameEventType type) => server.FindEvent(type);

    public GameEvent? FindEvent(int id) => server.FindEvent(id);
    public IEnumerable<GameEvent> Events => server.GetEvents();
    public List<DungeonRoomRules.OpenPeriod> OpenPeriods => server.GetOpenPeriods();

    // See ReactorRules.SendsEvent: a Reactor event no reactor row names is kept from the client.
    public bool SendsEvent(GameEvent gameEvent) => ReactorRules.SendsEvent(gameEvent.Metadata.Type, gameEvent.Id, TableMetadata.ReactorTable);

    public IEnumerable<PremiumMarketItem> GetPremiumMarketItems(params int[] tabIds) => server.GetPremiumMarketItems(tabIds);
    public IList<SystemBanner> GetSystemBanners() => server.GetSystemBanners();

    public PremiumMarketItem? GetPremiumMarketItem(int id, int subId = 0) => server.GetPremiumMarketItem(id, subId);

    public RewardRecord GetRewardContent(int id) {
        (RewardRecord record, Action give) = PrepareRewardContent(id);
        give();
        return record;
    }

    // The items of a reward content row at a level, made (not given); none without a field to make them in.
    public List<Item> RewardContentItems(int id, int level = 0) {
        if (!TableMetadata.RewardContentTable.BaseEntries.TryGetValue(id, out RewardContentTable.Base? baseMetadata)) {
            return [];
        }
        return RewardContentItems(baseMetadata, level > 0 ? level : Player.Value.Character.Level);
    }

    private List<Item> RewardContentItems(RewardContentTable.Base baseMetadata, int tableLevel) {
        List<Item> items = [];
        if (baseMetadata.ItemTableId > 0 && TableMetadata.RewardContentTable.ItemEntries.TryGetValue(baseMetadata.ItemTableId, out RewardContentTable.Item? itemMetadata)) {
            foreach (RewardContentTable.Item.Data data in itemMetadata.ItemData) {
                if ((data.MinLevel > 0 && tableLevel < data.MinLevel) ||
                    (data.MaxLevel > 0 && tableLevel > data.MaxLevel)) {
                    continue;
                }
                foreach (RewardItem rewardItem in data.RewardItems) {
                    Item? item = Field?.ItemDrop.CreateItem(rewardItem.ItemId, rewardItem.Rarity, rewardItem.Amount);
                    if (item != null) {
                        items.Add(item);
                    }
                }
            }
        }
        return items;
    }

    /// <summary>
    /// The amounts and items of a reward content row, and the action that gives them (meso, exp, items, the order
    /// KMS2 sends a dungeon's weekly reward in), for a caller that reports the reward before it is given.
    /// </summary>
    /// <param name="message">The message code of the exp and the meso.</param>
    /// <param name="level">The level the row's tables are read at (RewardContentsTable::GetReward takes it,
    /// rewardcontentstable.cpp:213); 0 for the character's level.</param>
    public (RewardRecord Record, Action Give) PrepareRewardContent(int id, ExpMessageCode message = ExpMessageCode.s_msg_take_exp, int level = 0) {
        if (!TableMetadata.RewardContentTable.BaseEntries.TryGetValue(id, out RewardContentTable.Base? baseMetadata)) {
            return (new RewardRecord(), () => { });
        }
        int tableLevel = level > 0 ? level : Player.Value.Character.Level;

        long exp = 0;
        if (baseMetadata.ExpTableId > 0) {
            if (baseMetadata.ExpTableId > 100000 && TableMetadata.RewardContentTable.ExpStaticEntries.TryGetValue(baseMetadata.ExpTableId, out exp)) {
                // Static exp entry found
            } else if (TableMetadata.ExpTable.ExpBase.TryGetValue(baseMetadata.ExpTableId, out IReadOnlyDictionary<int, long>? expTable)) {
                exp = expTable[tableLevel];
            }
            if (baseMetadata.ExpFactor > 0f) {
                exp = (long) (exp * baseMetadata.ExpFactor);
            }
        }

        long meso = 0;
        if (baseMetadata.MesoTableId > 0) {
            if (baseMetadata.MesoTableId > 100000 && TableMetadata.RewardContentTable.MesoStaticEntries.TryGetValue(baseMetadata.MesoTableId, out meso)) {
                // Static meso entry found
            } else if (TableMetadata.RewardContentTable.MesoEntries.TryGetValue(baseMetadata.MesoTableId, out Dictionary<int, long>? mesoTable)) {
                if (!mesoTable.TryGetValue(tableLevel, out meso)) {
                    Logger.Error("Failed to find meso for level {Level} in table {TableId}", tableLevel, baseMetadata.MesoTableId);
                }
            }
            if (baseMetadata.MesoFactor > 0f) {
                meso = (long) (meso * baseMetadata.MesoFactor);
            }
        }

        long prestigeExp = 0;
        // The row's adventureExpTableID names a fixed amount in the server's AdventureIdExpTable (the client does not
        // load that table: CAdventureLevelTable::FindUnused2, adventureleveltablehelper.cpp:315).
        if (baseMetadata.PrestigeExpTableId > 0 && ServerTableMetadata.PrestigeIdExpTable.Entries.TryGetValue(baseMetadata.PrestigeExpTableId, out PrestigeIdExpTable.Entry? prestigeExpTable)) {
            prestigeExp = prestigeExpTable.Value;
        }

        List<Item> items = RewardContentItems(baseMetadata, tableLevel);
        return (new RewardRecord(items, exp, prestigeExp, meso), Give);

        void Give() {
            if (meso > 0) {
                if (message == ExpMessageCode.s_msg_take_exp) {
                    Currency.ChangeMeso(meso);
                } else {
                    Currency.AddMeso(meso, message);
                }
            }
            if (prestigeExp > 0) {
                // Chosen here: the row's prestige is its fixed amount alone, so the exp brings no prestige of the
                // message's type (dungeonClear for the Colosseum rounds). The client's reward of a row has a single
                // adventure exp, filled from the adventureExpTableID alone (RewardContentsTable::GetReward,
                // rewardcontentstable.cpp:222; the client's lookup gives 0).
                Exp.AddExp(exp, message, ExpType.none);
                Exp.AddPrestigeExp(prestigeExp);
            } else {
                Exp.AddExp(exp, message);
            }
            foreach (Item item in items) {
                if (!Item.Inventory.Add(item, true)) {
                    Item.MailItem(item);
                }
            }
        }
    }

    public void ChannelBroadcast(ByteWriter packet) {
        server.Broadcast(packet);
    }

    // The World's daily, weekly and monthly resets set the stored rows (GameStorage.RunReset) and their time; the
    // session's state takes each reset once, when its time is later than the one the session's state holds: from
    // the World's message to the channels (GameServer.TakeResets), or from this session's next save, which reads the
    // times in its transaction before it writes (SaveRows, TrySavePurchase). The save used to write the state from
    // before the reset back over the reset rows when it ran between the reset and the message, or when the message
    // did not reach the session (a channel the message failed for, a session moving between channels, the World's
    // reset at its start, which sends none), and the old values were then kept.
    private ResetsSeen? resetsSeen;

    private sealed class ResetsSeen(GameStorage.ResetStamps stamps) {
        public GameStorage.ResetStamps Stamps = stamps;
        // False while EnterServer makes the managers the resets change.
        public bool Ready;
    }

    public void TakeResets(GameStorage.ResetStamps stamps) {
        ResetsSeen? seen = resetsSeen;
        if (seen == null) {
            // A session whose state EnterServer did not read (a test's): the first times it sees are the ones it holds.
            Interlocked.CompareExchange(ref resetsSeen, new ResetsSeen(stamps) { Ready = true }, null);
            return;
        }
        // A step's own store (AfterStore) runs after the reset, outside this lock: a store of another thread holding
        // the account lock may be waiting here.
        bool outer = BeginStoreScope();
        try {
            // One at a time: a save waits here until a reset taken on another thread is done, before it writes.
            lock (seen) {
                if (!seen.Ready) {
                    return;
                }
                GameStorage.ResetStamps held = seen.Stamps;
                seen.Stamps = new GameStorage.ResetStamps(Max(held.Daily, stamps.Daily), Max(held.Weekly, stamps.Weekly), Max(held.Monthly, stamps.Monthly));
                if (stamps.Daily > held.Daily) {
                    DailyReset();
                }
                if (stamps.Weekly > held.Weekly) {
                    WeeklyReset();
                }
                if (stamps.Monthly > held.Monthly) {
                    MonthlyReset();
                }
            }
        } finally {
            EndStoreScope(outer);
        }

        static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
    }

    // Runs a change the World asks for (a reset, a condition count) where the player's packets are handled: as a task
    // of the player's field, so it never runs during one of the player's field handlers. A session without a field
    // (between fields, or one a test made) runs it at once. A task whose session moved to another field before it
    // ran follows the session there.
    // The actions wait on the session, not on the field's queue alone: a field runs its queue only while it has
    // players (FieldManager.Update), so a task queued on a field the player left in the same tick (a dungeon, a home)
    // never ran, and a guild or club join count was lost. The next field runs them when the player enters it
    // (EnterField). A closed session drops them: its last save takes the resets (BeginWithResets).
    internal void RunAsPlayer(Action action) {
        FieldManager? field = Field;
        if (field == null) {
            action();
            return;
        }
        PlayerActions.Enqueue(action);
        field.Scheduler.Schedule(() => RunPlayerActions(field), TimeSpan.Zero);
    }

    private ConcurrentQueue<Action>? playerActions;
    // Set by Dispose: what still waits is dropped.
    private volatile bool playerActionsClosed;
    // Made on first use (a session built without its constructor, as the tests make one, has it too).
    private ConcurrentQueue<Action> PlayerActions => LazyInitializer.EnsureInitialized(ref playerActions);

    // The player's waiting actions, run once the player has entered its field (EnterField).
    internal void RunPlayerActionsAtEntry() {
        FieldManager? field = Field;
        if (field != null && !PlayerActions.IsEmpty) {
            field.Scheduler.Schedule(() => RunPlayerActions(field), TimeSpan.Zero);
        }
    }

    // On the thread of `field`; runs the waiting actions when the session still plays on it.
    private void RunPlayerActions(FieldManager field) {
        if (playerActionsClosed) {
            PlayerActions.Clear();
            return;
        }
        FieldManager? now = Field;
        if (now != field) {
            if (now != null) {
                now.Scheduler.Schedule(() => RunPlayerActions(now), TimeSpan.Zero);
            }
            return;
        }
        while (PlayerActions.TryDequeue(out Action? action)) {
            try {
                action();
            } catch (Exception ex) {
                Logger.Error(ex, "A player action failed AccountId={AccountId} CharacterId={CharacterId}", AccountId, CharacterId);
            }
        }
    }

    // Whether TakeResets(stamps) would reset the session's state.
    private bool ResetsDue(GameStorage.ResetStamps stamps) {
        ResetsSeen? seen = resetsSeen;
        if (seen == null) {
            return false;
        }
        lock (seen) {
            return seen.Ready && (stamps.Daily > seen.Stamps.Daily || stamps.Weekly > seen.Stamps.Weekly || stamps.Monthly > seen.Stamps.Monthly);
        }
    }

    // Begins a save's transaction with the reset times read under their share lock and every stored reset taken. A
    // reset the session does not hold yet is taken outside the transaction: the reset in memory writes rows of its
    // own (the dungeon records) and sends mails and a World message, which would lengthen the share lock the World's
    // next reset waits for. The transaction is begun again after it; a reset stored meanwhile is taken the same way.
    private void BeginWithResets(GameStorage.Request db, TimeSpan resetWait) {
        db.BeginTransaction();
        GameStorage.ResetStamps stamps = db.GetResetStamps(resetWait);
        while (ResetsDue(stamps)) {
            db.Rollback();
            TakeResets(stamps);
            db.BeginTransaction();
            stamps = db.GetResetStamps(resetWait);
        }
        // Nothing to take: this only records the first times a session without EnterServer sees.
        TakeResets(stamps);
    }

    // A reset's steps each run on their own: one that throws (a send, a World call, a manager not set yet) is
    // logged and the others still run. TakeResets counts a reset as taken before it runs, so a step skipped after a
    // throw (the rest of the daily reset, a monthly reset due with it) was never taken, and the next save wrote its
    // old values back over the stored reset (the month's meso market purchases, kept until the next month).
    private void ResetStep(string reset, string step, Action action) {
        try {
            action();
        } catch (Exception ex) {
            Logger.Error(ex, "{Reset}: step {Step} failed CharacterId={CharacterId}", reset, step, CharacterId);
        }
    }

    public void DailyReset() {
        const string reset = nameof(DailyReset);
        // The new day's play time starts now, as KMS2 sends it at the reset; the client counts no play seconds
        // from a start on an earlier day.
        ResetStep(reset, "play time", () => {
            DateTime playStart = DateTime.Now;
            GameEvent.ResetPlayTime(playStart);
            Send(TimeSyncPacket.Set(playStart));
        });
        // Gathering counts reset
        ResetStep(reset, "gathering", () => {
            Config.GatheringCounts.Clear();
            Send(UserEnvPacket.GatheringCounts(Config.GatheringCounts));
        });
        // Death Counter
        ResetStep(reset, "revival", () => Config.AddInstantReviveCount(-1));
        // PC bang: the new day's play and claims start empty (the open dialog asks for the info again).
        ResetStep(reset, "pc bang", () => {
            if (PcBang.On) {
                PcBang.SendStatus();
            }
        });
        // Premium Rewards Claimed
        ResetStep(reset, "premium", () => {
            Player.Value.Account.PremiumRewardsClaimed.Clear();
            Send(PremiumCubPacket.LoadItems(Player.Value.Account.PremiumRewardsClaimed));
        });
        // Prestige
        ResetStep(reset, "prestige", () => {
            Player.Value.Account.PrestigeExp = Player.Value.Account.PrestigeCurrentExp;
            Player.Value.Account.PrestigeLevelsGained = 0;
            Send(PrestigePacket.Load(Player.Value.Account));
        });
        // Home
        ResetStep(reset, "home", () => {
            Player.Value.Home.DecorationRewardTimestamp = 0;
            Send(CubePacket.DesignRankReward(Player.Value.Home));
        });
        // Meso Market
        ResetStep(reset, "meso market", () => {
            Player.Value.Account.MesoMarketListed = 0;
            Send(MesoMarketPacket.Quota(Player.Value.Account.MesoMarketListed, Player.Value.Account.MesoMarketPurchased));
        });
        // Shop restock
        ResetStep(reset, "shop", () => Shop.DailyReset());
        // Dungeon daily clears
        ResetStep(reset, "dungeon", () => Dungeon.ResetDailyClears());
        // Alliance missions, with the time of the reset. The reset runs on the player's field thread (RunAsPlayer),
        // where the quest and attendance packets are handled.
        DateTime resetTime = DateTime.Now;
        ResetStep(reset, "alliance missions", () => Quest.ResetAllianceMissions(resetTime));
        // Daily missions, daily field quests and wedding missions (Quest 0x19, 0x1E, 0x26).
        ResetStep(reset, "daily quests", () => Quest.ResetDailyQuests(resetTime));
        // Long-term attendance: the day's attend point.
        ResetStep(reset, "attend point", () => GameEvent.GrantLongTermAttendPoints());
        // Pet fusion count
        ResetStep(reset, "pet fusion", () => {
            Player.Value.Character.PetFusionCount = 0;
            Player.Value.Character.PetFusionTime = DateTime.Now.ToEpochSeconds();
            Send(PetPacket.FusionCount(0));
        });
        // Mini-Game M extra rewards
        Player.Value.Character.MassiveExtraRewardCount = 0;
        Player.Value.Character.MassiveExtraRewardTime = DateTime.Now.ToEpochSeconds();
        // Sky fortress mission attacks, with the reset's time.
        ResetStep(reset, "mission attacks", () => Quest.ResetMissionAttacks(resetTime));
    }

    public void WeeklyReset() {
        const string reset = nameof(WeeklyReset);
        // The prestige level rewards stay claimed: the client has no reset of its claim set (scenestategame.cpp:8870, :8876).
        // The prestige missions are weekly and start again (PrestigeRules.ResetMissions).
        ResetStep(reset, "prestige missions", () => {
            PrestigeRules.ResetMissions(Player.Value.Account.PrestigeMissions);
            Send(PrestigePacket.LoadMissions(Player.Value.Account));
        });
        // Dungeon enter limits
        ResetStep(reset, "dungeon limits", () => Dungeon.UpdateDungeonEnterLimit());
        // Dungeon weekly clears
        ResetStep(reset, "dungeon", () => Dungeon.ResetWeeklyClears());
        // Shop restock
        ResetStep(reset, "shop", () => Shop.WeeklyReset());
        // The rank duel window's weekly token gauge: the database row is reset already.
        ResetStep(reset, "rank duel", () => RankDuelStanding.Send(this));
        // Guild weekly contribution: the World's guild and the database row are reset already.
        ResetStep(reset, "guild", () => Guild.WeeklyReset());
    }

    public void MonthlyReset() {
        // Meso Market
        ResetStep(nameof(MonthlyReset), "meso market", () => {
            Player.Value.Account.MesoMarketPurchased = 0;
            Send(MesoMarketPacket.Quota(Player.Value.Account.MesoMarketListed, Player.Value.Account.MesoMarketPurchased));
        });
        // Merat payback: the month that ended is paid on the session's field.
        ResetStep(nameof(MonthlyReset), "payback", () => PaybackService.MonthlyReset(this));
    }

    public void MigrateToPlanner(PlotMode plotMode) {
        SessionState state = State;
        bool moved = false, saved = false;
        try {
            // Saved before the World is asked for the move (see Migrate).
            saved = !preMigrationSaved;
            if (!MigrationSave()) {
                Send(MigrationPacket.GameToGameError(MigrationError.s_move_err_default));
                return;
            }
            State = SessionState.ChangeMap;

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
            moved = true;
        } catch (RpcException ex) {
            Logger.Warning("Move to map {MapId} refused by the World server: {Status}", Constant.DefaultHomeMapId, ex.StatusCode);
            Send(MigrationPacket.GameToGameError(MoveRefusedError(ex.StatusCode)));
        } finally {
            if (!moved) {
                // Not moved: the session's last save (at Dispose) stores it again, when this move made the save. A
                // session the failed save ended stays closed.
                if (State is not SessionState.Disconnected) {
                    State = state;
                }
                if (saved) {
                    MigrationRefused();
                }
            } else {
                Disconnect();
            }
        }
    }

    // A move the World server refuses or answers without an address: the client shows its move error box
    // (CNetworkService::OnGameToGame, kind 2) and plays on in this field, so the session stays, as on a refused channel
    // change (ChannelHandler).
    internal static MigrationError MoveRefusedError(StatusCode status) => status switch {
        StatusCode.Unavailable => MigrationError.s_move_err_no_server,
        StatusCode.ResourceExhausted => MigrationError.s_move_err_member_limit,
        _ => MigrationError.s_move_err_default,
    };

    // beforeMove runs right before the migrate packet, only when the move is made (not when the map is refused or
    // the World server gives no address); a second save of what it changed that does not store puts the return
    // position back (Undo). position is the spot on the map to arrive at (the return field's saved
    // spot); default is the map's spawn.
    public void Migrate(int mapId, long ownerId = 0, Action? beforeMove = null, Vector3 position = default) {
        // Not to a map the played client does not have (see PrepareFieldInternal).
        if (MapMetadata.TryGet(mapId, out MapMetadata? target) && target.Property.NotInPlayedClient) {
            Send(NoticePacket.Message(FieldEntryRules.NotInPlayedClientNotice(target.Id, target.Name)));
            return;
        }

        bool isInstanced = ServerTableMetadata.InstanceFieldTable.Entries.ContainsKey(mapId);

        Character character = Player.Value.Character;
        (int mapBefore, short returnBefore, Vector3 returnPositionBefore, SessionState state) =
            (character.MapId, character.ReturnChannel, character.ReturnPosition, State);
        bool moved = false, saved = false;
        // Not moved: the character stays where it is, and the session's last save (at Dispose) stores that. Run also
        // by a save that does not store, before it ends the session (MigrationSave). The return position is the one
        // beforeMove sets; its other change (home_goto, HomeHandler) is kept: the client was sent the trophy update
        // already, and a condition count reaches quests and events too.
        void Undo() {
            character.MapId = mapBefore;
            character.ReturnChannel = returnBefore;
            character.ReturnPosition = returnPositionBefore;
            if (State is not SessionState.Disconnected) {
                State = state;
            }
        }
        try {
            if (isInstanced) {
                character.ReturnChannel = character.Channel;
            } else {
                character.MapId = mapId;
                character.ReturnChannel = 0;
            }

            // Saved before the World is asked for the move, as the channel change does. The move's token lives 30 s
            // (MigrateOut): a save after it that waited longer (on a reset being stored, up to 60 s) outlived the token
            // and the move failed. Saved after the move was sent, the client could enter the other server while the
            // save still waited, and that entry's claim of the account refused the save.
            saved = !preMigrationSaved;
            if (!MigrationSave(Undo)) {
                Send(MigrationPacket.GameToGameError(MigrationError.s_move_err_default));
                return;
            }
            State = SessionState.ChangeMap;

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
            if (position != default) {
                request.PositionX = position.X;
                request.PositionY = position.Y;
                request.PositionZ = position.Z;
            }

            MigrateOutResponse response = World.MigrateOut(request);
            if (!PlayerWarpUtil.TryEndpoint(response.IpAddress, response.Port, out IPEndPoint? endpoint)) {
                Logger.Error("{Where}: the World server answered with the address '{Address}' port {Port}", "Field migration", response.IpAddress, response.Port);
                Send(MigrationPacket.GameToGameError(MigrationError.s_move_err_default));
                return;
            }
            if (beforeMove != null) {
                beforeMove();
                if (saved) {
                    // What it changed (a trophy count, the return position) is stored with the move: a second save,
                    // short, since a reset the first save waited on is stored by now.
                    MigrationRefused();
                    if (!MigrationSave(Undo)) {
                        Send(MigrationPacket.GameToGameError(MigrationError.s_move_err_default));
                        return;
                    }
                }
            }
            Dungeon?.LeaveStage();
            Send(MigrationPacket.GameToGame(endpoint, response.Token, mapId));
            moved = true;
        } catch (RpcException ex) {
            Logger.Warning("Move to map {MapId} refused by the World server: {Status}", mapId, ex.StatusCode);
            Send(MigrationPacket.GameToGameError(MoveRefusedError(ex.StatusCode)));
        } finally {
            if (!moved) {
                Undo();
                if (saved) {
                    MigrationRefused();
                }
            } else {
                Disconnect();
            }
        }
    }

    // Taken: this caller holds the lock and gives it back. Held: another holder has it after the retries; the caller
    // neither writes (a store is refused) nor releases it (the release would free that holder's lock). WorldDown: the
    // World cannot be reached; a World that is down (stopped before this server) holds no locks, so a store goes on
    // without one (a logout's last save at shutdown), and nothing is released.
    private enum LockTake { Taken, Held, WorldDown }

    // The wait between two tries of AcquireLock (tests set 0).
    internal int LockBackoffMs = 500;
    // The tries of the last save (Dispose's, LastSave): about 32 s at 500 ms, past the World's 30 s lock expiry, so a
    // lock left by a holder that went away expires and is taken.
    internal const int LastSaveLockTries = 64;

    // token: the take's, which the release passes back (0 when not taken).
    private LockTake AcquireLock(long accountId, out long token, int maxRetries = 3) {
        token = 0;
        int retryCount = 0;
        int backoffMs = LockBackoffMs;

        while (retryCount < maxRetries) {
            LockResponse? response;
            try {
                response = World.AcquireLock(new LockRequest {
                    AccountId = accountId,
                });
            } catch (RpcException ex) when (WorldCallDeadline.NotSent(ex)) {
                // Not sent: the World is alive but not answering, and may hold the lock. The store is refused as for
                // a lock held elsewhere (without the lock it could write over another server's save).
                Logger.Warning("World not answering; lock for account {AccountId} not asked, taken as held", accountId);
                return LockTake.Held;
            } catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable) {
                // A world that is down (stopped before this server) holds no locks and grants none. The save goes on
                // without the lock, as after the retries below; a throw here would skip the save.
                Logger.Warning("World unavailable; lock for account {AccountId} not taken", accountId);
                return LockTake.WorldDown;
            }

            if (string.IsNullOrEmpty(response.Error)) {
                token = response.Token;
                return LockTake.Taken;
            }

            retryCount++;
            Thread.Sleep(backoffMs);
        }

        Logger.Error("Failed to acquire lock for account {AccountId} after {MaxRetries} retries", accountId, maxRetries);
        return LockTake.Held;
    }

    private void ReleaseLock(long accountId, long token) {
        try {
            LockResponse response = World.ReleaseLock(new LockRequest {
                AccountId = accountId,
                Token = token,
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
        // Loads from here on keep nothing (the final save writes what changed all the same).
        heldRows?.Dispose();

        // Snapshot values needed after teardown
        long fieldTickSnapshot = Field?.FieldTick ?? Environment.TickCount64;

        // A session that plays here tells the World it went offline only after its last save: while the World still
        // has it online, a login of the account kicks it (World.Disconnect), and the channel answers that kick only
        // once this Dispose has ended (ChannelService.Disconnect), so the next load reads what the save stored. Told
        // before the save, the login went on at once, and a load that claimed the account first (ClaimSession) left
        // that save writing nothing: what the session used up since its timed save came back.
        bool sendOffline = State == SessionState.Connected;

        // A host that leaves the channel (a logout, a channel change) ends its OX quiz event: the stadium script runs
        // its cancel.
        if (Player != null && FieldFactory?.FindUgcOx(AccountId) is { } ugcOx && ugcOx.Cancel()) {
            Logger.Information("OX quiz host {CharacterId} left the channel, event canceled", CharacterId);
        }

        // The character was never loaded (an arrival with a used or unknown token): there is nothing to save.
        if (Player == null) {
            try {
                if (sendOffline) {
                    SendOffline();
                }
                Scheduler.Stop();
                if (OnLoop != null) OnLoop -= Scheduler.InvokeAll;
                server.OnDisconnected(this);
                State = SessionState.Disconnected;
                base.Dispose(disposing);
            } catch (Exception ex) {
                Logger.Error(ex, "Error during cleanup of a session without a character");
            } finally {
                Interlocked.Exchange(ref gameDisposeState, 2);
                server?.OnClosed(this);
            }
            return;
        }

        ClosePackets();

        try {
            UserRecallUtil.DropPending(this);
        } catch (Exception ex) {
            Logger.Error(ex, "Error dropping the recall invite of {Player}", PlayerName);
        }

        try {
            Scheduler.Stop();
            if (OnLoop != null) OnLoop -= Scheduler.InvokeAll;
            server.OnDisconnected(this);
            CloseLeaveField();
            Player.Value.Character.Channel = -1;
            Player.Value.Account.Online = false;
            State = SessionState.Disconnected;
            playerActionsClosed = true;

            // Early base dispose to stop further network sends
            base.Dispose(disposing);

            // Cache config & persistence (the cache config only of a session that entered: see entered)
            if (entered) {
                SaveCacheConfig();
            }
            LastSave();
        } catch (Exception ex) {
            Logger.Error(ex, "Error during session cleanup for {Player}", PlayerName);
        } finally {
            if (sendOffline) {
                try { SendOffline(); } catch (Exception ex) { Logger.Error(ex, "Error sending the logout of {Player}", PlayerName); }
            }
            SafeDispose(Guild);
            SafeDispose(Buddy);
            SafeDispose(Mentoring);
            SafeDispose(Party);
            foreach ((int groupChatId, GroupChatManager groupChat) in GroupChats) {
                try { groupChat.CheckDisband(); } catch (Exception ex) { Logger.Error(ex, "Error disbanding group chat {Id} for {Player}", groupChatId, PlayerName); }
            }
            foreach ((long clubId, ClubManager club) in Clubs) {
                SafeDispose(club);
            }
            try { Player.Dispose(); } catch (Exception ex) { Logger.Error(ex, "Error disposing player for {Player}", PlayerName); }
            Interlocked.Exchange(ref gameDisposeState, 2);
            server?.OnClosed(this);
        }
        return;

        void SendOffline() {
            // The logout comes after the final save. A newer session of the character on this channel (a login after
            // the World had already set the player offline) is the one online: it is not ended here.
            if (server.GetSession(CharacterId, out GameSession? newer) && newer != this) {
                return;
            }
            PlayerInfo.SendUpdate(new PlayerUpdateRequest {
                AccountId = AccountId,
                CharacterId = CharacterId,
                LastOnlineTime = DateTime.UtcNow.ToEpochSeconds(),
                MapId = 0,
                Channel = -1,
                FromChannel = GameServer.GetChannel(),
                Async = false,
            });

            Party.CheckDisband();
        }

        void SafeDispose(IDisposable? disp) {
            if (disp == null) return;
            try { disp.Dispose(); } catch (Exception ex) { Logger.Error(ex, "Error disposing component for {Player}", PlayerName); }
        }

        void SaveCacheConfig() {
            List<Buff> buffs = Buffs.GetSaveCacheBuffs();
            IList<SkillCooldown> skillCooldowns = Config.GetCurrentSkillCooldowns();

            long stopTime = DateTime.Now.ToEpochSeconds();
            long fieldTick = fieldTickSnapshot;
            // A copy taken under the hold's lock: the session thread may be entering a map.
            (Dictionary<int, SkillCooldown> pvpHeld, bool pvpStay) = Config.PvpHold.Snapshot();

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
                                    RechargeMs = cooldown.RechargeMs,
                                }),
                        },
                        PvpHeldCooldowns = {
                            pvpHeld.Values.Select(cooldown => ConfigManager.ToCache(cooldown, fieldTick, stopTime)),
                        },
                        PvpStay = pvpStay,
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

    // True once Dispose has ended (its last save stored or given up), waiting up to `timeout` for a Dispose that runs
    // on another thread (the client's own close, a move's disconnect). False after the timeout, or for a session
    // whose Dispose has not started by then.
    internal bool WaitDisposed(TimeSpan timeout) {
        long end = Environment.TickCount64 + (long) timeout.TotalMilliseconds;
        while (Volatile.Read(ref gameDisposeState) != 2) {
            if (Environment.TickCount64 >= end) {
                return false;
            }
            Thread.Sleep(10);
        }
        return true;
    }

    // Runs once: the save before a migration is not tried again when the session is disposed, since the client was
    // already sent to the other channel, which may have loaded (and saved) the player by then.
    // A save that fails is no move: false, and the caller refuses the move (the client's move error box) and the
    // session plays on here, saving again by its timed save and at Dispose. A move made after a failed save sent the
    // client to a channel that loaded the last stored state, while the items made since were stored at once and the
    // ones used up were not: the rewards came back, and the meso spent since the last save was lost or kept. The
    // save is tried twice, as SessionSave (a passing error such as a deadlock), and a session whose second try fails
    // too is ended as there (Cut: what it holds since its last save is in its memory only), its Dispose trying one
    // last save. `undo` puts back what the move changed ahead of the save (the target map, the session's ChangeMap
    // state); it runs before the cut, so that Dispose stores the character where it is and tells the World the player
    // is offline (a session it finds in ChangeMap is one that left for another server). The client gets the reset
    // only: an error the caller sends after a false is not sent on a closed session. A reset that holds the save past ResetWait refuses the move only: a reset must not end sessions.
    // It waits for a reset being stored (up to ResetWait), on the field thread when a move handler calls it: the
    // move must be stored before the client is sent on, and coming back later would let the other channel load the
    // state from before it. The wait is the reset's own run, about 1 to 5 s for a daily reset over 50k characters,
    // once a day and only for the moves in those seconds; the save holds no row while it waits (the reset times are
    // its first read), so the reset never waits on it. 60 s bounds a reset that hangs (its statements time out at
    // 30 s each); before the share-lock read, the same save waited on the reset's rows in the database, up to 50 s.
    public bool MigrationSave(Action? undo = null) {
        SaveResult result = SaveBeforeLeaving();
        if (result == SaveResult.Stored) {
            return true;
        }
        undo?.Invoke();
        switch (result) {
            case SaveResult.ResetRunning:
                Logger.Warning("MigrationSave: a reset is still being stored after {Wait}, the move is refused AccountId={AccountId} CharacterId={CharacterId}",
                    ResetWait, AccountId, CharacterId);
                return false;
            case SaveResult.Unknown:
                // Logged with its stack where the commit threw, and the session is ended there (one try was made).
                return false;
        }
        Logger.Fatal("MigrationSave: not stored after a second try, the move is refused and the session is ended; the stored state is the last full save AccountId={AccountId} CharacterId={CharacterId}",
            AccountId, CharacterId);
        Cut();
        return false;
    }

    // The close's LeaveField, run on the field's thread at the start of its next update: the buffs, the pet, the
    // instrument and the trade it ends are state the field's update reads (changed beside it, a collection the update
    // went through changed under it), and a handler of the player still running there (past PacketBatchWait) ends
    // first, so the last save after it does not store a change half made. Waited up to CloseWait.
    private void CloseLeaveField() {
        FieldManager? field = Field;
        if (field == null) {
            LeaveField();
            return;
        }
        if (!field.RunOnFieldAndWait(CloseWaitLeft(), LeaveField)) {
            Logger.Warning("Close of {Session}: the field's thread did not take the leave (busy past {Wait}, or stuck before), leaving the field beside it",
                this, CloseWait);
        }
    }

    // What is left of the close's CloseWait, which starts at the first wait on it.
    private TimeSpan CloseWaitLeft() {
        long now = Environment.TickCount64;
        if (Interlocked.CompareExchange(ref closeWaitEnd, now + (long) CloseWait.TotalMilliseconds, 0) == 0) {
            return CloseWait;
        }
        return TimeSpan.FromMilliseconds(Math.Max(0, Interlocked.Read(ref closeWaitEnd) - now));
    }

    // A kick through the World (the GM commands), asked off the caller's thread: the World answers once the kicked
    // session's close ended (up to ChannelService.CloseWait), and that close leaves its field on the field's thread,
    // so a GM's field never waits on it. The answer is null when the World could not be asked.
    public Task<DisconnectResponse?> Kick(DisconnectRequest request) {
        return Task.Factory.StartNew(() => {
            try {
                return World.Disconnect(request);
            } catch (Exception ex) {
                Logger.Warning(ex, "Kick: the World's disconnect failed CharacterId={Target} All={All}", request.CharacterId, request.All);
                return (DisconnectResponse?) null;
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    // Dispose's last save: as a move's, with no session left to end.
    private void LastSave() {
        if (SaveBeforeLeaving(last: true) != SaveResult.Stored) {
            Logger.Error("LastSave: not stored, the stored state is the last full save AccountId={AccountId} CharacterId={CharacterId}", AccountId, CharacterId);
        }
    }

    // Stored also when an earlier save of this move (or of the session's close) stored it. A save that did not store
    // clears the flag again: the session's packets are handled again and its next save is made. last: Dispose's save,
    // the session's state is lost when it is not stored (LockedSave).
    private SaveResult SaveBeforeLeaving(bool last = false) {
        if (preMigrationSaved) return SaveResult.Stored;
        // A handler the receive thread runs (a guild fund donation, a limit break) ends before the flag is set, and
        // the ones read after it are dropped: none changes the wallet or the inventory while the save reads them, or
        // after it. Every move's save comes here (channel change, quit, portal and warp moves, dungeon and PvP moves,
        // the global portal and the field war). A handler that leads to the save itself (a portal move) holds the
        // gate already. The wait is bounded: a handler stuck past InlineGateWait (a World call without a deadline)
        // does not hold the field thread longer; the save then goes on, logged.
        // The close's save waits longer (what is left of CloseWait): no player waits on it, and saved beside a running
        // handler it would store that handler's change half made.
        TimeSpan gateWait = last ? CloseWaitLeft() : InlineGateWait;
        bool gated = Monitor.TryEnter(InlineGate, gateWait);
        try {
            if (preMigrationSaved) return SaveResult.Stored;
            preMigrationSaved = true;
        } finally {
            if (gated) Monitor.Exit(InlineGate);
        }
        if (!gated) {
            Logger.Warning("MigrationSave: a receive-thread handler still runs after {Wait}, saving beside it AccountId={AccountId} CharacterId={CharacterId}",
                gateWait, AccountId, CharacterId);
        }

        // An open trade ends first, as cancelled: the mesos offered (taken from the wallet when set) come back to it
        // before the wallet is stored. Cancelled later, at LeaveField, they came back in memory only and the save at
        // Dispose is skipped once this one ran, so a channel change, a move to another server or a quit to the
        // character list lost them. Ended here, the partner cannot finish the trade after the save either (its mesos
        // would reach this wallet unstored). The client sends these moves with a trade open (no trade check).
        Trade?.Dispose();

        SaveResult result = LockedSave(tries: 2, ResetWait, last);
        if (result != SaveResult.Stored) {
            preMigrationSaved = false;
        }
        return result;
    }

    // The start of a close (a dropped connection, a kick, the server's stop): no packet of the session is handled from
    // here on, and the one that runs ends first. Dispose runs on the closing thread, while the field thread may be in
    // one of the player's handlers (a reward claim): its item row is stored at once, and the save after LeaveField
    // read the claim, the wallet and the inventory beside it, or before it. The flag is set under the field's packet
    // queue (the running batch ends first) and under the receive-thread gate (a running inline handler ends first);
    // on the field thread inside a handler, or on the receive thread inside an inline one, the lock is its own.
    // Both waits are bounded (PacketBatchWait, InlineGateWait): a batch stuck past it (a move's save in a reset
    // wait) does not hold the close longer, which then goes on as before, logged.
    internal void ClosePackets() {
        if (packetsClosed) return;
        FieldManager? field = Field;
        if (field != null) {
            try {
                if (!field.AfterPacketBatch(PacketBatchWait, () => packetsClosed = true)) {
                    Logger.Warning("Close of {Session}: the field's packet batch still runs after {Wait}, closing beside it", this, PacketBatchWait);
                }
            } catch (Exception ex) {
                Logger.Error(ex, "Close of {Session}: waiting for the field's packet batch failed", this);
            }
        }
        packetsClosed = true;
        if (Monitor.TryEnter(InlineGate, InlineGateWait)) {
            Monitor.Exit(InlineGate);
        } else {
            Logger.Warning("Close of {Session}: a receive-thread handler still runs after {Wait}, closing beside it", this, InlineGateWait);
        }
    }

    // A move that called MigrationSave and then did not leave (the World or the endpoint refused it, and the session
    // plays on here): the session saves again, by its timed save and at Dispose.
    internal void MigrationRefused() {
        preMigrationSaved = false;
    }

    // The last channel change this session was refused (Environment.TickCount64) and its error, for the throttle of
    // ChannelHandler; 0 before any.
    internal long ChannelChangeRefusedAt;
    internal MigrationError ChannelChangeRefusedError;

    // A session whose save fails (after one immediate try again, for a passing error such as a deadlock) is ended:
    // what it gave or took since its last stored save is in its memory only, and items made since are stored at once
    // while the items it used up are written by the save (a stack's lower amount, a removed item). Played on, every
    // box opened or reward handed out would come back at the next login, again each session. Ending it is the one
    // step that closes every such path; refusing each reward and item action instead would have to reach every
    // handler that gives one. The cut takes the lost-connection path: Dispose tries one last full save
    // (MigrationSave), and the next login starts from the last stored state, whole, with no part written alone.
    // False when the session was ended: the caller does nothing more with it.
    // A session save asked for inside a store of this session (the account lock is not re-entrant) runs once the
    // outermost store of the thread gave its lock back (AfterStore), and is logged with its stack; true then, since
    // the session goes on. The deferred save ends the session as any session save does when it fails.
    public bool SessionSave() {
        if (Storing) {
            ReportNested("SessionSave deferred", "asked for inside another store of the same session, run after it");
            AfterStore(() => SessionSave());
            return true;
        }
        if (LockedSave(tries: 2, ResetWait) == SaveResult.Stored) {
            return true;
        }
        Logger.Fatal("SessionSave: not stored after a second try, the session is ended; the stored state is the last full save AccountId={AccountId} CharacterId={CharacterId}",
            AccountId, CharacterId);
        Cut();
        return false;
    }

    // The first save after the player is loaded (EnterServer): a character whose state cannot be stored (a row the
    // database refuses each time) is not let in, and stays out until its row is fixed. The entry is refused before
    // anything of it is created, so nothing is given that a later save could not match.
    internal bool EntrySave() {
        if (LockedSave(tries: 2, ResetWait) == SaveResult.Stored) {
            return true;
        }
        Logger.Fatal("EntrySave: the character's state cannot be stored, the entry is refused AccountId={AccountId} CharacterId={CharacterId}",
            AccountId, CharacterId);
        return false;
    }

    // Unknown: a store (a purchase save or a session save) whose commit threw and whose mark could not be read again
    // (storeUnknown).
    private enum SaveResult { Stored, Failed, ResetRunning, Unknown }

    // Set by a store whose commit threw and whose mark could not be read: its rows may be in the database or not,
    // while the session's memory is that of a failed store (a purchase's caller put back what it took). A later store
    // would write that memory over a purchase that was kept (a refund of a price the database took), so every store
    // of the session is refused from then on, its last one at Dispose too, and the session is ended: the next login
    // loads what the database holds, the purchase with its price or neither.
    private volatile bool storeUnknown;
    // How long the read of a purchase save's mark waits for the row's lock (a transaction whose connection is ending).
    private const int MarkLockWaitSeconds = 10;
    // Tests only: runs before the mark of a commit that threw is read again; a throw there is a read that failed.
    internal Action? MarkReadFault;

    private bool StoreRefused(string where) {
        if (!storeUnknown) {
            return false;
        }
        Logger.Error("{Where}: an earlier save of this session (a purchase or a session save) ended unknown, nothing more is stored AccountId={AccountId} CharacterId={CharacterId}",
            where, AccountId, CharacterId);
        return true;
    }

    // ResetRunning: a reset was being stored for longer than resetWait, and nothing was written. That is no failed
    // save: a reset must not end sessions. A save with a wait that still finds it running (a reset stuck for a
    // minute) is a failed one for its caller. last: the session's last save (every failed save ends the session, and
    // its Dispose makes this one), whose drop loses the player's state: it waits for the lock past the World's 30 s
    // expiry (LastSaveLockTries), and a lock still held after that does not drop it: the save goes on without the lock,
    // logged, as all saves did before the lock refused them (a session that entered after this one has claimed the
    // account, ClaimSession, and such a save writes nothing). A World that is down lets every save go on without the
    // lock; a World error other than that throws and refuses the save.
    private SaveResult LockedSave(int tries, TimeSpan resetWait, bool last = false) {
        if (StoreRefused("SessionSave") || NestedStore("SessionSave")) {
            return SaveResult.Failed;
        }
        bool outer = BeginStoreScope();
        EnterStore();
        bool locked = false;
        long lockToken = 0;
        bool unknown = false;
        try {
            LockTake take = AcquireLock(AccountId, out lockToken, last ? LastSaveLockTries : 5);
            locked = take == LockTake.Taken;
            if (take == LockTake.Held) {
                if (!last) {
                    Logger.Error("SessionSave: the account lock is held elsewhere, nothing stored AccountId={AccountId} CharacterId={CharacterId}", AccountId, CharacterId);
                    return SaveResult.Failed;
                }
                Logger.Error("LastSave: the account lock is still held elsewhere after {Tries} tries, saving without it AccountId={AccountId} CharacterId={CharacterId}",
                    LastSaveLockTries, AccountId, CharacterId);
            }
            for (int i = 0; i < tries; i++) {
                if (Save(resetWait)) {
                    return SaveResult.Stored;
                }
                // Its commit threw and whether it was kept cannot be read (CommitThrew): no second try.
                if (storeUnknown) {
                    unknown = true;
                    return SaveResult.Unknown;
                }
            }
            return SaveResult.Failed;
        } catch (ResetRunningException ex) {
            Logger.Information("Save: {Reason}, nothing written AccountId={AccountId} CharacterId={CharacterId}", ex.Message, AccountId, CharacterId);
            return SaveResult.ResetRunning;
        } catch (Exception ex) {
            Logger.Error(ex, "SessionSave failed AccountId={AccountId} CharacterId={CharacterId}", AccountId, CharacterId);
            return SaveResult.Failed;
        } finally {
            if (locked) {
                ReleaseLock(AccountId, lockToken);
            }
            // Before the queued steps: they run once the lock is given back, and may store.
            LeaveStore();
            EndStoreScope(outer);
            if (unknown) {
                Cut();
            }
        }
    }

    // The session whose store (LockedSave, PurchaseSave) runs on this thread. The World's account lock is not
    // re-entrant: a store started inside another store of the same session (from the rows' callback: a quest
    // completed by a currency change, a reset step of a clear) waited on the lock its own thread holds, and its
    // ReleaseLock then freed the outer store's lock while that one still wrote. Such a store is refused (a failed
    // save for its caller; a session save is deferred, SessionSave) and logged with its stack; the outer store goes
    // on. Every session storing on the thread is kept, not only the innermost: a store of this session inside another
    // session's store inside one of this session's is nested too.
    [ThreadStatic] private static List<GameSession>? storing;
    // Told of each nested store (where, stack): the tests fail any test that nests one.
    internal static Action<string, string>? OnNestedStore;

    private bool Storing => storing?.Contains(this) == true;

    private void EnterStore() => (storing ??= []).Add(this);

    private static void LeaveStore() => storing!.RemoveAt(storing.Count - 1);

    private bool NestedStore(string where) {
        if (!Storing) {
            return false;
        }
        ReportNested(where, "started inside another store of the same session, refused");
        return true;
    }

    private void ReportNested(string where, string what) {
        string stack = Environment.StackTrace;
        Logger.Error("{Where}: {What} AccountId={AccountId} CharacterId={CharacterId}{NewLine}{Stack}",
            where, what, AccountId, CharacterId, Environment.NewLine, stack);
        OnNestedStore?.Invoke(where, stack);
    }

    // The timed save, from the session's queue: it runs on the player's field thread, where the packets that change
    // the player are handled, and not while the session changes field or channel (State is ChangeMap then; that
    // move saves on its own), nor once a move made its MigrationSave (the other channel may have loaded the player,
    // and a later save here would write over what it stored). A failed timed save is tried again about 2 s later on
    // the same field queue (the field thread does not wait), and the session is ended when that fails too. A timed
    // save (or its try again) that finds a reset being stored does not wait for it on the field thread: it comes back
    // 2 s later, as often as needed, and is not counted as failed.
    private void TimedSave() {
        FieldManager? field = Field;
        if (State != SessionState.Connected || field == null) {
            return;
        }
        field.Scheduler.Schedule(() => TimedSaveOnField(field));
    }

    internal void TimedSaveOnField(FieldManager field) {
        if (!TimedSaveDue(field)) {
            return;
        }
        switch (LockedSave(tries: 1, TimeSpan.Zero)) {
            case SaveResult.Stored:
                return;
            case SaveResult.ResetRunning:
                field.Scheduler.Schedule(() => TimedSaveOnField(field), TimedSaveRetryDelay);
                return;
            case SaveResult.Unknown:
                // Logged where the commit threw, and the session is ended there: no try again.
                return;
        }
        Logger.Warning("TimedSave: not stored, tried again in {Delay} AccountId={AccountId} CharacterId={CharacterId}", TimedSaveRetryDelay, AccountId, CharacterId);
        field.Scheduler.Schedule(() => TimedSaveRetry(field), TimedSaveRetryDelay);
    }

    internal void TimedSaveRetry(FieldManager field) {
        if (!TimedSaveDue(field)) {
            return;
        }
        switch (LockedSave(tries: 1, TimeSpan.Zero)) {
            case SaveResult.Stored:
                return;
            case SaveResult.ResetRunning:
                field.Scheduler.Schedule(() => TimedSaveRetry(field), TimedSaveRetryDelay);
                return;
            case SaveResult.Unknown:
                return;
        }
        Logger.Fatal("TimedSave: not stored after a second try, the session is ended; the stored state is the last full save AccountId={AccountId} CharacterId={CharacterId}",
            AccountId, CharacterId);
        Cut();
    }

    private bool TimedSaveDue(FieldManager field) => State == SessionState.Connected && Field == field && !preMigrationSaved;

    /// <summary>
    /// Stores the currency right after a purchase, together with any rows of the same purchase,
    /// under the account lock. Only the currency and the given rows are written, not the whole session.
    /// Answers true when the currency and the purchase's rows were stored.
    /// </summary>
    // A purchase save does not wait for a reset being stored (its callers run on the field thread, which a wait would
    // hold for every player of the field): this one, whose callers go on whatever it answers, comes back 2 s later
    // on the same field queue, as often as needed while the session plays on that field, as the timed save does. A
    // session that has left the field by then stored the purchase with its move's save. It used to wait up to 60 s.
    public bool SavePurchase(Action<GameStorage.Request>? alsoSave = null) {
        SaveResult result = PurchaseSave(db => {
            alsoSave?.Invoke(db);
            return true;
        });
        if (result == SaveResult.ResetRunning && Field is { } field) {
            field.Scheduler.Schedule(() => SavePurchaseRetry(field, alsoSave), TimedSaveRetryDelay);
        }
        return result == SaveResult.Stored;
    }

    // SavePurchase for a price given back (the currency alone): a failed store is tried once more too, 2 s later on
    // the field queue (a reset being stored, as often as needed), so a refund does not wait in memory for the next
    // timed save. Whatever is still not stored then goes with that save.
    public bool SaveRefund() => SaveRefund(Field, failedTries: 1);

    private bool SaveRefund(FieldManager? field, int failedTries) {
        SaveResult result = PurchaseSave(_ => true);
        if (result == SaveResult.Stored) {
            return true;
        }
        int left = result == SaveResult.ResetRunning ? failedTries : failedTries - 1;
        if (field != null && left >= 0) {
            field.Scheduler.Schedule(() => {
                if (TimedSaveDue(field)) {
                    SaveRefund(field, left);
                }
            }, TimedSaveRetryDelay);
        } else {
            Logger.Error("SaveRefund: refund not stored, left to the next save AccountId={AccountId} CharacterId={CharacterId}", AccountId, CharacterId);
        }
        return false;
    }

    private void SavePurchaseRetry(FieldManager field, Action<GameStorage.Request>? alsoSave) {
        if (TimedSaveDue(field)) {
            SavePurchase(alsoSave);
        }
    }

    // SavePurchase for rows whose save reports its result: nothing is stored when alsoSave answers false. A reset
    // being stored refuses the purchase at once (false, as a failed save: the caller gives back what it took), so the
    // field thread does not wait for it; the player can buy again a few seconds later.
    public bool TrySavePurchase(Func<GameStorage.Request, bool> alsoSave) => PurchaseSave(alsoSave) == SaveResult.Stored;

    // TrySavePurchase that also says whether a reset being stored refused it (the caller may try again later).
    public bool TrySavePurchase(Func<GameStorage.Request, bool> alsoSave, out bool resetRunning) {
        SaveResult result = PurchaseSave(alsoSave);
        resetRunning = result == SaveResult.ResetRunning;
        return result == SaveResult.Stored;
    }

    // TrySavePurchase with both: the caller's memory put back (undo) before the account lock is given back, and
    // whether a reset being stored refused it.
    public bool TrySavePurchase(Func<GameStorage.Request, bool> alsoSave, Action undo, out bool resetRunning) {
        SaveResult result = PurchaseSave(alsoSave, undo: undo);
        resetRunning = result == SaveResult.ResetRunning;
        return result == SaveResult.Stored;
    }

    // TrySavePurchase with both: a refusal that is no failure (refused), and the caller's memory put back (undo) before
    // the account lock is given back.
    public bool TrySavePurchase(Func<GameStorage.Request, bool> alsoSave, Func<bool> refused, Action undo, out bool resetRunning) {
        SaveResult result = PurchaseSave(alsoSave, refused, undo);
        resetRunning = result == SaveResult.ResetRunning;
        return result == SaveResult.Stored;
    }

    // TrySavePurchase whose rows may be refused for a reason that is no failure (refused answers true then: a listing
    // another buyer took first); such a refusal is logged as information, not as an error.
    public bool TrySavePurchase(Func<GameStorage.Request, bool> alsoSave, Func<bool> refused, out bool resetRunning) {
        SaveResult result = PurchaseSave(alsoSave, refused);
        resetRunning = result == SaveResult.ResetRunning;
        return result == SaveResult.Stored;
    }

    // TrySavePurchase whose refusal puts the caller's memory back (undo) before the account lock is given back, so a
    // session save waiting for the lock writes the old values, not the ones the refused store held. resetWait: how
    // long the store waits for a reset being stored before it is refused (none for the others).
    public bool TrySavePurchase(Func<GameStorage.Request, bool> alsoSave, Action undo, TimeSpan resetWait = default) =>
        PurchaseSave(alsoSave, undo: undo, resetWait: resetWait) == SaveResult.Stored;

    // A reset step reached inside a store (BeginWithResets takes the stored resets) that stores on its own (a mail
    // with what marks it sent) must not take the account lock again: the World's lock is not re-entrant, so the inner
    // store waited for it, went on without it, and gave the outer store's lock back. Such a step runs through
    // AfterStore: inside a store of this thread it runs once that store has given its lock back, else at once.
    // The queue is the thread's, not the session's (another session's store may run inside this one's): each step
    // keeps the session that queued it, which its failure names.
    [ThreadStatic] private static List<(GameSession Session, Action Action)>? afterStore;

    public void AfterStore(Action action) {
        if (afterStore != null) {
            afterStore.Add((this, action));
            return;
        }
        action();
    }

    // True for the outermost store of this thread, which runs what AfterStore queued (EndStoreScope).
    private static bool BeginStoreScope() {
        if (afterStore != null) {
            return false;
        }
        afterStore = [];
        return true;
    }

    private void EndStoreScope(bool outer) {
        if (!outer) {
            return;
        }
        List<(GameSession Session, Action Action)> queued = afterStore ?? [];
        afterStore = null;
        foreach ((GameSession session, Action action) in queued) {
            try {
                action();
            } catch (Exception ex) {
                Logger.Error(ex, "After a store: a queued step failed AccountId={AccountId} CharacterId={CharacterId}", session.AccountId, session.CharacterId);
            }
        }
    }

    // The session still plays on the field (a try again queued on the field's queue runs only then).
    public bool PlaysOn(FieldManager field) => TimedSaveDue(field);

    // Keeps the try again of a refused attend; false when one is pending for that event already (nothing to queue).
    public bool AttendRefused(int eventId, Action tryAgain) {
        lock (attendRefused) {
            return attendRefused.TryAdd(eventId, tryAgain);
        }
    }

    // Runs the event's pending try again, taken off first so that a refusal keeps a new one.
    public void AttendAgain(int eventId) {
        Action? tryAgain;
        lock (attendRefused) {
            if (!attendRefused.Remove(eventId, out tryAgain)) {
                return;
            }
        }
        tryAgain();
    }

    // The tries again whose queued run found the session gone from their field.
    public void AttendAgainAtFieldEntry() {
        int[] eventIds;
        lock (attendRefused) {
            eventIds = attendRefused.Keys.ToArray();
        }
        foreach (int eventId in eventIds) {
            AttendAgain(eventId);
        }
    }

    private SaveResult PurchaseSave(Func<GameStorage.Request, bool> alsoSave, Func<bool>? refused = null, Action? undo = null,
            TimeSpan resetWait = default) {
        bool outer = BeginStoreScope();
        SaveResult result = SaveResult.Failed;
        bool locked = false;
        long lockToken = 0;
        bool entered = false;
        bool nested = StoreRefused("SavePurchase") || NestedStore("SavePurchase");
        try {
            if (nested) {
                return result;
            }
            EnterStore();
            entered = true;
            // AcquireLock answers only an unreachable World itself; any other World error refuses the store, as does
            // a lock held elsewhere.
            LockTake take;
            try {
                take = AcquireLock(AccountId, out lockToken);
            } catch (Exception ex) {
                Logger.Error(ex, "SavePurchase: account lock failed AccountId={AccountId} CharacterId={CharacterId}", AccountId, CharacterId);
                return result;
            }
            locked = take == LockTake.Taken;
            if (take == LockTake.Held) {
                Logger.Error("SavePurchase: the account lock is held elsewhere, nothing stored AccountId={AccountId} CharacterId={CharacterId}", AccountId, CharacterId);
                return result;
            }
            return result = PurchaseRows(alsoSave, refused, resetWait);
        } finally {
            if (result != SaveResult.Stored && undo != null) {
                try {
                    undo();
                } catch (Exception ex) {
                    Logger.Error(ex, "SavePurchase: undo failed AccountId={AccountId} CharacterId={CharacterId}", AccountId, CharacterId);
                }
            }
            if (locked) {
                ReleaseLock(AccountId, lockToken);
            }
            // Before the queued steps: they run once the lock is given back, and may store.
            if (entered) {
                LeaveStore();
            }
            EndStoreScope(outer);
            if (result == SaveResult.Unknown) {
                Cut();
            }
        }
    }

    // PurchaseSave's rows, under the account lock.
    private SaveResult PurchaseRows(Func<GameStorage.Request, bool> alsoSave, Func<bool>? refused, TimeSpan resetWait) {
        try {
            using GameStorage.Request db = GameStorage.Context();
            using var tokens = new RowTokens(Player);
            // A reset stored since the session's state was read is taken first, so the rows written hold it.
            BeginWithResets(db, resetWait);
            if (!HoldsSession(db)) {
                return SaveResult.Failed;
            }
            // The currency and the purchase's rows are stored together or not at all (the request rolls back what it
            // did not commit); the session keeps both, and its next save stores them.
            if (!db.SaveCurrency(Player)) {
                Logger.Error("SavePurchase: currency not saved AccountId={AccountId} CharacterId={CharacterId}", AccountId, CharacterId);
                return SaveResult.Failed;
            }
            int failed = db.FailedSaves;
            if (!alsoSave(db) || db.FailedSaves != failed) {
                if (refused?.Invoke() == true) {
                    Logger.Information("SavePurchase: purchase refused, nothing stored AccountId={AccountId} CharacterId={CharacterId}", AccountId, CharacterId);
                } else {
                    Logger.Error("SavePurchase: purchase rows not saved, nothing stored AccountId={AccountId} CharacterId={CharacterId}", AccountId, CharacterId);
                }
                return SaveResult.Failed;
            }
            // The mark tells a commit that throws (its answer lost with the connection) kept or not (CommitThrew).
            long mark = WriteMark(db);
            bool committed;
            try {
                committed = db.Commit();
            } catch (Exception ex) {
                return CommitThrew("SavePurchase", db, tokens, ex, mark) switch {
                    true => SaveResult.Stored,
                    false => SaveResult.Failed,
                    null => SaveResult.Unknown,
                };
            }
            return (tokens.Kept = committed) ? SaveResult.Stored : SaveResult.Failed;
        } catch (ResetRunningException ex) {
            Logger.Information("SavePurchase: {Reason}, nothing written AccountId={AccountId} CharacterId={CharacterId}", ex.Message, AccountId, CharacterId);
            return SaveResult.ResetRunning;
        } catch (Exception ex) {
            Logger.Error(ex, "SavePurchase failed AccountId={AccountId} CharacterId={CharacterId}", AccountId, CharacterId);
            return SaveResult.Failed;
        }
    }

    // A store (a purchase save, a session save) whose commit threw: its writes may be kept or not. Its mark, read
    // again once the transaction is let go, tells which: kept (true), it is stored (a purchase's caller gives what was
    // paid for and refunds nothing; a session save is no failed one); not kept (false), it is a failed store as any
    // other. A mark that cannot be read (or none, on a store without transactions) leaves it unknown (null,
    // storeUnknown): logged with the stack, and the session is ended. Before, every commit that threw was a failed
    // one, and a purchase the database kept was refunded in memory, and stored so by the next save.
    private bool? CommitThrew(string where, GameStorage.Request db, RowTokens tokens, Exception ex, long mark) {
        bool? kept = db.SettleCommit(() => mark == 0 ? null : MarkStored(where, mark));
        switch (kept) {
            case true:
                Logger.Error(ex, "{Where}: the commit threw, but its mark is stored: the store is kept AccountId={AccountId} CharacterId={CharacterId}",
                    where, AccountId, CharacterId);
                tokens.Kept = true;
                return true;
            case false:
                Logger.Error(ex, "{Where}: the commit threw and its mark is not stored, nothing stored AccountId={AccountId} CharacterId={CharacterId}",
                    where, AccountId, CharacterId);
                return false;
        }
        storeUnknown = true;
        Logger.Error(ex, "{Where}: the commit threw and whether it was kept cannot be read; nothing more of this session is stored and it is ended, the next login loads what the database holds AccountId={AccountId} CharacterId={CharacterId}{NewLine}{Stack}",
            where, AccountId, CharacterId, Environment.NewLine, Environment.StackTrace);
        return null;
    }

    // Writes a new mark of the account in the store's open transaction (CommitThrew reads it again); 0 when the
    // store has none (the in-memory one of the tests).
    private long WriteMark(GameStorage.Request db) {
        long mark = Random.Shared.NextInt64(1, long.MaxValue);
        return db.WriteStoreMark(AccountId, mark) ? mark : 0;
    }

    // Whether the account's stored mark is this one, read on a connection of its own; null when the read fails.
    private bool? MarkStored(string where, long mark) {
        try {
            MarkReadFault?.Invoke();
            using GameStorage.Request read = GameStorage.Context();
            return read.ReadStoreMark(AccountId, MarkLockWaitSeconds) == mark;
        } catch (Exception ex) {
            Logger.Error(ex, "{Where}: the mark of a commit that threw could not be read AccountId={AccountId} CharacterId={CharacterId}", where, AccountId, CharacterId);
            return null;
        }
    }

    // Don't call this directly, since it does not acquire the account lock.
    // The player row (currency) and every component are stored together or not at all: each component pays, holds or
    // gates an item, a currency or a reward (the config's lapenshards, skill and stat points; survival medals; the
    // home; event, achievement and quest rewards claimed; dungeon records), so storing one without the others would
    // let a reward be taken twice, or an item be lost, after the next login. A save that failed anywhere (an
    // exception, or a write the database refused) is rolled back whole; the session keeps its state (SessionSave
    // tries once again, then ends the session).
    // A save that finds a reset being stored for longer than resetWait throws ResetRunningException before it writes
    // anything: it is not a failed save (LockedSave).
    private bool Save(TimeSpan resetWait) {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try {
            if (SaveRows(resetWait)) {
                return true;
            }
        } catch (Exception ex) when (ex is not ResetRunningException) {
            // A throw anywhere in the save (a deadlock on a write, the rollback or the commit) is a failed save too.
            Logger.Error(ex, "Save failed AccountId={AccountId} CharacterId={CharacterId}", AccountId, CharacterId);
        } finally {
            if (watch.ElapsedMilliseconds > SlowSaveMs) {
                Logger.Warning("Save took {Ms} ms AccountId={AccountId} CharacterId={CharacterId}", watch.ElapsedMilliseconds, AccountId, CharacterId);
            }
            if (!firstSaveLogged) {
                firstSaveLogged = true;
                Logger.Information("First save took {Ms} ms, {ItemRows} item rows written AccountId={AccountId} CharacterId={CharacterId}",
                    watch.ElapsedMilliseconds, lastSaveItemRows, AccountId, CharacterId);
            }
        }
        Logger.Error("Save: not stored AccountId={AccountId} CharacterId={CharacterId}", AccountId, CharacterId);
        return false;
    }

    // A save of a session whose account another entry has claimed since (a relog or channel move that loaded the
    // player while this session's last save had not run): it writes nothing. The other session loaded the rows
    // before this save, plays on from them and writes them; written here, a row it does not change after would keep
    // this session's values while that session holds others (an item lost or doubled).
    private bool HoldsSession(GameStorage.Request db) {
        if (db.HoldsSession(AccountId, sessionGen)) {
            return true;
        }
        Logger.Error("Save: the account was entered again since this session's load, nothing stored AccountId={AccountId} CharacterId={CharacterId}", AccountId, CharacterId);
        return false;
    }

    private bool SaveRows(TimeSpan resetWait) {
        using GameStorage.Request db = GameStorage.Context();
        using var tokens = new RowTokens(Player);
        // A reset stored since the session's state was read is taken first, so the rows written hold it.
        BeginWithResets(db, resetWait);
        if (!HoldsSession(db)) {
            return false;
        }
        // The PC bang play since the last fold is part of the account's day record.
        if (PcBang != null && PcBang.On) {
            PcBang.Fold(DateTime.Now);
        }
        if (!db.SavePlayer(Player)) {
            Logger.Error("Save: player not saved, nothing stored AccountId={AccountId} CharacterId={CharacterId}", AccountId, CharacterId);
            return false;
        }
        // SavePlayer may fail a save on a concurrency conflict it then retries; count from after it.
        int failedSaves = db.FailedSaves;
        bool threw = false;
        long[] ugcRemovals = [];
        TrySaveComponent(UgcMarket, (manager, request) => ugcRemovals = manager.Save(request));
        TrySaveComponent(Shop, (manager, request) => manager.Save(request));
        TrySaveComponent(Item, (manager, request) => manager.Save(request));
        TrySaveComponent(Config, (manager, request) => manager.Save(request));
        TrySaveComponent(Survival, (manager, request) => manager.Save(request));
        TrySaveComponent(Housing, (manager, request) => manager.Save(request));
        TrySaveComponent(GameEvent, (manager, request) => manager.Save(request));
        TrySaveComponent(Achievement, (manager, request) => manager.Save(request));
        TrySaveComponent(Quest, (manager, request) => manager.Save(request));
        TrySaveComponent(Dungeon, (manager, request) => manager.Save(request));
        if (threw || !db.SaveChanges() || db.FailedSaves != failedSaves) {
            Logger.Error("Save: a component was not saved, nothing stored AccountId={AccountId} CharacterId={CharacterId}", AccountId, CharacterId);
            return false;
        }
        lastSaveItemRows = db.ItemRowsWritten;
        // The mark tells a commit that throws (its answer lost with the connection) kept or not (CommitThrew): kept,
        // the save is stored; not kept, a failed save; unknown, LockedSave ends the session.
        long mark = WriteMark(db);
        bool committed;
        try {
            committed = db.Commit();
        } catch (Exception ex) {
            committed = CommitThrew("Save", db, tokens, ex, mark) == true;
        }
        if (!(tokens.Kept = committed)) {
            return false;
        }
        UgcMarket?.RemovalsStored(ugcRemovals);
        return true;

        // A manager that was never built (the load threw before it, and the session is torn down) has nothing of its
        // own to store: the rows it would write are as loaded. It is skipped.
        void TrySaveComponent<T>(T? manager, Action<T, GameStorage.Request> save) where T : class {
            if (manager == null) {
                return;
            }
            try {
                save(manager, db);
            } catch (Exception ex) {
                threw = true;
                Logger.Error(ex, "Error saving component for {Player}", PlayerName);
            }
        }
    }

    // The player's row versions before a save: SavePlayer and SaveCurrency move them forward when their write is made,
    // so a save that is then rolled back puts them back (the next save would otherwise start with a conflict).
    private sealed class RowTokens : IDisposable {
        private readonly Player player;
        private readonly DateTime account, character, unlock;
        public bool Kept;

        public RowTokens(Player player) {
            this.player = player;
            account = player.Account.LastModified;
            character = player.Character.LastModified;
            unlock = player.Unlock.LastModified;
        }

        public void Dispose() {
            if (Kept) return;
            player.Account.LastModified = account;
            player.Character.LastModified = character;
            player.Unlock.LastModified = unlock;
        }
    }
}
