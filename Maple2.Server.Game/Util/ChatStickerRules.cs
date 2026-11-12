namespace Maple2.Server.Game.Util;

public static class ChatStickerRules {
    // A group with no time limit.
    public const long Permanent = long.MaxValue;

    // The client's IsEmoticonUsable, asked before it uses or marks a sticker: the sticker is in the table (its
    // group is known), the group is held and its time has not run out.
    public static bool Usable(IDictionary<int, long> held, int? groupId, long nowSeconds) {
        return groupId != null && held.TryGetValue(groupId.Value, out long expiry) && expiry >= nowSeconds;
    }

    // The expiry a sticker item gives its group, or null when the item adds nothing (the group is already
    // permanent, or the item's time is negative). durationSec 0 makes the group permanent. A timed item adds
    // its time to what is left of a held group; a held group whose time has already run out starts from now,
    // not from its old expiry.
    public static long? NewExpiry(long? heldExpiry, int durationSec, long nowSeconds) {
        if (heldExpiry == Permanent || durationSec < 0) {
            return null;
        }
        if (durationSec == 0) {
            return Permanent;
        }
        long start = heldExpiry != null && heldExpiry.Value > nowSeconds ? heldExpiry.Value : nowSeconds;
        return start + durationSec;
    }

    // The client names the sticker groups whose time it thinks has run out (CChatEmoticonCli::SendMessenger).
    // Only a group the character holds and whose expiry is before the server's clock is taken away.
    public static List<int> Expired(IDictionary<int, long> held, IEnumerable<int> asked, long nowSeconds) {
        var expired = new List<int>();
        foreach (int groupId in asked) {
            if (held.TryGetValue(groupId, out long expiry) && expiry < nowSeconds && !expired.Contains(groupId)) {
                expired.Add(groupId);
            }
        }
        return expired;
    }
}
