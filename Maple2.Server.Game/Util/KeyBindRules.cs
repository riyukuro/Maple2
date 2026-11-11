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

    // The key codes of the client's key table: single keys 1 to 0xCF, and combined keys packed as
    // second | first << 8 | mode << 16, both keys counted from 0x91. The first key is one of 0x96 to 0x99;
    // mode 0xF0 takes a second key of 0x92 to 0xA1 outside 0x96 to 0x99, mode 0xF1 one of 0xA3 to 0xB2.
    // That is 319 codes, so a player's key table cannot grow past it.
    public static bool IsClientKeyCode(int keyCode) {
        if (keyCode is >= 1 and < 0xD0) {
            return true;
        }

        int second = keyCode & 0xFF;
        int first = (keyCode >> 8) & 0xFF;
        int mode = (keyCode >> 16) & 0xFF;
        if ((uint) keyCode >> 24 != 0 || first is < 5 or > 8) {
            return false;
        }

        return mode switch {
            0xF0 => second is >= 1 and <= 16 and not (>= 5 and <= 8),
            0xF1 => second is >= 0x12 and <= 0x21,
            _ => false,
        };
    }
}
