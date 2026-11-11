using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

public static class GlobalFactorPacket {
    private const int RATE_KINDS = 3;
    private const int MAX_LEVEL = 99;

    // CGloabalFactor::ReadRates: three experience rate tables, then three meso rate tables. Each is
    // its kind (u8) followed by one float per level 1..99. The client starts every rate at 1.0, and
    // this server has no global rates, so all are 1.0.
    public static ByteWriter Rates() {
        var pWriter = Packet.Of(SendOp.GlobalFactor);
        for (int table = 0; table < 2; table++) {
            for (byte kind = 0; kind < RATE_KINDS; kind++) {
                pWriter.WriteByte(kind);
                for (int level = 1; level <= MAX_LEVEL; level++) {
                    pWriter.WriteFloat(1f);
                }
            }
        }

        return pWriter;
    }
}
