using System;
using System.Linq;
using Maple2.Model.Error;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// A mail between two sessions that share one database. The sender's handler stores the mail and tells the world;
// the world's routing is replaced by what the recipient's channel service does with the notification.
public class MailTwoSessionTests {
    private const byte Load = 0;
    private const byte Send = 1;
    private const byte Read = 2;

    private TestGameSession senderTest = null!;
    private TestGameSession recipientTest = null!;
    private readonly MailHandler handler = new();
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
    }

    private void SendMail(string name, string title = "hello", string content = "text") {
        handler.Handle(sender, TestGameSession.Packet(writer => {
            writer.WriteByte(Send);
            writer.WriteUnicodeString(name);
            writer.WriteUnicodeString(title);
            writer.WriteUnicodeString(content);
        }));
    }

    // ChannelService.MailNotification: the recipient's session refreshes its inbox and sends the unread count.
    private long Deliver() {
        MailNotificationRequest request = senderTest.World.MailNotifications.Single();
        Assert.That(request.CharacterId, Is.EqualTo(recipient.CharacterId));
        recipient.Mail.Notify(true);
        return request.MailId;
    }

    private static byte[][] Mail(TestGameSession test) {
        return test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Mail).ToArray();
    }

    private static string[] Hex(byte[][] packets) => packets.Select(Convert.ToHexString).ToArray();

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    [Test]
    public void Mail_ReachesTheRecipientAndIsRead() {
        Assume.That(sender.CharacterId, Is.Not.EqualTo(recipient.CharacterId));

        SendMail(recipient.PlayerName);

        Assert.That(Hex(Mail(senderTest)), Is.EqualTo(new[] { Hex(MailPacket.Send()) }));
        long mailId = Deliver();
        Assert.That(Hex(Mail(recipientTest)), Is.EqualTo(new[] { Hex(MailPacket.Notify(1, true)) }));

        // The list is a start mark, the mails and an end mark.
        handler.Handle(recipient, TestGameSession.Packet(writer => writer.WriteByte(Load)));
        byte[][] list = Mail(recipientTest);
        Assert.That(list.Select(packet => packet[2]), Is.EqualTo(new byte[] { 16, 0, 17 }));
        Assert.That(BitConverter.ToInt32(list[1], 3), Is.EqualTo(1));

        handler.Handle(recipient, TestGameSession.Packet(writer => {
            writer.WriteByte(Read);
            writer.WriteLong(mailId);
        }));
        byte[][] read = Mail(recipientTest);
        Assert.That(read, Is.Not.Empty);
        Assert.That(read[0][2], Is.EqualTo(Read));
        Assert.That(BitConverter.ToInt64(read[0], 3), Is.EqualTo(mailId));

        // Read mail no longer counts.
        recipient.Mail.Notify(true);
        Assert.That(Hex(Mail(recipientTest)).Last(), Is.EqualTo(Hex(MailPacket.Notify(0, false))));
        // The sender's inbox did not get it.
        handler.Handle(sender, TestGameSession.Packet(writer => writer.WriteByte(Load)));
        Assert.That(Mail(senderTest).Select(packet => packet[2]), Is.EqualTo(new byte[] { 16, 17 }));
    }

    // Only the recipient can read the mail.
    [Test]
    public void Mail_CannotBeReadByTheSender() {
        Assume.That(sender.CharacterId, Is.Not.EqualTo(recipient.CharacterId));
        SendMail(recipient.PlayerName);
        long mailId = Deliver();
        senderTest.TakeSent();

        handler.Handle(sender, TestGameSession.Packet(writer => {
            writer.WriteByte(Read);
            writer.WriteLong(mailId);
        }));

        Assert.That(Hex(Mail(senderTest)), Is.EqualTo(new[] { Hex(MailPacket.Error(MailError.mail_not_found, Read)) }));
        recipient.Mail.Notify(true);
        Assert.That(Hex(Mail(recipientTest)).Last(), Is.EqualTo(Hex(MailPacket.Notify(1, true))));
    }

    [Test]
    public void Mail_ToTheOwnNameIsRefused() {
        SendMail(sender.PlayerName);

        Assert.That(Hex(Mail(senderTest)), Is.EqualTo(new[] { Hex(MailPacket.Error(MailError.s_mail_error_recipient_equal_sender, Send)) }));
        Assert.That(senderTest.World.MailNotifications, Is.Empty);
    }

    [Test]
    public void Mail_ToANameNobodyHasIsRefused() {
        SendMail("nobody");

        Assert.That(Hex(Mail(senderTest)), Is.EqualTo(new[] { Hex(MailPacket.Error(MailError.s_mail_error_username, Send)) }));
        Assert.That(senderTest.World.MailNotifications, Is.Empty);
        Assert.That(Mail(recipientTest), Is.Empty);
    }

    [Test]
    public void Mail_WithoutATitleIsRefused() {
        SendMail(recipient.PlayerName, title: "");

        Assert.That(Hex(Mail(senderTest)), Is.EqualTo(new[] { Hex(MailPacket.Error(MailError.s_mail_error_sendmail, Send)) }));
        Assert.That(senderTest.World.MailNotifications, Is.Empty);
    }
}
