using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.GroupChat;
using Maple2.Server.Channel.Service;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game;
using Maple2.Server.Game.Service;
using Maple2.Server.Game.Session;
using Maple2.Server.Tests.Game;
using Maple2.Server.World.Containers;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;
using GameGroupChatManager = Maple2.Server.Game.Manager.GroupChatManager;
using WorldGroupChatManager = Maple2.Server.World.Containers.GroupChatManager;
using GroupChatRequest = Maple2.GroupChatRequest;
using GroupChatResponse = Maple2.GroupChatResponse;

namespace Maple2.Server.Tests.World;

// The last member of a group chat leaves. The client closes a chat only on GroupChat 4 (OnPacketRemoveChat,
// groupchatmanager.cpp:646) and keeps a chat with one name left after GroupChat 7 (OnPacketRemoveMember, :672). The
// World used to end a chat once one member was left: that member's window stayed open and its leave did nothing.
public class GroupChatLastMemberTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int ChannelId = 1;
    private const int ChatId = 7;

    // A channel that hands group chat requests on to a game server's channel service.
    private sealed class FakeChannel : ChannelClient {
        public ChannelService? Game;

        public override GroupChatResponse GroupChat(GroupChatRequest request, CallOptions options) {
            return Game?.GroupChat(request, null!).GetAwaiter().GetResult() ?? new GroupChatResponse();
        }
    }

    private TestGameSession test = null!;
    private GameSession session = null!;
    private WorldGroupChatManager world = null!;
    private long otherId;

    private static PlayerInfo Info(long characterId, string name) {
        return new PlayerInfo(new CharacterInfo(characterId + 100, characterId, name, "", "", Gender.Male, Job.Newbie, 20), "",
            new AchievementInfo(), []) {
            Channel = ChannelId,
        };
    }

    private static void AddChannel(ChannelClientLookup lookup, int id, ChannelClient client) {
        Type channelType = typeof(ChannelClientLookup).GetNestedType("Channel", BindingFlags.NonPublic)!;
        object entry = Activator.CreateInstance(channelType, Any, null,
            [Enum.Parse(typeof(ChannelClientLookup).GetNestedType("ChannelStatus", BindingFlags.NonPublic)!, "Active"), id, false, new IPEndPoint(IPAddress.Loopback, 0), client, null, (ushort) 0, 0], null)!;
        var entries = (System.Collections.IDictionary) typeof(ChannelClientLookup).GetField("channels", Any)!.GetValue(lookup)!;
        entries[id] = entry;
    }

    // A chat of the session's player and one other, in the World and in the session.
    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        session = test.Start();
        otherId = session.CharacterId + 500;
        test.World.AddPlayer(session.AccountId, session.CharacterId, session.PlayerName);
        test.World.AddPlayer(session.AccountId + 500, otherId, "Other");

        var server = (GameServer) RuntimeHelpers.GetUninitializedObject(typeof(GameServer));
        typeof(GameServer).GetField("sessions", Any)!.SetValue(server, new ConcurrentDictionary<long, GameSession> { [session.CharacterId] = session });
        var channels = new ChannelClientLookup();
        AddChannel(channels, ChannelId, new FakeChannel { Game = new ChannelService(server, null!, test.Storage, null!, null!, null!) });

        var chat = new GroupChat(ChatId);
        world = new WorldGroupChatManager(chat) { ChannelClients = channels };
        world.Create(Info(session.CharacterId, session.PlayerName));
        chat.Members.TryAdd(otherId, new GroupChatMember { Info = Info(otherId, "Other") });

        session.GroupChats ??= new ConcurrentDictionary<int, GameGroupChatManager>();
        session.GroupChats[ChatId] = new GameGroupChatManager(new GroupChatInfo {
            Id = ChatId,
            Members = {
                new GroupChatInfo.Types.Member { CharacterId = session.CharacterId },
                new GroupChatInfo.Types.Member { CharacterId = otherId },
            },
        }, session);
        test.TakeSent();
    }

    [TearDown]
    public void TearDown() {
        world.Dispose();
        session.Dispose();
    }

    private byte[][] GroupChatSent() => test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.GroupChat).ToArray();

    private static string Leave() => "8B00" + "04" + "07000000";

    [Test]
    public void OtherLeaves_TheChatStaysWithOneMember() {
        bool ended = world.Leave(otherId);

        byte[][] sent = GroupChatSent();
        Assert.Multiple(() => {
            Assert.That(ended, Is.False);
            Assert.That(world.GroupChat.Members.Keys, Is.EquivalentTo(new[] { session.CharacterId }));
            Assert.That(sent.Select(packet => packet[2]), Is.EqualTo(new byte[] { 7 })); // RemoveMember only, no close
            Assert.That(session.GroupChats.ContainsKey(ChatId), Is.True);
        });
    }

    [Test]
    public void LastMemberLeaves_TheWindowClosesAndTheChatEnds() {
        world.Leave(otherId);
        test.TakeSent();

        bool ended = world.Leave(session.CharacterId);

        byte[][] sent = GroupChatSent();
        Assert.Multiple(() => {
            Assert.That(ended, Is.True);
            Assert.That(world.GroupChat.Members, Is.Empty);
            Assert.That(sent.Select(Convert.ToHexString), Is.EqualTo(new[] { Leave() }));
            Assert.That(session.GroupChats.ContainsKey(ChatId), Is.False);
        });
    }

    // A chat the World ends (a logout leaving nobody online): a member still holding it gets GroupChat 4 too.
    [Test]
    public void Disband_ClosesTheWindow() {
        world.Dispose();

        Assert.Multiple(() => {
            Assert.That(GroupChatSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Leave() }));
            Assert.That(session.GroupChats.ContainsKey(ChatId), Is.False);
        });
    }
}
