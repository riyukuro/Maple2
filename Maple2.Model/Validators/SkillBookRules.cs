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

    // The client runs its text check on the name before it sends (uiskillbookautodistributedialog.cpp:1045-1053).
    // A name that passed there and holds a word of the server's list is refused with a notice.
    public static bool NameBanned(string name, System.Func<string, bool> isBanned) {
        return NameAllowed(name) && isBanned(name);
    }

    // Mode 4 names the skill window by its UI id (uiskillbookautodistributedialog.cpp:1112-1114).
    public const int DialogId = 0x4F2D;

    // Mode 4: i32 UI id, bool "pay with a coupon" check box of the price box.
    public static (int DialogId, bool UseCoupon) ReadExpand(IByteReader packet) {
        int dialogId = packet.ReadInt();
        bool useCoupon = packet.ReadBool();
        return (dialogId, useCoupon);
    }

    public enum ExpandPayment {
        None,
        Coupon,
        Meret,
    }

    // The client ticks the coupon box and still sends when it has no coupon but can pay the price
    // (uiskillbookautodistributedialog.cpp:1091-1115): a coupon is used only when asked for and owned,
    // and the meret price is charged otherwise.
    public static ExpandPayment ExpandPaidBy(bool useCoupon, bool hasCoupon, long meret, long price) {
        if (useCoupon && hasCoupon) {
            return ExpandPayment.Coupon;
        }
        return price >= 0 && meret >= price ? ExpandPayment.Meret : ExpandPayment.None;
    }

    // Mode 1: the counted tab list holds at most the tabs a book can have.
    public static bool TabCountAllowed(int count, int maxTabs) {
        return count >= 0 && count <= maxTabs;
    }
}
