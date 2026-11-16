using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Channel.Service;
using Maple2.Server.Game;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Service;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// The plot changes another channel sends (ChannelService.UpdateFieldPlot) as the field here shows them: a cube placed,
// replaced, rotated or removed on the plot (UpdateBlock), a plot forfeited or run out (UpdatePlot), and a home's
// architect scores. Each runs on the field's thread (FieldPlotThreadTests); the field's players get the client's cube
// packets. The cube item and its db row are made up.
public class FieldPlotUpdateTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int PlotNumber = 1;
    private const int CubeItemId = 50100000;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private FieldManager field = null!;
    private ChannelService channel = null!;
    private Plot plot = null!;

    [SetUp]
    public void Channel() {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(CubeItemId));
        test.Start();
        field = session.Field!;
        typeof(FieldManager).GetField(nameof(FieldManager.FieldActor), Any)!.SetValue(field, new FieldActor(field, 99, field.Metadata, null!));
        var factory = (FieldManager.Factory) RuntimeHelpers.GetUninitializedObject(typeof(FieldManager.Factory));
        void Set(string name, object value) => typeof(FieldManager.Factory).GetField(name, Any)!.SetValue(factory, value);
        Set("mapLocks", new ConcurrentDictionary<int, SemaphoreSlim>());
        Set("fields", new ConcurrentDictionary<int, ConcurrentDictionary<int, FieldManager>> {
            [field.MapId] = new ConcurrentDictionary<int, FieldManager> { [0] = field },
        });
        Set("homes", new ConcurrentDictionary<long, HomeFieldManager>());
        Set("<ServerTableMetadata>k__BackingField", test.ServerTableMetadata);
        var server = (GameServer) RuntimeHelpers.GetUninitializedObject(typeof(GameServer));
        typeof(GameServer).GetField("fieldFactory", Any)!.SetValue(server, factory);
        typeof(GameServer).GetField("sessions", Any)!.SetValue(server, new ConcurrentDictionary<long, GameSession> { [session.CharacterId] = session });
        channel = new ChannelService(server, null!, test.Storage, null!, null!, null!);
        plot = new Plot(new UgcMapGroup(PlotNumber, 0, 0, new UgcMapGroup.Cost(0, 0, 0), new UgcMapGroup.Cost(0, 0, 0),
            new UgcMapGroup.Limits(0, 0, 0, 0, 0, 0))) { Number = PlotNumber, MapId = field.MapId };
        field.Plots[PlotNumber] = plot;
        test.TakeSent();
    }

    private void Update(FieldPlotRequest request) {
        request.IgnoreChannel = -1;
        request.MapId = field.MapId;
        request.PlotNumber = PlotNumber;
        channel.UpdateFieldPlot(request, null!);
        field.RunFieldActions();
    }

    private void Block(FieldPlotRequest.Types.UpdateBlock block) => Update(new FieldPlotRequest { UpdateBlock = block });

    // The cube row the other channel saved, as GameStorage keeps it.
    private PlotCube Saved(Vector3B position) {
        using GameStorage.Request db = test.Storage.Context();
        var info = new PlotInfo(new UgcMapGroup(PlotNumber, 0, 0, new UgcMapGroup.Cost(0, 0, 0), new UgcMapGroup.Cost(0, 0, 0),
            new UgcMapGroup.Limits(0, 0, 0, 0, 0, 0))) { Id = 77, MapId = field.MapId, Number = PlotNumber };
        var cube = new PlotCube(TestGameSession.Metadata(CubeItemId)) { Position = position, Type = PlotCube.CubeType.Construction };
        return db.CreateCube(info, cube) ?? throw new InvalidOperationException("cube row not created");
    }

    private PlotCube Placed(Vector3B position, float rotation = 0) {
        var cube = new PlotCube(TestGameSession.Metadata(CubeItemId), 900 + position.X) {
            Position = position, Rotation = rotation, Type = PlotCube.CubeType.Construction,
        };
        plot.Cubes[position] = cube;
        return cube;
    }

    private string[] Sent() => test.TakeSent().Select(Convert.ToHexString).ToArray();

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    [Test]
    public void Add_PlacesTheSavedCubeOnThePlot() {
        var position = new Vector3B(1, 2, 3);
        PlotCube saved = Saved(position);

        Block(new FieldPlotRequest.Types.UpdateBlock { BlockUid = saved.Id, Add = new() });

        PlotCube cube = plot.Cubes[position];
        Assert.Multiple(() => {
            Assert.That(cube.Id, Is.EqualTo(saved.Id));
            Assert.That(cube.PlotId, Is.EqualTo(PlotNumber));
            Assert.That(Sent(), Is.EqualTo(new[] { Hex(CubePacket.PlaceCube(field.FieldActor.ObjectId, plot, cube)) }));
        });
    }

    // An add onto a taken spot keeps the cube there.
    [Test]
    public void Add_OntoATakenSpot_KeepsTheCubeThere() {
        var position = new Vector3B(1, 2, 3);
        PlotCube there = Placed(position);
        PlotCube saved = Saved(position);

        Block(new FieldPlotRequest.Types.UpdateBlock { BlockUid = saved.Id, Add = new() });

        Assert.That(plot.Cubes[position], Is.SameAs(there));
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void Replace_PutsTheSavedCubeInTheOldOnesPlace() {
        var position = new Vector3B(1, 2, 3);
        Placed(position);
        PlotCube saved = Saved(position);

        Block(new FieldPlotRequest.Types.UpdateBlock { BlockUid = saved.Id, Replace = new() });

        PlotCube cube = plot.Cubes[position];
        Assert.Multiple(() => {
            Assert.That(cube.Id, Is.EqualTo(saved.Id));
            Assert.That(plot.Cubes, Has.Count.EqualTo(1));
            Assert.That(Sent(), Is.EqualTo(new[] { Hex(CubePacket.ReplaceCube(field.FieldActor.ObjectId, cube)) }));
        });
    }

    // A cube row gone from the db (deleted again before this channel got the change) is not placed.
    [Test]
    public void Add_OfACubeNotSaved_PlacesNothing() {
        Block(new FieldPlotRequest.Types.UpdateBlock { BlockUid = 123456, Add = new() });

        Assert.That(plot.Cubes, Is.Empty);
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [TestCase(true, 270f)]
    [TestCase(false, 90f)]
    public void Rotate_TurnsTheCube(bool clockwise, float rotation) {
        var position = new Vector3B(1, 2, 3);
        PlotCube cube = Placed(position);

        Block(new FieldPlotRequest.Types.UpdateBlock {
            X = position.X, Y = position.Y, Z = position.Z, Rotate = new FieldPlotRequest.Types.UpdateBlock.Types.Rotate { Clockwise = clockwise },
        });

        Assert.That(cube.Rotation, Is.EqualTo(rotation));
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(CubePacket.RotateCube(field.FieldActor.ObjectId, cube)) }));
    }

    [Test]
    public void Rotate_OrRemove_AnEmptySpot_ChangesNothing() {
        Block(new FieldPlotRequest.Types.UpdateBlock { X = 4, Y = 4, Z = 4, Rotate = new() });
        Block(new FieldPlotRequest.Types.UpdateBlock { X = 4, Y = 4, Z = 4, Remove = new() });

        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void Remove_TakesTheCubeOffAndTellsTheField() {
        var position = new Vector3B(1, 2, 3);
        Placed(position);

        Block(new FieldPlotRequest.Types.UpdateBlock { X = position.X, Y = position.Y, Z = position.Z, Remove = new() });

        Assert.That(plot.Cubes, Is.Empty);
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(CubePacket.RemoveCube(field.FieldActor.ObjectId, position)) }));
    }

    // A change for a plot this field does not have, or a block change of no kind, is dropped.
    [Test]
    public void Block_OnAnUnknownPlotOrOfNoKind_ChangesNothing() {
        var position = new Vector3B(1, 2, 3);
        Placed(position);
        field.Plots.Remove(PlotNumber, out _);

        Block(new FieldPlotRequest.Types.UpdateBlock { X = position.X, Y = position.Y, Z = position.Z, Remove = new() });
        field.Plots[PlotNumber] = plot;
        Block(new FieldPlotRequest.Types.UpdateBlock { X = position.X, Y = position.Y, Z = position.Z });

        Assert.That(plot.Cubes, Has.Count.EqualTo(1));
        Assert.That(test.TakeSent(), Is.Empty);
    }

    // A forfeit (the World's expiry job, or the owner's forfeit on another channel): the plot's cubes leave the field,
    // the plots are read again, and the plot's forfeit is shown.
    [Test]
    public void Forfeit_TakesTheCubesOffAndReloadsThePlots() {
        var position = new Vector3B(1, 2, 3);
        Placed(position);

        Update(new FieldPlotRequest { UpdatePlot = new FieldPlotRequest.Types.UpdatePlot { Forfeit = true } });

        string[] sent = Sent();
        Assert.Multiple(() => {
            Assert.That(sent.First(), Is.EqualTo(Hex(CubePacket.RemoveCube(field.FieldActor.ObjectId, position))));
            Assert.That(sent.Last(), Is.EqualTo(Hex(CubePacket.ForfeitPlot(field.Plots[PlotNumber]))));
            Assert.That(sent, Has.Length.EqualTo(5), "the cube's removal, the plots' owners, states and expiries, the forfeit");
        });
    }

    // The owner online here gets the expiry notice and loses its outside plot, on its own thread.
    [Test]
    public void Forfeit_OfAnOnlineOwner_TakesThePlotOffItsSession() {
        Update(new FieldPlotRequest { UpdatePlot = new FieldPlotRequest.Types.UpdatePlot { Forfeit = true, AccountId = session.AccountId } });
        session.RunPlayerActionsAtEntry();
        test.RunFieldQueue();

        Assert.That(session.Housing.GetFieldPlot(), Is.Null);
        Assert.That(Sent(), Does.Contain(Hex(CubePacket.ConfirmForfeitPlot(field.MapId, PlotNumber, CubePacket.ForfeitExpired))));
    }

    // An owner not on this channel only has the field's plots read again.
    [Test]
    public void Update_OfAnOwnerNotHere_OnlyReloadsThePlots() {
        Update(new FieldPlotRequest { UpdatePlot = new FieldPlotRequest.Types.UpdatePlot { AccountId = session.AccountId + 100 } });

        Assert.That(test.TakeSentOps(), Has.Count.GreaterThanOrEqualTo(3));
        Assert.That(plot.Cubes, Is.Empty);
    }

    // A home's new architect scores reach its owner online here.
    [Test]
    public void ArchitectScore_ReachesTheOwner() {
        Update(new FieldPlotRequest {
            ArchitectScore = new FieldPlotRequest.Types.ArchitectScore { OwnerId = session.AccountId, Current = 3, Total = 40 },
        });

        Assert.That(session.Player.Value.Home.CurrentArchitectScore, Is.EqualTo(3));
        Assert.That(session.Player.Value.Home.ArchitectScore, Is.EqualTo(40));
    }
}
