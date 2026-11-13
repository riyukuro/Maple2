using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using Caching;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Tests.Game;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Maple2.Server.Tests.Database;

// Buying, extending and forfeiting an outdoor plot: null on any failure with nothing written (the caller refunds the
// cost), and a second buyer of the same plot gets null. Each test runs on the in-memory store, and on MySQL when
// MS2_TEST_GAME_DB holds the connection string of a scratch game database; the races and the failed deletes need
// the real database (another connection changes the row, or a delete fails, while the call runs). The map id is made up.
[TestFixture(false)]
[TestFixture(true)]
public class PlotPurchaseStorageTests(bool mysql) {
    private static readonly string? Connection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");
    private const int MapId = 99000101;
    private const int Number = 7;
    private const int CubeItemId = 50000101;
    private static readonly TimeSpan Days = TimeSpan.FromDays(15);

    private TestGameSession test = null!;
    private GameStorage storage = null!;
    private DbContextOptions plain = null!; // the other connection
    private readonly CommandHook hook = new();
    private readonly ErrorLog errors = new();
    private long owner;
    private long plotId;
    private long[] cubeIds = [];

    [SetUp]
    public void SetUp() {
        if (mysql && Connection == null) {
            Assert.Ignore("the MySQL fixture needs a real database: set MS2_TEST_GAME_DB");
        }
        errors.Lines.Clear();
        hook.Before = null;
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(CubeItemId));
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
        var ugcMaps = (LRUCache<int, UgcMapMetadata>) typeof(MapMetadataStorage)
            .GetField("UgcCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(test.MapMetadata)!;
        ugcMaps.AddReplace(MapId, new UgcMapMetadata(MapId, new Dictionary<int, UgcMapGroup> {
            [Number] = new(Number, 0, 0, cost, cost, new UgcMapGroup.Limits(0, 0, 0, 0, 0, 0)),
        }));
        object items = typeof(GameStorage).GetField("itemMetadata", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(test.Storage)!;
        storage = new GameStorage(options, (ItemMetadataStorage) items, test.MapMetadata, null!, null!, test.TableMetadata,
            test.ServerTableMetadata, errors, null!);

        // Account ids no account has: the rows are found, and removed, by the plot id.
        owner = 7_200_000_000_000 + Random.Shared.NextInt64(1_000_000_000) * 10;
        using var context = new Ms2Context(plain);
        var map = new Maple2.Database.Model.UgcMap {
            OwnerId = 0, MapId = MapId, Indoor = false, Number = Number, ExpiryTime = DateTimeOffset.UtcNow.AddDays(-1),
        };
        context.UgcMap.Add(map);
        context.SaveChanges();
        plotId = map.Id;

        // Two cubes the former owner left, one with a maid.
        using GameStorage.Request db = storage.Context();
        PlotCube first = db.CreateCube(Info(), Cube(1)) ?? throw new InvalidOperationException("cube row not created");
        PlotCube second = db.CreateCube(Info(), Cube(2)) ?? throw new InvalidOperationException("cube row not created");
        cubeIds = [first.Id, second.Id];
        Assert.That(db.CreateMaid(new Maid {
            Uid = first.Id, OwnerId = owner + 1, ItemId = CubeItemId, MaidId = 3, CreationTime = 1700000000, ExpiryTime = 1702592000,
            Profile = new MaidProfile { Name = "n" },
        }), Is.Not.Null);
    }

    [TearDown]
    public void TearDown() {
        if (!mysql || Connection == null) {
            return;
        }
        hook.Before = null;
        using var context = new Ms2Context(plain);
        context.Maid.Where(maid => cubeIds.Contains(maid.Id)).ExecuteDelete();
        context.UgcMapCube.Where(cube => cube.UgcMapId == plotId).ExecuteDelete();
        context.UgcMap.Where(map => map.Id == plotId).ExecuteDelete();
    }

    private static PlotCube Cube(int x) =>
        new(TestGameSession.Metadata(CubeItemId)) { Position = new Vector3B(x, 2, 3), Type = PlotCube.CubeType.Construction };

    private PlotInfo Info(long ownerId = 0) {
        var info = new PlotInfo(new UgcMapGroup(Number, 0, 0, new UgcMapGroup.Cost(0, 0, 0), new UgcMapGroup.Cost(0, 0, 0),
            new UgcMapGroup.Limits(0, 0, 0, 0, 0, 0))) {
            Id = plotId, MapId = MapId, Number = Number,
        };
        info.OwnerId = ownerId;
        return info;
    }

    private PlotInfo? Buy(long ownerId) {
        using GameStorage.Request db = storage.Context();
        return db.BuyPlot("buyer", ownerId, Info(), Days);
    }

    private PlotInfo? Extend(long ownerId) {
        using GameStorage.Request db = storage.Context();
        return db.ExtendPlot(Info(ownerId), Days);
    }

    private PlotInfo? Forfeit(long ownerId) {
        using GameStorage.Request db = storage.Context();
        return db.ForfeitPlot(ownerId, Info());
    }

    private (long Owner, DateTimeOffset Expiry, string Name) Row() {
        using var check = new Ms2Context(plain);
        Maple2.Database.Model.UgcMap row = check.UgcMap.AsNoTracking().Single(map => map.Id == plotId);
        return (row.OwnerId, row.ExpiryTime, row.Name);
    }

    private (int Cubes, int Maids) Rows() {
        using var check = new Ms2Context(plain);
        return (check.UgcMapCube.Count(cube => cube.UgcMapId == plotId), check.Maid.Count(maid => cubeIds.Contains(maid.Id)));
    }

    // A plot bought by owner, with the former owner's cubes gone.
    private void Bought() {
        Assert.That(Buy(owner), Is.Not.Null);
        using GameStorage.Request db = storage.Context();
        cubeIds = [.. cubeIds, db.CreateCube(Info(owner), Cube(3))!.Id];
    }

    private void RequireMySql() {
        if (!mysql) {
            Assert.Ignore("needs the MySQL fixture");
        }
    }

    [Test]
    public void BuyTakesThePlotAndDeletesTheOldCubesAndMaids() {
        PlotInfo? info = Buy(owner);

        Assert.Multiple(() => {
            Assert.That(info?.OwnerId, Is.EqualTo(owner));
            Assert.That(Row().Owner, Is.EqualTo(owner));
            Assert.That(Row().Name, Is.EqualTo("buyer"));
            Assert.That(Row().Expiry, Is.EqualTo(DateTimeOffset.UtcNow + Days).Within(TimeSpan.FromMinutes(1)));
            Assert.That(Rows(), Is.EqualTo((0, 0)));
            Assert.That(errors.Lines, Is.Empty);
        });
    }

    [Test]
    public void SecondBuyerGetsNull() {
        Bought();
        Assert.Multiple(() => {
            Assert.That(Buy(owner + 2), Is.Null);
            Assert.That(Row().Owner, Is.EqualTo(owner));
            Assert.That(Rows(), Is.EqualTo((1, 0)));
        });
    }

    // Another channel buys the plot, and places a cube, after this buy read it as free: this buy writes nothing, so
    // the other buyer's cube stays.
    [Test]
    public void BuyerThatRacedAnotherBuyerGetsNull() {
        RequireMySql();
        hook.Before = command => {
            if (command.CommandText.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
                || command.CommandText.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase)) {
                hook.Before = null;
                Assert.That(Buy(owner + 2), Is.Not.Null);
                using GameStorage.Request db = storage.Context();
                cubeIds = [.. cubeIds, db.CreateCube(Info(owner + 2), Cube(3))!.Id];
            }
        };
        PlotInfo? info = Buy(owner);

        Assert.Multiple(() => {
            Assert.That(info, Is.Null);
            Assert.That(Row().Owner, Is.EqualTo(owner + 2));
            Assert.That(Rows(), Is.EqualTo((1, 0)));
        });
    }

    // The cube delete fails after the plot row was claimed: nothing is written, the cubes and the maid stay.
    [Test]
    public void FailedBuyChangesNothing() {
        RequireMySql();
        hook.Before = command => {
            // The cube delete (the maid delete before it names the cube table only in its subquery).
            if (command.CommandText.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase) && command.CommandText.Contains("FROM `ugcmap-cube`")
                && !command.CommandText.Contains("EXISTS")) {
                throw new InvalidOperationException("delete refused");
            }
        };
        PlotInfo? info = Buy(owner);
        hook.Before = null;

        Assert.Multiple(() => {
            Assert.That(info, Is.Null);
            Assert.That(Row().Owner, Is.EqualTo(0));
            Assert.That(Row().Name, Is.Empty);
            Assert.That(Rows(), Is.EqualTo((2, 1)));
            Assert.That(errors.Lines, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void ExtendAddsTheDays() {
        Bought();
        DateTimeOffset expiry = Row().Expiry;
        Assert.Multiple(() => {
            Assert.That(Extend(owner), Is.Not.Null);
            Assert.That(Row().Expiry, Is.EqualTo(expiry + Days).Within(TimeSpan.FromSeconds(1)));
        });
    }

    [Test]
    public void ExtendOfAnotherOwnersPlotGetsNull() {
        Bought();
        DateTimeOffset expiry = Row().Expiry;
        Assert.Multiple(() => {
            Assert.That(Extend(owner + 2), Is.Null);
            Assert.That(Row().Expiry, Is.EqualTo(expiry).Within(TimeSpan.FromSeconds(1)));
        });
    }

    [Test]
    public void ExtendOfAnExpiredPlotGetsNull() {
        Assert.That(Extend(0), Is.Null);
    }

    // Two extends that read the same expiry: the second updates no row, so the plot is not extended once for two payments.
    [Test]
    public void ExtendThatRacedAnotherExtendGetsNull() {
        RequireMySql();
        Bought();
        DateTimeOffset expiry = Row().Expiry;
        hook.Before = command => {
            if (command.CommandText.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)) {
                hook.Before = null;
                Assert.That(Extend(owner), Is.Not.Null);
            }
        };
        PlotInfo? info = Extend(owner);

        Assert.Multiple(() => {
            Assert.That(info, Is.Null);
            Assert.That(Row().Expiry, Is.EqualTo(expiry + Days).Within(TimeSpan.FromSeconds(1)));
        });
    }

    [Test]
    public void ForfeitFreesThePlotAndDeletesItsCubes() {
        Bought();
        Assert.Multiple(() => {
            Assert.That(Forfeit(owner)?.OwnerId, Is.EqualTo(0));
            Assert.That(Row().Owner, Is.EqualTo(0));
            Assert.That(Rows(), Is.EqualTo((0, 0)));
        });
    }

    [Test]
    public void ForfeitOfAnExpiredPlotGetsNull() {
        using (var context = new Ms2Context(plain)) {
            context.UgcMap.Single(map => map.Id == plotId).OwnerId = owner;
            context.SaveChanges();
        }
        Assert.That(Forfeit(owner), Is.Null);
        Assert.That(Row().Owner, Is.EqualTo(owner));
    }

    // An extend saved between the forfeit's read and its update: the forfeit updates no row and deletes nothing, so the
    // extension just paid for is kept with the plot and its cubes.
    [Test]
    public void ForfeitThatRacedAnExtendGetsNull() {
        RequireMySql();
        Bought();
        DateTimeOffset expiry = Row().Expiry;
        hook.Before = command => {
            if (command.CommandText.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)) {
                hook.Before = null;
                Assert.That(Extend(owner), Is.Not.Null);
            }
        };
        PlotInfo? info = Forfeit(owner);

        Assert.Multiple(() => {
            Assert.That(info, Is.Null);
            Assert.That(Row().Owner, Is.EqualTo(owner));
            Assert.That(Row().Expiry, Is.EqualTo(expiry + Days).Within(TimeSpan.FromSeconds(1)));
            Assert.That(Rows(), Is.EqualTo((1, 0)));
        });
    }

    // Two forfeits that read the same row (two sessions of the owner): one frees the plot, the other gets null.
    [Test]
    public void ForfeitThatRacedAnotherForfeitGetsNull() {
        RequireMySql();
        Bought();
        hook.Before = command => {
            if (command.CommandText.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)) {
                hook.Before = null;
                Assert.That(Forfeit(owner), Is.Not.Null);
            }
        };
        PlotInfo? info = Forfeit(owner);

        Assert.Multiple(() => {
            Assert.That(info, Is.Null);
            Assert.That(Row().Owner, Is.EqualTo(0));
            Assert.That(Rows(), Is.EqualTo((0, 0)));
        });
    }

    // The maids go by a subquery on the plot's cubes, so a maid of another plot's cube stays.
    [Test]
    public void BuyKeepsTheMaidsOfOtherPlots() {
        RequireMySql();
        long other;
        using (var context = new Ms2Context(plain)) {
            var map = new Maple2.Database.Model.UgcMap {
                OwnerId = owner + 2, MapId = MapId, Indoor = false, Number = Number + 1, ExpiryTime = DateTimeOffset.UtcNow.AddDays(9),
            };
            context.UgcMap.Add(map);
            context.SaveChanges();
            other = map.Id;
        }
        try {
            using (GameStorage.Request db = storage.Context()) {
                PlotInfo otherInfo = Info(owner + 2);
                otherInfo.Id = other;
                PlotCube cube = db.CreateCube(otherInfo, Cube(4)) ?? throw new InvalidOperationException("cube row not created");
                cubeIds = [.. cubeIds, cube.Id];
                Assert.That(db.CreateMaid(new Maid {
                    Uid = cube.Id, OwnerId = owner + 2, ItemId = CubeItemId, MaidId = 3, CreationTime = 1700000000, ExpiryTime = 1702592000,
                    Profile = new MaidProfile { Name = "n" },
                }), Is.Not.Null);
            }
            Assert.That(Buy(owner), Is.Not.Null);
            using var check = new Ms2Context(plain);
            Assert.That(check.Maid.Count(maid => cubeIds.Contains(maid.Id)), Is.EqualTo(1));
        } finally {
            using var context = new Ms2Context(plain);
            context.Maid.Where(maid => cubeIds.Contains(maid.Id)).ExecuteDelete();
            context.UgcMapCube.Where(cube => cube.UgcMapId == other).ExecuteDelete();
            context.UgcMap.Where(map => map.Id == other).ExecuteDelete();
        }
    }

    [Test]
    public void FailedForfeitChangesNothing() {
        RequireMySql();
        Bought();
        hook.Before = command => {
            // The cube delete (the maid delete before it names the cube table only in its subquery).
            if (command.CommandText.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase) && command.CommandText.Contains("FROM `ugcmap-cube`")
                && !command.CommandText.Contains("EXISTS")) {
                throw new InvalidOperationException("delete refused");
            }
        };
        PlotInfo? info = Forfeit(owner);
        hook.Before = null;

        Assert.Multiple(() => {
            Assert.That(info, Is.Null);
            Assert.That(Row().Owner, Is.EqualTo(owner));
            Assert.That(Rows(), Is.EqualTo((1, 0)));
        });
    }

    private sealed class ErrorLog : ILogger<GameStorage> {
        public readonly List<string> Lines = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
            if (IsEnabled(logLevel)) {
                Lines.Add(formatter(state, exception) + (exception == null ? "" : ": " + exception.GetType().Name + " " + exception.Message));
            }
        }
    }

    // Runs Before ahead of each command of the call's own connections.
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
