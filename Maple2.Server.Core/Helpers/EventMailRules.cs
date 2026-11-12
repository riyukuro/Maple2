using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Server.Core.Helpers;

// The mails of the CharacterFirstConnect and SendMail game events. Each mail carries only the id of a
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
}
