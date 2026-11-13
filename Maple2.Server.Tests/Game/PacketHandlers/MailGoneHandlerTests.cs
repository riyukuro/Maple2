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

// A read or collect of a mail the player no longer has: the client has no text for the server's not-found code (OnMail
// mode 0x14 falls to "System Error: Mail"), so the mail is taken off its list with mode 0xd instead.
public class MailGoneHandlerTests {
    private const long Missing = 987654321;

    private TestGameSession test = null!;
    private readonly MailHandler handler = new();

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    private string[] MailSent() => test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Mail)
        .Select(Convert.ToHexString).ToArray();

    private void Handle(GameSession session, byte command, params long[] mailIds) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            if (command >= 18) {
                writer.WriteInt(mailIds.Length);
            }
            foreach (long mailId in mailIds) {
                writer.WriteLong(mailId);
            }
        }));
    }

    [TestCase((byte) 2)]  // Read
    [TestCase((byte) 11)] // Collect
    public void AMissingMailIsTakenOffTheList(byte command) {
        test = new TestGameSession();
        GameSession session = test.Start();
        test.TakeSent();

        Handle(session, command, Missing);

        Assert.That(MailSent(), Is.EqualTo(new[] { Hex(MailPacket.Deleted(Missing)) }));
    }

    [Test]
    public void ABulkReadGoesOnPastAMissingMail() {
        test = new TestGameSession();
        long mailId;
        using (GameStorage.Request db = test.Storage.Context()) {
            mailId = db.CreateMail(new Mail { ReceiverId = test.CharacterId, Type = MailType.System, Title = "t", Content = "c" })?.Id
                     ?? throw new InvalidOperationException("mail row not created");
        }
        GameSession session = test.Start();
        test.TakeSent();

        Handle(session, 18, Missing, mailId);

        string[] sent = MailSent();
        Assert.That(sent.First(), Is.EqualTo(Hex(MailPacket.Deleted(Missing))));
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.GetMail(mailId, test.CharacterId)?.ReadTime, Is.GreaterThan(0));
        }
    }
}
