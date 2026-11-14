using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Caching;
using Grpc.Core;
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
using Maple2.Server.Game.Model.Widget;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Trigger;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Server.Game.Util;
using Maple2.Server.World.Service;
using Serilog;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Tests.Game.Util;

// spawn_item_range on a field: every picked event item spawn point sends one FieldAddItem of its box's item
// at the point; the score board takes kill and orb points; a guild fund item goes to the World.
public class TriggerFieldDropTests {
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private const int OrbBoxId = 39990100;
    private const int OrbAmount = 700;

    private static void Set<T>(object target, string field, object? value) {
        FieldInfo info = typeof(T).GetField(field, Flags | BindingFlags.Public) ?? throw new MissingFieldException(typeof(T).Name, field);
        info.SetValue(target, value);
    }

    private static T Bare<T>() {
        var bare = (T) RuntimeHelpers.GetUninitializedObject(typeof(T));
        GC.SuppressFinalize(bare!);
        return bare;
    }

    private static ItemMetadata ItemOf(int id) {
        var property = new ItemMetadataProperty(false, 0, 100000, 4, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        return new ItemMetadata(id, $"{id}", [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [], null, null, null, null);
    }

    private static GameSession SessionOf(out BlockingCollection<(byte[] packet, int length)> sent) {
        var session = Bare<GameSession>();
        Type type = typeof(Maple2.Server.Core.Network.Session);
        sent = new BlockingCollection<(byte[] packet, int length)>();
        type.GetField("sendQueue", Flags)!.SetValue(session, sent);
        type.GetField("lastSentPackets", Flags)!.SetValue(session, new ConcurrentDictionary<SendOp, byte[]>());
        type.GetField("Logger", Flags)!.SetValue(session, new LoggerConfiguration().CreateLogger());
        return session;
    }

    // A field of map mapId with one player, the given event item spawn points and one individual drop box
    // (OrbBoxId: one group, one row of OrbAmount x itemId). Only what a trigger item drop touches is set.
    private static FieldManager FieldOf(int mapId, int itemId, IEnumerable<EventSpawnPointItem> spawns,
            out BlockingCollection<(byte[] packet, int length)> sent) {
        var field = Bare<FieldManager>();
        Set<FieldManager>(field, "initialized", true);
        Set<FieldManager>(field, "fieldItems", new ConcurrentDictionary<int, FieldItem>());
        Set<FieldManager>(field, "scoreBoardLock", new object());
        Set<FieldManager>(field, nameof(FieldManager.Widgets), new Dictionary<string, Widget>());

        GameSession session = SessionOf(out sent);
        var player = Bare<FieldPlayer>();
        typeof(FieldPlayer).GetField(nameof(FieldPlayer.Session))!.SetValue(player, session);
        Set<FieldManager>(field, "<Players>k__BackingField", new ConcurrentDictionary<int, FieldPlayer> { [1] = player });

        var map = Bare<MapMetadata>();
        Set<MapMetadata>(map, "<Id>k__BackingField", mapId);
        Set<FieldManager>(field, nameof(FieldManager.Metadata), map);
        Set<FieldManager>(field, "<MapId>k__BackingField", mapId);

        var entities = Bare<MapEntityMetadata>();
        Set<MapEntityMetadata>(entities, "<EventItemSpawns>k__BackingField", spawns.ToDictionary(spawn => spawn.SpawnPointId));
        Set<FieldManager>(field, "<Entities>k__BackingField", entities);
        Set<FieldManager>(field, "<ItemDrop>k__BackingField", new ItemDropManager(field));

        var row = new IndividualDropItemTable.Item([itemId], false, 0, 0, 100, new IndividualDropItemTable.Range<int>(OrbAmount, OrbAmount),
            [], 0, 0, false, false, false, false, [], false);
        var group = new IndividualDropItemTable.Entry(1, 0, [new IndividualDropItemTable.Entry.DropCount(1, 100)], 0, false, false, [row]);
        var drops = new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>> {
            [OrbBoxId] = new Dictionary<int, IndividualDropItemTable.Entry> { [1] = group },
        });
        var serverTables = Bare<ServerTableMetadataStorage>();
        Set<ServerTableMetadataStorage>(serverTables, "individualDropItemTable", new Lazy<IndividualDropItemTable>(() => drops));
        Set<ServerTableMetadataStorage>(serverTables, "featureTable", new Lazy<FeatureTable>(() => new FeatureTable(new Dictionary<string, int> { ["ItemSocketSystem"] = 1 })));
        Set<FieldManager>(field, "<ServerTableMetadata>k__BackingField", serverTables);

        var items = Bare<ItemMetadataStorage>();
        var cache = new LRUCache<int, ItemMetadata>(16, 1);
        cache.AddReplace(itemId, ItemOf(itemId));
        typeof(MetadataStorage<int, ItemMetadata>).GetField("Cache", Flags)!.SetValue(items, cache);
        Set<FieldManager>(field, "<ItemMetadata>k__BackingField", items);

        var tables = Bare<TableMetadataStorage>();
        var sockets = new ItemSocketTable(new Dictionary<int, IReadOnlyDictionary<int, ItemSocketMetadata>>());
        Set<TableMetadataStorage>(tables, "itemSocketTable", new Lazy<ItemSocketTable>(() => sockets));
        var stats = Bare<ItemStatsCalculator>();
        Set<ItemStatsCalculator>(stats, "<TableMetadata>k__BackingField", tables);
        Set<FieldManager>(field, "<ItemStatsCalc>k__BackingField", stats);
        Set<FieldManager>(field, "<TableMetadata>k__BackingField", tables);
        return field;
    }

    private static TriggerContext ContextOf(FieldManager field) {
        var trigger = Bare<FieldTrigger>();
        Set<FieldEntity<TriggerModel>>(trigger, "<Field>k__BackingField", field);
        Set<FieldEntity<TriggerModel>>(trigger, "<Value>k__BackingField", new TriggerModel(1, "main", Vector3.Zero, Vector3.Zero));
        var context = Bare<TriggerContext>();
        Set<TriggerContext>(context, "owner", trigger);
        Set<TriggerContext>(context, "logger", new LoggerConfiguration().CreateLogger());
        Set<TriggerContext>(context, "lastDebugKey", "");
        return context;
    }

    private static EventSpawnPointItem Spawn(int id) =>
        new(id, new Vector3(id * 10, id * 20, 300), Vector3.Zero, 0f, OrbBoxId, 0, 0, true);

    private static List<ByteReader> Packets(BlockingCollection<(byte[] packet, int length)> sent, SendOp op) {
        var readers = new List<ByteReader>();
        while (sent.TryTake(out (byte[] packet, int length) entry)) {
            var reader = new ByteReader(entry.packet.AsSpan(0, entry.length).ToArray(), 0);
            if (reader.Read<SendOp>() == op) {
                readers.Add(reader);
            }
        }
        return readers;
    }

    // Ten of nineteen points picked: ten FieldAddItem, each OrbAmount x the box's item at its own point, fixed,
    // anyone's (no receiver), of drop kind trigger.
    [Test]
    public void SpawnItemRange_SendsOneFieldAddItemPerPickedPoint() {
        const int itemId = 39990019;
        int[] ids = Enumerable.Range(1, 19).ToArray();
        FieldManager field = FieldOf(39990001, itemId, ids.Select(Spawn), out BlockingCollection<(byte[] packet, int length)> sent);

        ContextOf(field).SpawnItemRange(ids.ToArray(), 10);

        List<ByteReader> drops = Packets(sent, SendOp.FieldAddItem);
        Assert.That(drops, Has.Count.EqualTo(10));
        var points = new HashSet<int>();
        foreach (ByteReader reader in drops) {
            reader.ReadInt(); // object id
            Assert.That(reader.ReadInt(), Is.EqualTo(itemId));
            Assert.That(reader.ReadInt(), Is.EqualTo(OrbAmount));
            Assert.That(reader.ReadLong(), Is.EqualTo(0));
            Vector3 position = new(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat());
            int point = (int) position.X / 10;
            Assert.That(position, Is.EqualTo(Spawn(point).Position));
            Assert.That(points.Add(point), Is.True, "a point picked twice");
            Assert.That(reader.ReadInt(), Is.EqualTo(0)); // no owner object
            Assert.That(reader.ReadByte(), Is.EqualTo((byte) DropType.Player));
            reader.ReadInt(); // rarity
            Assert.That(reader.ReadShort(), Is.EqualTo(ItemDropKind.Trigger));
            Assert.That(reader.ReadBool(), Is.True); // fixed position
        }
        Assert.That(points.All(point => ids.Contains(point)), Is.True);
    }

    // A pick count over the points spawns each point once; an id with no spawn point sends nothing.
    [Test]
    public void SpawnItemRange_SkipsIdsWithoutAPoint() {
        FieldManager field = FieldOf(39990001, 39990019, [Spawn(1), Spawn(2)], out BlockingCollection<(byte[] packet, int length)> sent);

        ContextOf(field).SpawnItemRange([1, 2, 3], 5);

        Assert.That(Packets(sent, SendOp.FieldAddItem), Has.Count.EqualTo(2));
    }

    // The drops go to the players in Field.Players when the state runs (the bonus maps reach it by user_detected, which
    // reads Field.Players). With nobody in the field nothing is sent, but the drops stay in the field: a player who
    // enters later gets each of them (FieldManager.OnAddPlayer sends every field item).
    [Test]
    public void SpawnItemRange_KeepsTheDropsForAPlayerWhoEntersLater() {
        int[] ids = Enumerable.Range(1, 19).ToArray();
        FieldManager field = FieldOf(39990001, 39990019, ids.Select(Spawn), out BlockingCollection<(byte[] packet, int length)> sent);
        FieldPlayer player = field.Players[1];
        field.Players.Clear();

        ContextOf(field).SpawnItemRange(ids.ToArray(), 10);

        Assert.That(Packets(sent, SendOp.FieldAddItem), Is.Empty);
        var items = (ConcurrentDictionary<int, FieldItem>) typeof(FieldManager).GetField("fieldItems", Flags)!.GetValue(field)!;
        Assert.That(items.Values.Select(item => ((Item) item).Id), Is.EqualTo(Enumerable.Repeat(39990019, 10)));

        field.Players[1] = player;
        ContextOf(field).SpawnItemRange(ids.ToArray(), 10);
        Assert.That(Packets(sent, SendOp.FieldAddItem), Has.Count.EqualTo(10));
    }

    // Kill and orb points go to the open board, each total sent as its points; a script's set_score is the base.
    [Test]
    public void ScoreBoard_AddsKillAndOrbPointsToTheOpenBoard() {
        FieldManager field = FieldOf(39990001, FieldManager.ScoreBoardItemId, [], out BlockingCollection<(byte[] packet, int length)> sent);
        TriggerContext context = ContextOf(field);
        context.ScoreBoardCreate("ScoreBoardTopCenter", "", 0);
        context.ScoreBoardSetScore(0);
        Packets(sent, SendOp.ShadowExpedition);

        Assert.That(field.AddScoreBoardScore(100), Is.True);
        Assert.That(field.AddScoreBoardScore(1500), Is.True);
        Assert.That(field.TakeScoreOrb(new Item(ItemOf(FieldManager.ScoreBoardItemId), 1, OrbAmount)), Is.True);

        Assert.That(field.ScoreBoardScore, Is.EqualTo(100 + 1500 + OrbAmount));
        Assert.That(field.ScoreBoardOpen!.Points, Is.EqualTo(100 + 1500 + OrbAmount));
        Assert.That(context.ScoreBoardScore(2300, OperatorType.GreaterEqual), Is.True);
        Assert.That(context.ScoreBoardScore(2301, OperatorType.GreaterEqual), Is.False);
        List<int> points = Packets(sent, SendOp.ShadowExpedition).Select(reader => {
            Assert.That(reader.ReadByte(), Is.EqualTo(1)); // SetPoints
            return reader.ReadInt();
        }).ToList();
        Assert.That(points, Is.EqualTo(new[] { 100, 1600, 1600 + OrbAmount }));
    }

    // With no board open nothing is counted; an orb is still no item, any other item is.
    [Test]
    public void ScoreBoard_WithoutABoardCountsNothing() {
        FieldManager field = FieldOf(39990001, FieldManager.ScoreBoardItemId, [], out BlockingCollection<(byte[] packet, int length)> sent);

        Assert.That(field.AddScoreBoardScore(100), Is.False);
        Assert.That(field.TakeScoreOrb(new Item(ItemOf(FieldManager.ScoreBoardItemId), 1, OrbAmount)), Is.True);
        Assert.That(field.TakeScoreOrb(new Item(ItemOf(39990022), 1, OrbAmount)), Is.False);
        Assert.That(field.ScoreBoardScore, Is.Null);
        Assert.That(sent, Is.Empty);

        ContextOf(field).ScoreBoardCreate("ScoreBoardTopCenter", "", 0);
        Assert.That(field.AddScoreBoardScore(0), Is.False);
        Assert.That(field.ScoreBoardScore, Is.Null);
    }

    // The ScoreBoard widget of 02000385/02000386: OpenBoard opens the Dark Stream score dialog (0xC5 0) and places
    // it (0xD8 12, align 1); SetScore, kills and orbs send its score (0xC5 1); Compare reads "op,value";
    // CloseBoard closes it (0xD8 13), after which kills count no more and Compare still reads the score.
    [Test]
    public void ScoreBoardWidget_OpensCountsAndCloses() {
        FieldManager field = FieldOf(39990001, FieldManager.ScoreBoardItemId, [], out BlockingCollection<(byte[] packet, int length)> sent);
        TriggerContext context = ContextOf(field);
        context.CreateWidget("ScoreBoard");
        Assert.That(field.Widgets["ScoreBoard"], Is.TypeOf<ScoreBoardWidget>());
        Assert.That(field.AddScoreBoardScore(100), Is.False);

        context.WidgetAction("ScoreBoard", "OpenBoard", "1", "", 0);
        Assert.That(Packets(sent, SendOp.DarkStream).Select(reader => (reader.ReadByte(), reader.Available)), Is.EqualTo(new[] { ((byte) 0, 0) }));
        context.WidgetAction("ScoreBoard", "SetScore", "0", "", 0);
        Assert.That(field.AddScoreBoardScore(100), Is.True);
        Assert.That(field.TakeScoreOrb(new Item(ItemOf(FieldManager.ScoreBoardItemId), 1, OrbAmount)), Is.True);
        context.WidgetAction("ScoreBoard", "AddScore", "10", "", 0);

        Assert.That(context.WidgetValue("ScoreBoard", "Compare", ">=,810", false), Is.True);
        Assert.That(context.WidgetValue("ScoreBoard", "Compare", ">=,811", false), Is.False);
        Assert.That(((ScoreBoardWidget) field.Widgets["ScoreBoard"]).EnterPackets(), Has.Count.EqualTo(3));

        context.WidgetAction("ScoreBoard", "CloseBoard", "", "", 0);
        Assert.That(field.AddScoreBoardScore(100), Is.False);
        Assert.That(context.WidgetValue("ScoreBoard", "Compare", ">=,810", false), Is.True);
        Assert.That(((ScoreBoardWidget) field.Widgets["ScoreBoard"]).EnterPackets(), Is.Empty);
        Assert.That(field.ScoreBoardScore, Is.Null);
    }

    // The packets in order: open, place at align 1, then the score of each change; close.
    [Test]
    public void ScoreBoardWidget_Packets() {
        FieldManager field = FieldOf(39990001, FieldManager.ScoreBoardItemId, [], out BlockingCollection<(byte[] packet, int length)> sent);
        TriggerContext context = ContextOf(field);
        context.CreateWidget("ScoreBoard");
        context.WidgetAction("ScoreBoard", "OpenBoard", "1", "", 0);
        context.WidgetAction("ScoreBoard", "SetScore", "0", "", 0);
        field.AddScoreBoardScore(100);
        context.WidgetAction("ScoreBoard", "CloseBoard", "", "", 0);

        var packets = new List<string>();
        while (sent.TryTake(out (byte[] packet, int length) item)) {
            var reader = new ByteReader(item.packet.AsSpan(0, item.length).ToArray(), 0);
            var op = reader.Read<SendOp>();
            byte command = reader.ReadByte();
            packets.Add(reader.Available >= 4 ? $"{op} {command} {reader.ReadInt()}" : reader.Available == 1 ? $"{op} {command} {reader.ReadByte()}" : $"{op} {command}");
        }
        Assert.That(packets, Is.EqualTo(new[] {
            "DarkStream 0", "PlayArcade 12 1", "DarkStream 1 0", "DarkStream 1 100", "PlayArcade 13",
        }));
    }

    // OpenBoard 2 places the board at the top right; any other value at the center.
    [TestCase("2", (byte) 2)]
    [TestCase("1", (byte) 1)]
    [TestCase("0", (byte) 1)]
    public void ScoreBoardWidget_Align(string arg, byte align) {
        FieldManager field = FieldOf(39990001, FieldManager.ScoreBoardItemId, [], out _);
        TriggerContext context = ContextOf(field);
        context.CreateWidget("ScoreBoard");
        context.WidgetAction("ScoreBoard", "OpenBoard", arg, "", 0);
        Assert.That(((ScoreBoardWidget) field.Widgets["ScoreBoard"]).Align, Is.EqualTo(align));
    }

    private sealed class GuildWorld : WorldClient {
        public readonly List<GuildRequest> Requests = [];

        public override GuildResponse Guild(GuildRequest request, CallOptions options) {
            Requests.Add(request);
            return new GuildResponse();
        }
    }

    // A guild fund item (90000021) adds its amount to the guild's fund through the World, not to Reverse Coins
    // (the session has no currency to touch); without a guild it gives nothing.
    [Test]
    public void GuildFundItem_AddsToTheGuildFundThroughTheWorld() {
        var session = Bare<GameSession>();
        var world = new GuildWorld();
        Set<GameSession>(session, "<World>k__BackingField", world);
        Set<Maple2.Server.Core.Network.Session>(session, "<CharacterId>k__BackingField", 4000000001L);
        var guildManager = Bare<GuildManager>();
        Set<GuildManager>(guildManager, "session", session);
        Set<GuildManager>(guildManager, "logger", new LoggerConfiguration().CreateLogger());
        session.Guild = guildManager;
        var inventory = Bare<InventoryManager>();
        Set<InventoryManager>(inventory, "session", session);
        MethodInfo addCurrency = typeof(InventoryManager).GetMethod("AddCurrency", Flags)!;
        var fund = new Item(ItemOf(90000021), 1, 250);

        addCurrency.Invoke(inventory, [fund]);
        Assert.That(world.Requests, Is.Empty);

        Set<GuildManager>(guildManager, "<Guild>k__BackingField", new Guild(39990077, "g", 1, 4000000001L, "a"));
        addCurrency.Invoke(inventory, [fund]);

        Assert.That(world.Requests, Has.Count.EqualTo(1));
        GuildRequest request = world.Requests[0];
        Assert.That(request.RequestorId, Is.EqualTo(4000000001L));
        Assert.That(request.GuildCase, Is.EqualTo(GuildRequest.GuildOneofCase.AddExpFund));
        Assert.That(request.AddExpFund.GuildId, Is.EqualTo(39990077));
        Assert.That(request.AddExpFund.Exp, Is.EqualTo(0));
        Assert.That(request.AddExpFund.Fund, Is.EqualTo(250));
    }
}
