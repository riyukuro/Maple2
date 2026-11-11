using System;
using Maple2.Database.Extensions;

namespace Maple2.Server.Tests.Game.Packets;

public class MailExpiryBackfillTests {
    [Test]
    public void SelectsOnlyTheUnsetExpiry() {
        // Expiry 0 as CreateMail stored it, in this zone and in the extreme zones.
        Assert.That(MailExpiryBackfill.Selects(0L.FromEpochSeconds()), Is.True);
        foreach (int hours in new[] { -12, -5, 0, 9, 14 }) {
            DateTime stored = DateTimeOffset.FromUnixTimeSeconds(0).ToOffset(TimeSpan.FromHours(hours)).DateTime;
            Assert.That(MailExpiryBackfill.Selects(stored), Is.True, $"UTC{hours:+0;-0}");
        }

        // Any real expiry, even one long run out, is left alone.
        Assert.That(MailExpiryBackfill.Selects(new DateTime(1970, 1, 2)), Is.False);
        Assert.That(MailExpiryBackfill.Selects(1_700_000_000L.FromEpochSeconds()), Is.False);
    }

    [Test]
    public void SqlUsesTheMigrationTimeAndTheSameCutoff() {
        Assert.That(MailExpiryBackfill.Days, Is.EqualTo(30));
        Assert.That(MailExpiryBackfill.Sql(), Is.EqualTo(
            "UPDATE `mail` SET `ExpiryTime` = DATE_ADD(NOW(6), INTERVAL 30 DAY) WHERE `ExpiryTime` < '1970-01-02 00:00:00';"));
    }
}
