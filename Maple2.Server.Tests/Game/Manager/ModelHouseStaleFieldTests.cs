using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Maple2.Database.Context;
using UgcMapCube = Maple2.Database.Model.UgcMapCube;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Maple2.Server.Tests.Game.Manager;

// A model house applied (InitNewHome with a template) while an instance of the unnamed home is still loaded with
// the cubes it had: the owner left it and the field waits for its empty-time dispose, or it runs on another
// channel. The template and item ids are made up.
public class ModelHouseStaleFieldTests {
    private const int OldCube = 50400011, TemplateCube = 50400012;
    private TestGameSession test = null!;
    private ExportedUgcMapMetadata template = null!;

    private (GameSession Session, Plot Plot) Start(int oldCubes = 2) {
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
        foreach (int x in new[] { -1, -2 }.Take(oldCubes)) {
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

    // The field of a named home, loaded with the cubes the home had (none here), as the factory loads it.
    private HomeFieldManager NamedHomeField(GameSession session) {
        Home own = session.Player.Value.Home;
        own.Indoor.Name = "home";
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.SavePlotInfo(own.Indoor), Is.True);
        }
        var home = (HomeFieldManager) session.Field!;
        using (GameStorage.Request db = test.Storage.Context()) {
            typeof(HomeFieldManager).GetField("<Home>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(home, db.GetHome(session.AccountId)!);
        }
        home.Plots.Clear();
        home.LoadPlots();
        Assert.That(home.Home.Indoor.Name, Is.Not.Empty);
        return home;
    }

    // The number of cubes the client is given when it loads the map of the session's field (RequestLoadUgcMap):
    // LoadCubes mode 0 (Load), after its mode byte and a bool.
    private int LoadedCubes(GameSession session) {
        test.TakeSent();
        new LoadUgcMapHandler { GameStorage = test.Storage }.Handle(session, TestGameSession.Packet(writer => writer.WriteInt(GameSession.FIELD_KEY)));
        byte[] load = test.TakeSent().Single(packet => TestGameSession.Op(packet) == SendOp.LoadCubes && packet[2] == 0);
        return BitConverter.ToInt32(load, 4);
    }

    private bool Outdated(HomeFieldManager field) {
        using GameStorage.Request db = test.Storage.Context();
        return field.Outdated(db);
    }

    private List<long> CubeRows(GameSession session) {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetCubeIds(session.Player.Value.Home.Indoor.Id).ToList();
    }

    // A home field loaded while the home was named and had no cubes; then the model house is applied (the template
    // cubes are written to the database). The next entry does not get the old field: it is retired and the home is
    // loaded from the database again, whose map load gives the client the template cubes. Before, the field was
    // entered (only a field loaded unnamed was reloaded) and its empty floor was shown.
    [Test]
    public void NamedAtLoad_EntryAfterTheModelHouse_LoadsTheCubesTheDatabaseHas() {
        (GameSession session, Plot _) = Start(oldCubes: 0);
        HomeFieldManager home = NamedHomeField(session);
        Commit(home, session);
        home.Players.Clear();
        FieldManager.Factory factory = Factory(home);
        Assert.That(factory.Get(home.MapId, session.AccountId), Is.SameAs(home));

        session.Housing.InitNewHome("home", template);
        Assert.That(CubeRows(session), Has.Count.EqualTo(2));
        Assert.That(Outdated(home), Is.True);
        FieldManager? entered = factory.Get(home.MapId, session.AccountId);

        // The field the factory makes in its place (CreateHome; the test map has no navmesh, so it is built here).
        test.EnterHome();
        var fresh = (HomeFieldManager) session.Field!;
        using (GameStorage.Request db = test.Storage.Context()) {
            typeof(HomeFieldManager).GetField("<Home>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(fresh, db.GetHome(session.AccountId)!);
        }
        fresh.Plots.Clear();
        fresh.LoadPlots();

        Assert.Multiple(() => {
            Assert.That(entered, Is.Not.SameAs(home));
            Assert.That(home.Retired, Is.True);
            Assert.That(Outdated(fresh), Is.False);
            Assert.That(LoadedCubes(session), Is.EqualTo(2));
            Assert.That(fresh.Plots.Values.Single().Cubes.Values.Select(cube => cube.Id), Is.EquivalentTo(CubeRows(session)));
        });
    }

    // The same old field kept by a visitor in it (not retired), with the owner coming back into it and leaving: the
    // leave saves nothing, so the template cubes stay in the database for the next load. Before, its empty list was
    // saved and every template cube row was deleted.
    [Test]
    public void OutdatedField_OwnerLeaving_KeepsTheDatabaseCubes() {
        (GameSession session, Plot _) = Start(oldCubes: 0);
        HomeFieldManager home = NamedHomeField(session);
        Commit(home, session);

        session.Housing.InitNewHome("home", template);
        Assert.That(Count(session, TemplateCube), Is.EqualTo((0, 2)));
        Assert.That(Outdated(home), Is.True);
        try {
            Commit(home, session);
        } catch (TargetInvocationException) {
            // Checked below by the rows.
        }

        Assert.That(Count(session, TemplateCube), Is.EqualTo((0, 2)));
    }

    // A field's own place and remove leave it current (they write and delete their rows at once), as does its save.
    [Test]
    public void OwnEdits_LeaveTheFieldCurrent() {
        (GameSession session, Plot plot) = Start();
        var home = (HomeFieldManager) session.Field!;
        Assert.That(Outdated(home), Is.False);
        Commit(home, session);
        Assert.That(Outdated(home), Is.False);
        Assert.That(session.Housing.TryRemoveCube(plot, new Vector3B(-1, -1, 0), out _), Is.True);
        session.ItemMetadata.TryGet(OldCube, out ItemMetadata? metadata);
        Assert.That(session.Housing.TryPlaceCube(new PlotCube(metadata!, session.Item.Furnishing.AddCube(OldCube)) { Type = PlotCube.CubeType.Default },
            plot, metadata!, new Vector3B(-3, -1, 0), 0, out _), Is.True);
        Assert.That(Outdated(home), Is.False);
        Commit(home, session);
        Assert.That(Outdated(home), Is.False);
    }

    // A rotation writes its row at once, so it is kept even when the field is not saved at the leave (an outdated
    // field), and the field stays current. Before, a rotation changed only the held cube and was saved at the leave.
    [Test]
    public void Rotation_WritesItsRowAtOnce() {
        (GameSession session, Plot plot) = Start();
        var home = (HomeFieldManager) session.Field!;
        Commit(home, session);
        Assert.That(session.Housing.TryRotateCube(plot, new Vector3B(-1, -1, 0), true, out PlotCube? cube), Is.True);
        using GameStorage.Request db = test.Storage.Context();
        Assert.Multiple(() => {
            Assert.That(cube!.Rotation, Is.EqualTo(270f));
            Assert.That(db.GetCubeRotations(plot.Id)[cube.Id], Is.EqualTo(270f));
            Assert.That(Outdated(home), Is.False);
        });
    }

    // A rotation whose row write fails leaves the cube as it was, to the exact value: a rotation outside 0..360 (as a
    // row can hold) is kept, so the field's rotation still equals its row's. Before, the turn-back rotated the other
    // way, which gives the old value only modulo 360.
    [Test]
    public void FailedRotation_KeepsTheExactRotation() {
        (GameSession session, Plot plot) = Start();
        var home = (HomeFieldManager) session.Field!;
        Commit(home, session);
        PlotCube cube = plot.Cubes[new Vector3B(-1, -1, 0)];
        cube.Rotation = -90f;
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.DeleteCube(cube), Is.True);
        }
        Assert.Multiple(() => {
            Assert.That(session.Housing.TryRotateCube(plot, cube.Position, true, out _), Is.False);
            Assert.That(cube.Rotation, Is.EqualTo(-90f));
        });
    }

    // A rotation made in another instance of the home (its row has another rotation, the same ids) makes this field
    // outdated: its leave would write the old rotation back. Before, only the row ids were compared.
    [Test]
    public void RotationElsewhere_MakesTheFieldOutdated() {
        (GameSession session, Plot plot) = Start();
        var home = (HomeFieldManager) session.Field!;
        Commit(home, session);
        Assert.That(Outdated(home), Is.False);
        long id = plot.Cubes.Values.First().Id;
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.SetCubeRotation(id, 90f), Is.True);
        }
        Assert.That(Outdated(home), Is.True);
    }

    // The owner's leave saves while a build helper's edits run on another thread (a place, a rotation and a remove,
    // each writing its row at once). Every save takes one copy of the cubes under the plot's edit lock: no save
    // fails, and at the end the rows are the cubes the plot holds, with their rotations. Before, the save enumerated
    // the live cube list (it could throw), deleted the row of a cube placed while it ran, and wrote back the old
    // rotation of a cube turned meanwhile.
    [Test]
    public void SaveWhileEditing_WritesOneCopy() {
        (GameSession session, Plot plot) = Start();
        var home = (HomeFieldManager) session.Field!;
        Commit(home, session);
        session.ItemMetadata.TryGet(OldCube, out ItemMetadata? metadata);
        var spot = new Vector3B(-3, -1, 0);
        bool done = false;
        Exception? failure = null;
        var other = new Thread(() => {
            try {
                while (!Volatile.Read(ref done)) {
                    long uid = session.Item.Furnishing.AddCube(OldCube);
                    session.Housing.TryPlaceCube(new PlotCube(metadata!, uid) { Type = PlotCube.CubeType.Default }, plot, metadata!, spot, 0, out _);
                    session.Housing.TryRotateCube(plot, new Vector3B(-1, -1, 0), true, out _);
                    session.Housing.TryRemoveCube(plot, spot, out _);
                    session.Housing.TryPlaceCube(new PlotCube(metadata!, uid) { Type = PlotCube.CubeType.Default }, plot, metadata!, spot, 0, out _);
                }
            } catch (Exception e) {
                failure = e;
            }
        });
        other.Start();
        int thrown = 0;
        for (int i = 0; i < 300; i++) {
            try {
                Commit(home, session);
            } catch (TargetInvocationException) {
                thrown++;
            }
            if (i % 20 == 0) {
                test.TakeSent();
            }
        }
        Volatile.Write(ref done, true);
        other.Join();
        test.TakeSent();

        using GameStorage.Request db = test.Storage.Context();
        Dictionary<long, float> rows = db.GetCubeRotations(plot.Id);
        Assert.Multiple(() => {
            Assert.That(failure, Is.Null);
            Assert.That(thrown, Is.Zero);
            Assert.That(rows.Keys, Is.EquivalentTo(plot.Cubes.Values.Select(cube => cube.Id)));
            Assert.That(plot.Cubes.Values.All(cube => rows[cube.Id] == cube.Rotation), Is.True);
            Assert.That(Outdated(home), Is.False);
        });
    }

    // A row the field took off the plot but could not delete (a stray row) leaves the field current, and the next
    // save deletes it. Before, such a row kept the field outdated for its life, and its leave saved nothing.
    [Test]
    public void StrayRow_KeepsTheFieldCurrent_AndTheSaveDeletesIt() {
        (GameSession session, Plot plot) = Start();
        var home = (HomeFieldManager) session.Field!;
        Commit(home, session);
        PlotCube cube = plot.Cubes[new Vector3B(-1, -1, 0)];
        lock (plot.EditLock) {
            // As a place refused after its row was written, whose delete then failed.
            plot.Cubes.Remove(cube.Position);
            plot.StrayRows.Add(cube.Id);
        }
        Assert.That(CubeRows(session), Does.Contain(cube.Id));
        Assert.That(Outdated(home), Is.False);
        Commit(home, session);
        Assert.Multiple(() => {
            Assert.That(CubeRows(session), Does.Not.Contain(cube.Id));
            Assert.That(plot.StrayRows, Is.Empty);
            Assert.That(Outdated(home), Is.False);
        });
    }

    // A remove landing in every try's write, between its read of the rows and the write (the helper's delete is
    // written at once): the save leaves that cube out and writes the rest in the same try. Before, the update of the
    // missing row failed the whole write, and a remove in each of the 3 tries made the leave throw.
    [Test]
    public void RemoveDuringTheWrite_LeavesThatCubeOut() {
        (GameSession session, Plot plot) = Start();
        var home = (HomeFieldManager) session.Field!;
        session.ItemMetadata.TryGet(OldCube, out ItemMetadata? metadata);
        Assert.That(session.Housing.TryPlaceCube(new PlotCube(metadata!, session.Item.Furnishing.AddCube(OldCube)) { Type = PlotCube.CubeType.Default },
            plot, metadata!, new Vector3B(-3, -1, 0), 0, out _), Is.True);
        Commit(home, session);
        var contexts = new HashSet<Guid>();
        test.FailSave = context => {
            if (context.ChangeTracker.Entries<UgcMapCube>().Any(entry => entry.State == EntityState.Modified)
                && contexts.Add(context.ContextId.InstanceId)) {
                // Each try's first write: a remove of one of the cubes it is about to update.
                Assert.That(session.Housing.TryRemoveCube(plot, plot.Cubes.Keys.OrderBy(position => position.X).First(), out _), Is.True);
            }
            return false;
        };
        Assert.That(() => Commit(home, session), Throws.Nothing);
        test.FailSave = null;
        Assert.Multiple(() => {
            Assert.That(contexts, Has.Count.EqualTo(1));
            Assert.That(CubeRows(session), Is.EquivalentTo(plot.Cubes.Values.Select(cube => cube.Id)));
            Assert.That(plot.Cubes, Has.Count.EqualTo(2));
            Assert.That(Outdated(home), Is.False);
            Assert.That(Count(session, OldCube), Is.EqualTo((1, 2)));
        });
    }

    // The rule: a row the field neither holds nor saved, or a held cube without its row, makes the field outdated.
    [TestCase(new long[] { 1, 2 }, new long[] { 1, 2 }, new long[] { 1, 2 }, false)]
    [TestCase(new long[] { 1, 2 }, new long[] { 1 }, new long[] { 1, 2 }, false)] // removed here, saved row not yet deleted
    [TestCase(new long[] { 1 }, new long[] { 1, 3 }, new long[] { 1, 3 }, false)] // placed here
    [TestCase(new long[] { }, new long[] { }, new long[] { 4, 5 }, true)] // written elsewhere (model house)
    [TestCase(new long[] { 1, 2 }, new long[] { 1, 2 }, new long[] { 4 }, true)] // replaced elsewhere
    public void OutdatedRule(long[] saved, long[] held, long[] rows, bool outdated) {
        Assert.That(HomeFieldManager.Outdated(saved.ToHashSet(), held.ToHashSet(), rows.ToHashSet()), Is.EqualTo(outdated));
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
