using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.File.Ingest.Helpers;

// The values of the CharacterFirstConnect and SendMail rows, both server only. A row without a mail is not stored.
public static class EventMailReader {
    // value1: the id of a string/systemmailcontent row.
    public static CharacterFirstConnectMail? FirstConnect(string? value1) {
        return int.TryParse(value1?.Trim(), out int contentId) && contentId > 0 ? new CharacterFirstConnectMail(contentId) : null;
    }

    // value1 the players (ReturnUser), value2 "<systemmailcontent id>,<mail kind>" (no kind: a system mail), value3
    // the days. Only a kind whose content the client reads as a systemmailcontent id is kept (CMailInfo::Read: 3 and
    // 101); a ReturnUser row needs days.
    public static SendMailEvent? SendMail(string? value1, string? value2, string? value3) {
        string target = value1?.Trim() ?? string.Empty;
        string[] mail = (value2 ?? string.Empty).Split(',', StringSplitOptions.TrimEntries);
        if (target.Length == 0 || !int.TryParse(mail[0], out int contentId) || contentId <= 0) {
            return null;
        }
        var kind = MailType.System;
        if (mail.Length > 1) {
            if (!byte.TryParse(mail[1], out byte value) || (MailType) value is not (MailType.Notice or MailType.System)) {
                return null;
            }
            kind = (MailType) value;
        }
        int days = int.TryParse(value3?.Trim(), out int parsed) ? parsed : 0;
        if (target == "ReturnUser" && days <= 0) {
            return null;
        }
        return new SendMailEvent(target, contentId, kind, days);
    }
}
