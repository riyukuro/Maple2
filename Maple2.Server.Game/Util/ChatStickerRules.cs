namespace Maple2.Server.Game.Util;

public static class ChatStickerRules {
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
