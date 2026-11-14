using System;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The client's mail menu badge is set only by the count notice (OnMail mode 14): a read or delete that changes the
// unread count sends it again, one per request, without the new-mail notice.
public class MailUnreadCountTests {
    private TestGameSession test = null!;
    private readonly MailHandler handler = new() { BanWordStorage = TestGameSession.BanWords() };

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    private long[] StoreMails(int count) {
        using GameStorage.Request db = test.Storage.Context();
        return Enumerable.Range(0, count)
            .Select(_ => (db.CreateMail(new Mail { ReceiverId = test.CharacterId, Type = MailType.Player, Title = "t", Content = "c" })
                          ?? throw new InvalidOperationException("mail row not created")).Id)
            .ToArray();
    }

    private void Bulk(GameSession session, byte mode, params long[] mailIds) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(mode);
            writer.WriteInt(mailIds.Length);
            foreach (long mailId in mailIds) {
                writer.WriteLong(mailId);
            }
        }));
    }

    private string[] MailSent() => test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Mail).Select(Convert.ToHexString).ToArray();

    [Test]
    public void BulkDelete_OfUnreadMails_SendsTheNewCountOnce() {
        test = new TestGameSession();
        long[] mails = StoreMails(5);
        GameSession session = test.Start();
        test.TakeSent();

        Bulk(session, 13, mails[0], mails[1], mails[2]);

        Assert.That(MailSent(), Is.EqualTo(new[] {
            Hex(MailPacket.Deleted(mails[0])), Hex(MailPacket.Deleted(mails[1])), Hex(MailPacket.Deleted(mails[2])),
            Hex(MailPacket.Notify(2, false, 0)),
        }));
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void Delete_OfAReadMail_SendsNoCount() {
        test = new TestGameSession();
        long[] mails = StoreMails(2);
        GameSession session = test.Start();
        Bulk(session, 18, mails[0]);
        test.TakeSent();

        Bulk(session, 13, mails[0]);

        Assert.That(MailSent(), Is.EqualTo(new[] { Hex(MailPacket.Deleted(mails[0])) }));
    }

    [Test]
    public void BulkRead_SendsTheNewCountAfterTheReads() {
        test = new TestGameSession();
        long[] mails = StoreMails(3);
        GameSession session = test.Start();
        test.TakeSent();

        Bulk(session, 18, mails[0], mails[1]);

        string[] sent = MailSent();
        Assert.That(sent, Has.Length.EqualTo(3));
        Assert.That(sent[^1], Is.EqualTo(Hex(MailPacket.Notify(1, false, 0))));
    }
}
