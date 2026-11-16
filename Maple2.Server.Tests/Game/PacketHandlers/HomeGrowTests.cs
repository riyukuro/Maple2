using System.Collections.Generic;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// RequestCube 37 / 44 (grow the area / the height) in the owner's own home: one step up, stored with the home; in a
// planner mode only the planner's size grows and nothing is stored; at the largest size nothing changes; in
// another's home nothing changes.
public class HomeGrowTests {
    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private Plot plot = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.SetTable(new UgcHousingPointRewardTable(new Dictionary<int, UgcHousingPointRewardTable.Entry>()));
        test.SetTable(new MasteryUgcHousingTable(new Dictionary<int, MasteryUgcHousingTable.Entry> { [1] = new(1, 0, 0) }));
        test.Start();
        session.Housing.InitNewHome("home", null);
        plot = test.EnterHome();
        session.Field!.Players.Clear();
        test.TakeSent();
    }

    private void Send(byte command) {
        new RequestCubeHandler { TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(command)));
    }

    private Home Stored() {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetHome(session.AccountId)!;
    }

    [Test]
    public void IncreaseArea_GrowsByOne_AndIsStored() {
        Home home = session.Player.Value.Home;
        home.SetArea(10);
        byte area = home.Area;

        Send(37);

        Assert.That(home.Area, Is.EqualTo(area + 1));
        Assert.That(Stored().Area, Is.EqualTo(area + 1));
    }

    [Test]
    public void IncreaseHeight_GrowsByOne_AndIsStored() {
        Home home = session.Player.Value.Home;
        home.SetHeight(5);

        Send(44);

        Assert.That(home.Height, Is.EqualTo(6));
        Assert.That(Stored().Height, Is.EqualTo(6));
    }

    [Test]
    public void AtTheLargestSize_NothingChanges() {
        Home home = session.Player.Value.Home;
        home.SetArea(Constant.MaxHomeArea);
        home.SetHeight(Constant.MaxHomeHeight);

        Send(37);
        Send(44);

        Assert.That((home.Area, home.Height), Is.EqualTo((Constant.MaxHomeArea, Constant.MaxHomeHeight)));
    }

    [Test]
    public void InAPlannerMode_OnlyThePlannersSizeGrows() {
        Home home = session.Player.Value.Home;
        home.SetPlannerArea(8);
        home.SetPlannerHeight(5);
        byte area = home.Area;
        byte height = home.Height;
        session.Player.Value.Home.Indoor.PlotMode = Maple2.Model.Enum.PlotMode.DecorPlanner;

        Send(37);
        Send(44);

        Assert.That((home.PlannerArea, home.PlannerHeight), Is.EqualTo(((byte) 9, (byte) 6)));
        Assert.That((home.Area, home.Height), Is.EqualTo((area, height)));
    }

    [Test]
    public void InAnotherPlayersHome_NothingChanges() {
        Home home = session.Player.Value.Home;
        byte area = home.Area;
        byte height = home.Height;
        typeof(Home).GetField("<AccountId>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(home, session.AccountId + 1);

        Send(37);
        Send(44);

        Assert.That((home.Area, home.Height), Is.EqualTo((area, height)));
    }
}
