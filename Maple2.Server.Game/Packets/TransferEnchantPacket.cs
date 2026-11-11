using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

public static class TransferEnchantPacket {
    // CUIItemEnchantTransformDialog::OnTransformResult reads no field after the opcode.
    public static ByteWriter Result() {
        return Packet.Of(SendOp.TransferEnchant);
    }
}
