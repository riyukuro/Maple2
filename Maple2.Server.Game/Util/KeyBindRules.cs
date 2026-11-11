using Maple2.Model.Game;

namespace Maple2.Server.Game.Util;

// The client's debug mode is toggled by action 600001. The client's default key file binds it to key 41
// (the ` key) and the client itself only turns the mode on at GM level 50 or more. The key table the server
// sends holds that bind for a GM account only.
public static class KeyBindRules {
    public const long DebugModeAction = 600001;
    public const int DebugModeKey = 41; // `
    public const int DebugModeKeyAlt = 70; // Scroll Lock
    private const int ActionType = 2;

    // The binds to send. An empty table is left alone: the client then loads its own default file.
    public static ICollection<KeyBind> ForSend(ICollection<KeyBind> stored, bool gm) {
        if (stored.Count == 0) {
            return stored;
        }
        if (!gm) {
            return stored.Any(bind => bind.OptionGuid == DebugModeAction)
                ? stored.Where(bind => bind.OptionGuid != DebugModeAction).ToList()
                : stored;
        }
        if (stored.Any(bind => bind.OptionGuid == DebugModeAction)) {
            return stored;
        }

        // First of the two keys that holds no other action; the client keeps one bind per key.
        foreach (int key in new[] { DebugModeKey, DebugModeKeyAlt }) {
            if (stored.Any(bind => bind.KeyCode == key && bind.OptionGuid != 0)) {
                continue;
            }
            List<KeyBind> result = stored.Where(bind => bind.KeyCode != key).ToList();
            result.Add(new KeyBind(key, ActionType, DebugModeAction, 0, 0));
            return result;
        }
        return stored;
    }
}
