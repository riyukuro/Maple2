using System;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// Mail 13, 18 and 19 name mails from the client's list; a count above what that list can hold (the inbox and the
// expired mails dropped since login) or below 0 is refused whole, before any id is looked up.
public class MailBulkCountTests {
    private const byte Delete = 13;
    private const byte BulkRead = 18;
    private const byte BulkCollect = 19;

    private TestGameSession test = null!;
    private readonly MailHandler handler = new() { BanWordStorage = TestGameSession.BanWords() };

    private long StoreMail(long expiryTime = 0) {
        using GameStorage.Request db = test.Storage.Context();
        var mail = new Mail { ReceiverId = test.CharacterId, Type = MailType.System, Title = "t", Content = "c" };
        if (expiryTime != 0) {
            mail.ExpiryTime = expiryTime;
        }
        return db.CreateMail(mail)?.Id ?? throw new InvalidOperationException("mail row not created");
    }

    private void Handle(GameSession session, byte command, int count, params long[] mailIds) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            writer.WriteInt(count);
            foreach (long mailId in mailIds) {
                writer.WriteLong(mailId);
            }
        }));
    }

    private Mail? Stored(long mailId) {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetMail(mailId, test.CharacterId);
    }

    [TestCase(Delete, 2)]
    [TestCase(BulkRead, 2)]
    [TestCase(BulkCollect, 2)]
    [TestCase(Delete, -1)]
    [TestCase(BulkRead, -1)]
    [TestCase(BulkCollect, -1)]
    public void ACountOutsideTheListIsRefused(byte command, int count) {
        test = new TestGameSession();
        long mailId = StoreMail();
        GameSession session = test.Start();
        test.TakeSent();

        Handle(session, command, count, mailId, mailId);

        Assert.That(test.TakeSent(), Is.Empty);
        Mail? stored = Stored(mailId);
        Assert.That(stored, Is.Not.Null);
        Assert.That(stored!.ReadTime, Is.EqualTo(0));
    }

    [TestCase(Delete)]
    [TestCase(BulkRead)]
    public void ACountOfTheWholeListIsDone(byte command) {
        test = new TestGameSession();
        long first = StoreMail();
        long second = StoreMail();
        GameSession session = test.Start();
        test.TakeSent();

        Handle(session, command, 2, first, second);

        foreach (long mailId in new[] { first, second }) {
            Mail? stored = Stored(mailId);
            if (command == Delete) {
                Assert.That(stored, Is.Null);
            } else {
                Assert.That(stored?.ReadTime, Is.GreaterThan(0));
            }
        }
    }

    [Test]
    public void AnExpiredMailDroppedFromTheInboxStillCountsOnTheList() {
        test = new TestGameSession();
        long mailId = StoreMail();
        long expired = StoreMail(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 60);
        GameSession session = test.Start();
        test.TakeSent();
        Assert.That(Stored(expired), Is.Null, "the login fetch deletes the expired mail");

        Handle(session, BulkRead, 2, expired, mailId);

        Assert.That(Stored(mailId)?.ReadTime, Is.GreaterThan(0));
    }
}
