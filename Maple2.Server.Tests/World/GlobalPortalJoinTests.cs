using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Channel.Service;
using Maple2.Server.World.Containers;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;

namespace Maple2.Server.Tests.World;

// The World server's global portal rooms (GlobalPortalManager) and its world boss announce: a join fills the entry's
// room up to the map's instancefield maxCount and then opens one new room, also when many join at once; a room that
// is gone is made again; a channel that fails refuses the join instead of throwing; the announce and the close reach
// every channel when one of them is down; the world boss announce names the player channels.
public class GlobalPortalJoinTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int PortalId = 90000401;
    private const int MapId = 2000001;
    private const int Cap = 3;

    // A channel with rooms of the map: GetField with room 0 makes room 100, 101, ...; with a room id it answers the
    // room and its players, or no field when the room is gone. Every other request is recorded.
    private sealed class FakeChannel : ChannelClient {
        private readonly object mutex = new();
        public readonly Dictionary<int, int> Players = [];
        public readonly List<int> Made = [];
        public readonly List<TimeEventRequest> Requests = [];
        public int GetFieldDelayMs;

        public override TimeEventResponse TimeEvent(TimeEventRequest request, CallOptions options) {
            if (request.GetField == null) {
                lock (mutex) {
                    Requests.Add(request);
                }
                return new TimeEventResponse();
            }
            Thread.Sleep(GetFieldDelayMs);
            lock (mutex) {
                int roomId = request.GetField.RoomId;
                if (roomId == 0) {
                    roomId = 100 + Made.Count;
                    Made.Add(roomId);
                    Players[roomId] = 0;
                } else if (!Players.ContainsKey(roomId)) {
                    return new TimeEventResponse();
                }
                var field = new Maple2.Server.Channel.Service.FieldInfo { MapId = request.GetField.MapId, RoomId = roomId };
                field.PlayerIds.Add(Enumerable.Range(1, Players[roomId]).Select(id => (long) id));
                return new TimeEventResponse { Field = field };
            }
        }
    }

    // A channel that is down.
    private sealed class DownChannel : ChannelClient {
        public override TimeEventResponse TimeEvent(TimeEventRequest request, CallOptions options) {
            throw new RpcException(new Status(StatusCode.Unavailable, "the test's channel is down"));
        }
    }

    private static void AddChannel(ChannelClientLookup lookup, int id, ChannelClient client) {
        Type channelType = typeof(ChannelClientLookup).GetNestedType("Channel", BindingFlags.NonPublic)!;
        object channel = Activator.CreateInstance(channelType, Any, null,
            [Enum.Parse(typeof(ChannelClientLookup).GetNestedType("ChannelStatus", BindingFlags.NonPublic)!, "Active"), id, false, new IPEndPoint(IPAddress.Loopback, 0), client, null, (ushort) 0, 0], null)!;
        var channels = (System.Collections.IDictionary) typeof(ChannelClientLookup).GetField("channels", Any)!.GetValue(lookup)!;
        channels[id] = channel;
    }

    private static ServerTableMetadataStorage Tables(bool instanceField = true) {
        var tables = (ServerTableMetadataStorage) RuntimeHelpers.GetUninitializedObject(typeof(ServerTableMetadataStorage));
        var entries = new Dictionary<int, InstanceFieldMetadata>();
        if (instanceField) {
            entries[MapId] = new InstanceFieldMetadata(MapId, default(InstanceType), 0, false, 0, false, 0, Cap, 0, 0);
        }
        typeof(ServerTableMetadataStorage).GetField("instanceFieldTable", Any)!
            .SetValue(tables, new Lazy<InstanceFieldTable>(() => new InstanceFieldTable(entries)));
        return tables;
    }

    private static GlobalPortalMetadata Metadata() => new(PortalId, 100, DateTime.MinValue, DateTime.MaxValue, TimeSpan.FromHours(1),
        TimeSpan.Zero, TimeSpan.FromMinutes(5), "", "", [new GlobalPortalMetadata.Field("", MapId, 1)]);

    private static (GlobalPortalManager, FakeChannel) Manager(bool instanceField = true) {
        var channels = new ChannelClientLookup();
        var channel = new FakeChannel();
        AddChannel(channels, 1, channel);
        var manager = new GlobalPortalManager(Metadata(), 1, 0) {
            ChannelClients = channels,
            GameStorage = null!,
            ServerTableMetadata = Tables(instanceField),
        };
        manager.CreateFields();
        return (manager, channel);
    }

    [Test]
    public void Join_FillsTheRoomToItsCapThenOpensOneNew() {
        (GlobalPortalManager manager, FakeChannel channel) = Manager();
        Assert.That(manager.RoomIds[0], Is.EqualTo(100));

        Assert.That(manager.Join(MapId, 0), Is.EqualTo(100));
        channel.Players[100] = Cap;
        Assert.That(manager.Join(MapId, 0), Is.EqualTo(101));
        Assert.That(manager.Join(MapId, 0), Is.EqualTo(101));
        Assert.That(channel.Made, Is.EqualTo(new[] { 100, 101 }));
    }

    // Many joins at the cap at once: one new room, every join gets it (before, each could make its own room and the
    // last one written was kept, the others filling rooms no later join found).
    [Test]
    public void Join_ManyAtTheCapOpenOneRoom() {
        (GlobalPortalManager manager, FakeChannel channel) = Manager();
        channel.Players[100] = Cap;
        channel.GetFieldDelayMs = 20;

        int[] rooms = Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => manager.Join(MapId, 0)))).Result;

        Assert.That(channel.Made, Is.EqualTo(new[] { 100, 101 }));
        Assert.That(rooms, Is.All.EqualTo(101));
    }

    [Test]
    public void Join_ARoomThatIsGoneIsMadeAgain() {
        (GlobalPortalManager manager, FakeChannel channel) = Manager();
        channel.Players.Remove(100);

        Assert.That(manager.Join(MapId, 0), Is.EqualTo(101));
        Assert.That(manager.RoomIds[0], Is.EqualTo(101));
    }

    // A map without an instancefield row has no cap: its room takes everyone.
    [Test]
    public void Join_WithoutAnInstanceFieldRowHasNoCap() {
        (GlobalPortalManager manager, FakeChannel channel) = Manager(instanceField: false);
        channel.Players[100] = 500;

        Assert.That(manager.Join(MapId, 0), Is.EqualTo(100));
        Assert.That(channel.Made, Is.EqualTo(new[] { 100 }));
    }

    [Test]
    public void Join_OnAChannelThatFailsIsRefused() {
        var channels = new ChannelClientLookup();
        AddChannel(channels, 1, new DownChannel());
        var manager = new GlobalPortalManager(Metadata(), 1, 0) {
            ChannelClients = channels,
            GameStorage = null!,
            ServerTableMetadata = Tables(),
        };
        manager.Channel = 1;

        Assert.That(manager.Join(MapId, 0), Is.EqualTo(0));
    }

    // A channel that is down does not keep the announce and the close from the channels after it.
    [Test]
    public void AnnounceAndClose_ReachEveryChannelWhenOneIsDown() {
        var channels = new ChannelClientLookup();
        var live = new FakeChannel();
        AddChannel(channels, 1, live);
        AddChannel(channels, 2, new DownChannel());
        var third = new FakeChannel();
        AddChannel(channels, 3, third);
        var manager = new GlobalPortalManager(Metadata(), 1, 0) {
            ChannelClients = channels,
            GameStorage = null!,
            ServerTableMetadata = Tables(),
        };

        manager.CreateFields();
        manager.Dispose();

        foreach (FakeChannel channel in new[] { live, third }) {
            Assert.That(channel.Requests.Count(request => request.AnnounceGlobalPortal != null), Is.EqualTo(1));
            Assert.That(channel.Requests.Count(request => request.CloseGlobalPortal != null), Is.EqualTo(1));
        }
    }

    // The world boss announce names the player channels, so each channel sends a "boss appeared" row for every one.
    [Test]
    public void WorldBossAnnounce_NamesThePlayerChannels() {
        var channels = new ChannelClientLookup();
        var first = new FakeChannel();
        var second = new FakeChannel();
        AddChannel(channels, 2, second);
        AddChannel(channels, 1, first);
        var metadata = new WorldBossMetadata(1000, 100, DateTime.MinValue, DateTime.MaxValue, TimeSpan.FromHours(1), TimeSpan.Zero,
            TimeSpan.FromMinutes(5), [MapId], [100], [23000013], 1000, true, false, 1, false, false);
        var manager = new WorldBossManager(metadata, 1, 0, 0) {
            ChannelClients = channels,
        };

        manager.Announce();

        foreach (FakeChannel channel in new[] { first, second }) {
            TimeEventRequest.Types.AnnounceWorldBoss announce = channel.Requests.Single(request => request.AnnounceWorldBoss != null).AnnounceWorldBoss;
            Assert.That(announce.Channels, Is.EqualTo(new[] { 1, 2 }));
        }
    }
}
