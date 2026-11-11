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

    // The client sends a tab name with no text check of its own (uiclosetdialog.cpp:1173-1196), so the
    // server's ban-word list is the only one the name meets.
    public static bool NameBanned(string name, System.Func<string, bool> isBanned) {
        return !string.IsNullOrWhiteSpace(name) && isBanned(name);
    }

    public enum RenameStep {
        Ignore,
        Refuse,
        RefuseRestore,
        Rename,
        Create,
    }

    // The client renames its tab before the answer. Only a refused name on a tab that exists is answered
    // with the tab (mode 5, uiclosetdialog.cpp:963), which puts the stored name back.
    public static RenameStep Rename(bool allowed, bool banned, bool tabExists) {
        if (!allowed) {
            return RenameStep.Ignore;
        }
        if (banned) {
            return tabExists ? RenameStep.RefuseRestore : RenameStep.Refuse;
        }
        return tabExists ? RenameStep.Rename : RenameStep.Create;
    }
}
