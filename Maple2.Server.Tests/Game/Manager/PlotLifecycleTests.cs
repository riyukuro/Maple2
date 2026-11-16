using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Caching;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// The owner's outdoor plot through its life, on a real plot row, a field the game server finds, and the World plot
// updates: a buy takes its cost once, writes the owner and the expiry and tells the other channels, and a second buy
// of any plot is refused; an extension takes its cost once and moves the expiry by the extension's days, and cannot
// be repeated while more than thirty days are left; a forfeit frees the row, gives nothing and tells the other
// channels, and a second forfeit finds nothing to give up. The map, plot and costs are made up.
public class PlotLifecycleTests {
    private const int MapId = 2000902, PlotNumber = 7, Meso = 90000001;
    private const int ContractCost = 1000, ExtensionCost = 400;
    private static readonly TimeSpan ContractDays = TimeSpan.FromDays(30);

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private long rowId;

    private void Start(long owner = 0, TimeSpan? expiresIn = null) {
        test = new TestGameSession();
        test.Start();
        session.Housing.InitNewHome("home", null);
        var metadata = new UgcMapGroup(PlotNumber, 0, 0, new UgcMapGroup.Cost(ContractCost, Meso, 30), new UgcMapGroup.Cost(ExtensionCost, Meso, 30),
            new UgcMapGroup.Limits(0, 0, 0, 0, 0, 0));
        var ugcMaps = (LRUCache<int, UgcMapMetadata>) typeof(MapMetadataStorage)
            .GetField("UgcCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(test.MapMetadata)!;
        ugcMaps.AddReplace(MapId, new UgcMapMetadata(MapId, new Dictionary<int, UgcMapGroup> { [PlotNumber] = metadata }));
        using (var context = new Ms2Context(test.Options)) {
            var model = new Maple2.Database.Model.UgcMap {
                MapId = MapId, Number = PlotNumber, Indoor = false,
                OwnerId = owner == 0 ? 0 : session.AccountId,
                ExpiryTime = owner == 0 ? default : DateTimeOffset.UtcNow + (expiresIn ?? TimeSpan.FromDays(10)),
            };
            context.UgcMap.Add(model);
            context.SaveChanges();
            rowId = model.Id;
        }
        test.EnterMap(TestGameSession.Map(MapId));
        test.ServeField();
        var plot = new Plot(metadata) { Id = rowId, MapId = MapId, Number = PlotNumber };
        if (owner != 0) {
            plot.OwnerId = session.AccountId;
            plot.ExpiryTime = (DateTimeOffset.UtcNow + (expiresIn ?? TimeSpan.FromDays(10))).ToUnixTimeSeconds();
            session.Housing.SetPlot(plot);
        }
        session.Field!.Plots[PlotNumber] = plot;
        session.Currency.Meso = 5000;
        test.TakeSent();
    }

    private Maple2.Database.Model.UgcMap Row() {
        using var context = new Ms2Context(test.Options);
        return context.UgcMap.AsEnumerable().Single(row => row.Id == rowId);
    }

    private string[] Sent() => test.TakeSent().Select(Convert.ToHexString).ToArray();
    private static string Error(UgcMapError error) => Convert.ToHexString(CubePacket.Error(error).ToArray());

    [Test]
    public void Buy_TakesTheCostOnce_StoresTheOwner_AndTellsTheOtherChannels() {
        Start();

        Assert.That(session.Housing.BuyPlot(PlotNumber), Is.True);

        Assert.That(session.Currency.Meso, Is.EqualTo(5000 - ContractCost));
        Maple2.Database.Model.UgcMap row = Row();
        Assert.That(row.OwnerId, Is.EqualTo(session.AccountId));
        Assert.That(row.ExpiryTime, Is.GreaterThan(DateTimeOffset.UtcNow + ContractDays - TimeSpan.FromMinutes(5)));
        Assert.That(session.Player.Value.Home.Outdoor?.Number, Is.EqualTo(PlotNumber));
        FieldPlotRequest update = test.World.PlotUpdates.Single();
        Assert.That((update.MapId, update.PlotNumber), Is.EqualTo((MapId, PlotNumber)));
        Assert.That(update.UpdatePlot.Forfeit, Is.False);
    }

    [Test]
    public void Buy_AgainIsRefused_AndTakesNothing() {
        Start();
        session.Housing.BuyPlot(PlotNumber);
        test.TakeSent();

        Assert.That(session.Housing.BuyPlot(PlotNumber), Is.False);

        Assert.That(Sent(), Is.EqualTo(new[] { Error(UgcMapError.s_ugcmap_my_house) }));
        Assert.That(session.Currency.Meso, Is.EqualTo(5000 - ContractCost));
        Assert.That(test.World.PlotUpdates, Has.Count.EqualTo(1));
    }

    [Test]
    public void Extend_TakesTheCostOnce_AndMovesTheExpiry() {
        Start(owner: 1, expiresIn: TimeSpan.FromDays(10));
        DateTimeOffset before = Row().ExpiryTime;

        session.Housing.ExtendPlot();

        Assert.That(session.Currency.Meso, Is.EqualTo(5000 - ExtensionCost));
        Assert.That(Row().ExpiryTime - before, Is.EqualTo(TimeSpan.FromDays(30)).Within(TimeSpan.FromSeconds(1)));
        Assert.That(test.World.PlotUpdates.Single().UpdatePlot.Forfeit, Is.False);
    }

    [Test]
    public void Extend_AgainWithMoreThanThirtyDaysLeft_IsRefusedAndTakesNothing() {
        Start(owner: 1, expiresIn: TimeSpan.FromDays(10));
        session.Housing.ExtendPlot();
        test.TakeSent();
        DateTimeOffset after = Row().ExpiryTime;

        session.Housing.ExtendPlot();

        Assert.That(Sent(), Is.EqualTo(new[] { Error(UgcMapError.s_ugcmap_not_extension_date) }));
        Assert.That(session.Currency.Meso, Is.EqualTo(5000 - ExtensionCost));
        Assert.That(Row().ExpiryTime, Is.EqualTo(after));
    }

    [Test]
    public void Forfeit_FreesTheRow_GivesNothing_AndTellsTheOtherChannels() {
        Start(owner: 1);

        PlotInfo? plot = session.Housing.ForfeitPlot();

        Assert.That(plot, Is.Not.Null);
        Assert.That(Row().OwnerId, Is.EqualTo(0));
        Assert.That(session.Currency.Meso, Is.EqualTo(5000));
        Assert.That(session.Player.Value.Home.Outdoor, Is.Null);
        FieldPlotRequest update = test.World.PlotUpdates.Single();
        Assert.That((update.MapId, update.PlotNumber, update.UpdatePlot.AccountId, update.UpdatePlot.Forfeit),
            Is.EqualTo((MapId, PlotNumber, session.AccountId, true)));
    }

    [Test]
    public void Forfeit_Twice_FindsNothingToGiveUp() {
        Start(owner: 1);
        session.Housing.ForfeitPlot();
        test.TakeSent();

        Assert.That(session.Housing.ForfeitPlot(), Is.Null);

        Assert.That(Sent(), Is.EqualTo(new[] { Error(UgcMapError.s_ugcmap_dont_have_ownership) }));
        Assert.That(test.World.PlotUpdates, Has.Count.EqualTo(1));
    }
}
