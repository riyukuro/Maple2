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
}
