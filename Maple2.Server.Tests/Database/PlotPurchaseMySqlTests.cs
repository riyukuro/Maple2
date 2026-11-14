using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Caching;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;
using Maple2.Server.Tests.Game;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Database;

// A plot buy or extension on MySQL: the cost and the plot row are one transaction (the purchase save), which the
// in-memory store cannot show. A buy refused after the wallet was written in it (the row is owned meanwhile) leaves
// the stored wallet as it was; a buy or extension that is stored takes the cost from the stored wallet at once.
// Runs only when MS2_TEST_GAME_DB holds the connection string of a scratch MySQL game database. The map is made up.
public class PlotPurchaseMySqlTests {
    private static readonly string? Connection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");
    private const int MapId = 99000102, PlotNumber = 7, Mesos = 90000001;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private DbContextOptions plain = null!;
    private IDisposable? held;
    private Plot plot = null!;

    [SetUp]
    public void SetUp() {
        if (Connection == null) {
            Assert.Ignore("the purchase save's rollback needs a real database: set MS2_TEST_GAME_DB");
        }
        ServerVersion version = ServerVersion.AutoDetect(Connection);
        plain = new DbContextOptionsBuilder().UseMySql(Connection, version).Options;
        test = new TestGameSession(new DbContextOptionsBuilder().UseMySql(Connection, version).Options);
        var metadata = new UgcMapGroup(PlotNumber, 0, 0, new UgcMapGroup.Cost(1000, Mesos, 30), new UgcMapGroup.Cost(400, Mesos, 30),
            new UgcMapGroup.Limits(0, 0, 0, 0, 0, 0));
        var ugcMaps = (LRUCache<int, UgcMapMetadata>) typeof(MapMetadataStorage)
            .GetField("UgcCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(test.MapMetadata)!;
        ugcMaps.AddReplace(MapId, new UgcMapMetadata(MapId, new Dictionary<int, UgcMapGroup> { [PlotNumber] = metadata }));
        using (var context = new Ms2Context(plain)) {
            context.UgcMap.Where(row => row.MapId == MapId).ExecuteDelete();
            var model = new Maple2.Database.Model.UgcMap { MapId = MapId, Number = PlotNumber, Indoor = false, ExpiryTime = DateTimeOffset.UtcNow.AddDays(-1) };
            context.UgcMap.Add(model);
            context.SaveChanges();
            plot = new Plot(metadata) { Id = model.Id, MapId = MapId, Number = PlotNumber };
        }
        // As a session that took the account lock at its entry (GameSession.EnterServer).
        held = GameStorage.HoldOwners(test.AccountId, test.CharacterId);
        test.Start();
        session.Housing.InitNewHome("home", null);
        test.EnterMap(TestGameSession.Map(MapId));
        session.Field!.Plots[PlotNumber] = plot;
        session.Currency.Meso = 5000;
        Assume.That(session.SavePurchase(), Is.True, "the wallet stored before the buy");
        test.TakeSent();
    }

    [TearDown]
    public void TearDown() {
        held?.Dispose();
        held = null;
        if (Connection == null) {
            return;
        }
        using var context = new Ms2Context(plain);
        context.UgcMap.Where(row => row.MapId == MapId).ExecuteDelete();
    }

    private (long Meso, long Owner, DateTimeOffset Expiry) Stored() {
        using var context = new Ms2Context(plain);
        long meso = context.Character.AsNoTracking().Single(row => row.Id == test.CharacterId).Currency.Meso;
        Maple2.Database.Model.UgcMap row = context.UgcMap.AsNoTracking().Single(map => map.Id == plot.Id);
        return (meso, row.OwnerId, row.ExpiryTime);
    }

    [Test]
    public void Buy_StoresTheCostWithThePlot() {
        Assert.That(session.Housing.BuyPlot(PlotNumber), Is.True);

        (long meso, long owner, _) = Stored();
        Assert.Multiple(() => {
            Assert.That(meso, Is.EqualTo(4000));
            Assert.That(owner, Is.EqualTo(test.AccountId));
        });
    }

    // The row was bought on another channel: the wallet written in the purchase's transaction is rolled back with it.
    [Test]
    public void Buy_OwnedMeanwhile_StoresNothing() {
        using (var context = new Ms2Context(plain)) {
            context.UgcMap.Where(row => row.Id == plot.Id).ExecuteUpdate(update => update.SetProperty(row => row.OwnerId, 4242L));
        }

        Assert.That(session.Housing.BuyPlot(PlotNumber), Is.False);

        (long meso, long owner, _) = Stored();
        Assert.Multiple(() => {
            Assert.That(session.Currency.Meso, Is.EqualTo(5000));
            Assert.That(meso, Is.EqualTo(5000));
            Assert.That(owner, Is.EqualTo(4242));
        });
    }

    [Test]
    public void Extend_StoresTheCostWithTheExpiry() {
        Assume.That(session.Housing.BuyPlot(PlotNumber), Is.True);
        DateTimeOffset bought = Stored().Expiry;
        // Within 30 days of the expiry, as an extension needs.
        using (var context = new Ms2Context(plain)) {
            context.UgcMap.Where(row => row.Id == plot.Id).ExecuteUpdate(update => update.SetProperty(row => row.ExpiryTime, DateTimeOffset.UtcNow.AddDays(10)));
        }
        session.Player.Value.Home.Outdoor!.ExpiryTime = DateTimeOffset.UtcNow.AddDays(10).ToUnixTimeSeconds();
        Assume.That(bought, Is.GreaterThan(DateTimeOffset.UtcNow.AddDays(20)));

        session.Housing.ExtendPlot();

        (long meso, _, DateTimeOffset expiry) = Stored();
        Assert.Multiple(() => {
            Assert.That(meso, Is.EqualTo(3600));
            Assert.That(expiry, Is.GreaterThan(DateTimeOffset.UtcNow.AddDays(39)));
        });
    }
}
