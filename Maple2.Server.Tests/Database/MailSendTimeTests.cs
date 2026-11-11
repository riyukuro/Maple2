using System;
using Maple2.Model.Game;

namespace Maple2.Server.Tests.Database;

// Mail.SendTime is the epoch the mail list shows as "Sent". A new mail takes the server's time when it is
// converted for the insert, so the epoch read back is now, whatever time zone the database runs in.
public class MailSendTimeTests {
    [Test]
    public void NewMailIsSentNow() {
        Maple2.Database.Model.Mail model = new Mail();
        Assert.That(model.SendTime.Kind, Is.EqualTo(DateTimeKind.Local));

        // The column reads back with no kind; the epoch is the same.
        model.SendTime = DateTime.SpecifyKind(model.SendTime, DateTimeKind.Unspecified);
        Mail mail = model;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Assert.That(mail.SendTime, Is.InRange(now - 5, now + 5));
    }

    [Test]
    public void SentMailKeepsItsTime() {
        long sent = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3600;
        Maple2.Database.Model.Mail model = new Mail { SendTime = sent };
        Mail mail = model;
        Assert.That(mail.SendTime, Is.EqualTo(sent));
    }
}
