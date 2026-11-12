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

    // The recipient is online: the world knows the character and hands the line to its game server.
    private void RecipientOnline() {
        senderTest.World.AddPlayer(recipient.AccountId, recipient.CharacterId, recipient.PlayerName);
        var server = (GameServer) RuntimeHelpers.GetUninitializedObject(typeof(GameServer));
        var sessions = new ConcurrentDictionary<long, GameSession>();
        sessions[recipient.CharacterId] = recipient;
        typeof(GameServer).GetField("sessions", Any)!.SetValue(server, sessions);
        var channel = new ChannelService(server, null!, recipientTest.Storage, null!, null!, null!);
        senderTest.World.ChatDelivery = request => channel.Chat(request, null!).GetAwaiter().GetResult();
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
        // The sender's own copy names the recipient's character and no account.
        Assert.That(Chat(senderTest), Is.EqualTo(new[] {
            Hex(ChatPacket.Whisper(0, recipient.CharacterId, recipient.PlayerName, "psst")),
        }));
        ChatRequest request = senderTest.World.Chats.Single();
        Assert.That(request.Whisper.RecipientId, Is.EqualTo(recipient.CharacterId));
    }

    // The name is matched without case, and the own copy shows the name as the character has it.
    [Test]
    public void Whisper_FindsTheNameInAnyCase() {
        RecipientOnline();

        Whisper(recipient.PlayerName.ToUpperInvariant(), "psst");

        Assert.That(Chat(recipientTest), Has.Length.EqualTo(1));
        Assert.That(Chat(senderTest), Is.EqualTo(new[] {
            Hex(ChatPacket.Whisper(0, recipient.CharacterId, recipient.PlayerName, "psst")),
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
