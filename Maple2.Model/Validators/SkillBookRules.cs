using Maple2.PacketLib.Tools;

namespace Maple2.Model.Validators;

// RequestSkillBookTree: what the request may ask before any tab is changed.
public static class SkillBookRules {
    // A server limit; the client refuses an empty name and runs its own text check before it sends.
    public const int MaxNameLength = 64;

    // Mode 2: i64 tab id, string name (uiskillbookautodistributedialog.cpp:1054).
    public static (long TabId, string Name) ReadRename(IByteReader packet) {
        long tabId = packet.ReadLong();
        string name = packet.ReadUnicodeString();
        return (tabId, name);
    }

    public static bool NameAllowed(string name) {
        return !string.IsNullOrWhiteSpace(name) && name.Length <= MaxNameLength;
    }

    // Mode 1: the counted tab list holds at most the tabs a book can have.
    public static bool TabCountAllowed(int count, int maxTabs) {
        return count >= 0 && count <= maxTabs;
    }
}
