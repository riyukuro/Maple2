using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.CompilerServices;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;

namespace Maple2.Server.Tests.Game;

// A group chat invite turned down by the client's refuse-invites option: the client leaves with GroupChat 4 and
// refused 1 (groupchatmanager.cpp:630-643), and the other members get RemoveMember 7 with reason 1, which the group
// messenger shows as a declined invitation instead of a member leaving (uigroupmessenger.cpp:773-776).
public class GroupChatDeclineTests {
    private const int ChatId = 5;

    [TestCase(false, 0)]
    [TestCase(true, 1)]
    public void RemoveMember_IsIdReasonName(bool refused, byte reason) {
        var reader = new ByteReader(GroupChatPacket.RemoveMember("other", ChatId, refused).ToArray());
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.GroupChat));
            Assert.That(reader.ReadByte(), Is.EqualTo(7));
            Assert.That(reader.ReadInt(), Is.EqualTo(ChatId));
            Assert.That(reader.ReadByte(), Is.EqualTo(reason));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("other"));
            Assert.That(reader.Available, Is.EqualTo(0));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Leave_PassesTheRefusalToTheWorld(bool refused) {
        var test = new TestGameSession();
        GameSession session = test.Start();
        session.GroupChats ??= new ConcurrentDictionary<int, GroupChatManager>();
        session.GroupChats[ChatId] = (GroupChatManager) RuntimeHelpers.GetUninitializedObject(typeof(GroupChatManager));
        var handler = new GroupChatHandler { World = test.World };

        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(4);
            writer.WriteInt(ChatId);
            writer.WriteBool(refused);
        }));

        GroupChatRequest request = test.World.GroupChats.Single();
        Assert.Multiple(() => {
            Assert.That(request.GroupChatCase, Is.EqualTo(GroupChatRequest.GroupChatOneofCase.Leave));
            Assert.That(request.Leave.Refused, Is.EqualTo(refused));
        });
    }

    [TestCase(false, 0)]
    [TestCase(true, 1)]
    public void MemberRemoval_TellsTheOthersTheReason(bool refused, byte reason) {
        var test = new TestGameSession();
        GameSession session = test.Start();
        long otherId = session.CharacterId + 500;
        test.World.AddPlayer(session.AccountId + 500, otherId, "other");
        var manager = new GroupChatManager(new GroupChatInfo {
            Id = ChatId,
            Members = { new GroupChatInfo.Types.Member { CharacterId = otherId } },
        }, session);
        test.TakeSent();

        Assert.That(manager.RemoveMember(otherId, refused), Is.True);

        byte[] sent = test.TakeSent().Single(packet => TestGameSession.Op(packet) == SendOp.GroupChat);
        Assert.That(Convert.ToHexString(sent), Is.EqualTo("8B00" + "07" + "05000000" + reason.ToString("X2") + "0500" + Convert.ToHexString(System.Text.Encoding.Unicode.GetBytes("other"))));
    }
}
