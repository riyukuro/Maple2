using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using Grpc.Core;
using Maple2.Server.World.Containers;
using Maple2.Server.World.Service;
using Serilog;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;

namespace Maple2.Server.Tests.World;

// WorldService.Chat for a world or super line goes to every channel: a channel that is down misses it and the
// others still get it, and the sender's channel gets no error back (its cost is kept).
public class WorldChatBroadcastTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private sealed class FakeChannel : ChannelClient {
        public readonly List<ChatRequest> Chats = [];
        public readonly List<DateTime?> Deadlines = [];
        public bool Down;

        public override ChatResponse Chat(ChatRequest request, CallOptions options) {
            Deadlines.Add(options.Deadline);
            if (Down) {
                throw new RpcException(new Status(StatusCode.Unavailable, "down"));
            }
            Chats.Add(request);
            return new ChatResponse();
        }
    }

    private static void AddChannel(ChannelClientLookup lookup, int id, ChannelClient client) {
        Type channelType = typeof(ChannelClientLookup).GetNestedType("Channel", BindingFlags.NonPublic)!;
        object channel = Activator.CreateInstance(channelType, Any, null,
            [Enum.Parse(typeof(ChannelClientLookup).GetNestedType("ChannelStatus", BindingFlags.NonPublic)!, "Active"), id, false, new IPEndPoint(IPAddress.Loopback, 0), client, null, (ushort) 0, 0], null)!;
        var channels = (System.Collections.IDictionary) typeof(ChannelClientLookup).GetField("channels", Any)!.GetValue(lookup)!;
        channels[id] = channel;
    }

    [TestCase(1, false)]
    [TestCase(2, false)]
    [TestCase(1, true)]
    [TestCase(2, true)]
    public void Line_WithOneChannelDownReachesTheOthers(int down, bool super) {
        var lookup = new ChannelClientLookup();
        var channels = new Dictionary<int, FakeChannel>();
        for (int id = 1; id <= 3; id++) {
            channels[id] = new FakeChannel { Down = id == down };
            AddChannel(lookup, id, channels[id]);
        }
        var service = (WorldService) RuntimeHelpers.GetUninitializedObject(typeof(WorldService));
        typeof(WorldService).GetField("channelClients", Any)!.SetValue(service, lookup);
        typeof(WorldService).GetField("logger", Any)!.SetValue(service, new LoggerConfiguration().CreateLogger());

        var request = new ChatRequest { AccountId = 11, CharacterId = 11, Name = "sender", Message = "hello", Channel = 1 };
        if (super) {
            request.Super = new ChatRequest.Types.Super { ItemId = 20800100 };
        } else {
            request.World = new ChatRequest.Types.World();
        }
        Assert.DoesNotThrow(() => service.Chat(request, null!).GetAwaiter().GetResult());

        for (int id = 1; id <= 3; id++) {
            Assert.That(channels[id].Chats, Has.Count.EqualTo(id == down ? 0 : 1), $"channel {id}");
            // A channel that hangs is cut off by its own deadline, not waited on with the rest behind it.
            Assert.That(channels[id].Deadlines.Single(), Is.Not.Null.And.LessThanOrEqualTo(DateTime.UtcNow + WorldService.ChannelChatDeadline), $"channel {id}");
        }
    }
}
