using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Caching;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Tests.Game;
using Maple2.Server.World;
using Maple2.Server.World.Containers;
using Maple2.Tools.Scheduler;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Database;

// World's plot expiry check takes back an expired plot in one write: the row loses its owner, its cubes go, and they
// come back to the owner's furnishing storage as new rows. A stored stack is not raised (a game server holding the
// storage writes its own amount back over it), and a plot extended meanwhile, a cube picked up meanwhile, or a write
// that fails half way writes nothing; a later check takes the plot then. Each test runs on the in-memory store, and on
// MySQL when MS2_TEST_GAME_DB holds the connection string of a scratch game database; the races and the failed write
// need the real database. The map id is made up.
[TestFixture(false)]
[TestFixture(true)]
public class PlotExpiryTests(bool mysql) {
    private static readonly string? Connection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");
    private const int MapId = 99000102;
    private const int Number = 7;
    private const int CubeItemId = 50000103;
    private const int MaidItemId = 50000104;
    private const int OtherNumber = 8;
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic;

    private TestGameSession test = null!;
    private GameStorage storage = null!;
    private ItemMetadataStorage items = null!;
    private DbContextOptions plain = null!; // the other connection
    private readonly CommandHook hook = new();
    private long owner;
    private long plotId;
    private readonly List<long> plots = [];
    private long[] cubeIds = [];
    private DateTimeOffset expiry;

    [SetUp]
    public void SetUp() {
        if (mysql && Connection == null) {
            Assert.Ignore("the MySQL fixture needs a real database: set MS2_TEST_GAME_DB");
        }
        hook.Before = null;
        plots.Clear();
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(CubeItemId), MaidMetadata());
        DbContextOptions options;
        if (mysql) {
            ServerVersion version = ServerVersion.AutoDetect(Connection);
            plain = new DbContextOptionsBuilder().UseMySql(Connection, version).Options;
            options = new DbContextOptionsBuilder().UseMySql(Connection, version).AddInterceptors(hook).Options;
        } else {
            plain = test.Options;
            options = test.Options;
        }
        var cost = new UgcMapGroup.Cost(0, 0, 0);
        var ugcMaps = (LRUCache<int, UgcMapMetadata>) typeof(MapMetadataStorage).GetField("UgcCache", Any)!.GetValue(test.MapMetadata)!;
        ugcMaps.AddReplace(MapId, new UgcMapMetadata(MapId, new Dictionary<int, UgcMapGroup> {
            [Number] = new(Number, 0, 0, cost, cost, new UgcMapGroup.Limits(0, 0, 0, 0, 0, 0)),
            [OtherNumber] = new(OtherNumber, 0, 0, cost, cost, new UgcMapGroup.Limits(0, 0, 0, 0, 0, 0)),
        }));
        items = (ItemMetadataStorage) typeof(GameStorage).GetField("itemMetadata", Any)!.GetValue(test.Storage)!;
        storage = new GameStorage(options, items, test.MapMetadata, null!, null!, test.TableMetadata, test.ServerTableMetadata,
            NullLogger<GameStorage>.Instance, null!);

        // An account id no account has: the rows are found, and removed, by it and by the plot id. The plot ran out
        // an hour ago with two cubes on it, and the owner has a stack of the same furnishing in storage.
        owner = 7_300_000_000_000 + Random.Shared.NextInt64(1_000_000_000) * 10;
        expiry = DateTimeOffset.UtcNow.AddHours(-1);
        plotId = AddPlot(owner, Number);

        Item stack;
        using (GameStorage.Request create = storage.Context()) {
            stack = create.CreateItem(owner, new Item(TestGameSession.Metadata(CubeItemId), amount: 4)) ?? throw new InvalidOperationException("item row not created");
        }
        stack.Group = ItemGroup.Furnishing;
        using (GameStorage.Request save = storage.Context()) {
            if (!save.SaveItems(owner, stack)) {
                throw new InvalidOperationException("item row not saved");
            }
        }

        using GameStorage.Request db = storage.Context();
        PlotCube first = db.CreateCube(Info(), Cube(1)) ?? throw new InvalidOperationException("cube row not created");
        PlotCube second = db.CreateCube(Info(), Cube(2)) ?? throw new InvalidOperationException("cube row not created");
        cubeIds = [first.Id, second.Id];
    }

    private long AddPlot(long plotOwner, int number) {
        using var context = new Ms2Context(plain);
        var map = new Maple2.Database.Model.UgcMap {
            OwnerId = plotOwner, Name = "home", MapId = MapId, Indoor = false, Number = number, ExpiryTime = expiry,
        };
        context.UgcMap.Add(map);
        context.SaveChanges();
        plots.Add(map.Id);
        return map.Id;
    }

    private static ItemMetadata MaidMetadata() {
        var property = new ItemMetadataProperty(false, 0, 100, 0, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        return new ItemMetadata(MaidItemId, $"{MaidItemId}", [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [],
            null, null, new ItemMetadataHousing(0, 0, 0, default, true, 3), null);
    }

    [TearDown]
    public void TearDown() {
        if (!mysql || Connection == null) {
            return;
        }
        hook.Before = null;
        using var context = new Ms2Context(plain);
        List<long> ids = context.Item.Where(item => item.OwnerId == owner || item.OwnerId == owner + 1).Select(item => item.Id).ToList();
        context.Maid.Where(maid => ids.Contains(maid.Id) || maid.OwnerId == owner).ExecuteDelete();
        context.UgcMapCube.Where(cube => plots.Contains(cube.UgcMapId)).ExecuteDelete();
        context.UgcMap.Where(map => plots.Contains(map.Id)).ExecuteDelete();
        context.Item.Where(item => item.OwnerId == owner || item.OwnerId == owner + 1).ExecuteDelete();
    }

    private static PlotCube Cube(int x) =>
        new(TestGameSession.Metadata(CubeItemId)) { Position = new Vector3B(x, 2, 3), Type = PlotCube.CubeType.Construction };

    private PlotInfo Info() => Info(plotId, Number);

    private static PlotInfo Info(long id, int number) {
        return new PlotInfo(new UgcMapGroup(number, 0, 0, new UgcMapGroup.Cost(0, 0, 0), new UgcMapGroup.Cost(0, 0, 0),
            new UgcMapGroup.Limits(0, 0, 0, 0, 0, 0))) {
            Id = id, MapId = MapId, Number = number,
        };
    }

    // The World server's expiry check on this storage, with no channel to tell; the next check is queued, not run.
    private void Check() {
        var world = (WorldServer) RuntimeHelpers.GetUninitializedObject(typeof(WorldServer));
        void Set(string name, object value) => typeof(WorldServer).GetField(name, Any)!.SetValue(world, value);
        Set("gameStorage", storage);
        Set("channelClients", new ChannelClientLookup());
        Set("serverTableMetadata", test.ServerTableMetadata);
        Set("itemMetadata", items);
        Set("scheduler", new EventQueue(Serilog.Log.Logger));
        Set("logger", Serilog.Log.ForContext<WorldServer>());
        world.FieldPlotExpiryCheck();
    }

    private (long Owner, int Cubes) Plot() => Plot(plotId);

    private (long Owner, int Cubes) Plot(long id) {
        using var check = new Ms2Context(plain);
        return (check.UgcMap.AsNoTracking().Single(map => map.Id == id).OwnerId, check.UgcMapCube.Count(cube => cube.UgcMapId == id));
    }

    // The owner's furnishing rows, by amount.
    private int[] Stored() => Stored(owner);

    private int[] Stored(long of) {
        using var check = new Ms2Context(plain);
        return check.Item.AsNoTracking().Where(item => item.OwnerId == of && item.Group == ItemGroup.Furnishing && item.ItemId == CubeItemId)
            .Select(item => item.Amount).AsEnumerable().Order().ToArray();
    }

    private void RequireMySql() {
        if (!mysql) {
            Assert.Ignore("needs another connection's write while the check runs");
        }
    }

    // The cubes come back as a new row (stacked as the storage stacks them); the stack the owner had stays 4.
    [Test]
    public void Expired_CubesComeBackAsNewRows() {
        Check();

        Assert.Multiple(() => {
            Assert.That(Plot(), Is.EqualTo((0L, 0)));
            Assert.That(Stored(), Is.EqualTo(new[] { 2, 4 }));
        });
    }

    // The owner extends the plot after the check listed it: the plot stays the owner's with its cubes.
    [Test]
    public void ExtendedMeanwhile_IsKept() {
        RequireMySql();
        int reads = 0;
        bool listed = false;
        hook.Before = command => {
            // The plot's next read after the list: the plot is extended in between.
            if (listed || !command.CommandText.Contains("FROM `ugcmap`") || ++reads < 2) {
                return;
            }
            listed = true;
            using var other = new Ms2Context(plain);
            other.UgcMap.Where(map => map.Id == plotId).ExecuteUpdate(update => update.SetProperty(map => map.ExpiryTime, expiry.AddDays(30)));
        };

        Check();
        hook.Before = null;

        Assert.Multiple(() => {
            Assert.That(listed, Is.True);
            Assert.That(Plot(), Is.EqualTo((owner, 2)));
            Assert.That(Stored(), Is.EqualTo(new[] { 4 }));
        });
    }

    // A cube is picked up while the cubes are removed: nothing is written (the picked-up cube is not credited twice),
    // and the next check takes the plot with the cube left.
    [Test]
    public void CubeGoneMeanwhile_WritesNothing_ThenNextCheckTakesIt() {
        RequireMySql();
        bool picked = false;
        hook.Before = command => {
            if (picked || !command.CommandText.Contains("DELETE FROM `ugcmap-cube`")) {
                return;
            }
            picked = true;
            using var other = new Ms2Context(plain);
            other.UgcMapCube.Where(cube => cube.Id == cubeIds[1]).ExecuteDelete();
        };

        Check();
        hook.Before = null;

        Assert.Multiple(() => {
            Assert.That(picked, Is.True);
            Assert.That(Plot(), Is.EqualTo((owner, 1)));
            Assert.That(Stored(), Is.EqualTo(new[] { 4 }));
        });

        Check();
        Assert.Multiple(() => {
            Assert.That(Plot(), Is.EqualTo((0L, 0)));
            Assert.That(Stored(), Is.EqualTo(new[] { 1, 4 }));
        });
    }

    // The furnishing write fails: the plot keeps its owner and cubes (they used to be gone, with nothing given back).
    [Test]
    public void ItemsNotWritten_WritesNothing() {
        RequireMySql();
        hook.Before = command => {
            if (command.CommandText.Contains("INSERT INTO `item`")) {
                throw new InvalidOperationException("write refused");
            }
        };

        Check();
        hook.Before = null;

        Assert.Multiple(() => {
            Assert.That(Plot(), Is.EqualTo((owner, 2)));
            Assert.That(Stored(), Is.EqualTo(new[] { 4 }));
        });
    }

    // Two plots in one check, the first failing (a cube picked up meanwhile): the second is still taken back, and
    // none of the first's changes are written with it (they stayed pending on the check's request and were written,
    // or failed, with the next plot's save).
    [Test]
    public void FirstPlotFails_SecondIsTakenAlone() {
        RequireMySql();
        long otherOwner = owner + 1;
        long other = AddPlot(otherOwner, OtherNumber);
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.CreateCube(Info(other, OtherNumber), Cube(5)), Is.Not.Null);
        }
        bool picked = false;
        hook.Before = command => {
            if (picked || !command.CommandText.Contains("DELETE FROM `ugcmap-cube`")) {
                return;
            }
            picked = true;
            using var meanwhile = new Ms2Context(plain);
            meanwhile.UgcMapCube.Where(cube => cube.Id == cubeIds[1]).ExecuteDelete();
        };

        Check();
        hook.Before = null;

        Assert.Multiple(() => {
            Assert.That(picked, Is.True);
            Assert.That(Plot(), Is.EqualTo((owner, 1)));
            Assert.That(Stored(), Is.EqualTo(new[] { 4 }));
            Assert.That(Plot(other), Is.EqualTo((0L, 0)));
            Assert.That(Stored(otherOwner), Is.EqualTo(new[] { 1 }));
        });
    }

    // A maid cube's maid moves to the maid item's new row, keeping its state, as on a pickup (it used to be deleted).
    [Test]
    public void MaidCube_MaidMovesToItsItem() {
        long cubeId;
        using (GameStorage.Request db = storage.Context()) {
            var maidCube = new PlotCube(MaidMetadata()) { Position = new Vector3B(4, 2, 3), Type = PlotCube.CubeType.Construction };
            cubeId = (db.CreateCube(Info(), maidCube) ?? throw new InvalidOperationException("cube row not created")).Id;
            Assert.That(db.CreateMaid(new Maid {
                Uid = cubeId, OwnerId = owner, ItemId = MaidItemId, MaidId = 3, Mood = 77, CreationTime = 1700000000, ExpiryTime = 1702592000,
            }), Is.Not.Null);
            // Item rows of another account, so that the maid item's new uid is not the cube's (the in-memory store
            // numbers each table from 1).
            for (int i = 0; i < 4; i++) {
                Assert.That(db.CreateItem(owner + 1, new Item(TestGameSession.Metadata(CubeItemId))), Is.Not.Null);
            }
        }

        Check();

        using var check = new Ms2Context(plain);
        long itemUid = check.Item.AsNoTracking().Single(item => item.OwnerId == owner && item.ItemId == MaidItemId).Id;
        Assert.Multiple(() => {
            Assert.That(Plot(), Is.EqualTo((0L, 0)));
            Assert.That(itemUid, Is.Not.EqualTo(cubeId));
            Assert.That(check.Maid.AsNoTracking().Any(maid => maid.Id == cubeId), Is.False);
            Assert.That(check.Maid.AsNoTracking().SingleOrDefault(maid => maid.Id == itemUid)?.Mood, Is.EqualTo(77));
        });
    }

    private sealed class CommandHook : DbCommandInterceptor {
        public Action<DbCommand>? Before;

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result) {
            Before?.Invoke(command);
            return result;
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
                                                                          InterceptionResult<DbDataReader> result) {
            Before?.Invoke(command);
            return result;
        }
    }
}
