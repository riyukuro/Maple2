using System;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
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

    // Normal chat goes to the field. The int after the GM flag is the channel in a field that is no instance and 0
    // in an instanced one.
    [TestCase(InstanceType.none, Channel)]
    [TestCase(InstanceType.solo, 0)]
    [TestCase(InstanceType.ugcMap, 0)]
    public void NormalChat_GoesToTheFieldWithTheChannelOutsideInstances(InstanceType instance, int expected) {
        GameSession session = Login(meret: 0);
        typeof(FieldManager).GetProperty("FieldInstance")!.SetValue(session.Field,
            new InstanceFieldMetadata(0, instance, 0, false, 0, true, 0, 0, 0, 0));

        Say(session, ChatType.Normal, "hi");

        byte[] line = test.TakeSent().Single(packet => TestGameSession.Op(packet) == Maple2.Server.Core.Constants.SendOp.UserChat);
        int flag = 2 + 8 + 8 + (2 + session.PlayerName.Length * 2) + 1 + (2 + 2 * 2) + 4;
        Assert.That(BitConverter.ToInt32(line, flag - 4), Is.EqualTo((int) ChatType.Normal));
        Assert.That(BitConverter.ToInt32(line, flag + 1), Is.EqualTo(expected));
        Assert.That(test.World.Chats, Is.Empty);
    }

    // Party and guild lines hand the same channel int to the world.
    [TestCase(InstanceType.none, Channel)]
    [TestCase(InstanceType.solo, 0)]
    public void LineChannel_IsZeroInAnInstance(InstanceType instance, int expected) {
        GameSession session = Login(meret: 0);
        typeof(FieldManager).GetProperty("FieldInstance")!.SetValue(session.Field,
            new InstanceFieldMetadata(0, instance, 0, false, 0, true, 0, 0, 0, 0));

        Assert.That(UserChatHandler.LineChannel(session), Is.EqualTo(expected));
    }

    // The received whisper carries the sender's channel after the GM flag; the sender's own copy keeps 0.
    [Test]
    public void Whisper_CarriesTheChannelInTheReceivedLineOnly() {
        byte[] received = ChatPacket.Whisper(1, 2, "ab", "hi", "pic", channel: Channel).ToArray();
        byte[] plain = ChatPacket.Whisper(1, 2, "ab", "hi", "pic").ToArray();
        // op 2, ids 16, name 2 + 4, flag 1, text 2 + 4, type 4, gm 1: the int starts at 36.
        Assert.That(BitConverter.ToInt32(received, 36), Is.EqualTo(Channel));
        Assert.That(BitConverter.ToInt32(plain, 36), Is.EqualTo(0));
        Assert.That(ChatPacket.Whisper(0, 2, "ab", "hi", channel: Channel).ToArray(),
            Is.EqualTo(ChatPacket.Whisper(0, 2, "ab", "hi").ToArray()));
    }

    // A rock-paper-scissors line (type 23) goes back to the field as type 23 with channel 0 and nothing to the world.
    [Test]
    public void MicroGameLine_GoesToTheFieldWithItsType() {
        GameSession session = Login(meret: 0);

        Say(session, (ChatType) 23, "hi");

        byte[][] lines = test.TakeSent().Where(packet => TestGameSession.Op(packet) == Maple2.Server.Core.Constants.SendOp.UserChat).ToArray();
        Assert.That(lines, Has.Length.EqualTo(1));
        Assert.That(lines[0], Is.EqualTo(ChatPacket.Message(session.AccountId, session.CharacterId, session.PlayerName, ChatType.MicroGame, "hi").ToArray()));
        // op 2, ids 16, name 2 + 2n, flag 1, text 2 + 4: the type, then gm 1 and the channel.
        int type = 2 + 16 + 2 + 2 * session.PlayerName.Length + 1 + 2 + 4;
        Assert.That(BitConverter.ToInt32(lines[0], type), Is.EqualTo(23));
        Assert.That(BitConverter.ToInt32(lines[0], type + 5), Is.EqualTo(0));
        Assert.That(test.World.Chats, Is.Empty);
    }

    // Party, guild and system notice lines carry the channel the request names.
    [Test]
    public void Message_WritesTheChannelOfTheRequest() {
        foreach (ChatType type in new[] { ChatType.Party, ChatType.Guild, ChatType.SystemNotice }) {
            byte[] line = ChatPacket.Message(1, 2, "ab", type, "hi", channel: Channel).ToArray();
            Assert.That(BitConverter.ToInt32(line, 36), Is.EqualTo(Channel), type.ToString());
        }
    }
}
