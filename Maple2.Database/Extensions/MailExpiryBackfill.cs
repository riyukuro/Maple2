using System.Globalization;

namespace Maple2.Database.Extensions;

// Mails created before CreateMail filled the expiry were stored with expiry 0, which the client treats as
// expired and never lists. The migration MailExpiryBackfill gives each of them an expiry.
public static class MailExpiryBackfill {
    // MailExpiryDays of table/Server/constants.xml (the client's constants table has the same 30). A migration
    // cannot read the server tables, so the value is written here.
    public const int Days = 30;

    // Expiry 0 is stored as the epoch in the server's local time (FromEpochSeconds), so within 14 hours of
    // 1970-01-01 00:00 for any time zone. A real expiry is decades later.
    public static readonly DateTime UnsetBefore = new(1970, 1, 2, 0, 0, 0, DateTimeKind.Unspecified);

    public static bool Selects(DateTime storedExpiry) => storedExpiry < UnsetBefore;

    // The migration time plus the mail expiry, not the send time plus it: a mail sent more than 30 days ago
    // would run out at once with the items still attached. NOW() is the database clock, as for CreationTime.
    public static string Sql(int days = Days) {
        string cutoff = UnsetBefore.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        return $"UPDATE `mail` SET `ExpiryTime` = DATE_ADD(NOW(6), INTERVAL {days} DAY) WHERE `ExpiryTime` < '{cutoff}';";
    }
}
