using Maple2.Model.Game;

namespace Maple2.Server.Game.Util;

// Wedding 34: the couple message a partner types in the wedding page.
public static class CoupleMessageRule {
    // Server-side bound on the stored text; the client's own input limit is not known.
    public const int MaxLength = 256;

    public static string Clean(string message) {
        return message.Length > MaxLength ? message[..MaxLength] : message;
    }

    // Stores the message on the partner entry of the given character. False when the character is not in the marriage.
    public static bool Apply(Marriage marriage, long characterId, string message) {
        if (marriage.Id == 0 || characterId == 0) {
            return false;
        }

        if (marriage.Partner1.CharacterId == characterId) {
            marriage.Partner1.Message = Clean(message);
            return true;
        }
        if (marriage.Partner2.CharacterId == characterId) {
            marriage.Partner2.Message = Clean(message);
            return true;
        }
        return false;
    }

    // Takes both messages from a freshly loaded copy, matched by character id (the two copies may list the partners in either order).
    public static void Merge(Marriage marriage, Marriage fresh) {
        foreach (MarriagePartner partner in new[] { marriage.Partner1, marriage.Partner2 }) {
            if (partner.CharacterId == 0) {
                continue;
            }
            if (partner.CharacterId == fresh.Partner1.CharacterId) {
                partner.Message = fresh.Partner1.Message;
            } else if (partner.CharacterId == fresh.Partner2.CharacterId) {
                partner.Message = fresh.Partner2.Message;
            }
        }
    }
}
