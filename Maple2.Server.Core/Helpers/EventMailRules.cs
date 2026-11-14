using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Server.Core.Helpers;

// The mails of the CharacterFirstConnect, SendMail and ReturnUserYearRound game events. Each mail carries only the id of a
// string/systemmailcontent row as its content: the client reads a content of digits of a system (101) or
// notice (3) mail as that id and takes the sender, subject and text (with its $maview link) from the table
// (CMailInfo::Read).
public static class EventMailRules {
    // The mails a new character gets from the running CharacterFirstConnect rows.
    public static IEnumerable<Mail> FirstConnect(IEnumerable<GameEventMetadata> running, long characterId) {
        foreach (GameEventMetadata meta in running) {
            if (meta.Data is CharacterFirstConnectMail row) {
                yield return new Mail {
                    Type = MailType.System,
                    ReceiverId = characterId,
                    Content = row.ContentId.ToString(),
                };
            }
        }
    }

    // The mails of the running SendMail ReturnUser rows for a character that enters at `now` after its last save
    // at `lastOnline` (epoch seconds): one per row whose days have gone by. A character never saved gets none.
    public static IEnumerable<Mail> ReturnUser(IEnumerable<GameEventMetadata> running, long characterId, long lastOnline, long now) {
        if (lastOnline <= 0) {
            yield break;
        }
        foreach (GameEventMetadata meta in running) {
            if (meta.Data is SendMailEvent { Target: "ReturnUser" } row && now - lastOnline >= row.Days * 86400L) {
                yield return new Mail {
                    Type = row.Kind,
                    ReceiverId = characterId,
                    Content = row.ContentId.ToString(),
                };
            }
        }
    }

    // One returner gift of a running ReturnUserYearRound row: its system mail (the content id) and the item.
    public readonly record struct YearRoundGift(int EventId, Mail Mail, RewardItem Item, int WaitDays);

    // The gifts of the running ReturnUserYearRound rows for a character that enters at `now` after its last save at
    // `lastOnline` (epoch seconds): one per row whose days away have gone by and whose wait since the account's last
    // gift of that row (lastGiven, epoch seconds, null for none) is over. A character never saved gets none.
    // Chosen here: the days away are the entering character's, as the SendMail ReturnUser mail's; the wait is the
    // account's, so its other characters do not get the gift again in it.
    public static IEnumerable<YearRoundGift> YearRound(IEnumerable<GameEventMetadata> running, long characterId, long lastOnline, long now,
            Func<int, long?> lastGiven) {
        if (lastOnline <= 0) {
            yield break;
        }
        foreach (GameEventMetadata meta in running) {
            if (meta.Data is not ReturnUserYearRound row || now - lastOnline < row.MinDaysInactive * 86400L) {
                continue;
            }
            long? given = lastGiven(meta.Id);
            if (given != null && now - given.Value < row.CooldownDays * 86400L) {
                continue;
            }
            yield return new YearRoundGift(meta.Id, new Mail {
                Type = MailType.System,
                ReceiverId = characterId,
                Content = row.MailId.ToString(),
            }, row.Item, row.CooldownDays);
        }
    }
}
