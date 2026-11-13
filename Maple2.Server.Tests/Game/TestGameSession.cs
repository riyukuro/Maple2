using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Caching;
using Grpc.Core;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Server.Game.Util.Sync;
using Maple2.Server.World.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Tests.Game;

// A GameSession for handler tests: no socket, no world server, no field thread and no database server.
//
//   var test = new TestGameSession();                       // account and character rows in an in-memory storage
//   Item scroll = test.Store(TestGameSession.Metadata(...)); // rows the character owns before it logs in
//   test.SetTable(new DefaultItemsTable(...));              // client tables the handler reads
//   GameSession session = test.Start();                     // player, inventory, equips, currency, buffs, ...
//   test.AddEvent(1, GameEventType.StampEvent, new StampEvent(...)); // a live event the server holds
//   handler.Handle(session, TestGameSession.Packet(writer => writer.WriteLong(scroll.Uid)));
//   test.TakeSent()                                         // the packets sent since the last call, in order
//
// The session is made without its constructor, so only what Start sets exists: Player, Item (Equips, Inventory,
// Furnishing), ItemBox, ItemMerge, ItemEnchant, Stats (no actor: Refresh does nothing), PlayerInfo, Currency, Buffs, Achievement, Quest (no quests), Marriage (single), Exp, Dungeon (no room),
// Housing, Buddy (no entries), Guild (no guild), GameEvent (the user values stored for the events added with AddEvent), Field (a field
// that only broadcasts to this player and rolls item drops from the tables
// set here) and World (grants every lock, answers a move with MoveAddress). A handler that reaches anything else
// needs it set first.
//
// Trophies and maps come from AddAchievement and AddMap; EnterMap, EnterHome, EnterDungeon, StartQuest and SetQuest
// put the started session in a place or give it a quest. A move (GameSession.Migrate) saves nothing and closes
// nothing here.
// Storage is the real GameStorage; pass MySQL options to run the same test against a database.
public sealed class TestGameSession {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public readonly DbContextOptions Options;
    public readonly GameStorage Storage;
    public readonly TableMetadataStorage TableMetadata = Uninitialized<TableMetadataStorage>();
    public readonly ServerTableMetadataStorage ServerTableMetadata = Uninitialized<ServerTableMetadataStorage>();
    public readonly LockWorld World = new();
    public readonly MapMetadataStorage MapMetadata = Uninitialized<MapMetadataStorage>();
    public readonly long AccountId;
    public readonly long CharacterId;
    public GameSession Session { get; private set; } = null!;
    // What the session logged as an error. Several session methods catch and log (SavePurchase does), so a test
    // that expects a save should also expect this to be empty.
    public readonly List<string> Errors = [];
    // A save this returns true for throws, as a database that refuses the write would; TrySaveChanges then answers
    // false. In-memory store only.
    public Func<DbContext, bool>? FailSave {
        get => faults.Fail;
        set => faults.Fail = value;
    }
    private readonly SaveFault faults = new();

    private readonly LRUCache<int, ItemMetadata> items = new(1024, 1);
    private readonly ScriptMetadataStorage scripts = new(new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));
    private readonly Dictionary<int, GameEvent> events = new();
    private readonly ItemMetadataStorage itemMetadata = Uninitialized<ItemMetadataStorage>();
    private BlockingCollection<(byte[] packet, int length)> sendQueue = new(new ConcurrentQueue<(byte[], int)>());
    private GameSession? started;
    private readonly LRUCache<int, MapMetadata> maps = new(64, 1);
    private readonly LRUCache<int, AchievementMetadata> achievements;
    private readonly AchievementMetadataStorage achievementMetadata;
    private ItemStatsCalculator stats = null!;
    private FieldPlayer fieldPlayer = null!;

    public TestGameSession(DbContextOptions? options = null, Gender gender = Gender.Male) {
        Options = options ?? new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(faults)
            .Options;
        // ItemMetadataStorage's constructor runs SQL the in-memory provider lacks; TryGet looks in the cache first.
        typeof(MetadataStorage<int, ItemMetadata>).GetField("Cache", Any)!.SetValue(itemMetadata, items);
        // An empty item table behind the registered items, so an unknown id is not found rather than a fault.
        typeof(MetadataStorage<int, ItemMetadata>).GetField("Context", Any)!.SetValue(itemMetadata,
            new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));
        // The home's plot, which LoadPlayer looks up for the account's home.
        typeof(MetadataStorage<int, MapMetadata>).GetField("Cache", Any)!.SetValue(MapMetadata, maps);
        var ugcMaps = new LRUCache<int, UgcMapMetadata>(4, 1);
        var cost = new UgcMapGroup.Cost(0, 0, 0);
        ugcMaps.AddReplace(Constant.DefaultHomeMapId, new UgcMapMetadata(Constant.DefaultHomeMapId, new Dictionary<int, UgcMapGroup> {
            [Constant.DefaultHomeNumber] = new(Constant.DefaultHomeNumber, 0, 0, cost, cost, new UgcMapGroup.Limits(0, 0, 0, 0, 0, 0)),
        }));
        typeof(MapMetadataStorage).GetField("UgcCache", Any)!.SetValue(MapMetadata, ugcMaps);
        typeof(MapMetadataStorage).GetField("ExportedUgcCache", Any)!.SetValue(MapMetadata, new Dictionary<string, ExportedUgcMapMetadata>());
        // The trophies: an empty store whose cache AddAchievement fills. Every condition type counts as read, so
        // GetType answers from the cache alone.
        var metadataOptions = new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var metadataContext = new MetadataContext(metadataOptions);
        achievementMetadata = new AchievementMetadataStorage(metadataContext);
        // A map that AddMap did not add is looked up in the empty store and not found.
        typeof(MetadataStorage<int, MapMetadata>).GetField("Context", Any)!.SetValue(MapMetadata, metadataContext);
        achievements = (LRUCache<int, AchievementMetadata>) typeof(MetadataStorage<int, AchievementMetadata>).GetField("Cache", Any)!.GetValue(achievementMetadata)!;
        var cachedTypes = (HashSet<ConditionType>) typeof(AchievementMetadataStorage).GetField("cachedTypes", Any)!.GetValue(achievementMetadata)!;
        cachedTypes.UnionWith(Enum.GetValues<ConditionType>());
        Storage = new GameStorage(Options, itemMetadata, MapMetadata, achievementMetadata, null!, TableMetadata, ServerTableMetadata, NullLogger<GameStorage>.Instance, null!);
        SetServerTable(new ConstantsTable(
            bagSlotTabGameCount: [48, 48], bagSlotTabSkinCount: [48, 48], bagSlotTabSummonCount: [48, 48],
            bagSlotTabMaterialCount: [48, 48], bagSlotTabMasteryCount: [48, 48], bagSlotTabLifeCount: [48, 48],
            bagSlotTabQuestCount: [48, 48], bagSlotTabGemCount: [48, 48], bagSlotTabPetCount: [48, 48],
            bagSlotTabActiveSkillCount: [48, 48], bagSlotTabCoinCount: [48, 48], bagSlotTabBadgeCount: [48, 48],
            bagSlotTabMiscCount: [48, 48], bagSlotTabLapenShardCount: [48, 48], bagSlotTabPieceCount: [48, 48]));
        // Every created item asks for its sockets.
        SetTable(new ItemSocketTable(new Dictionary<int, IReadOnlyDictionary<int, ItemSocketMetadata>>()));
        // What the Exp and Dungeon managers read when they are made, and a move reads: empty until a test sets them.
        SetTable(new PrestigeMissionTable(new Dictionary<int, PrestigeMissionMetadata>()));
        SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata>()));
        SetTable(new DungeonConfigTable(new Dictionary<int, int>(), new Dictionary<int, DungeonMissionRankMetadata>()));
        SetServerTable(new InstanceFieldTable(new Dictionary<int, InstanceFieldMetadata>()));
        // A shop load reads the running game events (NpcShopShowItem).
        SetServerTable(new GameEventTable(new Dictionary<int, GameEventMetadata>()));

        // The rows a login finds. The account row is added directly: CreateAccount hashes a password, which is slow.
        string name = Guid.NewGuid().ToString("N")[..12];
        using (var context = new Ms2Context(Options)) {
            Maple2.Database.Model.Account account = new Account { Username = name };
            // In the in-memory store the account's id differs from the character's (both would be 1), so the rows
            // an account owns (its storage) are not mixed up with the character's.
            account.Id = options == null ? 1000 : 0;
            context.Account.Add(account);
            context.SaveChanges();
            AccountId = account.Id;
            context.Home.Add(new Maple2.Database.Model.Home { AccountId = AccountId });
            context.UgcMap.Add(new Maple2.Database.Model.UgcMap {
                OwnerId = AccountId,
                MapId = Constant.DefaultHomeMapId,
                Indoor = true,
                Number = Constant.DefaultHomeNumber,
            });
            context.SaveChanges();
        }
        using GameStorage.Request db = Storage.Context();
        Character character = db.CreateCharacter(new Character {
            AccountId = AccountId,
            Name = name,
            Gender = gender,
            Job = Job.Newbie,
            Mastery = new Mastery(),
            MapId = 2000023,
            ReturnMaps = new Maple2.Tools.LimitedStack<int>(3, 2000023),
        }) ?? throw new InvalidOperationException("character row not created");
        CharacterId = character.Id;
        // A Debug build's CreateCharacter starts the character with mesos; the tests start from none in any build.
        using (var context = new Ms2Context(Options)) {
            context.Character.Find(CharacterId)!.Currency = new Maple2.Database.Model.CharacterCurrency();
            context.SaveChanges();
        }
        if (!db.InitNewCharacter(CharacterId, new Unlock())) {
            throw new InvalidOperationException("character unlock row not created");
        }
    }

    #region Before Start
    public static ItemMetadata Metadata(int id, EquipSlot? slot = null, Gender gender = Gender.All, ItemMetadataFunction? function = null,
                                        int type = 0, int slotMax = 100) {
        var property = new ItemMetadataProperty(false, 0, slotMax, type, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(gender, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        return new ItemMetadata(id, $"{id}", slot is null ? [] : [slot.Value], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, function, [], null, null, null, null);
    }

    // An npc script the session's ScriptMetadata finds.
    public void Register(ScriptMetadata script) {
        var cache = (LRUCache<int, ScriptMetadata>) typeof(MetadataStorage<int, ScriptMetadata>).GetField("Cache", Any)!.GetValue(scripts)!;
        cache.AddReplace(script.Id, script);
    }

    // Quest data the session's QuestMetadata finds (after Start).
    public void Register(QuestMetadata quest) {
        var cache = (LRUCache<int, QuestMetadata>) typeof(MetadataStorage<int, QuestMetadata>).GetField("Cache", Any)!.GetValue(Session.QuestMetadata)!;
        cache.AddReplace(quest.Id, quest);
    }

    // Pet data the session's ItemMetadata finds by pet id.
    public void RegisterPet(int petId, int type = 1, bool extraction = false) {
        var field = typeof(ItemMetadataStorage).GetField("petLookup", Any)!;
        var lookup = (Maple2.Tools.ConcurrentMultiDictionary<int, int, PetMetadata>?) field.GetValue(itemMetadata);
        if (lookup == null) {
            lookup = new Maple2.Tools.ConcurrentMultiDictionary<int, int, PetMetadata>();
            field.SetValue(itemMetadata, lookup);
        }
        lookup.TryAdd(petId, petId, new PetMetadata(petId, $"{petId}", type, [], petId, 0, extraction, 0, 0f, null, [],
            new PetMetadataDistance(0, 0, 0), new PetMetadataTime(0, 0, 0, 0, 0)));
    }

    // Item data the storage and the session's ItemMetadata find. Store registers its item by itself.
    public void Register(params ItemMetadata[] metadata) {
        foreach (ItemMetadata entry in metadata) {
            items.AddReplace(entry.Id, entry);
        }
    }

    // A row of the character: in the bag (group Default), or worn (group Gear/Outfit with the slot).
    public Item Store(ItemMetadata metadata, int amount = 1, ItemGroup group = ItemGroup.Default, EquipSlot slot = EquipSlot.Unknown) {
        Register(metadata);
        Item item;
        using (GameStorage.Request create = Storage.Context()) {
            item = create.CreateItem(CharacterId, new Item(metadata, amount: amount)) ?? throw new InvalidOperationException("item row not created");
        }
        if (group != ItemGroup.Default) {
            item.Group = group;
            item.Slot = (short) slot;
            using GameStorage.Request db = Storage.Context();
            if (!db.SaveItems(CharacterId, item)) {
                throw new InvalidOperationException("item row not saved");
            }
        }
        return item;
    }

    // A trophy the session's AchievementMetadata finds, before or after Start.
    public void AddAchievement(params AchievementMetadata[] metadata) {
        foreach (AchievementMetadata entry in metadata) {
            achievements.AddReplace(entry.Id, entry);
        }
    }

    // A character trophy of one condition type: one grade per goal, in order, without rewards. codes are the
    // condition's code ids; codeRange its code range.
    public static AchievementMetadata Trophy(int id, ConditionType type, long[] goals, int[]? codes = null, (int Min, int Max)? codeRange = null,
                                             bool locked = false, AchievementCategory category = AchievementCategory.Life) {
        ConditionMetadata.Parameters? code = null;
        if (codes != null || codeRange != null) {
            code = new ConditionMetadata.Parameters(Integers: codes,
                Range: codeRange is { } range ? new ConditionMetadata.Range<int>(range.Min, range.Max) : null);
        }
        var grades = new Dictionary<int, AchievementMetadataGrade>();
        for (int i = 0; i < goals.Length; i++) {
            grades[i + 1] = new AchievementMetadataGrade(i + 1, new ConditionMetadata(type, goals[i], code, null), null);
        }
        return new AchievementMetadata(id, $"{id}", false, category, [], grades, locked);
    }

    // A map the session's MapMetadata and the handlers' MapMetadataStorage find.
    public void AddMap(params MapMetadata[] metadata) {
        foreach (MapMetadata entry in metadata) {
            maps.AddReplace(entry.Id, entry);
        }
    }

    public static MapMetadata Map(int id, bool homeReturnable = true, bool notInPlayedClient = false) {
        var property = new MapMetadataProperty(Continent.VictoriaIsland, MapRegion.None, 0, MapType.None, false, 0, 0, 0, 0, AutoReviveType.None, 0,
            false, false, false, false, homeReturnable, false, false, false, false, false, 0f, NotInPlayedClient: notInPlayedClient);
        return new MapMetadata(id, $"{id}", string.Empty, property, null!, null!, [], null!, []);
    }

    // A quest without requirements or rewards, of the character.
    public static QuestMetadata QuestData(int id, QuestType type = QuestType.EpicQuest) {
        var basic = new QuestMetadataBasic(0, type, 0, 0, false, string.Empty, false, false, false, 0, 0, null, null);
        var require = new QuestMetadataRequire(0, 0, [], [], [], 0, (0, 0), 0);
        var reward = new QuestMetadataReward(0, 0, ExpType.none, 0, 0, 0, 0, 0, 0, 0, [], []);
        return new QuestMetadata(id, $"{id}", basic, require, reward, reward, new QuestRemoteAccept(QuestRemoteType.None, 0),
            new QuestRemoteComplete(QuestRemoteType.None, 0, false), new QuestMetadataGoToNpc(false, 0, 0),
            new QuestMetadataGoToDungeon(QuestState.None, 0, 0), null, null, null, QuestEventMissionType.none, []);
    }

    // A dungeon room without limits whose clear gives no exp, mesos or items; rewardCount clears count per period.
    public static DungeonRoomMetadata DungeonRoom(int id, int lobbyFieldId, int rewardCount = 10) {
        var reward = new DungeonRoomRewardMetadata(false, rewardCount, 0, 0, 0f, 0, [], [], 0, 0, 0);
        var limit = new DungeonRoomLimitMetadata(1, 4, 0, 0, 0, false, [], [], [], false, false, false, false, false, 0);
        return new DungeonRoomMetadata(id, 1, DungeonPlayType.none, DungeonGroupType.none, DungeonCooldownType.dayOfWeeks, 0, 0, lobbyFieldId,
            [lobbyFieldId], reward, limit, 0, 0, 0, false, 0, 0, false, [], [], false, DungeonTimerType.clock, false);
    }

    // A client table (TableMetadata) or a server table (ServerTableMetadata) the handler reads.
    public void SetTable<T>(T table) where T : Table => SetLazy(TableMetadata, table);
    public void SetServerTable<T>(T table) where T : ServerTable => SetLazy(ServerTableMetadata, table);

    // A live event (started three days ago, ending in thirty) of the game server the session asks for its events.
    // Added before Start: the login reads the character's stored user values of it.
    public GameEvent AddEvent(int id, GameEventType type, GameEventData data, DateTime? start = null, DateTime? end = null) {
        var gameEvent = new GameEvent(new GameEventMetadata(id, type, start ?? DateTime.Now.AddDays(-3), end ?? DateTime.Now.AddDays(30),
            TimeSpan.Zero, TimeSpan.Zero, [], data, string.Empty, string.Empty, string.Empty, string.Empty));
        events[id] = gameEvent;
        return gameEvent;
    }

    // An individual drop box whose groups each give all of their rows' items: one group per item id list, every
    // row certain (so a test knows what the box gives).
    public static IDictionary<int, IndividualDropItemTable.Entry> DropBox(params (int ItemId, int Amount)[][] groups) {
        var box = new Dictionary<int, IndividualDropItemTable.Entry>();
        foreach ((int ItemId, int Amount)[] rows in groups) {
            int groupId = box.Count + 1;
            box[groupId] = new IndividualDropItemTable.Entry(groupId, 0, [new IndividualDropItemTable.Entry.DropCount(1, 100)], 0, false, false,
                rows.Select(row => new IndividualDropItemTable.Item([row.ItemId], false, 0, 0, 100,
                    new IndividualDropItemTable.Range<int>(row.Amount, row.Amount), [], 0, 0, false, false, false, false, [], false)).ToList());
        }
        return box;
    }
    #endregion

    // The login: the player and the managers read the stored rows, as GameSession.EnterServer does.
    public GameSession Start() {
        var session = Uninitialized<GameSession>();
        GC.SuppressFinalize(session);
        Type core = typeof(Maple2.Server.Core.Network.Session);
        core.GetField("sendQueue", Any)!.SetValue(session, sendQueue);
        core.GetField("lastSentPackets", Any)!.SetValue(session, new ConcurrentDictionary<SendOp, byte[]>());
        core.GetField("Logger", Any)!.SetValue(session, new LoggerConfiguration().WriteTo.Sink(new ErrorSink(Errors)).CreateLogger());
        Set(session, nameof(GameSession.AccountId), AccountId);
        typeof(GameSession).GetField(nameof(GameSession.WhisperedBy), Any)!.SetValue(session, new ConcurrentDictionary<string, long>());
        Set(session, nameof(GameSession.CharacterId), CharacterId);
        Set(session, nameof(GameSession.GameStorage), Storage);
        Set(session, nameof(GameSession.ItemMetadata), itemMetadata);
        Set(session, nameof(GameSession.TableMetadata), TableMetadata);
        Set(session, nameof(GameSession.ServerTableMetadata), ServerTableMetadata);
        Set(session, nameof(GameSession.World), World);
        Set(session, nameof(GameSession.AchievementMetadata), achievementMetadata);
        Set(session, nameof(GameSession.MapMetadata), MapMetadata);
        // A move (Migrate) ends with the save and the disconnect of a session that leaves this server: the save and
        // the teardown are marked as done, so the move changes only what it sends and sets. The disconnect itself
        // closes the send queue; TakeSent opens a new one, so a test can go on with the same session.
        typeof(GameSession).GetField("preMigrationSaved", Any)!.SetValue(session, true);
        typeof(GameSession).GetField("gameDisposeState", Any)!.SetValue(session, 2);
        started = session;
        // The server, for its events (those added with AddEvent).
        var server = Uninitialized<Maple2.Server.Game.GameServer>();
        FieldInfo eventCache = typeof(Maple2.Server.Game.GameServer).GetField("eventCache", Any)!;
        var cache = (IDictionary<int, GameEvent>) Activator.CreateInstance(eventCache.FieldType)!;
        foreach ((int id, GameEvent gameEvent) in events) {
            cache[id] = gameEvent;
        }
        eventCache.SetValue(server, cache);
        typeof(Maple2.Server.Game.GameServer).GetField("endedOpenPeriods", Any)!.SetValue(server, new List<GameEvent>());
        typeof(GameSession).GetField("server", Any)!.SetValue(session, server);
        var metadataOptions = new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var quests = new QuestMetadataStorage(new MetadataContext(metadataOptions));
        Set(session, nameof(GameSession.QuestMetadata), quests);
        // The storage reads a stored quest's data from the same place (a quest the session starts is stored).
        typeof(GameStorage).GetField("questMetadata", Any)!.SetValue(Storage, quests);
        Set(session, nameof(GameSession.ScriptMetadata), scripts);
        // What placing a cube and a design upload read: no function cubes, and no upload fee held.
        Set(session, nameof(GameSession.FunctionCubeMetadata), new FunctionCubeMetadataStorage(new MetadataContext(metadataOptions)));
        typeof(GameSession).GetField(nameof(GameSession.UgcFees), Any)!.SetValue(session, new Maple2.Model.Validators.UgcFeeBook());
        Set(session, nameof(GameSession.PlayerInfo), new PlayerInfoStorage(World));

        using GameStorage.Request db = Storage.Context();
        Player player = db.LoadPlayer(AccountId, CharacterId, 1, 1) ?? throw new InvalidOperationException("player not loaded");
        fieldPlayer = Uninitialized<FieldPlayer>();
        GC.SuppressFinalize(fieldPlayer);
        typeof(FieldPlayer).GetField(nameof(FieldPlayer.Session), Any)!.SetValue(fieldPlayer, session);
        typeof(Actor<Player>).GetField("<Value>k__BackingField", Any)!.SetValue(fieldPlayer, player);
        typeof(Actor<Player>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(fieldPlayer, player.ObjectId);
        typeof(Actor<Player>).GetField("<Transform>k__BackingField", Any)!.SetValue(fieldPlayer, new Maple2.Tools.VectorMath.Transform());
        // The moves the client synced: a server move (MoveByPortal) starts them again.
        typeof(FieldPlayer).GetField(nameof(FieldPlayer.Moves), Any)!.SetValue(fieldPlayer, new MoveWindow());
        Set(session, nameof(GameSession.Player), fieldPlayer);
        // The objects and cubes whose interaction the session began.
        typeof(GameSession).GetField(nameof(GameSession.InteractStarted), Any)!.SetValue(session, new HashSet<string>());
        typeof(GameSession).GetField(nameof(GameSession.CubeStarted), Any)!.SetValue(session, new HashSet<string>());

        stats = Uninitialized<ItemStatsCalculator>();
        typeof(ItemStatsCalculator).GetField("<TableMetadata>k__BackingField", Any)!.SetValue(stats, TableMetadata);
        // What an item box roll reads from the field: its map, the tables and the item data.
        var map = Uninitialized<MapMetadata>();
        typeof(MapMetadata).GetField("<Id>k__BackingField", Any)!.SetValue(map, player.Character.MapId);
        session.Field = NewField<FieldManager>(map);

        session.Currency = new CurrencyManager(session);
        session.Buffs = new BuffManager(fieldPlayer);
        session.Achievement = new AchievementManager(session);
        var quest = Uninitialized<QuestManager>();
        typeof(QuestManager).GetField("session", Any)!.SetValue(quest, session);
        typeof(QuestManager).GetField("accountValues", Any)!.SetValue(quest, new Dictionary<int, Quest>());
        typeof(QuestManager).GetField("characterValues", Any)!.SetValue(quest, new Dictionary<int, Quest>());
        // What QuestManager.Load reads: the mission lists (none) and the exploration progress (0).
        typeof(QuestManager).GetField("dailyMissions", Any)!.SetValue(quest, new HashSet<int>());
        typeof(QuestManager).GetField("weeklyMissions", Any)!.SetValue(quest, new HashSet<int>());
        typeof(QuestManager).GetField("missionMetadata", Any)!.SetValue(quest, new List<QuestMetadata>());
        // No condition type is throttled here: every counted update is sent.
        typeof(QuestManager).GetField("throttleStates", Any)!.SetValue(quest, new HashSet<ConditionType>());
        typeof(QuestManager).GetField("logger", Any)!.SetValue(quest, Serilog.Log.Logger.ForContext<QuestManager>());
        session.Config = Uninitialized<ConfigManager>();
        // No favorite designer: what the meret market manager reads.
        typeof(ConfigManager).GetField("favoriteDesigners", Any)!.SetValue(session.Config, new List<long>());
        session.Quest = quest;
        var marriage = Uninitialized<MarriageManager>();
        typeof(MarriageManager).GetField("session", Any)!.SetValue(marriage, session);
        marriage.Marriage = Marriage.Default;
        session.Marriage = marriage;
        session.Guild = Uninitialized<GuildManager>();
        session.Exp = new ExperienceManager(session);
        session.Mastery = new MasteryManager(session);
        session.Dungeon = new DungeonManager(session);
        session.Housing = new HousingManager(session, TableMetadata);
        session.Item = new ItemManager(db, session, stats);
        session.ItemBox = new ItemBoxManager(session);
        session.Mail = new MailManager(session);
        // An empty friend and block list: what a mail send reads.
        session.Buddy = new BuddyManager(db, session);
        session.BlackMarket = new BlackMarketManager(session);
        // The account's listings, read from the db at Start.
        session.UgcMarket = new UgcMarketManager(session);
        session.Shop = new ShopManager(session);
        session.ItemMerge = new ItemMergeManager(session);
        typeof(GameSession).GetField(nameof(GameSession.DismantleStaging), Any)!.SetValue(session, new (long Uid, int Amount)[100]);
        typeof(GameSession).GetField(nameof(GameSession.ChatRestriction), Any)!.SetValue(session, new ChatRestriction());
        session.ItemEnchant = new ItemEnchantManager(session);
        // Stats without an actor: Refresh does nothing. The values start empty.
        session.Stats = Uninitialized<StatsManager>();
        typeof(StatsManager).GetField("<Values>k__BackingField", Any)!.SetValue(session.Stats, new Stats(new Dictionary<BasicAttribute, long>(), 0));
        session.GameEvent = new GameEventManager(session);
        session.Mentoring = new MentoringManager(session);
        session.AdventureProtect = new AdventureProtectManager(session);

        Session = session;
        return session;
    }

    #region After Start
    public static ByteReader Packet(Action<ByteWriter> write) {
        var writer = new ByteWriter();
        write(writer);
        return new ByteReader(writer.ToArray(), 0);
    }

    // The packets sent since the last call, in the order they were sent; each starts with its opcode.
    // A quest the player holds in the given state (an account quest, as the event quests are). Only its id is
    // set in the quest's data.
    public Quest SetQuest(int questId, QuestState state) {
        var quest = new Quest(QuestData(questId)) { State = state };
        var held = (Dictionary<int, Quest>) typeof(QuestManager).GetField("accountValues", Any)!.GetValue(Session.Quest)!;
        held[questId] = quest;
        // Its row is stored too, under the owner Complete stores it for (the account for an account quest).
        long ownerId = quest.Metadata.Basic.Account > 0 ? AccountId : CharacterId;
        using (GameStorage.Request db = Storage.Context()) {
            db.DeleteQuest(ownerId, questId);
            db.CreateQuest(ownerId, quest);
        }
        return quest;
    }

    // The character's stored user values of an event, as the logout stores them.
    public Dictionary<GameEventUserValueType, string> StoredEventValues(int eventId) {
        using var context = new Ms2Context(Options);
        return context.GameEventUserValue.AsNoTracking().Where(row => row.CharacterId == CharacterId && row.EventId == eventId)
            .ToDictionary(row => row.Type, row => row.Value);
    }

    public List<byte[]> TakeSent() {
        var sent = new List<byte[]>();
        while (sendQueue.TryTake(out (byte[] packet, int length) entry)) {
            sent.Add(entry.packet);
        }
        if (sendQueue.IsAddingCompleted && started != null) {
            Type core = typeof(Maple2.Server.Core.Network.Session);
            sendQueue = new BlockingCollection<(byte[] packet, int length)>(new ConcurrentQueue<(byte[], int)>());
            core.GetField("sendQueue", Any)!.SetValue(started, sendQueue);
            core.GetField("disconnecting", Any)!.SetValue(started, 0);
        }
        return sent;
    }

    public static SendOp Op(byte[] packet) => (SendOp) (packet[1] << 8 | packet[0]);

    // The packet with every long that holds the present time (unix seconds, within five of now) set to 0. A player's
    // info writes the time of writing, so a packet built a moment after the sent one can differ by one second.
    public static byte[] NoClock(byte[] packet) {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        byte[] result = (byte[]) packet.Clone();
        for (int i = 2; i + 8 <= result.Length; i++) {
            if (Math.Abs(BitConverter.ToInt64(result, i) - now) <= 5) {
                Array.Clear(result, i, 8);
                i += 7;
            }
        }
        return result;
    }

    public List<SendOp> TakeSentOps() => TakeSent().Select(Op).ToList();

    // A row as the database holds it now. A discarded item keeps its row with owner 0.
    internal Maple2.Database.Model.Item? Row(long uid) {
        using var context = new Ms2Context(Options);
        return context.Item.AsNoTracking().SingleOrDefault(row => row.Id == uid);
    }

    internal List<Maple2.Database.Model.Item> Rows() {
        using var context = new Ms2Context(Options);
        return context.Item.AsNoTracking().Where(row => row.OwnerId == CharacterId).ToList();
    }

    // The session stands in a field of this map (AddMap is not needed for it).
    public void EnterMap(MapMetadata map) {
        Session.Field = NewField<FieldManager>(map);
    }

    // An interact object of the session's field, at the player's place and ready to be used.
    public FieldInteract AddInteract(InteractObjectMetadata metadata, IInteractObject interactObject) {
        var interact = new FieldInteract(Session.Field, 9000 + metadata.Id, interactObject.EntityId, metadata, interactObject);
        typeof(FieldInteract).GetField("<State>k__BackingField", Any)!.SetValue(interact, InteractState.Reactable);
        var interacts = (ConcurrentDictionary<string, FieldInteract>) typeof(FieldManager).GetField("fieldInteracts", Any)!.GetValue(Session.Field)!;
        interacts[interact.EntityId] = interact;
        return interact;
    }

    // A billboard standing in the session's field.
    public FieldInteract AddBillboard(InteractObjectMetadata metadata, InteractBillBoardObject board) {
        var interact = new FieldInteract(Session.Field, 9000 + metadata.Id, board.EntityId, metadata, board);
        typeof(FieldInteract).GetField("<State>k__BackingField", Any)!.SetValue(interact, InteractState.Reactable);
        var boards = (ConcurrentDictionary<string, FieldInteract>) typeof(FieldManager).GetField("fieldAdBalloons", Any)!.GetValue(Session.Field)!;
        boards[interact.EntityId] = interact;
        return interact;
    }

    // The field's delayed steps that are due run now.
    public void RunFieldQueue() {
        Session.Field!.Scheduler.InvokeAll();
    }

    // A function cube of the session's field, at the player's place.
    public FieldFunctionInteract AddFunctionCube(InteractCube cube) {
        var interact = new FieldFunctionInteract(Session.Field, 9500, cube, 1);
        var interacts = (ConcurrentDictionary<string, FieldFunctionInteract>) typeof(FieldManager).GetField("fieldFunctionInteracts", Any)!.GetValue(Session.Field)!;
        interacts[cube.Id] = interact;
        return interact;
    }

    // The session's gathering counts start empty (the config manager is not loaded here).
    public void SetGatheringCounts() {
        typeof(ConfigManager).GetField(nameof(ConfigManager.GatheringCounts), Any)!.SetValue(Session.Config, new Dictionary<int, int>());
    }

    // The session stands in its own home, whose plot is returned (cubes can be put into Plot.Cubes).
    public Plot EnterHome() {
        Home home = Session.Player.Value.Home;
        var field = NewField<HomeFieldManager>(Map(home.Indoor.MapId));
        typeof(HomeFieldManager).GetField("<Home>k__BackingField", Any)!.SetValue(field, home);
        var plot = new Plot(home.Indoor.Metadata) {
            Number = home.Indoor.Number,
            OwnerId = AccountId,
            MapId = home.Indoor.MapId,
            Name = home.Indoor.Name,
        };
        field.Plots[plot.Number] = plot;
        Session.Field = field;
        return plot;
    }

    // The session stands in the lobby field of a dungeon room it entered; the room's record is returned (its State
    // decides whether CompleteDungeon is a clear).
    public DungeonRoomRecord EnterDungeon(DungeonRoomMetadata metadata, bool randomMatch = false) {
        var field = NewField<DungeonFieldManager>(Map(metadata.LobbyFieldId));
        var record = new DungeonRoomRecord(metadata) { RandomMatch = randomMatch };
        typeof(DungeonFieldManager).GetField(nameof(DungeonFieldManager.DungeonMetadata), Any)!.SetValue(field, metadata);
        typeof(DungeonFieldManager).GetField("<DungeonRoomRecord>k__BackingField", Any)!.SetValue(field, record);
        typeof(DungeonFieldManager).GetField("<Lobby>k__BackingField", Any)!.SetValue(field, field);
        typeof(FieldManager).GetField("<DungeonId>k__BackingField", Any)!.SetValue(field, metadata.Id);
        Session.Field = field;
        typeof(DungeonManager).GetField("<Lobby>k__BackingField", Any)!.SetValue(Session.Dungeon, field);
        Session.Dungeon.Metadata = metadata;
        Session.Dungeon.UserRecord = new DungeonUserRecord(metadata.Id, CharacterId);
        return record;
    }

    // A quest the character has in progress, with its row stored as a started quest's is (Complete stores the row).
    public Quest StartQuest(QuestMetadata metadata) {
        var quest = new Quest(metadata) { State = QuestState.Started };
        for (int i = 0; i < metadata.Conditions.Length; i++) {
            quest.Conditions.Add(i, new Quest.Condition(metadata.Conditions[i]));
        }
        // The row comes back as null when the storage has no metadata of a test quest; it is stored all the same.
        using (GameStorage.Request db = Storage.Context()) {
            db.CreateQuest(metadata.Basic.Account > 0 ? AccountId : CharacterId, quest);
        }
        string values = metadata.Basic.Account > 0 ? "accountValues" : "characterValues";
        ((IDictionary<int, Quest>) typeof(QuestManager).GetField(values, Any)!.GetValue(Session.Quest)!).Add(metadata.Id, quest);
        return quest;
    }

    // The counter of a trophy the character has; null when it was never counted.
    public long? TrophyCounter(int id) {
        return Session.Achievement.TryGetAchievement(id, out Achievement? achievement) ? achievement.Counter : null;
    }

    public Character StoredCharacter() {
        using GameStorage.Request db = Storage.Context();
        return db.GetCharacter(CharacterId) ?? throw new InvalidOperationException("character row gone");
    }
    #endregion

    // The world's account lock, granted at once.
    private sealed class SaveFault : SaveChangesInterceptor {
        public Func<DbContext, bool>? Fail;

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) {
            if (eventData.Context != null && Fail?.Invoke(eventData.Context) == true) {
                throw new DbUpdateException("The test made this save fail");
            }
            return result;
        }
    }

    public sealed class LockWorld : WorldClient {
        public int Acquired;
        public int Released;
        // The answer to a move to another game server, and the moves asked for. An empty address is a refusal
        // (the session sends the move error).
        public string MoveAddress = "127.0.0.1";
        public int MovePort = 20002;
        public readonly List<MigrateOutRequest> Moves = [];

        // The rank duel queue's answers (PvpResponse by request kind, none when not set) and the requests.
        public readonly List<PvpRequest> PvpRequests = [];
        public readonly Dictionary<PvpRequest.PvpOneofCase, PvpResponse> PvpResponses = [];

        public override PvpResponse Pvp(PvpRequest request, CallOptions options) {
            PvpRequests.Add(request);
            return PvpResponses.TryGetValue(request.PvpCase, out PvpResponse? response) ? response : new PvpResponse();
        }

        public override MigrateOutResponse MigrateOut(MigrateOutRequest request, CallOptions options) {
            Moves.Add(request);
            return new MigrateOutResponse { IpAddress = MoveAddress, Port = MovePort, Token = 1 };
        }

        // Called at each account lock taken (on the taking thread).
        public Action? OnAcquire;

        public override LockResponse AcquireLock(LockRequest request, CallOptions options) {
            Acquired++;
            OnAcquire?.Invoke();
            return new LockResponse();
        }

        public override LockResponse ReleaseLock(LockRequest request, CallOptions options) {
            Released++;
            return new LockResponse();
        }

        // The plot changes sent to the other channels.
        public readonly List<FieldPlotRequest> PlotUpdates = [];

        public override FieldPlotResponse UpdateFieldPlot(FieldPlotRequest request, CallOptions options) {
            PlotUpdates.Add(request);
            return new FieldPlotResponse();
        }

        // The world's open global portal (the answer to GetGlobalPortal); none is open while it is null.
        public GlobalPortalInfo? OpenPortal;
        public int TimeEventError;
        public FieldWarInfo? OpenFieldWar;

        public override TimeEventResponse TimeEvent(TimeEventRequest request, CallOptions options) {
            var response = new TimeEventResponse { Error = TimeEventError };
            if (request.GetGlobalPortal != null && OpenPortal != null) {
                response.GlobalPortalInfo = OpenPortal;
            }
            if (request.GetFieldWar != null && OpenFieldWar != null) {
                response.FieldWarInfo = OpenFieldWar;
            }
            return response;
        }

        // The black market's world side: every listing change is answered with BlackMarketAnswer (none: accepted).
        public readonly List<BlackMarketRequest> BlackMarketRequests = [];
        public int BlackMarketAnswer;

        public override BlackMarketResponse BlackMarket(BlackMarketRequest request, CallOptions options) {
            BlackMarketRequests.Add(request);
            return new BlackMarketResponse { Error = BlackMarketAnswer };
        }

        // The other players the world knows; a character that was not added is not found.
        public readonly Dictionary<long, PlayerInfoResponse> Players = [];

        public void AddPlayer(long accountId, long characterId, string name) {
            Players[characterId] = new PlayerInfoResponse {
                AccountId = accountId,
                CharacterId = characterId,
                Name = name,
                Motto = "",
                Picture = "",
                GuildName = "",
                Health = new HealthUpdate(),
                Trophy = new TrophyUpdate(),
                Home = new HomeUpdate { Name = "", ExpiryTime = new Google.Protobuf.WellKnownTypes.Timestamp() },
            };
        }

        // The character ids the PlayerInfo requests asked for.
        public readonly List<long> PlayerInfoAsked = [];

        public override PlayerInfoResponse PlayerInfo(PlayerInfoRequest request, CallOptions options) {
            PlayerInfoAsked.Add(request.CharacterId);
            // As the world server: no character id is a refused request, not a missing character.
            if (request.CharacterId <= 0) {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "AccountId and CharacterId not specified"));
            }
            if (Players.TryGetValue(request.CharacterId, out PlayerInfoResponse? response)) {
                return response;
            }
            throw new RpcException(new Status(StatusCode.NotFound, "no such character"));
        }

        // The world's party side: no party at login; a test scripts what the world answers to a party request.
        public Func<PartyRequest, PartyResponse>? PartyWorld;

        public override PartyInfoResponse PartyInfo(PartyInfoRequest request, CallOptions options) {
            return new PartyInfoResponse();
        }

        public override PartyResponse Party(PartyRequest request, CallOptions options) {
            return PartyWorld?.Invoke(request) ?? new PartyResponse();
        }

        // The guild requests handed to the world.
        public readonly List<GuildRequest> GuildRequests = [];
        // The world's answer to a guild request; an empty answer without it.
        public Func<GuildRequest, GuildResponse>? GuildWorld;

        public override GuildResponse Guild(GuildRequest request, CallOptions options) {
            GuildRequests.Add(request);
            return GuildWorld?.Invoke(request) ?? new GuildResponse();
        }

        public readonly List<MailNotificationRequest> MailNotifications = [];

        public override MailNotificationResponse MailNotification(MailNotificationRequest request, CallOptions options) {
            MailNotifications.Add(request);
            return new MailNotificationResponse();
        }

        // The chat lines handed to the world.
        public readonly List<ChatRequest> Chats = [];
        // Where the world hands a chat line on (the recipient's game server); it may throw what the world would.
        public Action<ChatRequest>? ChatDelivery;

        public override ChatResponse Chat(ChatRequest request, CallOptions options) {
            Chats.Add(request);
            ChatDelivery?.Invoke(request);
            return new ChatResponse();
        }

        // The group chat requests handed to the world.
        public readonly List<GroupChatRequest> GroupChats = [];

        public override GroupChatResponse GroupChat(GroupChatRequest request, CallOptions options) {
            GroupChats.Add(request);
            return new GroupChatResponse();
        }

        // The club requests handed to the world; ClubError is what the world answers (0: done).
        public readonly List<ClubRequest> Clubs = [];
        public int ClubError;

        public override Maple2.Server.World.Service.ClubResponse Club(ClubRequest request, CallOptions options) {
            Clubs.Add(request);
            return new Maple2.Server.World.Service.ClubResponse { Error = ClubError };
        }

        // The friend changes handed to the world; BuddyWorld answers as the world would (the other player's game
        // server, or none when it is offline). Without it the world finds no one.
        public readonly List<BuddyRequest> BuddyRequests = [];
        public Func<BuddyRequest, BuddyResponse>? BuddyWorld;

        public override BuddyResponse Buddy(BuddyRequest request, CallOptions options) {
            BuddyRequests.Add(request);
            return BuddyWorld?.Invoke(request) ?? new BuddyResponse();
        }

        // The mentoring changes handed to the world; MentoringWorld delivers them as the world would (the receiver's
        // game server), none when it is not set.
        public readonly List<MentoringRequest> MentoringRequests = [];
        public Action<MentoringRequest>? MentoringWorld;

        public override MentoringResponse Mentoring(MentoringRequest request, CallOptions options) {
            MentoringRequests.Add(request);
            MentoringWorld?.Invoke(request);
            return new MentoringResponse();
        }

        // What PlayerInfo.SendUpdate sent to the world.
        public readonly List<PlayerUpdateRequest> PlayerUpdates = [];

        public override PlayerUpdateResponse UpdatePlayer(PlayerUpdateRequest request, CallOptions options) {
            PlayerUpdates.Add(request);
            return new PlayerUpdateResponse();
        }
    }

    private sealed class ErrorSink(List<string> errors) : Serilog.Core.ILogEventSink {
        public void Emit(Serilog.Events.LogEvent logEvent) {
            if (logEvent.Level >= Serilog.Events.LogEventLevel.Error) {
                lock (errors) {
                    errors.Add($"{logEvent.RenderMessage()} {logEvent.Exception}");
                }
            }
        }
    }

    // A field of the map that holds only this player.
    private T NewField<T>(MapMetadata map) where T : FieldManager {
        var field = Uninitialized<T>();
        GC.SuppressFinalize(field);
        var players = new ConcurrentDictionary<int, FieldPlayer>();
        players[fieldPlayer.ObjectId] = fieldPlayer;
        typeof(FieldManager).GetField("<Players>k__BackingField", Any)!.SetValue(field, players);
        typeof(FieldManager).GetField("initialized", Any)!.SetValue(field, true);
        typeof(FieldManager).GetField(nameof(FieldManager.Metadata), Any)!.SetValue(field, map);
        typeof(FieldManager).GetField("<MapId>k__BackingField", Any)!.SetValue(field, map.Id);
        typeof(FieldManager).GetField(nameof(FieldManager.Plots), Any)!.SetValue(field, new ConcurrentDictionary<int, Plot>());
        typeof(FieldManager).GetField("<ItemMetadata>k__BackingField", Any)!.SetValue(field, itemMetadata);
        // An item a player drops is saved before it lands (SpawnItem).
        typeof(FieldManager).GetField("<GameStorage>k__BackingField", Any)!.SetValue(field, Storage);
        typeof(FieldManager).GetField("<TableMetadata>k__BackingField", Any)!.SetValue(field, TableMetadata);
        typeof(FieldManager).GetField("<ServerTableMetadata>k__BackingField", Any)!.SetValue(field, ServerTableMetadata);
        typeof(FieldManager).GetField("<ItemStatsCalc>k__BackingField", Any)!.SetValue(field, stats);
        typeof(FieldManager).GetField("<ItemDrop>k__BackingField", Any)!.SetValue(field, new ItemDropManager(field));
        // The field's own lists that an interaction reads: its objects, its cubes and the items on the ground.
        typeof(FieldManager).GetField("fieldInteracts", Any)!.SetValue(field, new ConcurrentDictionary<string, FieldInteract>());
        typeof(FieldManager).GetField("fieldFunctionInteracts", Any)!.SetValue(field, new ConcurrentDictionary<string, FieldFunctionInteract>());
        typeof(FieldManager).GetField("fieldItems", Any)!.SetValue(field, new ConcurrentDictionary<int, FieldItem>());
        typeof(FieldManager).GetField("<Npcs>k__BackingField", Any)!.SetValue(field, new ConcurrentDictionary<int, FieldNpc>());
        typeof(FieldManager).GetField("fieldInstruments", Any)!.SetValue(field, new ConcurrentDictionary<int, FieldInstrument>());
        // The maids of the placed cubes (none): a removed cube asks for its maid.
        System.Reflection.FieldInfo maids = typeof(FieldManager).GetField("maids", Any)!;
        maids.SetValue(field, Activator.CreateInstance(maids.FieldType));
        // The billboards and the field's queue of delayed steps (a removed object leaves through it).
        typeof(FieldManager).GetField("fieldAdBalloons", Any)!.SetValue(field, new ConcurrentDictionary<string, FieldInteract>());
        var scheduler = new Maple2.Tools.Scheduler.EventQueue(Log.Logger);
        scheduler.Start();
        typeof(FieldManager).GetField(nameof(FieldManager.Scheduler), Any)!.SetValue(field, scheduler);
        return field;
    }

    private static T Uninitialized<T>() => (T) RuntimeHelpers.GetUninitializedObject(typeof(T));

    private static void Set(GameSession session, string property, object value) {
        PropertyInfo info = typeof(GameSession).GetProperty(property, Any)!;
        (info.DeclaringType!.GetProperty(property, Any) ?? info).SetValue(session, value);
    }

    private static void SetLazy<T>(object storage, T table) where T : class {
        FieldInfo field = storage.GetType().GetFields(Any).Single(candidate => candidate.FieldType == typeof(Lazy<T>));
        field.SetValue(storage, new Lazy<T>(() => table));
    }
}
