using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// RequestCube 38 / 45 (shrink the area / the height) take cubes off the plot only when the size changed. At the
// minimum height (3) a cube of the top layer (z 3) is in bounds; the decrease kept the height and still took it off.
// FunctionCube 5 on furniture or a switch is taken only within reach of the character (the client sends it from the
// cube's action key, functioncubeobject.cpp:615); a use from across the map toggled it. A skill hit reaches farther,
// and getting up is not checked.
public class HomeShrinkAndFurnitureReachTests {
    private const int CubeItemId = 50200001;

    private TestGameSession test = null!;

    private (GameSession Session, Plot Plot) StartInHome() {
        test = new TestGameSession();
        test.SetTable(new UgcHousingPointRewardTable(new Dictionary<int, UgcHousingPointRewardTable.Entry>()));
        test.SetTable(new MasteryUgcHousingTable(new Dictionary<int, MasteryUgcHousingTable.Entry> { [1] = new(1, 0, 0) }));
        test.Register(TestGameSession.Metadata(CubeItemId));
        GameSession session = test.Start();
        session.Housing.InitNewHome("home", null);
        Plot plot = test.EnterHome();
        // The session's player has no field of its own here, so it is not moved to the safe position.
        session.Field!.Players.Clear();
        typeof(GameSession).GetField("<SeatRelease>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(session, new SeatReleaseEcho()); // the uninitialized session skips its initializers
        test.TakeSent();
        return (session, plot);
    }

    private void SendCube(GameSession session, byte command) {
        new RequestCubeHandler { TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(command)));
    }

    private static PlotCube Cube(Plot plot, Vector3B position) {
        var cube = new PlotCube(TestGameSession.Metadata(CubeItemId)) { Position = position, Type = PlotCube.CubeType.Construction };
        plot.Cubes[position] = cube;
        return cube;
    }

    [Test]
    public void DecreaseHeight_AtTheMinimum_KeepsTheTopLayer() {
        (GameSession session, Plot plot) = StartInHome();
        session.Player.Value.Home.SetHeight(Constant.MinHomeHeight);
        var top = new Vector3B(-1, -1, (int) Constant.MinHomeHeight);
        Cube(plot, top);

        SendCube(session, 45);

        Assert.Multiple(() => {
            Assert.That(session.Player.Value.Home.Height, Is.EqualTo(Constant.MinHomeHeight));
            Assert.That(plot.Cubes.Keys, Is.EqualTo(new[] { top }));
        });
    }

    [Test]
    public void DecreaseHeight_AboveTheMinimum_TakesOffOnlyTheCubesAboveTheNewHeight() {
        (GameSession session, Plot plot) = StartInHome();
        session.Player.Value.Home.SetHeight(5);
        var kept = new Vector3B(-1, -1, 4);
        Cube(plot, kept);
        Cube(plot, new Vector3B(-2, -1, 5));

        SendCube(session, 45);

        Assert.Multiple(() => {
            Assert.That(session.Player.Value.Home.Height, Is.EqualTo(4));
            Assert.That(plot.Cubes.Keys, Is.EqualTo(new[] { kept }));
        });
    }

    [Test]
    public void DecreaseHeight_SaveFails_KeepsTheHeightAndTheCubes() {
        (GameSession session, Plot plot) = StartInHome();
        session.Player.Value.Home.SetHeight(5);
        var top = new Vector3B(-1, -1, 5);
        Cube(plot, top);
        test.FailSave = _ => true;

        SendCube(session, 45);
        test.FailSave = null;

        Assert.Multiple(() => {
            Assert.That(session.Player.Value.Home.Height, Is.EqualTo(5));
            Assert.That(plot.Cubes.Keys, Is.EqualTo(new[] { top }));
        });
    }

    [Test]
    public void DecreaseHeight_InThePlanner_ShrinksThePlannerOnly() {
        (GameSession session, Plot plot) = StartInHome();
        session.Player.Value.Home.SetHeight(5);
        session.Player.Value.Home.EnterPlanner(PlotMode.DecorPlanner);
        var kept = new Vector3B(-1, -1, 4);
        Cube(plot, kept);
        Cube(plot, new Vector3B(-2, -1, 5));

        SendCube(session, 45);

        Assert.Multiple(() => {
            Assert.That(session.Player.Value.Home.PlannerHeight, Is.EqualTo(4));
            Assert.That(session.Player.Value.Home.Height, Is.EqualTo(5));
            Assert.That(plot.Cubes.Keys, Is.EqualTo(new[] { kept }));
        });
    }

    [Test]
    public void DecreaseArea_AtTheMinimum_KeepsTheCubes() {
        (GameSession session, Plot plot) = StartInHome();
        session.Player.Value.Home.SetArea(Constant.MinHomeArea);
        var edge = new Vector3B(1 - Constant.MinHomeArea, 0, 1);
        Cube(plot, edge);

        SendCube(session, 38);

        Assert.Multiple(() => {
            Assert.That(session.Player.Value.Home.Area, Is.EqualTo(Constant.MinHomeArea));
            Assert.That(plot.Cubes.Keys, Is.EqualTo(new[] { edge }));
        });
    }

    private void Use(GameSession session, FieldFunctionInteract cube, byte via = 1) {
        new FunctionCubeHandler {
            FunctionCubeMetadataStorage = session.FunctionCubeMetadata,
            ServerTableMetadataStorage = test.ServerTableMetadata,
        }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(5);
            writer.WriteInt(1);
            writer.WriteUnicodeString(cube.InteractCube.Id);
            writer.WriteByte(via);
        }));
    }

    [TestCase(InteractCubeControlType.None, 0f, true, TestName = "Furniture_InReach_TakenIntoUse")]
    [TestCase(InteractCubeControlType.None, InteractRules.OtherReach + InteractRules.ReachSlack + 500f, false, TestName = "Furniture_AcrossTheMap_NotTaken")]
    [TestCase(InteractCubeControlType.Switch, InteractRules.OtherReach + InteractRules.ReachSlack + 500f, false, TestName = "Switch_AcrossTheMap_NotTaken")]
    public void Use_TakenOnlyInReach(InteractCubeControlType control, float distance, bool taken) {
        (GameSession session, _) = StartInHome();
        (InteractCube interact, FieldFunctionInteract cube) = AddCube(session, control, distance);

        Use(session, cube);

        Assert.That(interact.State, Is.EqualTo(taken ? InteractCubeState.InUse : InteractCubeState.Available));
    }

    private (InteractCube, FieldFunctionInteract) AddCube(GameSession session, InteractCubeControlType control, float distance) {
        var interact = new InteractCube(new Vector3B(0, 0, 0), new FunctionCubeMetadata(CubeItemId, 0, ConfigurableCubeType.None,
            InteractCubeState.Available, control, [], 0, null));
        FieldFunctionInteract cube = test.AddFunctionCube(interact);
        cube.Position = session.Player.Position + new Vector3(distance, 0, 0);
        return (interact, cube);
    }

    // A skill hit (flag 2) reaches as far as a skill's area: within SkillHitReach, not beyond.
    [TestCase(InteractRules.OtherReach + InteractRules.ReachSlack + 500f, true, TestName = "Switch_SkillHitBeyondTheActionKeyReach_Taken")]
    [TestCase(InteractRules.SkillHitReach + InteractRules.ReachSlack + 500f, false, TestName = "Switch_SkillHitAcrossTheMap_NotTaken")]
    public void SkillHit_TakenWithinTheSkillReach(float distance, bool taken) {
        (GameSession session, _) = StartInHome();
        (InteractCube interact, FieldFunctionInteract cube) = AddCube(session, InteractCubeControlType.Switch, distance);

        Use(session, cube, via: 2);

        Assert.That(interact.State, Is.EqualTo(taken ? InteractCubeState.InUse : InteractCubeState.Available));
    }

    // A seated player moved away (a home shrink's safe move) still gets up: the seat is free for the others.
    [Test]
    public void Furniture_InUse_ReleasedFromAfar() {
        (GameSession session, _) = StartInHome();
        (InteractCube interact, FieldFunctionInteract cube) = AddCube(session, InteractCubeControlType.None, 0f);
        Use(session, cube);
        Assert.That(interact.State, Is.EqualTo(InteractCubeState.InUse));
        cube.Position = session.Player.Position + new Vector3(InteractRules.OtherReach + InteractRules.ReachSlack + 500f, 0, 0);

        Use(session, cube);

        Assert.Multiple(() => {
            Assert.That(interact.State, Is.EqualTo(InteractCubeState.Available));
            Assert.That(interact.InteractingCharacterId, Is.EqualTo(0));
        });
    }
}
