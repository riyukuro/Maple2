using Maple2.PacketLib.Tools;

namespace Maple2.Model.Validators;

// InteractObject 0x0031 modes 11 (start) and 12 (end): i32 count, then that many object names.
// The client sends the start list when the interaction begins and, when it ends, the end list
// with those of the same objects that can still be used.
public static class InteractRules {
    // Server rule: the names kept from one start packet.
    public const int MaxStarted = 64;

    public static List<string> ReadNames(IByteReader packet) {
        int count = packet.ReadInt();
        var names = new List<string>();
        for (int i = 0; i < count; i++) {
            names.Add(packet.ReadString());
        }
        return names;
    }

    // An end is taken only for an object the last start packet named.
    public static bool CanEnd(ICollection<string> started, string name) {
        return started.Contains(name);
    }
}
