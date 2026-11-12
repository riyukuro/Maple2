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
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
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
//   handler.Handle(session, TestGameSession.Packet(writer => writer.WriteLong(scroll.Uid)));
//   test.TakeSent()                                         // the packets sent since the last call, in order
//
// The session is made without its constructor, so only what Start sets exists: Player, Item (Equips, Inventory,
// Furnishing), Currency, Buffs, Achievement, Quest (no quests), Marriage (single), Field (a field that only
// broadcasts to this player) and World (grants every lock). A handler that reaches anything else needs it set first.
// Storage is the real GameStorage; pass MySQL options to run the same test against a database.
public sealed class TestGameSession {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public readonly DbContextOptions Options;
    public readonly GameStorage Storage;
    public readonly TableMetadataStorage TableMetadata = Uninitialized<TableMetadataStorage>();
    public readonly ServerTableMetadataStorage ServerTableMetadata = Uninitialized<ServerTableMetadataStorage>();
    public readonly LockWorld World = new();
    public readonly long AccountId;
    public readonly long CharacterId;
    public GameSession Session { get; private set; } = null!;
    // What the session logged as an error. Several session methods catch and log (SavePurchase does), so a test
    // that expects a save should also expect this to be empty.
    public readonly List<string> Errors = [];

    private readonly LRUCache<int, ItemMetadata> items = new(1024, 1);
    private readonly ItemMetadataStorage itemMetadata = Uninitialized<ItemMetadataStorage>();
    private readonly BlockingCollection<(byte[] packet, int length)> sendQueue = new(new ConcurrentQueue<(byte[], int)>());

    public TestGameSession(DbContextOptions? options = null, Gender gender = Gender.Male) {
        Options = options ?? new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        // ItemMetadataStorage's constructor runs SQL the in-memory provider lacks; TryGet looks in the cache first.
        typeof(MetadataStorage<int, ItemMetadata>).GetField("Cache", Any)!.SetValue(itemMetadata, items);
        // The home's plot, which LoadPlayer looks up for the account's home.
        var maps = Uninitialized<MapMetadataStorage>();
        var ugcMaps = new LRUCache<int, UgcMapMetadata>(4, 1);
        var cost = new UgcMapGroup.Cost(0, 0, 0);
        ugcMaps.AddReplace(Constant.DefaultHomeMapId, new UgcMapMetadata(Constant.DefaultHomeMapId, new Dictionary<int, UgcMapGroup> {
            [Constant.DefaultHomeNumber] = new(Constant.DefaultHomeNumber, 0, 0, cost, cost, new UgcMapGroup.Limits(0, 0, 0, 0, 0, 0)),
        }));
        typeof(MapMetadataStorage).GetField("UgcCache", Any)!.SetValue(maps, ugcMaps);
        Storage = new GameStorage(Options, itemMetadata, maps, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);
        SetServerTable(new ConstantsTable(
            bagSlotTabGameCount: [48, 48], bagSlotTabSkinCount: [48, 48], bagSlotTabSummonCount: [48, 48],
            bagSlotTabMaterialCount: [48, 48], bagSlotTabMasteryCount: [48, 48], bagSlotTabLifeCount: [48, 48],
            bagSlotTabQuestCount: [48, 48], bagSlotTabGemCount: [48, 48], bagSlotTabPetCount: [48, 48],
            bagSlotTabActiveSkillCount: [48, 48], bagSlotTabCoinCount: [48, 48], bagSlotTabBadgeCount: [48, 48],
            bagSlotTabMiscCount: [48, 48], bagSlotTabLapenShardCount: [48, 48], bagSlotTabPieceCount: [48, 48]));

        // The rows a login finds. The account row is added directly: CreateAccount hashes a password, which is slow.
        string name = Guid.NewGuid().ToString("N")[..12];
        using (var context = new Ms2Context(Options)) {
            Maple2.Database.Model.Account account = new Account { Username = name };
            account.Id = 0;
            context.Account.Add(account);
            context.SaveChanges();
            AccountId = account.Id;
            context.Home.Add(new Maple2.Database.Model.Home { AccountId = AccountId });
            context.UgcMap.Add(new Maple2.Database.Model.UgcMap {
                OwnerId = AccountId, MapId = Constant.DefaultHomeMapId, Indoor = true,
                Number = Constant.DefaultHomeNumber,
            });
            context.SaveChanges();
        }
        using GameStorage.Request db = Storage.Context();
        Character character = db.CreateCharacter(new Character {
            AccountId = AccountId, Name = name, Gender = gender, Job = Job.Newbie, Mastery = new Mastery(),
            MapId = 2000023, ReturnMaps = new Maple2.Tools.LimitedStack<int>(3, 2000023),
        }) ?? throw new InvalidOperationException("character row not created");
        CharacterId = character.Id;
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

    // A client table (TableMetadata) or a server table (ServerTableMetadata) the handler reads.
    public void SetTable<T>(T table) where T : Table => SetLazy(TableMetadata, table);
    public void SetServerTable<T>(T table) where T : ServerTable => SetLazy(ServerTableMetadata, table);
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
        Set(session, nameof(GameSession.CharacterId), CharacterId);
        Set(session, nameof(GameSession.GameStorage), Storage);
        Set(session, nameof(GameSession.ItemMetadata), itemMetadata);
        Set(session, nameof(GameSession.TableMetadata), TableMetadata);
        Set(session, nameof(GameSession.ServerTableMetadata), ServerTableMetadata);
        Set(session, nameof(GameSession.World), World);
        var metadataOptions = new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        Set(session, nameof(GameSession.AchievementMetadata), new AchievementMetadataStorage(new MetadataContext(metadataOptions)));

        using GameStorage.Request db = Storage.Context();
        Player player = db.LoadPlayer(AccountId, CharacterId, 1, 1) ?? throw new InvalidOperationException("player not loaded");
        var fieldPlayer = Uninitialized<FieldPlayer>();
        GC.SuppressFinalize(fieldPlayer);
        typeof(FieldPlayer).GetField(nameof(FieldPlayer.Session), Any)!.SetValue(fieldPlayer, session);
        typeof(Actor<Player>).GetField("<Value>k__BackingField", Any)!.SetValue(fieldPlayer, player);
        typeof(Actor<Player>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(fieldPlayer, player.ObjectId);
        Set(session, nameof(GameSession.Player), fieldPlayer);

        var field = Uninitialized<FieldManager>();
        GC.SuppressFinalize(field);
        var players = new ConcurrentDictionary<int, FieldPlayer>();
        players[fieldPlayer.ObjectId] = fieldPlayer;
        typeof(FieldManager).GetField("<Players>k__BackingField", Any)!.SetValue(field, players);
        typeof(FieldManager).GetField("initialized", Any)!.SetValue(field, true);
        session.Field = field;

        session.Currency = new CurrencyManager(session);
        session.Buffs = new BuffManager(fieldPlayer);
        session.Achievement = new AchievementManager(session);
        var quest = Uninitialized<QuestManager>();
        typeof(QuestManager).GetField("session", Any)!.SetValue(quest, session);
        typeof(QuestManager).GetField("accountValues", Any)!.SetValue(quest, new Dictionary<int, Quest>());
        typeof(QuestManager).GetField("characterValues", Any)!.SetValue(quest, new Dictionary<int, Quest>());
        session.Quest = quest;
        var marriage = Uninitialized<MarriageManager>();
        typeof(MarriageManager).GetField("session", Any)!.SetValue(marriage, session);
        marriage.Marriage = Marriage.Default;
        session.Marriage = marriage;
        session.Item = new ItemManager(db, session, Uninitialized<ItemStatsCalculator>());

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
    public List<byte[]> TakeSent() {
        var sent = new List<byte[]>();
        while (sendQueue.TryTake(out (byte[] packet, int length) entry)) {
            sent.Add(entry.packet);
        }
        return sent;
    }

    public static SendOp Op(byte[] packet) => (SendOp) (packet[1] << 8 | packet[0]);

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

    public Character StoredCharacter() {
        using GameStorage.Request db = Storage.Context();
        return db.GetCharacter(CharacterId) ?? throw new InvalidOperationException("character row gone");
    }
    #endregion

    // The world's account lock, granted at once.
    public sealed class LockWorld : WorldClient {
        public int Acquired;
        public int Released;

        public override LockResponse AcquireLock(LockRequest request, CallOptions options) {
            Acquired++;
            return new LockResponse();
        }

        public override LockResponse ReleaseLock(LockRequest request, CallOptions options) {
            Released++;
            return new LockResponse();
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
