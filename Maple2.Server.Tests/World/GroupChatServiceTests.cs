using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Tests.Game;
using Maple2.Server.World.Containers;
using Maple2.Server.World.Service;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;
using GroupChatRequest = Maple2.GroupChatRequest;
using GroupChatResponse = Maple2.GroupChatResponse;

namespace Maple2.Server.Tests.World;

// The World's group chats (GroupChat create, invite, leave, chat, disband) through WorldService.GroupChat as a
// channel calls it: the client's limits (3 chats a player, 20 members a chat, CGroupChatManager), and each line or
// member change sent to the channel each member is on now.
public class GroupChatServiceTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private sealed class FakeChannel : ChannelClient {
        public readonly List<GroupChatRequest> Requests = [];

        public override GroupChatResponse GroupChat(GroupChatRequest request, CallOptions options) {
            Requests.Add(request.Clone());
            return new GroupChatResponse();
        }
    }

    private TestGameSession test = null!;
    private readonly FakeChannel first = new();
    private readonly FakeChannel second = new();
    private ConcurrentDictionary<long, PlayerInfo> cache = null!;
    private PlayerInfoLookup playerLookup = null!;
    private GroupChatLookup lookup = null!;
    private WorldService world = null!;
    private const long A = 1001;
    private const long B = 1002;

    private static PlayerInfo Info(long characterId, string name, short channel) {
        return new PlayerInfo(new CharacterInfo(characterId + 100, characterId, name, "", "", Gender.Male, Job.Newbie, 20), "",
            new AchievementInfo(), []) {
            Channel = channel,
        };
    }

    private static void AddChannel(ChannelClientLookup channels, int id, ChannelClient client) {
        Type channelType = typeof(ChannelClientLookup).GetNestedType("Channel", BindingFlags.NonPublic)!;
        object entry = Activator.CreateInstance(channelType, Any, null,
            [Enum.Parse(typeof(ChannelClientLookup).GetNestedType("ChannelStatus", BindingFlags.NonPublic)!, "Active"), id, false, new IPEndPoint(IPAddress.Loopback, 0), client, null, (ushort) 0, 0], null)!;
        var entries = (System.Collections.IDictionary) typeof(ChannelClientLookup).GetField("channels", Any)!.GetValue(channels)!;
        entries[id] = entry;
    }

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        first.Requests.Clear();
        second.Requests.Clear();
        var channels = new ChannelClientLookup();
        AddChannel(channels, 1, first);
        AddChannel(channels, 2, second);
        playerLookup = new PlayerInfoLookup(test.Storage, channels);
        cache = (ConcurrentDictionary<long, PlayerInfo>) typeof(PlayerInfoLookup).GetField("cache", Any)!.GetValue(playerLookup)!;
        for (long id = A; id < A + 25; id++) {
            cache[id] = Info(id, $"P{id}", 1);
        }
        lookup = new GroupChatLookup(channels, playerLookup);
        world = new WorldService(null!, null!, channels, playerLookup, null!, null!, null!, lookup, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!);
    }

    [TearDown]
    public void TearDown() {
        lookup.Dispose();
        playerLookup.Dispose();
    }

    private GroupChatResponse Call(long requesterId, int chatId, Action<GroupChatRequest> fill) {
        var request = new GroupChatRequest { RequesterId = requesterId, GroupChatId = chatId };
        fill(request);
        return world.GroupChat(request, null!).GetAwaiter().GetResult();
    }

    private int Create(long requesterId) {
        GroupChatResponse response = Call(requesterId, 0, request => request.Create = new GroupChatRequest.Types.Create());
        Assert.That(response.Error, Is.Zero);
        return response.GroupChat.Id;
    }

    private GroupChatResponse Invite(int chatId, long requesterId, long receiverId) =>
        Call(requesterId, chatId, request => request.Invite = new GroupChatRequest.Types.Invite { ReceiverId = receiverId });

    private GroupChatResponse Chat(int chatId, long requesterId, string message) =>
        Call(requesterId, chatId, request => request.Chat = new GroupChatRequest.Types.Chat { Message = message });

    [Test]
    public void CreateInviteChat_ReachTheMembers() {
        int chat = Create(A);
        Assert.That(Invite(chat, A, B).Error, Is.Zero);
        first.Requests.Clear();

        GroupChatResponse response = Chat(chat, B, "hello");

        Assert.That(response.GroupChatId, Is.EqualTo(chat));
        GroupChatRequest sent = first.Requests.Single();
        Assert.Multiple(() => {
            Assert.That((sent.GroupChatId, sent.Chat.Message, sent.Chat.RequesterName), Is.EqualTo((chat, "hello", $"P{B}")));
            Assert.That(sent.ReceiverIds, Is.EquivalentTo(new[] { A, B }));
        });
        GroupChatInfoResponse info = world.GroupChatInfo(new GroupChatInfoRequest { CharacterId = B }, null!).GetAwaiter().GetResult();
        Assert.That(info.Infos.Single().Members.Select(member => member.CharacterId), Is.EquivalentTo(new[] { A, B }));
    }

    // A member who moved to channel 2 after joining gets the chat's lines there. The World kept a copy of the
    // member's PlayerInfo from the join, so every line, member change and disband still went to channel 1, where
    // the member no longer is.
    [Test]
    public void MemberOnAnotherChannelNow_GetsTheLinesThere() {
        int chat = Create(A);
        Assert.That(Invite(chat, A, B).Error, Is.Zero);
        first.Requests.Clear();
        cache[B].Channel = 2;

        Chat(chat, A, "hello");

        Assert.That(first.Requests.Single().ReceiverIds, Is.EqualTo(new[] { A }));
        Assert.That(second.Requests.Single().ReceiverIds, Is.EqualTo(new[] { B }));
    }

    // The client's limits: a fourth chat of a player, an invite of a player in 3 chats, a 21st member.
    [Test]
    public void Limits_AreTheClients() {
        for (int i = 0; i < Constant.GroupChatMaxCount; i++) {
            Create(A);
        }
        Assert.That(Call(A, 0, request => request.Create = new GroupChatRequest.Types.Create()).Error,
            Is.EqualTo((int) GroupChatError.s_err_groupchat_maxgroup));

        int chat = Create(B);
        Assert.That(Invite(chat, B, A).Error, Is.EqualTo((int) GroupChatError.s_err_groupchat_maxgroup));
        for (long id = B + 1; id < B + Constant.GroupChatMaxCapacity; id++) {
            Assert.That(Invite(chat, B, id).Error, Is.Zero, $"invite {id}");
        }
        Assert.That(Invite(chat, B, B + Constant.GroupChatMaxCapacity).Error, Is.EqualTo((int) GroupChatError.s_err_groupchat_add_member_target));
    }

    // Not a member, an unknown player, a member already in, an unknown chat.
    [Test]
    public void Invite_Refusals() {
        int chat = Create(A);
        Assert.That(Invite(chat, B, B + 1).Error, Is.EqualTo((int) GroupChatError.s_err_groupchat_null_target_user));
        Assert.That(Invite(chat, A, 99_999).Error, Is.EqualTo((int) GroupChatError.s_err_groupchat_null_target_user));
        Assert.That(Invite(chat, A, A).Error, Is.EqualTo((int) GroupChatError.s_err_groupchat_add_member_target));
        Assert.That(Invite(chat + 100, A, B).Error, Is.EqualTo((int) GroupChatError.s_err_groupchat_null_target_user));
        Assert.That(Chat(chat, B, "not a member").Error, Is.EqualTo((int) GroupChatError.s_err_groupchat_null_target_user));
    }

    // The last member's leave ends the chat: it is dropped and no longer listed at login.
    [Test]
    public void LastLeave_DropsTheChat() {
        int chat = Create(A);
        Assert.That(Invite(chat, A, B).Error, Is.Zero);

        Call(B, chat, request => request.Leave = new GroupChatRequest.Types.Leave());
        Assert.That(lookup.TryGet(chat, out _), Is.True);
        Call(A, chat, request => request.Leave = new GroupChatRequest.Types.Leave());

        Assert.That(lookup.TryGet(chat, out _), Is.False);
        Assert.That(world.GroupChatInfo(new GroupChatInfoRequest { CharacterId = A }, null!).GetAwaiter().GetResult().Infos, Is.Empty);
    }
}
