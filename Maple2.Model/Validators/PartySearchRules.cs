using System;
using Maple2.PacketLib.Tools;

namespace Maple2.Model.Validators;

// PartySearch 0x006E.
public static class PartySearchRules {
    // The size the dialog sends when no row of its list is selected.
    public const int DefaultSize = 10;
    public const int MinTitleLength = 2;

    // Mode 0: unicode title, u8 join without approval, i32 party size.
    public static (string Title, bool NoApproval, int Size) ReadAdd(IByteReader packet) {
        string title = packet.ReadUnicodeString();
        bool noApproval = packet.ReadBool();
        int size = packet.ReadInt();
        return (title, noApproval, size);
    }

    // The register dialog sends a title of two characters or more and a size picked from its list.
    public static bool ValidAdd(string title, int size, int[]? sizes) {
        if (title.Length < MinTitleLength) {
            return false;
        }
        // The dialog falls back to DefaultSize when no row of its list is selected.
        return size == DefaultSize || (sizes != null && Array.IndexOf(sizes, size) >= 0);
    }
}
