using Maple2.PacketLib.Tools;

namespace Maple2.Model.Validators;

// Wardrobe: the closet tab requests.
public static class WardrobeRules {
    // Mode 6: i32 tab index, string name (uiclosetdialog.cpp:1229-1235, 1535-1541).
    public static (int Index, string Name) ReadRename(IByteReader packet) {
        int index = packet.ReadInt();
        string name = packet.ReadUnicodeString();
        return (index, name);
    }

    // A new tab and a renamed tab have the same length limit.
    public static bool NameAllowed(string name, int maxLength) {
        return name.Length <= maxLength;
    }
}
