using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using Grpc.Core;
using Maple2.Server.World;
using Maple2.Server.World.Containers;
using Maple2.Server.World.Service;
using Serilog;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;

namespace Maple2.Server.Tests.World;

// The World service's admin call: the maintenance needs a message to start and answers its state, a string board is
// added, listed and removed and told to every channel, and an alert reaches every channel even when one of them is
// down (its failure is told in the answer, not thrown).
public class WorldAdminTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private sealed class FakeChannel : ChannelClient {
        public readonly List<AdminRequest> Admins = [];
        public StatusCode? Fail;

        public override AdminResponse Admin(AdminRequest request, CallOptions options) {
            Admins.Add(request);
            if (Fail is { } code) {
                throw new RpcException(new Status(code, "down"));
            }
            return new AdminResponse();
        }
    }

    private FakeChannel first = null!;
    private FakeChannel second = null!;
    private WorldService service = null!;

    [SetUp]
    public void SetUp() {
        var channels = new ChannelClientLookup();
        first = new FakeChannel();
        second = new FakeChannel();
        Type channelType = typeof(ChannelClientLookup).GetNestedType("Channel", BindingFlags.NonPublic)!;
        object active = Enum.Parse(typeof(ChannelClientLookup).GetNestedType("ChannelStatus", BindingFlags.NonPublic)!, "Active");
        var entries = (System.Collections.IDictionary) typeof(ChannelClientLookup).GetField("channels", Any)!.GetValue(channels)!;
        int id = 1;
        foreach (FakeChannel client in new[] { first, second }) {
            entries[id] = Activator.CreateInstance(channelType, Any, null,
                [active, id, false, new IPEndPoint(IPAddress.Loopback, 0), client, null, (ushort) 0, 0], null)!;
            id++;
        }
        var world = (WorldServer) RuntimeHelpers.GetUninitializedObject(typeof(WorldServer));
        GC.SuppressFinalize(world);
        typeof(WorldServer).GetField("noticeMutex", Any)!.SetValue(world, new object());
        typeof(WorldServer).GetField("memoryStringBoards", Any)!.SetValue(world, new ConcurrentDictionary<int, string>());
        service = (WorldService) RuntimeHelpers.GetUninitializedObject(typeof(WorldService));
        typeof(WorldService).GetField("channelClients", Any)!.SetValue(service, channels);
        typeof(WorldService).GetField("worldServer", Any)!.SetValue(service, world);
        typeof(WorldService).GetField("logger", Any)!.SetValue(service, new LoggerConfiguration().CreateLogger());
    }

    private AdminResponse Call(AdminRequest request) => service.Admin(request, null!).GetAwaiter().GetResult();

    private AdminResponse Maintenance(int action, string message = "") =>
        Call(new AdminRequest { Maintenance = new AdminRequest.Types.Maintenance { Action = action, Message = message } });

    [Test]
    public void Maintenance_NeedsAMessageToStartAndEndsOnAction0() {
        Assert.That(Maintenance(1, "  ").Error, Is.EqualTo(1));
        Assert.That(Maintenance(2).Maintenance, Is.Empty);

        Assert.That(Maintenance(1, "Back at noon").Maintenance, Is.EqualTo("Back at noon"));
        Assert.That(Maintenance(2).Maintenance, Is.EqualTo("Back at noon"));
        Assert.That(Maintenance(0).Maintenance, Is.Empty);
    }

    [Test]
    public void Maintenance_AnUnknownActionIsRefused() {
        AdminResponse response = Maintenance(9);
        Assert.That(response.Error, Is.EqualTo(1));
        Assert.That(response.Message, Does.Contain("9"));
    }

    [Test]
    public void AStringBoard_IsAddedToEveryChannelListedAndRemoved() {
        Assert.That(Call(new AdminRequest { AddStringBoard = new AdminRequest.Types.AddStringBoard { Message = "Hello" } }).Message, Is.Empty);
        int id = first.Admins[0].AddStringBoard.Id;
        Assert.That(second.Admins[0].AddStringBoard.Message, Is.EqualTo("Hello"));
        Assert.That(Call(new AdminRequest { ListStringBoard = new AdminRequest.Types.ListStringBoard() }).Message, Does.Contain($"ID: {id}, Message: Hello"));

        Assert.That(Call(new AdminRequest { RemoveStringBoard = new AdminRequest.Types.RemoveStringBoard { Id = id } }).Message, Is.Empty);
        Assert.That(second.Admins[1].RemoveStringBoard.Id, Is.EqualTo(id));
        Assert.That(Call(new AdminRequest { ListStringBoard = new AdminRequest.Types.ListStringBoard() }).Message, Is.Empty);

        Assert.That(Call(new AdminRequest { RemoveStringBoard = new AdminRequest.Types.RemoveStringBoard { Id = id } }).Message, Does.Contain("Failed"));
    }

    [Test]
    public void AnEmptyStringBoard_IsRefusedAndToldToNoChannel() {
        Assert.That(Call(new AdminRequest { AddStringBoard = new AdminRequest.Types.AddStringBoard { Message = "" } }).Message, Does.Contain("Failed"));
        Assert.That(first.Admins, Is.Empty);
    }

    [Test]
    public void AnAlert_ReachesEveryChannelEvenWhenOneIsDown() {
        first.Fail = StatusCode.Unavailable;
        AdminResponse response = Call(new AdminRequest { Alert = new AdminRequest.Types.Alert { Message = "Notice" } });
        Assert.That(second.Admins, Has.Count.EqualTo(1));
        Assert.That(response.Message, Does.Contain("Channel 1 failed: Unavailable"));
    }

    [Test]
    public void AnEmptyRequest_AnswersEmpty() {
        Assert.That(Call(new AdminRequest()).Message, Is.Empty);
    }
}
