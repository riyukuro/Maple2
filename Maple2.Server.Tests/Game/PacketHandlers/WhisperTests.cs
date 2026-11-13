using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Service;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// A whisper between two sessions that share one character table. The sender's handler asks the world; the world's
// routing is replaced by a direct call of the recipient's channel service, which is what the world calls.
public class WhisperTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private TestGameSession senderTest = null!;
    private TestGameSession recipientTest = null!;
    private UserChatHandler handler = null!;
    private GameSession sender => senderTest.Session;
    private GameSession recipient => recipientTest.Session;

    [SetUp]
    public void Login() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        senderTest = new TestGameSession(options);
        recipientTest = new TestGameSession(options);
        foreach (TestGameSession test in new[] { senderTest, recipientTest }) {
            test.AddMap(TestGameSession.Map(2000023));
            test.Start();
            test.EnterMap(TestGameSession.Map(2000023));
            test.TakeSent();
        }
        handler = new UserChatHandler { World = senderTest.World, GameStorage = senderTest.Storage, ServerTableMetadata = senderTest.ServerTableMetadata };
    }

    // The channel service of the game server that holds one session.
    private static ChannelService ChannelOf(GameSession session, TestGameSession test) {
        var server = (GameServer) RuntimeHelpers.GetUninitializedObject(typeof(GameServer));
        var sessions = new ConcurrentDictionary<long, GameSession>();
        sessions[session.CharacterId] = session;
        typeof(GameServer).GetField("sessions", Any)!.SetValue(server, sessions);
        return new ChannelService(server, null!, test.Storage, null!, null!, null!);
    }

    // The recipient is online: the world knows the character and hands the line to its game server.
    private void RecipientOnline() {
        senderTest.World.AddPlayer(recipient.AccountId, recipient.CharacterId, recipient.PlayerName);
        ChannelService channel = ChannelOf(recipient, recipientTest);
        senderTest.World.ChatDelivery = request => channel.Chat(request, null!).GetAwaiter().GetResult();
    }

    // The way back: the recipient's world hands lines for the sender to the sender's game server.
    private void SenderOnline() {
        ChannelService channel = ChannelOf(sender, senderTest);
        recipientTest.World.ChatDelivery = request => channel.Chat(request, null!).GetAwaiter().GetResult();
    }

    // Chat kind 6, as the client's whisper block option sends it: the sender's name and no text.
    private void Reject(string name) {
        new UserChatHandler { World = recipientTest.World, GameStorage = recipientTest.Storage, ServerTableMetadata = recipientTest.ServerTableMetadata }
            .Handle(recipient, TestGameSession.Packet(writer => {
                writer.WriteInt((int) ChatType.WhisperReject);
                writer.WriteUnicodeString();
                writer.WriteUnicodeString(name);
                writer.WriteLong();
            }));
    }

    private void Whisper(string name, string text) {
        handler.Handle(sender, TestGameSession.Packet(writer => {
            writer.WriteInt((int) ChatType.WhisperTo);
            writer.WriteUnicodeString(text);
            writer.WriteUnicodeString(name);
            writer.WriteLong();
        }));
    }

    private static string[] Chat(TestGameSession test) {
        return test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.UserChat).Select(Convert.ToHexString).ToArray();
    }

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    [Test]
    public void Whisper_ReachesTheRecipientWithTheSendersPicture() {
        Assume.That(sender.CharacterId, Is.Not.EqualTo(recipient.CharacterId));
        RecipientOnline();
        sender.Player.Value.Character.Picture = "portrait";

        Whisper(recipient.PlayerName, "psst");

        Assert.That(Chat(recipientTest), Is.EqualTo(new[] {
            Hex(ChatPacket.Whisper(sender.AccountId, sender.CharacterId, sender.PlayerName, "psst", "portrait")),
        }));
        // The sender's own copy names the recipient, with account and character id 0 as KMS2 sends it.
        Assert.That(Chat(senderTest), Is.EqualTo(new[] {
            Hex(ChatPacket.Whisper(0, 0, recipient.PlayerName, "psst")),
        }));
        ChatRequest request = senderTest.World.Chats.Single();
        Assert.That(request.Whisper.RecipientId, Is.EqualTo(recipient.CharacterId));
    }

    // The client sends the text after "/w name" as typed (uimessenger.cpp:4377-4440): a leading "/" is the line's, not
    // a command.
    [Test]
    public void Whisper_StartingWithASlashIsDeliveredAndNotRun() {
        RecipientOnline();

        Whisper(recipient.PlayerName, "/help");

        Assert.That(Chat(recipientTest), Is.EqualTo(new[] {
            Hex(ChatPacket.Whisper(sender.AccountId, sender.CharacterId, sender.PlayerName, "/help", sender.Player.Value.Character.Picture)),
        }));
        Assert.That(Chat(senderTest), Is.EqualTo(new[] {
            Hex(ChatPacket.Whisper(0, 0, recipient.PlayerName, "/help")),
        }));
    }

    // The name is matched without case, and the own copy shows the name as the character has it.
    [Test]
    public void Whisper_FindsTheNameInAnyCase() {
        RecipientOnline();

        Whisper(recipient.PlayerName.ToUpperInvariant(), "psst");

        Assert.That(Chat(recipientTest), Has.Length.EqualTo(1));
        Assert.That(Chat(senderTest), Is.EqualTo(new[] {
            Hex(ChatPacket.Whisper(0, 0, recipient.PlayerName, "psst")),
        }));
    }

    // The character exists but its game server has no session: the sender gets the notice and no own copy.
    [Test]
    public void Whisper_ToAPlayerWhoIsOfflineSendsTheNotice() {
        senderTest.World.AddPlayer(recipient.AccountId, recipient.CharacterId, recipient.PlayerName);
        senderTest.World.ChatDelivery = _ => throw new RpcException(new Status(StatusCode.NotFound, "offline"));

        Whisper(recipient.PlayerName, "psst");

        Assert.That(Chat(senderTest), Is.EqualTo(new[] { Hex(ChatPacket.Alert(StringCode.s_err_cannot_find_user)) }));
        Assert.That(Chat(recipientTest), Is.Empty);
    }

    // The recipient's client refuses whispers: it answers with kind 6 naming the sender, and the sender gets the
    // notice with the recipient's ids, name and channel (KMS2 layout).
    [Test]
    public void Reject_SendsTheNoticeToTheWhisperer() {
        RecipientOnline();
        SenderOnline();
        Whisper(recipient.PlayerName, "psst");
        Chat(senderTest);
        Chat(recipientTest);

        Reject(sender.PlayerName);

        Assert.That(Chat(senderTest), Is.EqualTo(new[] {
            Hex(ChatPacket.WhisperReject(recipient.AccountId, recipient.CharacterId, recipient.PlayerName, UserChatHandler.LineChannel(recipient))),
        }));
        Assert.That(Chat(recipientTest), Is.Empty);
        ChatRequest request = recipientTest.World.Chats.Single();
        Assert.That(request.Whisper.Reject, Is.True);
        Assert.That(request.Whisper.RecipientId, Is.EqualTo(sender.CharacterId));
        Assert.That(request.Message, Is.Empty);
    }

    // A kind 6 for a player whose whisper never reached this session, or a second one for the same whisper, sends
    // nothing.
    [Test]
    public void Reject_WithoutAWhisperSendsNothing() {
        RecipientOnline();
        SenderOnline();

        Reject(sender.PlayerName);
        Assert.That(recipientTest.World.Chats, Is.Empty);

        Whisper(recipient.PlayerName, "psst");
        Reject(sender.PlayerName);
        Reject(sender.PlayerName);
        Assert.That(recipientTest.World.Chats, Has.Count.EqualTo(1));
    }

    // The notice line: the client drops a line with empty text before it looks at the type, so it carries common
    // string 0, as KMS2 sends it.
    [Test]
    public void RejectLine_HasTheKms2Layout() {
        byte[] line = ChatPacket.WhisperReject(5, 6, "ab", 3).ToArray();

        // op 2, account 8, character 8, name 2 + 4, common 1, string id 4, type 4, gm 1, channel 4, end 1.
        Assert.That(line, Has.Length.EqualTo(2 + 8 + 8 + 6 + 1 + 4 + 4 + 1 + 4 + 1));
        Assert.That(BitConverter.ToInt64(line, 2), Is.EqualTo(5));
        Assert.That(BitConverter.ToInt64(line, 10), Is.EqualTo(6));
        Assert.That(line[24], Is.EqualTo(1));
        Assert.That(BitConverter.ToInt32(line, 25), Is.EqualTo(0));
        Assert.That(BitConverter.ToInt32(line, 29), Is.EqualTo((int) ChatType.WhisperReject));
        Assert.That(BitConverter.ToInt32(line, 34), Is.EqualTo(3));
    }

    [Test]
    public void Whisper_ToANameNobodyHasSendsTheNotice() {
        Whisper("nobody", "psst");

        Assert.That(Chat(senderTest), Is.EqualTo(new[] { Hex(ChatPacket.Alert(StringCode.s_whisper_err_target)) }));
        Assert.That(senderTest.World.Chats, Is.Empty);
    }

    [Test]
    public void Whisper_ToOneselfSendsTheNotice() {
        Whisper(sender.PlayerName, "psst");

        Assert.That(Chat(senderTest), Is.EqualTo(new[] { Hex(ChatPacket.Alert(StringCode.s_whisper_err_myself)) }));
        Assert.That(senderTest.World.Chats, Is.Empty);
    }
}
