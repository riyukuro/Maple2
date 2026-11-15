using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Maple2.Model.Common;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Channel.Service;
using Maple2.Server.Game;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Service;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// A plot change another channel sends (ChannelService.UpdateFieldPlot) arrives on a gRPC thread, while the field's own
// thread iterates the plot's cubes (its handlers, LoadUgcMap, the field's cube lookups). Before, the gRPC thread changed
// Plot.Cubes itself (a plain Dictionary): an iteration on the field thread at that moment threw and the field's thread
// ended. The change now waits for the field thread (FieldManager.RunOnField), which runs it at its next update with or
// without players.
public class FieldPlotThreadTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int PlotNumber = 1;

    private TestGameSession test = null!;
    private FieldManager field = null!;
    private ChannelService channel = null!;

    [SetUp]
    public void Channel() {
        test = new TestGameSession();
        GameSession session = test.Start();
        field = session.Field!;
        var factory = (FieldManager.Factory) RuntimeHelpers.GetUninitializedObject(typeof(FieldManager.Factory));
        void Set(string name, object value) => typeof(FieldManager.Factory).GetField(name, Any)!.SetValue(factory, value);
        Set("mapLocks", new ConcurrentDictionary<int, SemaphoreSlim>());
        Set("fields", new ConcurrentDictionary<int, ConcurrentDictionary<int, FieldManager>> {
            [field.MapId] = new ConcurrentDictionary<int, FieldManager> { [0] = field },
        });
        Set("<ServerTableMetadata>k__BackingField", test.ServerTableMetadata);
        var server = (GameServer) RuntimeHelpers.GetUninitializedObject(typeof(GameServer));
        typeof(GameServer).GetField("fieldFactory", Any)!.SetValue(server, factory);
        typeof(GameServer).GetField("sessions", Any)!.SetValue(server, new ConcurrentDictionary<long, GameSession>());
        channel = new ChannelService(server, null!, test.Storage, null!, null!, null!);
    }

    [Test]
    public void ACubeRemovedOnAnotherChannel_IsRemovedByTheFieldThread() {
        var position = new Vector3B(1, 2, 3);
        var plot = new Plot(new UgcMapGroup(0, 0, 0, new UgcMapGroup.Cost(0, 0, 0), new UgcMapGroup.Cost(0, 0, 0),
            new UgcMapGroup.Limits(0, 0, 0, 0, 0, 0))) { Number = PlotNumber };
        plot.Cubes[position] = new PlotCube(TestGameSession.Metadata(50100000)) { Position = position, Type = PlotCube.CubeType.Default };
        field.Plots[PlotNumber] = plot;

        channel.UpdateFieldPlot(new FieldPlotRequest {
            IgnoreChannel = -1,
            MapId = field.MapId,
            PlotNumber = PlotNumber,
            UpdateBlock = new FieldPlotRequest.Types.UpdateBlock { X = position.X, Y = position.Y, Z = position.Z, Remove = new() },
        }, null!);
        bool keptOnTheCall = plot.Cubes.ContainsKey(position);
        field.RunFieldActions();

        Assert.Multiple(() => {
            Assert.That(keptOnTheCall, Is.True, "the gRPC thread leaves the cubes alone");
            Assert.That(plot.Cubes, Does.Not.ContainKey(position), "the field thread removes it");
        });
    }

    // One action that throws is logged; the others still run.
    [Test]
    public void AFieldActionThatThrows_LeavesTheOthersRunning() {
        int ran = 0;
        field.RunOnField(() => throw new InvalidOperationException("test"));
        field.RunOnField(() => ran++);

        Assert.DoesNotThrow(field.RunFieldActions);
        Assert.That(ran, Is.EqualTo(1));
    }
}
