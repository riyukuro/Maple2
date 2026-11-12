using System;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Server.Game;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// UserChat from the client: i32 type, text, recipient name, i64 club id. World and channel lines carry the
// sender's channel, which the client prints in front of a world line.
public class UserChatHandlerTests {
    private const short Channel = 7;
    private static readonly FieldInfo ChannelField = typeof(GameServer).GetField("_channel", BindingFlags.NonPublic | BindingFlags.Static)!;

    private TestGameSession test = null!;
    private UserChatHandler handler = null!;
    private short channelBefore;

    [SetUp]
    public void SetUp() {
        channelBefore = GameServer.GetChannel();
        ChannelField.SetValue(null, Channel);
    }

    [TearDown]
    public void TearDown() {
        ChannelField.SetValue(null, channelBefore);
    }

    private GameSession Login(long meret) {
        test = new TestGameSession();
        test.AddMap(TestGameSession.Map(2000023));
        handler = new UserChatHandler { World = test.World, GameStorage = test.Storage, ServerTableMetadata = test.ServerTableMetadata };
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        session.Currency.Meret = meret;
        test.TakeSent();
        return session;
    }

    private void Say(GameSession session, ChatType type, string text) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteInt((int) type);
            writer.WriteUnicodeString(text);
            writer.WriteUnicodeString();
            writer.WriteLong();
        }));
    }

    [Test]
    public void WorldChat_HandsTheSendersChannelToTheWorld() {
        GameSession session = Login(meret: 100000);

        Say(session, ChatType.World, "hello");

        Assert.That(test.World.Chats, Has.Count.EqualTo(1));
        ChatRequest request = test.World.Chats[0];
        Assert.That(request.ChatCase, Is.EqualTo(ChatRequest.ChatOneofCase.World));
        Assert.That(request.Channel, Is.EqualTo(Channel));
        Assert.That(request.Message, Is.EqualTo("hello"));
        Assert.That(session.Currency.Meret, Is.EqualTo(100000 - test.ServerTableMetadata.ConstantsTable.MeratConsumeWorldChat));
    }

    [Test]
    public void WorldChat_WithoutMeretSendsNothingToTheWorld() {
        GameSession session = Login(meret: 0);
        Assume.That(test.ServerTableMetadata.ConstantsTable.MeratConsumeWorldChat, Is.GreaterThan(0));

        Say(session, ChatType.World, "hello");

        Assert.That(test.World.Chats, Is.Empty);
    }

    // Normal chat goes to the field with no channel: the int after the GM flag stays 0.
    [Test]
    public void NormalChat_GoesToTheFieldWithChannelZero() {
        GameSession session = Login(meret: 0);

        Say(session, ChatType.Normal, "hi");

        byte[] line = test.TakeSent().Single(packet => TestGameSession.Op(packet) == Maple2.Server.Core.Constants.SendOp.UserChat);
        int flag = 2 + 8 + 8 + (2 + session.PlayerName.Length * 2) + 1 + (2 + 2 * 2) + 4;
        Assert.That(BitConverter.ToInt32(line, flag - 4), Is.EqualTo((int) ChatType.Normal));
        Assert.That(BitConverter.ToInt32(line, flag + 1), Is.EqualTo(0));
        Assert.That(test.World.Chats, Is.Empty);
    }
}
