using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Service;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;

namespace Maple2.Server.Tests.Game;

// GroupChat 3, 6, 8 and 9 as the channel sends them, in the layout of the client's readers (groupchatmanager.cpp):
// 3 (FUN_14137f790, :630) the inviter's name, the invited player's name and the chat id, after the chat's Load (0),
// which makes the chat the reader looks up; 6 (OnPacketAddMember, :657) the chat id, the inviter's name and the
// member (CGroupChatMember::ReadFromPacket: the state byte, then the player); 8 and 9 (OnPacketMemberOnline and
// OnPacketMemberOffline, :688 and :703) the chat id and the member's name.
public class GroupChatNoticeTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int ChatId = 5;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private ChannelService service = null!;
    private long otherId;
    private long thirdId;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        session.GroupChats ??= new ConcurrentDictionary<int, GroupChatManager>();
        otherId = session.CharacterId + 500;
        thirdId = session.CharacterId + 600;
        test.World.AddPlayer(session.AccountId, session.CharacterId, session.PlayerName);
        test.World.AddPlayer(session.AccountId + 500, otherId, "other");
        test.World.AddPlayer(session.AccountId + 600, thirdId, "third");
        var server = (GameServer) RuntimeHelpers.GetUninitializedObject(typeof(GameServer));
        typeof(GameServer).GetField("sessions", Any)!.SetValue(server, new ConcurrentDictionary<long, GameSession> { [session.CharacterId] = session });
        service = new ChannelService(server, session.PlayerInfo, test.Storage, null!, null!, null!);
        test.TakeSent();
    }

    private string[] Sent() => test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.GroupChat).Select(Convert.ToHexString).ToArray();
    private static string Hex(ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    private void Add(long added, long requester, string requesterName, params long[] receivers) {
        service.GroupChat(new GroupChatRequest {
            GroupChatId = ChatId,
            ReceiverIds = { receivers },
            AddMember = new GroupChatRequest.Types.AddMember { CharacterId = added, RequesterId = requester, RequesterName = requesterName },
        }, null!).GetAwaiter().GetResult();
    }

    // The invited player gets the chat (0), then the invite line (3), and holds the chat.
    [Test]
    public void Invited_GetsTheChatThenTheInvite() {
        Add(session.CharacterId, otherId, "other", session.CharacterId, otherId);

        string[] sent = Sent();
        Assert.Multiple(() => {
            Assert.That(sent, Has.Length.EqualTo(2));
            Assert.That(sent[0][4..6], Is.EqualTo("00"), "the chat's Load first");
            Assert.That(sent[1], Is.EqualTo(Hex(GroupChatPacket.Join("other", session.PlayerName, ChatId))));
            Assert.That(session.GroupChats.ContainsKey(ChatId), Is.True);
        });

        var reader = new ByteReader(Convert.FromHexString(sent[1]));
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.GroupChat));
            Assert.That(reader.ReadByte(), Is.EqualTo(3));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("other"));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo(session.PlayerName));
            Assert.That(reader.ReadInt(), Is.EqualTo(ChatId));
            Assert.That(reader.Available, Is.EqualTo(0));
        });
    }

    // A member of the chat gets the new member with the inviter's name (6), and holds the member.
    [Test]
    public void Member_GetsTheAddedMember() {
        var manager = new GroupChatManager(new GroupChatInfo {
            Id = ChatId,
            Members = { new GroupChatInfo.Types.Member { CharacterId = otherId } },
        }, session);
        session.GroupChats[ChatId] = manager;
        test.TakeSent();

        Add(thirdId, otherId, "other", session.CharacterId, otherId, thirdId);

        string[] sent = Sent();
        Assert.That(manager.GetMember(thirdId), Is.Not.Null);
        Assert.That(sent, Is.EqualTo(new[] { Hex(GroupChatPacket.AddMember(manager.GetMember(thirdId)!.Info, "other", ChatId)) }));
        var reader = new ByteReader(Convert.FromHexString(sent[0]));
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.GroupChat));
            Assert.That(reader.ReadByte(), Is.EqualTo(6));
            Assert.That(reader.ReadInt(), Is.EqualTo(ChatId));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("other"));
            Assert.That(reader.ReadByte(), Is.EqualTo(1), "the member's state: online");
            Assert.That(reader.ReadLong(), Is.EqualTo(session.AccountId + 600), "the player block starts with the account id");
        });
    }

    // A member who logs out (channel -1) gives 9, one who comes back gives 8; an update that keeps the state gives
    // neither.
    [Test]
    public void MemberLogoutAndLogin_SendTheNotices() {
        _ = new GroupChatManager(new GroupChatInfo {
            Id = ChatId,
            Members = { new GroupChatInfo.Types.Member { CharacterId = otherId } },
        }, session);
        test.TakeSent();

        void Update(int channel) => session.PlayerInfo.ReceiveUpdate(new PlayerUpdateRequest {
            AccountId = session.AccountId + 500,
            CharacterId = otherId,
            Channel = channel,
        });

        Update(-1);
        string[] logout = Sent();
        Update(-1);
        string[] again = Sent();
        Update(1);
        string[] login = Sent();

        Assert.Multiple(() => {
            Assert.That(logout, Is.EqualTo(new[] { "8B00" + "09" + "05000000" + "0500" + Convert.ToHexString(System.Text.Encoding.Unicode.GetBytes("other")) }));
            Assert.That(again, Is.Empty);
            Assert.That(login, Is.EqualTo(new[] { "8B00" + "08" + "05000000" + "0500" + Convert.ToHexString(System.Text.Encoding.Unicode.GetBytes("other")) }));
        });
    }

    // A member who is offline is state 2 in the chat's Load and in AddMember (before, both wrote 1 for every member, so
    // the client counted and listed an offline member as online until the member's next login and logout).
    [TestCase(0, 1)]
    [TestCase(-1, 2)]
    public void MemberState_IsOnlineOrOffline(int channel, byte state) {
        test.World.Players[otherId].Channel = channel;
        test.World.Players[thirdId].Channel = channel;
        var manager = new GroupChatManager(new GroupChatInfo {
            Id = ChatId,
            Members = { new GroupChatInfo.Types.Member { CharacterId = otherId } },
        }, session);
        session.GroupChats[ChatId] = manager;
        byte[] load = test.TakeSent().Single(packet => TestGameSession.Op(packet) == SendOp.GroupChat);
        test.World.Players[otherId].Channel = 0;
        Add(thirdId, otherId, "other", session.CharacterId, otherId, thirdId);
        byte[] added = Convert.FromHexString(Sent().Single());

        Assert.Multiple(() => {
            Assert.That(load[2], Is.EqualTo(0), "Load");
            Assert.That(load[7], Is.EqualTo(1), "one member");
            Assert.That(load[8], Is.EqualTo(state), "the member's state in Load");
            int stateAt = 2 + 1 + 4 + 2 + 2 * "other".Length;
            Assert.That(added[2], Is.EqualTo(6), "AddMember");
            Assert.That(added[stateAt], Is.EqualTo(state), "the member's state in AddMember");
        });
    }
}
