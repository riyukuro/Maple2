using Maple2.PacketLib.Tools;

namespace Maple2.Server.Game.PacketHandlers.Field;

// The copy of a packet that waits for the field thread. Its buffer is rented and can be longer than the packet,
// so Length and Available count the rented size; PacketLength is the size of the packet itself.
public sealed class FieldPacketReader : ByteReader {
    public int PacketLength { get; }

    public FieldPacketReader(byte[] buffer, int packetLength, int offset) : base(buffer, offset) {
        PacketLength = packetLength;
    }
}

public static class PacketReaderExtensions {
    // Bytes of the packet that are not read yet.
    public static int Remaining(this IByteReader packet) {
        return packet is FieldPacketReader field ? field.PacketLength - field.Position : packet.Available;
    }
}
