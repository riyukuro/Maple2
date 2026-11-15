using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.CompilerServices;
using Grpc.Core;
using Maple2.Model.Error;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The group messenger commands the client sends (groupchatmanager.cpp): create, invite, leave and chat. Each answer
// is the exact packet the client's group messenger reads.
public class GroupChatHandlerTests {
    private const int ChatId = 5;

    private static (TestGameSession Test, GameSession Session, GroupChatHandler Handler) Scene() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        session.GroupChats ??= new ConcurrentDictionary<int, GroupChatManager>();
        test.TakeSent();
        return (test, session, new GroupChatHandler { World = test.World });
    }

    private static void Join(GameSession session, int id = ChatId) {
        session.GroupChats[id] = (GroupChatManager) RuntimeHelpers.GetUninitializedObject(typeof(GroupChatManager));
    }

    private static string Hex(ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    private static string[] Sent(TestGameSession test) =>
        test.TakeSent().Where(p => TestGameSession.Op(p) == SendOp.GroupChat).Select(Convert.ToHexString).ToArray();

    private static void Create(GroupChatHandler handler, GameSession session, string name) {
        handler.Handle(session, TestGameSession.Packet(w => {
            w.WriteByte(1);
            w.WriteUnicodeString(name);
        }));
    }

    private static void Invite(GroupChatHandler handler, GameSession session, string name, int id) {
        handler.Handle(session, TestGameSession.Packet(w => {
            w.WriteByte(2);
            w.WriteUnicodeString(name);
            w.WriteInt(id);
        }));
    }

    private static GroupChatResponse Made(int id) => new() { GroupChat = new GroupChatInfo { Id = id } };

    [Test]
    public void Create_NameAbove24Letters_IsIgnored() {
        (TestGameSession test, GameSession session, GroupChatHandler handler) = Scene();

        Create(handler, session, new string('a', 25));

        Assert.That(test.World.GroupChats, Is.Empty);
        Assert.That(Sent(test), Is.Empty);
    }

    [Test]
    public void Create_AtThreeChats_AnswersMaxGroup() {
        (TestGameSession test, GameSession session, GroupChatHandler handler) = Scene();
        Join(session, 1);
        Join(session, 2);
        Join(session, 3);

        Create(handler, session, "");

        Assert.That(test.World.GroupChats, Is.Empty);
        Assert.That(Sent(test), Is.EqualTo(new[] {
            Hex(GroupChatPacket.Error(GroupChatError.s_err_groupchat_maxgroup, session.PlayerName, session.PlayerName)),
        }));
    }

    [Test]
    public void Create_Empty_MakesTheChatAndAnswersItsId() {
        (TestGameSession test, GameSession session, GroupChatHandler handler) = Scene();
        test.World.GroupChatWorld = _ => Made(ChatId);

        Create(handler, session, "");

        GroupChatRequest request = test.World.GroupChats.Single();
        Assert.Multiple(() => {
            Assert.That(request.GroupChatCase, Is.EqualTo(GroupChatRequest.GroupChatOneofCase.Create));
            Assert.That(request.RequesterId, Is.EqualTo(session.CharacterId));
            Assert.That(session.GroupChats.ContainsKey(ChatId), Is.True);
            // The manager loads the chat to the client first, then the create answer follows.
            Assert.That(Sent(test).TakeLast(1), Is.EqualTo(new[] { Hex(GroupChatPacket.Create(ChatId)) }));
        });
    }

    [Test]
    public void Create_UnknownTarget_AnswersNullTargetAndMakesNoChat() {
        (TestGameSession test, GameSession session, GroupChatHandler handler) = Scene();
        test.World.GroupChatWorld = _ => Made(ChatId);

        Create(handler, session, "nobodyhere");

        Assert.Multiple(() => {
            Assert.That(test.World.GroupChats, Is.Empty);
            Assert.That(session.GroupChats, Is.Empty);
            Assert.That(Sent(test), Is.EqualTo(new[] {
                Hex(GroupChatPacket.Error(GroupChatError.s_err_groupchat_null_target_user, session.PlayerName, "nobodyhere")),
            }));
        });
    }

    [Test]
    public void Create_TargetAtThreeChats_AnswersMaxGroupAndMakesNoChat() {
        (TestGameSession test, GameSession session, GroupChatHandler handler) = Scene();
        test.World.GroupChatInfoWorld = _ => new GroupChatInfoResponse { Infos = { new GroupChatInfo(), new GroupChatInfo(), new GroupChatInfo() } };

        Create(handler, session, session.PlayerName);

        Assert.Multiple(() => {
            Assert.That(test.World.GroupChats, Is.Empty);
            Assert.That(Sent(test), Is.EqualTo(new[] {
                Hex(GroupChatPacket.Error(GroupChatError.s_err_groupchat_maxgroup, session.PlayerName, session.PlayerName)),
            }));
        });
    }

    [Test]
    public void Create_TargetLookupDown_AnswersAddMemberFailure() {
        (TestGameSession test, GameSession session, GroupChatHandler handler) = Scene();
        test.World.GroupChatInfoWorld = _ => throw new RpcException(new Status(StatusCode.Unavailable, "down"));

        Create(handler, session, session.PlayerName);

        Assert.Multiple(() => {
            Assert.That(test.World.GroupChats, Is.Empty);
            Assert.That(Sent(test), Is.EqualTo(new[] {
                Hex(GroupChatPacket.Error(GroupChatError.s_err_groupchat_add_member_target, session.PlayerName, session.PlayerName)),
            }));
        });
    }

    [Test]
    public void Create_WorldRefuses_SendsItsErrorWithoutATarget() {
        (TestGameSession test, GameSession session, GroupChatHandler handler) = Scene();
        test.World.GroupChatWorld = _ => new GroupChatResponse { Error = (int) GroupChatError.s_err_groupchat_maxgroup };

        Create(handler, session, "");

        Assert.Multiple(() => {
            Assert.That(session.GroupChats, Is.Empty);
            Assert.That(Sent(test), Is.EqualTo(new[] {
                Hex(GroupChatPacket.Error(GroupChatError.s_err_groupchat_maxgroup, session.PlayerName, "")),
            }));
        });
    }

    // A target is checked first, then the chat is made, then the invite goes out in that order.
    [Test]
    public void Create_WithTarget_MakesTheChatThenInvites() {
        (TestGameSession test, GameSession session, GroupChatHandler handler) = Scene();
        test.World.GroupChatWorld = request => request.GroupChatCase == GroupChatRequest.GroupChatOneofCase.Create ? Made(ChatId) : new GroupChatResponse();

        Create(handler, session, session.PlayerName);

        Assert.Multiple(() => {
            Assert.That(test.World.GroupChats.Select(r => r.GroupChatCase), Is.EqualTo(new[] {
                GroupChatRequest.GroupChatOneofCase.Create, GroupChatRequest.GroupChatOneofCase.Invite,
            }));
            Assert.That(test.World.GroupChats[1].GroupChatId, Is.EqualTo(ChatId));
            Assert.That(test.World.GroupChats[1].Invite.ReceiverId, Is.EqualTo(session.CharacterId));
            Assert.That(Sent(test).TakeLast(2), Is.EqualTo(new[] {
                Hex(GroupChatPacket.Create(ChatId)),
                Hex(GroupChatPacket.Invite(session.PlayerName, session.PlayerName, ChatId)),
            }));
        });
    }

    [Test]
    public void Invite_NotInTheChat_IsIgnored() {
        (TestGameSession test, GameSession session, GroupChatHandler handler) = Scene();

        Invite(handler, session, session.PlayerName, ChatId);

        Assert.That(test.World.GroupChats, Is.Empty);
        Assert.That(Sent(test), Is.Empty);
    }

    [Test]
    public void Invite_NameAbove24Letters_IsIgnored() {
        (TestGameSession test, GameSession session, GroupChatHandler handler) = Scene();
        Join(session);

        Invite(handler, session, new string('a', 25), ChatId);

        Assert.That(test.World.GroupChats, Is.Empty);
        Assert.That(Sent(test), Is.Empty);
    }

    [Test]
    public void Invite_UnknownTarget_AnswersNullTarget() {
        (TestGameSession test, GameSession session, GroupChatHandler handler) = Scene();
        Join(session);

        Invite(handler, session, "nobodyhere", ChatId);

        Assert.That(test.World.GroupChats, Is.Empty);
        Assert.That(Sent(test), Is.EqualTo(new[] {
            Hex(GroupChatPacket.Error(GroupChatError.s_err_groupchat_null_target_user, session.PlayerName, "nobodyhere")),
        }));
    }

    [Test]
    public void Invite_Member_SendsTheInviteAndAsksTheWorld() {
        (TestGameSession test, GameSession session, GroupChatHandler handler) = Scene();
        Join(session);

        Invite(handler, session, session.PlayerName, ChatId);

        GroupChatRequest request = test.World.GroupChats.Single();
        Assert.Multiple(() => {
            Assert.That(request.GroupChatCase, Is.EqualTo(GroupChatRequest.GroupChatOneofCase.Invite));
            Assert.That(request.GroupChatId, Is.EqualTo(ChatId));
            Assert.That(request.Invite.ReceiverId, Is.EqualTo(session.CharacterId));
            Assert.That(Sent(test), Is.EqualTo(new[] { Hex(GroupChatPacket.Invite(session.PlayerName, session.PlayerName, ChatId)) }));
        });
    }

    [Test]
    public void Invite_WorldRefuses_AddsItsError() {
        (TestGameSession test, GameSession session, GroupChatHandler handler) = Scene();
        Join(session);
        test.World.GroupChatWorld = _ => new GroupChatResponse { Error = (int) GroupChatError.s_err_groupchat_maxgroup };

        Invite(handler, session, session.PlayerName, ChatId);

        Assert.That(Sent(test), Is.EqualTo(new[] {
            Hex(GroupChatPacket.Invite(session.PlayerName, session.PlayerName, ChatId)),
            Hex(GroupChatPacket.Error(GroupChatError.s_err_groupchat_maxgroup, session.PlayerName, session.PlayerName)),
        }));
    }

    [Test]
    public void Invite_WorldDown_AnswersAddMemberFailureAndKeepsTheSession() {
        (TestGameSession test, GameSession session, GroupChatHandler handler) = Scene();
        Join(session);
        test.World.GroupChatWorld = _ => throw new RpcException(new Status(StatusCode.Unavailable, "down"));

        Assert.DoesNotThrow(() => Invite(handler, session, session.PlayerName, ChatId));

        Assert.That(Sent(test).Last(), Is.EqualTo(
            Hex(GroupChatPacket.Error(GroupChatError.s_err_groupchat_add_member_target, session.PlayerName, session.PlayerName))));
    }

    [Test]
    public void Leave_NotInTheChat_IsIgnored() {
        (TestGameSession test, GameSession session, GroupChatHandler handler) = Scene();

        handler.Handle(session, TestGameSession.Packet(w => {
            w.WriteByte(4);
            w.WriteInt(ChatId);
            w.WriteBool(false);
        }));

        Assert.That(test.World.GroupChats, Is.Empty);
    }

    [Test]
    public void Leave_WorldDown_KeepsTheSession() {
        (TestGameSession test, GameSession session, GroupChatHandler handler) = Scene();
        Join(session);
        test.World.GroupChatWorld = _ => throw new RpcException(new Status(StatusCode.Unavailable, "down"));

        Assert.DoesNotThrow(() => handler.Handle(session, TestGameSession.Packet(w => {
            w.WriteByte(4);
            w.WriteInt(ChatId);
            w.WriteBool(false);
        })));
    }

    private static void Chat(GroupChatHandler handler, GameSession session, string text, int id) {
        handler.Handle(session, TestGameSession.Packet(w => {
            w.WriteByte(10);
            w.WriteUnicodeString(text);
            w.WriteInt(id);
        }));
    }

    [Test]
    public void Chat_NotInTheChat_IsIgnored() {
        (TestGameSession test, GameSession session, GroupChatHandler handler) = Scene();

        Chat(handler, session, "hello", ChatId);

        Assert.That(test.World.GroupChats, Is.Empty);
    }

    [TestCase("")]
    [TestCase("TOOLONG")]
    public void Chat_EmptyOrAbove2048Letters_IsIgnored(string text) {
        (TestGameSession test, GameSession session, GroupChatHandler handler) = Scene();
        Join(session);

        Chat(handler, session, text == "TOOLONG" ? new string('a', 2049) : text, ChatId);

        Assert.That(test.World.GroupChats, Is.Empty);
    }

    [Test]
    public void Chat_At2048Letters_GoesToTheWorld() {
        (TestGameSession test, GameSession session, GroupChatHandler handler) = Scene();
        Join(session);
        string text = new('a', 2048);

        Chat(handler, session, text, ChatId);

        Assert.That(test.World.GroupChats.Single().Chat.Message, Is.EqualTo(text));
    }

    [Test]
    public void Chat_WorldDown_KeepsTheSession() {
        (TestGameSession test, GameSession session, GroupChatHandler handler) = Scene();
        Join(session);
        test.World.GroupChatWorld = _ => throw new RpcException(new Status(StatusCode.Unavailable, "down"));

        Assert.DoesNotThrow(() => Chat(handler, session, "hi", ChatId));
    }

    [Test]
    public void UnknownCommand_DoesNothing() {
        (TestGameSession test, GameSession session, GroupChatHandler handler) = Scene();

        handler.Handle(session, TestGameSession.Packet(w => w.WriteByte(9)));

        Assert.That(test.World.GroupChats, Is.Empty);
        Assert.That(Sent(test), Is.Empty);
    }
}
