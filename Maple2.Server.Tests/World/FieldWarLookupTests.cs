using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using Grpc.Core;
using Maple2.Database.Storage;
using Maple2.Model.Metadata;
using Maple2.Server.Channel.Service;
using Maple2.Server.World.Containers;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;

namespace Maple2.Server.Tests.World;

// A character joins a field war once (FieldWarLookup): a repeat enter (from the new session after a move out of a
// room) gets no room, and the field-enter announce no longer finds the war for that character.
public class FieldWarLookupTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int MapId = 2020061;

    // A channel that makes room 100, 101, ... for each GetField.
    private sealed class FakeChannel : ChannelClient {
        public readonly List<int> Rooms = [];

        public override TimeEventResponse TimeEvent(TimeEventRequest request, CallOptions options) {
            if (request.GetField == null) {
                return new TimeEventResponse();
            }
            int roomId = 100 + Rooms.Count;
            Rooms.Add(roomId);
            return new TimeEventResponse {
                Field = new Maple2.Server.Channel.Service.FieldInfo {
                    MapId = request.GetField.MapId,
                    RoomId = roomId,
                },
            };
        }
    }

    private static void AddChannel(ChannelClientLookup lookup, int id, ChannelClient client) {
        Type channelType = typeof(ChannelClientLookup).GetNestedType("Channel", BindingFlags.NonPublic)!;
        object channel = Activator.CreateInstance(channelType, Any, null,
            [Enum.Parse(typeof(ChannelClientLookup).GetNestedType("ChannelStatus", BindingFlags.NonPublic)!, "Active"), id, false, new IPEndPoint(IPAddress.Loopback, 0), client, null, (ushort) 0, 0], null)!;
        var channels = (System.Collections.IDictionary) typeof(ChannelClientLookup).GetField("channels", Any)!.GetValue(lookup)!;
        channels[id] = channel;
    }

    private static (FieldWarLookup, FakeChannel) Lookup() {
        var channels = new ChannelClientLookup();
        var channel = new FakeChannel();
        AddChannel(channels, 1, channel);
        var tables = (ServerTableMetadataStorage) RuntimeHelpers.GetUninitializedObject(typeof(ServerTableMetadataStorage));
        typeof(ServerTableMetadataStorage).GetField("constantsTable", Any)!
            .SetValue(tables, new Lazy<ConstantsTable>(() => new ConstantsTable(FieldWarInstanceEnterableDurationSeconds: 600)));
        typeof(ServerTableMetadataStorage).GetField("instanceFieldTable", Any)!
            .SetValue(tables, new Lazy<InstanceFieldTable>(() => new InstanceFieldTable(new Dictionary<int, InstanceFieldMetadata>())));
        return (new FieldWarLookup(channels, tables), channel);
    }

    [Test]
    public void Join_OncePerCharacter() {
        (FieldWarLookup lookup, FakeChannel channel) = Lookup();
        long uid = lookup.Open(new FieldWarMetadata(1, 1, 100, MapId, 0, 0));

        Assert.That(lookup.Get(10)?.EventId, Is.EqualTo(uid));
        Assert.That(lookup.Join(uid, 10)?.RoomId, Is.EqualTo(100));
        // The same character again (a new session after the room sent it out): no room, no new room made.
        Assert.That(lookup.Join(uid, 10), Is.Null);
        Assert.That(channel.Rooms, Is.EqualTo(new[] { 100 }));
        // Another character still joins the open room.
        Assert.That(lookup.Join(uid, 11)?.RoomId, Is.EqualTo(100));
    }

    [Test]
    public void Get_SkipsACharacterThatJoined() {
        (FieldWarLookup lookup, _) = Lookup();
        long uid = lookup.Open(new FieldWarMetadata(1, 1, 100, MapId, 0, 0));
        lookup.Join(uid, 10);

        Assert.That(lookup.Get(10), Is.Null);
        Assert.That(lookup.Get(11)?.EventId, Is.EqualTo(uid));
    }

    // A new war (a new uid) takes every character again.
    [Test]
    public void NextWar_TakesTheCharacterAgain() {
        (FieldWarLookup lookup, _) = Lookup();
        long first = lookup.Open(new FieldWarMetadata(1, 1, 100, MapId, 0, 0));
        lookup.Join(first, 10);
        lookup.Close(first);
        long second = lookup.Open(new FieldWarMetadata(1, 1, 100, MapId, 0, 0));

        Assert.That(lookup.Join(first, 10), Is.Null);
        Assert.That(lookup.Get(10)?.EventId, Is.EqualTo(second));
        Assert.That(lookup.Join(second, 10)?.RoomId, Is.EqualTo(101));
    }
}
