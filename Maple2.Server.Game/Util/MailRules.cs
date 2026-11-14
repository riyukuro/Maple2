using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;

namespace Maple2.Server.Game.Util;

public static class MailRules {
    // The client's send dialog refuses an empty title or text, a title over 100 characters and a text over 2000.
    public const int MaxTitleLength = 100;
    public const int MaxContentLength = 2000;

    public static bool CanSend(string title, string content) {
        return title.Length is > 0 and <= MaxTitleLength && content.Length is > 0 and <= MaxContentLength;
    }

    // The player's send dialog and the guild mail (CGuildManager::SendGuildMail) check the title and the text
    // for banned words (CheckMailTitleText, CheckContentText) before they send.
    public static bool TextBanned(string title, string content, Func<string, bool> isBanned) {
        return isBanned(title) || isBanned(content);
    }

    // CMailInfo::IsExpired: a mail is expired once the server time reaches its expiry.
    public static bool IsExpired(Mail mail, long now) {
        return mail.ExpiryTime > 0 && now >= mail.ExpiryTime;
    }

    // The badge counts what the list shows as unread: mails without a read time that are not expired.
    public static int UnreadCount(IEnumerable<Mail> mails, long now) {
        return mails.Count(mail => mail.ReadTime == 0 && !IsExpired(mail, now));
    }

    // The menu highlight count: the unread mails of the kinds the client highlights by itself once it has the list
    // (100 to 112, UIHighlightGameMenu), so a player's mail is not counted.
    public static int UnreadAlarmCount(IEnumerable<Mail> mails, long now) {
        return mails.Count(mail => mail.ReadTime == 0 && !IsExpired(mail, now) && IsAlarmKind(mail.Type));
    }

    public static bool IsAlarmKind(MailType type) {
        return (byte) type >= 100 && (byte) type < 113;
    }

    // A mail whose attached items include one with an end time still ahead: the client's "temporary item" notice
    // (OnMail mode 0xf) is about such a mail.
    public static bool HasTemporaryItem(Mail mail, long now) {
        return !IsExpired(mail, now) && mail.Items.Any(item => item.ExpiryTime > now);
    }

    public static bool HasUncollected(Mail mail) {
        return mail.Items.Count > 0 || !mail.MesoCollected() || !mail.MeretCollected() || !mail.GameMeretCollected();
    }

    // UIMailListDialog::HasLockedAttachment: an uncollected item that is locked keeps the mail from being deleted.
    public static bool HasLockedItem(Mail mail) {
        return mail.Items.Any(item => item.IsLocked);
    }

    // What refuses a collect before anything is given.
    public static MailError CollectError(Mail mail, long now) {
        if (!HasUncollected(mail)) {
            return MailError.s_mail_error_already_receive;
        }
        if (IsExpired(mail, now)) {
            return MailError.s_mail_error_receive_expired;
        }
        if (mail.Items.Any(item => item.Amount <= 0)) {
            return MailError.s_mail_error_attachcount;
        }
        return MailError.none;
    }

    // Read-all and collect-all (chosen here): a mail that is already read or already collected is passed over and
    // the rest of the list is still done; any other error ends the list and is shown.
    public static bool BulkSkips(MailError error) {
        return error is MailError.s_mail_error_alreadyread or MailError.s_mail_error_already_receive;
    }
}
