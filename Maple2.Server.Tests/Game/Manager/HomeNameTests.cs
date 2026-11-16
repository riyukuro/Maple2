using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// Naming the home: a blank name or one over the limit is refused with s_ugcmap_center and changes nothing; a name is
// stored with the indoor row; with an outdoor plot it is stored with the plot's row and the plot change goes to the
// other channels, and a plot row that is gone answers the database error.
public class HomeNameTests {
    private TestGameSession test = null!;
    private GameSession session => test.Session;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.SetTable(new UgcHousingPointRewardTable(new Dictionary<int, UgcHousingPointRewardTable.Entry>()));
        test.SetTable(new MasteryUgcHousingTable(new Dictionary<int, MasteryUgcHousingTable.Entry> { [1] = new(1, 0, 0) }));
        test.Start();
        session.Housing.InitNewHome("home", null);
        test.EnterHome();
        session.Field!.Players.Clear();
        test.TakeSent();
    }

    private string[] Sent() => test.TakeSent().Select(Convert.ToHexString).ToArray();
    private static string Error(UgcMapError error) => Convert.ToHexString(CubePacket.Error(error).ToArray());

    [TestCase("")]
    [TestCase("   ")]
    public void ABlankName_IsRefused(string name) {
        session.Housing.SetName(name);

        Assert.That(Sent(), Is.EqualTo(new[] { Error(UgcMapError.s_ugcmap_center) }));
        Assert.That(session.Player.Value.Home.Indoor.Name, Is.EqualTo("home"));
    }

    [Test]
    public void ANameOverTheLimit_IsRefused() {
        session.Housing.SetName(new string('a', Constant.HomeNameMaxLength + 1));

        Assert.That(Sent(), Is.EqualTo(new[] { Error(UgcMapError.s_ugcmap_center) }));
        Assert.That(session.Player.Value.Home.Indoor.Name, Is.EqualTo("home"));
    }

    [Test]
    public void AName_IsStoredWithTheIndoorRow() {
        session.Housing.SetName(new string('a', Constant.HomeNameMaxLength));

        Assert.That(session.Player.Value.Home.Indoor.Name, Is.EqualTo(new string('a', Constant.HomeNameMaxLength)));
        Assert.That(test.World.PlotUpdates, Is.Empty);
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.GetHome(session.AccountId)!.Indoor.Name, Is.EqualTo(new string('a', Constant.HomeNameMaxLength)));
    }

    [Test]
    public void WithAnOutdoorPlot_TheNameGoesToThePlotAndTheOtherChannels() {
        PlotInfo plot = session.Player.Value.Home.Indoor;
        session.Housing.SetPlot(plot);
        test.TakeSent();

        session.Housing.SetName("Outdoor");

        Assert.That(plot.Name, Is.EqualTo("Outdoor"));
        FieldPlotRequest update = test.World.PlotUpdates.Single();
        Assert.That((update.MapId, update.PlotNumber), Is.EqualTo((session.Field!.MapId, plot.Number)));
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.GetHome(session.AccountId)!.Indoor.Name, Is.EqualTo("Outdoor"));
    }

    [Test]
    public void WithAnOutdoorPlotWhoseRowIsGone_AnswersTheDatabaseError() {
        var gone = new PlotInfo(session.Player.Value.Home.Indoor.Metadata) { Id = 987654321, MapId = session.Field!.MapId };
        session.Housing.SetPlot(gone);
        test.TakeSent();

        session.Housing.SetName("Outdoor");

        Assert.That(Sent(), Is.EqualTo(new[] { Error(UgcMapError.s_ugcmap_db) }));
        Assert.That(test.World.PlotUpdates, Is.Empty);
    }
}
