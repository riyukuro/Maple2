using Maple2.PacketLib.Tools;

namespace Maple2.Model.Validators;

// MyInfo: the player's own profile text.
public static class MyInfoRules {
    // Mode 0: string motto (uipcinfodialog.cpp:653-655).
    public static string ReadMotto(IByteReader packet) {
        return packet.ReadUnicodeString();
    }

    // The client runs the motto through its own ban-text check before it sends (uipcinfodialog.cpp:644-651),
    // so a banned motto on the wire did not come from the client's box.
    public static bool MottoBanned(string motto, System.Func<string, bool> isBanned) {
        return !string.IsNullOrWhiteSpace(motto) && isBanned(motto);
    }
}
