using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.World.Containers;
using Maple2.Server.World.Service;
using Serilog;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;

namespace Maple2.Server.Tests.World;

// WorldService.Chat for a whisper: the line goes to the channel of the recipient, not of the sender, and a recipient
// who is offline or gone from that channel is NotFound for the sender's channel (s_err_cannot_find_user).
public class WhisperRoutingTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const long SenderId = 11;
    private const long RecipientId = 22;

    private sealed class FakeChannel : ChannelClient {
        public readonly List<ChatRequest> Chats = [];
        public bool NotFound;

        public override ChatResponse Chat(ChatRequest request, CallOptions options) {
            if (NotFound) {
                throw new RpcException(new Status(StatusCode.NotFound, "gone"));
            }
            Chats.Add(request);
            return new ChatResponse();
        }
    }

    private FakeChannel first = null!;
    private FakeChannel second = null!;
    private ConcurrentDictionary<long, PlayerInfo> players = null!;
    private WorldService service = null!;

    private static void AddChannel(ChannelClientLookup lookup, int id, ChannelClient client) {
        Type channelType = typeof(ChannelClientLookup).GetNestedType("Channel", BindingFlags.NonPublic)!;
        object channel = Activator.CreateInstance(channelType, Any, null,
            [Enum.Parse(typeof(ChannelClientLookup).GetNestedType("ChannelStatus", BindingFlags.NonPublic)!, "Active"), id, false, new IPEndPoint(IPAddress.Loopback, 0), client, null, (ushort) 0, 0], null)!;
        var channels = (System.Collections.IDictionary) typeof(ChannelClientLookup).GetField("channels", Any)!.GetValue(lookup)!;
        channels[id] = channel;
    }

    private static PlayerInfo Player(long characterId, string name, int channel) {
        return new PlayerInfo(new CharacterInfo(characterId, characterId, name, "", "", Gender.Male, Job.Newbie, 1), "", new AchievementInfo(), []) {
            Channel = (short) channel,
        };
    }

    [SetUp]
    public void SetUp() {
        var channels = new ChannelClientLookup();
        first = new FakeChannel();
        second = new FakeChannel();
        AddChannel(channels, 1, first);
        AddChannel(channels, 2, second);

        // The lookup's cache only: no database and no sync loop.
        var lookup = (PlayerInfoLookup) RuntimeHelpers.GetUninitializedObject(typeof(PlayerInfoLookup));
        players = new ConcurrentDictionary<long, PlayerInfo>();
        typeof(PlayerInfoLookup).GetField("cache", Any)!.SetValue(lookup, players);
        players[SenderId] = Player(SenderId, "sender", 1);
        players[RecipientId] = Player(RecipientId, "recipient", 2);

        service = (WorldService) RuntimeHelpers.GetUninitializedObject(typeof(WorldService));
        typeof(WorldService).GetField("channelClients", Any)!.SetValue(service, channels);
        typeof(WorldService).GetField("playerLookup", Any)!.SetValue(service, lookup);
        typeof(WorldService).GetField("logger", Any)!.SetValue(service, new LoggerConfiguration().CreateLogger());
    }

    private void Whisper(bool reject = false) {
        service.Chat(new ChatRequest {
            AccountId = SenderId,
            CharacterId = SenderId,
            Name = "sender",
            Message = "psst",
            Channel = 1,
            Whisper = new ChatRequest.Types.Whisper { RecipientId = RecipientId, RecipientName = "recipient", Reject = reject },
        }, null!).GetAwaiter().GetResult();
    }

    [Test]
    public void Whisper_GoesToTheRecipientsChannel() {
        Whisper();

        Assert.That(first.Chats, Is.Empty);
        Assert.That(second.Chats, Has.Count.EqualTo(1));
        Assert.That(second.Chats[0].Whisper.RecipientId, Is.EqualTo(RecipientId));
        Assert.That(second.Chats[0].Whisper.Reject, Is.False);
        Assert.That(second.Chats[0].Name, Is.EqualTo("sender"));
    }

    // The reject notice takes the same road, with its flag.
    [Test]
    public void Reject_GoesToTheWhisperersChannelWithItsFlag() {
        Whisper(reject: true);

        Assert.That(second.Chats, Has.Count.EqualTo(1));
        Assert.That(second.Chats[0].Whisper.Reject, Is.True);
    }

    [Test]
    public void Whisper_ToAnOfflineRecipientIsNotFound() {
        players[RecipientId].Channel = -1;

        var ex = Assert.Throws<RpcException>(() => Whisper());

        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.NotFound));
        Assert.That(first.Chats, Is.Empty);
        Assert.That(second.Chats, Is.Empty);
    }

    // The recipient is online on a channel the world has no client for (down, or not registered yet): NotFound too,
    // so the sender gets s_err_cannot_find_user instead of nothing.
    [Test]
    public void Whisper_ToAChannelWithNoClientIsNotFound() {
        players[RecipientId].Channel = 3;

        var ex = Assert.Throws<RpcException>(() => Whisper());

        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.NotFound));
        Assert.That(first.Chats, Is.Empty);
        Assert.That(second.Chats, Is.Empty);
    }

    // The world still lists the recipient on a channel that has no session for it any more.
    [Test]
    public void Whisper_TheRecipientsChannelDoesNotFindIsNotFound() {
        second.NotFound = true;

        var ex = Assert.Throws<RpcException>(() => Whisper());

        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.NotFound));
    }
}
