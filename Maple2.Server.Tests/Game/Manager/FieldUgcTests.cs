using System;
using System.Linq;
using Maple2.Model.Common;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// FieldManager's UGC part: a cube place with nothing held, another item held, or no housing data is refused (the
// first two with an answer, the last without one), and a plot update of another map or an unknown plot changes nothing
// while one of this field's plots takes the new owner, name, expiry and map.
public class FieldUgcTests {
    private const int CubeItem = 50400001;
    private TestGameSession test = null!;
    private GameSession session => test.Session;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        test.Register(TestGameSession.Metadata(CubeItem));
        test.EnterHome();
        test.TakeSent();
    }

    private PlotCube Cube() {
        session.ItemMetadata.TryGet(CubeItem, out ItemMetadata? metadata);
        return new PlotCube(metadata!) { Type = PlotCube.CubeType.Default };
    }

    private string[] Sent() => test.TakeSent().Select(Convert.ToHexString).ToArray();

    [Test]
    public void Place_WithNothingHeld_IsRefusedWithAnAnswer() {
        session.HeldCube = null;

        session.Field!.PlaceCube(session, Cube(), new Vector3B(0, 0, 1), 0);

        Assert.That(Sent(), Is.EqualTo(new[] {
            Convert.ToHexString(CubePacket.EditError(CubePacket.Edit.Place, UgcMapError.s_ugcmap_null_cube_item_info, session.Player.ObjectId).ToArray()),
        }));
    }

    [Test]
    public void Place_OfAnotherItemThanHeld_IsRefusedWithAnAnswer() {
        test.Register(TestGameSession.Metadata(CubeItem + 1));
        session.ItemMetadata.TryGet(CubeItem + 1, out ItemMetadata? other);
        session.HeldCube = new PlotCube(other!) { Type = PlotCube.CubeType.Default };

        session.Field!.PlaceCube(session, Cube(), new Vector3B(0, 0, 1), 0);

        Assert.That(Sent(), Has.Length.EqualTo(1));
    }

    [Test]
    public void Place_OfAnUnknownItem_IsDroppedSilently() {
        PlotCube cube = Cube();
        typeof(HeldCube).GetProperty(nameof(HeldCube.ItemId))!.SetValue(cube, CubeItem + 99);

        session.Field!.PlaceCube(session, cube, new Vector3B(0, 0, 1), 0);

        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void Place_OfAnItemWithoutHousingData_IsDroppedSilently() {
        session.HeldCube = Cube();

        session.Field!.PlaceCube(session, Cube(), new Vector3B(0, 0, 1), 0);

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(session.Player.Value.Home.Indoor.Name, Is.Not.Null);
    }

    private static PlotInfo Info(int mapId, int number, long owner, string name, long expiry) {
        return new PlotInfo(new UgcMapGroup(number, 0, 0, new UgcMapGroup.Cost(0, 0, 0), new UgcMapGroup.Cost(0, 0, 0),
            new UgcMapGroup.Limits(0, 0, 0, 0, 0, 0))) { Number = number, MapId = mapId, OwnerId = owner, Name = name, ExpiryTime = expiry };
    }

    [Test]
    public void UpdatePlotInfo_OfAnotherMap_ChangesNothing() {
        Plot plot = session.Field!.Plots.Values.Single();

        Assert.That(session.Field.UpdatePlotInfo(Info(session.Field.MapId + 1, plot.Number, 5, "x", 9)), Is.False);
        Assert.That(plot.OwnerId, Is.Not.EqualTo(5));
    }

    [Test]
    public void UpdatePlotInfo_OfAnUnknownPlot_ChangesNothing() {
        Plot plot = session.Field!.Plots.Values.Single();

        Assert.That(session.Field.UpdatePlotInfo(Info(session.Field.MapId, plot.Number + 5, 5, "x", 9)), Is.False);
        Assert.That(session.Field.Plots, Has.Count.EqualTo(1));
    }

    [Test]
    public void UpdatePlotInfo_OfAPlot_TakesOwnerNameAndExpiry() {
        Plot plot = session.Field!.Plots.Values.Single();

        Assert.That(session.Field.UpdatePlotInfo(Info(session.Field.MapId, plot.Number, 5, "New", 99)), Is.True);
        Assert.That((plot.OwnerId, plot.Name, plot.ExpiryTime), Is.EqualTo((5L, "New", 99L)));
    }

    [Test]
    public void UpdatePlotInfo_WithAPlotObject_ReplacesTheField() {
        Plot old = session.Field!.Plots.Values.Single();
        var replacement = new Plot(old.Metadata) { Number = old.Number, MapId = old.MapId, OwnerId = 8 };

        Assert.That(session.Field.UpdatePlotInfo(replacement), Is.True);
        Assert.That(session.Field.Plots[old.Number], Is.SameAs(replacement));
    }
}
