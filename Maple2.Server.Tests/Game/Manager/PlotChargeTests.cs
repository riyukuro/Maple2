using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Database.Storage;
using Caching;
using Maple2.Database.Context;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// Buying and extending an outdoor plot take the contract or extension cost before the plot row is saved and give it
// back when the save fails: a buy whose row is gone or taken costs nothing and leaves the home's outdoor plot as it
// was, and an extension whose save fails costs nothing. Each case runs with a meso and a meret cost. A buy that
// succeeds is not run here: GameStorage.BuyPlot deletes the old cubes with a batch delete, which the in-memory store
// has not. The map, plot and costs are made up.
public class PlotChargeTests {
    private const int MapId = 2000901, PlotNumber = 7, Mesos = 90000001, Merets = 90000004;

    private TestGameSession test = null!;

    private static long Money(GameSession session, int currency) => currency == Mesos ? session.Currency.Meso : session.Currency.Meret;

    private (GameSession Session, Plot Plot) Start(int currency, bool row = true, long rowOwner = 0, DateTimeOffset expiry = default) {
        test = new TestGameSession();
        GameSession session = test.Start();
        session.Housing.InitNewHome("home", null);
        var cost = new UgcMapGroup.Cost(1000, currency, 30);
        var metadata = new UgcMapGroup(PlotNumber, 0, 0, cost, new UgcMapGroup.Cost(400, currency, 30), new UgcMapGroup.Limits(0, 0, 0, 0, 0, 0));
        // The storage turns a saved row into a plot through the map's UGC metadata.
        var ugcMaps = (LRUCache<int, UgcMapMetadata>) typeof(MapMetadataStorage)
            .GetField("UgcCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(test.MapMetadata)!;
        ugcMaps.AddReplace(MapId, new UgcMapMetadata(MapId, new Dictionary<int, UgcMapGroup> { [PlotNumber] = metadata }));
        long id = 0;
        if (row) {
            using var context = new Ms2Context(test.Options);
            var model = new Maple2.Database.Model.UgcMap { MapId = MapId, Number = PlotNumber, Indoor = false, OwnerId = rowOwner, ExpiryTime = expiry };
            context.UgcMap.Add(model);
            context.SaveChanges();
            id = model.Id;
        }
        test.EnterMap(TestGameSession.Map(MapId));
        var plot = new Plot(metadata) { Id = row ? id : 999_999, MapId = MapId, Number = PlotNumber };
        session.Field!.Plots[PlotNumber] = plot;
        session.Currency.Meso = 5000;
        session.Currency.Meret = 5000;
        test.TakeSent();
        return (session, plot);
    }

    [TestCase(Mesos)]
    [TestCase(Merets)]
    public void BuyPlot_WithoutMoney_ChargesNothing(int currency) {
        (GameSession session, _) = Start(currency);
        session.Currency.Meso = 999;
        session.Currency.Meret = 999;

        Assert.That(session.Housing.BuyPlot(PlotNumber), Is.False);
        Assert.That(Money(session, currency), Is.EqualTo(999));
    }

    // The row is taken (bought on another channel) though this field still shows the plot open: the cost taken
    // before the save is given back in the same currency, and the other currency is not touched.
    [TestCase(Mesos)]
    [TestCase(Merets)]
    public void BuyPlot_RowTakenElsewhere_RefundsTheCost(int currency) {
        (GameSession session, _) = Start(currency, rowOwner: 4242);

        Assert.That(session.Housing.BuyPlot(PlotNumber), Is.False);
        Assert.That(session.Currency.Meso, Is.EqualTo(5000));
        Assert.That(session.Currency.Meret, Is.EqualTo(5000));
        Assert.That(session.Player.Value.Home.Outdoor, Is.Null);
    }

    [TestCase(Mesos)]
    [TestCase(Merets)]
    public void BuyPlot_NoRow_RefundsTheCost(int currency) {
        (GameSession session, _) = Start(currency, row: false);

        Assert.That(session.Housing.BuyPlot(PlotNumber), Is.False);
        Assert.That(session.Currency.Meso, Is.EqualTo(5000));
        Assert.That(session.Currency.Meret, Is.EqualTo(5000));
        Assert.That(session.Player.Value.Home.Outdoor, Is.Null);
    }

    // The player's plot, 10 days before its expiry (an extension is open within 30 days of it). A failed save gives
    // the cost back and leaves the expiry; a save that works takes it once.
    [TestCase(Mesos)]
    [TestCase(Merets)]
    public void ExtendPlot_RefundsTheCost_WhenTheSaveFails(int currency) {
        DateTimeOffset expiry = DateTimeOffset.UtcNow.AddDays(10);
        (GameSession session, Plot plot) = Start(currency, rowOwner: 4242, expiry: expiry);
        plot.OwnerId = session.AccountId;
        plot.ExpiryTime = expiry.ToUnixTimeSeconds();
        session.Housing.SetPlot(plot);

        test.FailSave = context => context.ChangeTracker.Entries<Maple2.Database.Model.UgcMap>().Any();
        session.Housing.ExtendPlot();
        Assert.That(session.Currency.Meso, Is.EqualTo(5000));
        Assert.That(session.Currency.Meret, Is.EqualTo(5000));
        Assert.That(session.Player.Value.Home.Outdoor!.ExpiryTime, Is.EqualTo(expiry.ToUnixTimeSeconds()));

        test.FailSave = null;
        session.Housing.ExtendPlot();
        Assert.That(Money(session, currency), Is.EqualTo(4600));
        Assert.That(Money(session, currency == Mesos ? Merets : Mesos), Is.EqualTo(5000));
        Assert.That(session.Player.Value.Home.Outdoor!.ExpiryTime, Is.GreaterThan(expiry.AddDays(29).ToUnixTimeSeconds()));
    }
}
