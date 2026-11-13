using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Service;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// ChannelService.MailNotification on the receiver's game server: a notification with a gift also sends the gift
// notice (Mail 0x16); a plain one does not.
public class MailGiftChannelTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private TestGameSession receiverTest = null!;
    private GameSession receiver => receiverTest.Session;
    private ChannelService channel = null!;

    [SetUp]
    public void SetUp() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        receiverTest = new TestGameSession(options);
        receiverTest.AddMap(TestGameSession.Map(2000023));
        receiverTest.Start();
        receiverTest.EnterMap(TestGameSession.Map(2000023));
        receiverTest.TakeSent();

        var server = (GameServer) RuntimeHelpers.GetUninitializedObject(typeof(GameServer));
        var sessions = new ConcurrentDictionary<long, GameSession>();
        sessions[receiver.CharacterId] = receiver;
        typeof(GameServer).GetField("sessions", Any)!.SetValue(server, sessions);
        channel = new ChannelService(server, null!, receiverTest.Storage, null!, null!, null!);
    }

    private string[] MailSent() {
        return receiverTest.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Mail).Select(Convert.ToHexString).ToArray();
    }

    [Test]
    public void AGiftNotification_SendsTheGiftNotice() {
        MailNotificationResponse response = channel.MailNotification(new MailNotificationRequest {
            CharacterId = receiver.CharacterId,
            MailId = 1,
            Gift = new MailGift { Sender = "Ab", ItemId = 11300001, Rarity = 4, Amount = 1, UgcUrl = "u", UgcTitle = "T" },
        }, null!).GetAwaiter().GetResult();

        Assert.That(response.Delivered, Is.True);
        string gift = Convert.ToHexString(MailPacket.Gift("Ab", 11300001, 4, 1, "u", "T").ToArray());
        Assert.That(MailSent(), Has.Exactly(1).EqualTo(gift));
    }

    [Test]
    public void APlainNotification_SendsNoGiftNotice() {
        channel.MailNotification(new MailNotificationRequest {
            CharacterId = receiver.CharacterId,
            MailId = 1,
        }, null!).GetAwaiter().GetResult();

        string[] sent = MailSent();
        Assert.That(sent, Is.Not.Empty);
        Assert.That(sent.Select(hex => hex.Substring(4, 2)), Has.None.EqualTo("16"));
    }
}
