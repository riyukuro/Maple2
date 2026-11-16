using System;
using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// Applying a layout to a plot: the area and height become the home's (the planner's own when the plot is in a planner
// mode); a blueprint also sets the background, lighting and camera of a plain home, and a planner mode only shows them
// without keeping them.
public class ApplyLayoutTests {
    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private Plot plot = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        plot = test.EnterHome();
        test.TakeSent();
    }

    private static HomeLayout Layout() => new(1, "layout", 12, 7, DateTimeOffset.UtcNow, new List<PlotCube>()) {
        Background = HomeBackground.MoonlitForest,
        Lighting = HomeLighting.Warm,
        Camera = HomeCamera.TopView,
    };

    [Test]
    public void Layout_SetsTheAreaAndHeight_NotTheLook() {
        Home home = session.Player.Value.Home;

        session.Housing.ApplyLayout(plot, Layout());

        Assert.That((home.Area, home.Height), Is.EqualTo(((byte) 12, (byte) 7)));
        Assert.That((home.Background, home.Lighting, home.Camera), Is.EqualTo((HomeBackground.Basic, HomeLighting.Basic, HomeCamera.QuarterView)));
    }

    [Test]
    public void Blueprint_AlsoSetsTheLook() {
        Home home = session.Player.Value.Home;

        session.Housing.ApplyLayout(plot, Layout(), isBlueprint: true);

        Assert.That((home.Area, home.Height), Is.EqualTo(((byte) 12, (byte) 7)));
        Assert.That((home.Background, home.Lighting, home.Camera), Is.EqualTo((HomeBackground.MoonlitForest, HomeLighting.Warm, HomeCamera.TopView)));
    }

    [Test]
    public void PlannerPlot_SetsThePlannersAreaOnly_AndKeepsTheLookOff() {
        Home home = session.Player.Value.Home;
        byte area = home.Area;
        byte height = home.Height;
        plot.SetPlannerMode(PlotMode.DecorPlanner);

        session.Housing.ApplyLayout(plot, Layout(), isBlueprint: true);

        Assert.That((home.PlannerArea, home.PlannerHeight), Is.EqualTo(((byte) 12, (byte) 7)));
        Assert.That((home.Area, home.Height), Is.EqualTo((area, height)));
        Assert.That((home.Background, home.Lighting, home.Camera), Is.EqualTo((HomeBackground.Basic, HomeLighting.Basic, HomeCamera.QuarterView)));
    }
}
