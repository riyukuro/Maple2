using Maple2.Model.Enum;
using Maple2.Model.Game;
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

    // A ReturnUserYearRound row (KR 180001-180005): value1 the systemmailcontent id of the returner mail, value2 the
    // item, "id,count,rank,0" as a DailyLoginReward item (ServerTableMapper.ReadDailyLoginReward), value3 "<days
    // away>,<days before the next gift>". Chosen here: no client code reads the row (tableexporter only), and the
    // names are upstream's. The first field cannot be a level: the 2016 row (180001) has 60, above the KR level cap
    // of the time. A row without a mail, an item or days away is not stored; no second field is no wait.
    public static ReturnUserYearRound? YearRound(string? value1, string? value2, string? value3) {
        if (!int.TryParse(value1?.Trim(), out int contentId) || contentId <= 0) {
            return null;
        }
        string[] item = (value2 ?? string.Empty).Split(',', StringSplitOptions.TrimEntries);
        if (item.Length < 3 || !int.TryParse(item[0], out int itemId) || itemId <= 0
            || !int.TryParse(item[1], out int count) || count <= 0 || !short.TryParse(item[2], out short rank) || rank < 0) {
            return null;
        }
        string[] days = (value3 ?? string.Empty).Split(',', StringSplitOptions.TrimEntries);
        if (!int.TryParse(days[0], out int away) || away <= 0) {
            return null;
        }
        int wait = days.Length > 1 && int.TryParse(days[1], out int parsed) && parsed > 0 ? parsed : 0;
        return new ReturnUserYearRound(contentId, new RewardItem(itemId: itemId, rarity: rank, amount: count), away, wait);
    }
}
