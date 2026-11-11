using System.Collections.Generic;
using Maple2.PacketLib.Tools;

namespace Maple2.Model.Validators;

// ChangeAttributes 0x0075 mode 0 and ChangeAttributesScroll 0x0076 mode 1 end with the options the
// player keeps: u8 count, then per option u8 kind (0 basic, 1 special) and i16 attribute.
public static class ItemRemakeRules {
    public readonly record struct Lock(bool Special, short Attribute);

    public static List<Lock> ReadLocks(IByteReader packet) {
        byte count = packet.ReadByte();
        var locks = new List<Lock>(count);
        for (int i = 0; i < count; i++) {
            bool special = packet.ReadBool();
            short attribute = packet.ReadShort();
            locks.Add(new Lock(special, attribute));
        }
        return locks;
    }

    // One lock item keeps one option; a request that keeps more is refused whole.
    public static bool LocksAllowed(int count) => count is 0 or 1;
}
