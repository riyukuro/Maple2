using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using Grpc.Core;
using Maple2.Model.Metadata;
using Maple2.Server.Channel.Service;
using Maple2.Server.World.Containers;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;

namespace Maple2.Server.Tests.World;

// The global portal close on the World server (GlobalPortalLookup.Dispose): the event is gone from the lookup
// before any channel hears the close, so a field enter during the close does not get the portal again.
public class GlobalPortalCloseTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int PortalId = 90000401;

    // A channel that records each close and whether the lookup still had the event at that moment.
    private sealed class FakeChannel : ChannelClient {
        public GlobalPortalLookup? Lookup;
        public readonly List<(int EventId, bool StillListed)> Closes = [];

        public override TimeEventResponse TimeEvent(TimeEventRequest request, CallOptions options) {
            if (request.CloseGlobalPortal != null) {
                Closes.Add((request.CloseGlobalPortal.EventId, Lookup!.TryGet(out _)));
            }
            return new TimeEventResponse();
        }
    }

    private static void AddChannel(ChannelClientLookup lookup, int id, ChannelClient client) {
        Type channelType = typeof(ChannelClientLookup).GetNestedType("Channel", BindingFlags.NonPublic)!;
        object channel = Activator.CreateInstance(channelType, Any, null,
            [Enum.Parse(typeof(ChannelClientLookup).GetNestedType("ChannelStatus", BindingFlags.NonPublic)!, "Active"), id, false, new IPEndPoint(IPAddress.Loopback, 0), client, null, (ushort) 0, 0], null)!;
        var channels = (System.Collections.IDictionary) typeof(ChannelClientLookup).GetField("channels", Any)!.GetValue(lookup)!;
        channels[id] = channel;
    }

    private static GlobalPortalMetadata Metadata() => new(PortalId, 100, DateTime.MinValue, DateTime.MaxValue, TimeSpan.FromHours(1),
        TimeSpan.Zero, TimeSpan.FromMinutes(5), "", "", [new GlobalPortalMetadata.Field("", 2000001, 1)]);

    [Test]
    public void Dispose_ClearsTheEventBeforeTellingEveryChannel() {
        var channels = new ChannelClientLookup();
        var first = new FakeChannel();
        var second = new FakeChannel();
        AddChannel(channels, 1, first);
        AddChannel(channels, 2, second);
        var lookup = new GlobalPortalLookup(channels, null!, null!);
        first.Lookup = lookup;
        second.Lookup = lookup;
        lookup.Create(Metadata(), 0, out _);
        Assert.That(lookup.TryGet(out _), Is.True);

        lookup.Dispose();

        Assert.That(first.Closes, Is.EqualTo(new[] { (PortalId, false) }));
        Assert.That(second.Closes, Is.EqualTo(new[] { (PortalId, false) }));
        Assert.That(lookup.TryGet(out _), Is.False);
    }

    // No event open: nothing to tell; a second close tells no one again.
    [Test]
    public void Dispose_WithoutAnEventTellsNoOne() {
        var channels = new ChannelClientLookup();
        var channel = new FakeChannel();
        AddChannel(channels, 1, channel);
        var lookup = new GlobalPortalLookup(channels, null!, null!);
        channel.Lookup = lookup;

        lookup.Dispose();
        lookup.Create(Metadata(), 0, out _);
        lookup.Dispose();
        lookup.Dispose();

        Assert.That(channel.Closes, Has.Count.EqualTo(1));
    }
}
