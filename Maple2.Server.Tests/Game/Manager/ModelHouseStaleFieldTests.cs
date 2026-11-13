using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Serilog;

namespace Maple2.Server.Tests.Game.Manager;

// A model house applied (InitNewHome with a template) while an instance of the unnamed home is still loaded with
// the cubes it had: the owner left it and the field waits for its empty-time dispose, or it runs on another
// channel. The template and item ids are made up.
public class ModelHouseStaleFieldTests {
    private const int OldCube = 50400011, TemplateCube = 50400012;
    private TestGameSession test = null!;
    private ExportedUgcMapMetadata template = null!;

    private (GameSession Session, Plot Plot) Start() {
        test = new TestGameSession();
        test.SetTable(new UgcHousingPointRewardTable(new Dictionary<int, UgcHousingPointRewardTable.Entry>()));
        test.SetTable(new MasteryUgcHousingTable(new Dictionary<int, MasteryUgcHousingTable.Entry> { [1] = new(1, 0, 0) }));
        test.SetTable(new FurnishingShopTable(new Dictionary<int, FurnishingShopTable.Entry>()));
        foreach (int id in new[] { OldCube, TemplateCube }) {
            test.Register(TestGameSession.Metadata(id, slotMax: 100) with {
                Housing = new ItemMetadataHousing(0, 0, 0, HousingCategory.None, false),
                Install = new ItemMetadataInstall(true, 0, 0, false, MapAttribute.none),
            });
        }
        var cache = (Dictionary<string, ExportedUgcMapMetadata>) typeof(MapMetadataStorage)
            .GetField("ExportedUgcCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(test.MapMetadata)!;
        template = new ExportedUgcMapMetadata("1", new Vector3B(-3, -3, 0), [10, 10, 4], [
            new ExportedUgcMapMetadata.Cube(TemplateCube, new Vector3B(0, 0, 0), 0, 0),
            new ExportedUgcMapMetadata.Cube(TemplateCube, new Vector3B(1, 0, 0), 0, 0),
        ]);
        cache["1"] = template;
        GameSession session = test.Start();
        session.Player.Value.Home.SetArea(10);
        session.Player.Value.Home.SetHeight(4);
        Assert.That(session.Player.Value.Home.Indoor.Name, Is.Empty);
        Plot plot = test.EnterHome();
        plot.Id = session.Player.Value.Home.Indoor.Id;
        foreach (int x in new[] { -1, -2 }) {
            session.ItemMetadata.TryGet(OldCube, out ItemMetadata? metadata);
            long uid = session.Item.Furnishing.AddCube(OldCube);
            Assert.That(session.Housing.TryPlaceCube(new PlotCube(metadata!, uid) { Type = PlotCube.CubeType.Default }, plot, metadata!,
                new Vector3B(x, -1, 0), 0, out _), Is.True);
        }
        test.TakeSent();
        return (session, plot);
    }

    private static void Commit(FieldManager field, GameSession session) =>
        typeof(FieldManager).GetMethod("CommitPlot", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(field, [session]);

    // As a relog would load it: storage is written first (it is saved with the character).
    private (int Stored, int Placed) Count(GameSession session, int itemId) {
        using (GameStorage.Request db = test.Storage.Context()) {
            session.Item.Furnishing.Save(db);
        }
        using var context = new Ms2Context(test.Options);
        int stored = context.Item.Where(row => row.OwnerId == session.AccountId && row.ItemId == itemId).Sum(row => (int?) row.Amount) ?? 0;
        int placed = context.UgcMapCube.Count(row => row.UgcMapId == session.Player.Value.Home.Indoor.Id && row.ItemId == itemId);
        return (stored, placed);
    }

    // The owner leaves the home (its save), applies the model house elsewhere, and comes back to the instance that is
    // still loaded: there the old cubes stand, the template's do not. Picking an old cube up and leaving again must
    // give no cube twice: each old cube once in storage, each template cube once placed.
    [Test]
    public void StaleInstance_GivesNoCubeTwice() {
        (GameSession session, Plot plot) = Start();
        FieldManager home = session.Field!;
        Commit(home, session);
        Assert.That(Count(session, OldCube), Is.EqualTo((0, 2)));

        session.Housing.InitNewHome("home", template);
        Assert.Multiple(() => {
            Assert.That(Count(session, OldCube), Is.EqualTo((2, 0)));
            Assert.That(Count(session, TemplateCube), Is.EqualTo((0, 2)));
        });

        bool removed = session.Housing.TryRemoveCube(plot, new Vector3B(-1, -1, 0), out _);
        try {
            Commit(home, session);
        } catch (TargetInvocationException) {
            // The save of the old list fails (its rows are gone); checked below by the rows.
        }

        Assert.Multiple(() => {
            Assert.That(removed, Is.False);
            Assert.That(Count(session, OldCube), Is.EqualTo((2, 0)));
            Assert.That(Count(session, TemplateCube), Is.EqualTo((0, 2)));
            Assert.That(session.Item.Furnishing.GetItem(OldCube)?.Amount, Is.EqualTo(2));
        });
    }

    // The channel's field factory with the home field loaded (no dispose thread runs).
    private FieldManager.Factory Factory(HomeFieldManager home) {
        const BindingFlags any = BindingFlags.Instance | BindingFlags.NonPublic;
        test.AddMap(home.Metadata);
        test.SetServerTable(new InstanceFieldTable(new Dictionary<int, InstanceFieldMetadata> {
            [home.MapId] = new(home.MapId, InstanceType.ugcMap, 0, false, 0, false, 0, 0, 0, 0),
        }));
        var factory = (FieldManager.Factory) RuntimeHelpers.GetUninitializedObject(typeof(FieldManager.Factory));
        void Set(string name, object value) => typeof(FieldManager.Factory).GetField(name, any)!.SetValue(factory, value);
        Set("logger", Log.Logger);
        Set("mapLocks", new ConcurrentDictionary<int, SemaphoreSlim>());
        Set("fields", new ConcurrentDictionary<int, ConcurrentDictionary<int, FieldManager>>());
        Set("homes", new ConcurrentDictionary<long, HomeFieldManager> { [home.OwnerId] = home });
        Set("ownerRooms", new ConcurrentDictionary<(int MapId, long OwnerId), int>());
        foreach (FieldInfo field in typeof(FieldManager.Factory).GetFields(any).Where(field => field.Name == "retired")) {
            field.SetValue(factory, Activator.CreateInstance(field.FieldType));
        }
        Set("dungeons", new ConcurrentDictionary<int, DungeonFieldManager>());
        Set("subDungeonFields", new ConcurrentDictionary<int, DungeonFieldManager>());
        Set("cancel", new CancellationTokenSource());
        var done = new Thread(() => { });
        done.Start();
        Set("thread", done);
        Set("<GameStorage>k__BackingField", test.Storage);
        Set("<MapMetadata>k__BackingField", test.MapMetadata);
        Set("<ServerTableMetadata>k__BackingField", test.ServerTableMetadata);
        return factory;
    }

    // The owner leaves the unnamed home and applies the model house before the field's empty time ends: the next
    // entry does not get the field with the old cubes; the home is loaded again (here no field is made: the test
    // map has no navmesh). Before, the old field was entered.
    [Test]
    public void EntryAfterTheModelHouse_LoadsTheHomeAgain() {
        (GameSession session, Plot _) = Start();
        var home = (HomeFieldManager) session.Field!;
        Commit(home, session);
        home.Players.Clear();
        using (GameStorage.Request db = test.Storage.Context()) {
            // As the factory loads it (CreateHome), not the session's own Home.
            typeof(HomeFieldManager).GetField("<Home>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(home, db.GetHome(session.AccountId)!);
        }
        FieldManager.Factory factory = Factory(home);
        FieldManager? before = factory.Get(home.MapId, session.AccountId);

        session.Housing.InitNewHome("home", template);
        FieldManager? after = factory.Get(home.MapId, session.AccountId);

        Assert.Multiple(() => {
            Assert.That(before, Is.SameAs(home));
            Assert.That(after, Is.Not.SameAs(home));
            Assert.That(factory.FindHome(session.AccountId), Is.Not.SameAs(home));
        });
    }

    // The unnamed home with the owner's (or a visitor's) map still loading (spawned by PrepareField, not yet
    // entered): an entry after the model house keeps that field, as one with players in it, so the loading player
    // does not come into a field the factory disposes. Once the player has left it, the home is loaded again.
    // Before, the field was retired with the player on the way in.
    [Test]
    public void PlayerOnTheWayIn_KeepsTheField() {
        (GameSession session, Plot _) = Start();
        var home = (HomeFieldManager) session.Field!;
        Commit(home, session);
        home.Players.Clear();
        using (GameStorage.Request db = test.Storage.Context()) {
            typeof(HomeFieldManager).GetField("<Home>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(home, db.GetHome(session.AccountId)!);
        }
        FieldManager.Factory factory = Factory(home);
        // As SpawnPlayer leaves the player it made (the test session's managers cannot spawn one).
        const int objectId = 77;
        ((ConcurrentDictionary<int, byte>) typeof(FieldManager).GetField("arriving", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(home)!)[objectId] = 0;

        session.Housing.InitNewHome("home", template);
        FieldManager? loading = factory.Get(home.MapId, session.AccountId);
        home.RemovePlayer(objectId, out _);
        FieldManager? left = factory.Get(home.MapId, session.AccountId);

        Assert.Multiple(() => {
            Assert.That(loading, Is.SameAs(home));
            Assert.That(left, Is.Not.SameAs(home));
            Assert.That(home.Retired, Is.True);
        });
    }

    // A home retired since the dispose loop's last pass is disposed when the factory is (the channel's shutdown).
    // Before, it was left undisposed.
    [Test]
    public void Shutdown_DisposesARetiredHome() {
        (GameSession session, Plot _) = Start();
        var home = (HomeFieldManager) session.Field!;
        Commit(home, session);
        home.Players.Clear();
        using (GameStorage.Request db = test.Storage.Context()) {
            typeof(HomeFieldManager).GetField("<Home>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(home, db.GetHome(session.AccountId)!);
        }
        FieldManager.Factory factory = Factory(home);
        session.Housing.InitNewHome("home", template);
        Assert.That(factory.Get(home.MapId, session.AccountId), Is.Not.SameAs(home));
        // What the field's own Dispose stops: its thread (done here), its token and its navigation.
        void Set(string name, object value) => typeof(FieldManager).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(home, value);
        Set("logger", Log.Logger);
        Set("cancel", new CancellationTokenSource());
        var done = new Thread(() => { });
        done.Start();
        Set("thread", done);
        Set("<Navigation>k__BackingField", RuntimeHelpers.GetUninitializedObject(typeof(Navigation)));
        Set("<DebugGraphicsContext>k__BackingField", new Maple2.Server.Game.DebugGraphics.HeadlessGraphicsContext());

        factory.Dispose();

        Assert.That(home.Disposed, Is.True);
    }
}
