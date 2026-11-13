using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// Opcode 0x131: one byte, the adventurer protection state (CSceneStateGame::FUN_14173f7c0; AdventureProtectRules).
public static class AdventureProtectPacket {
    public static ByteWriter State(byte state) {
        ByteWriter pWriter = Packet.Of(SendOp.AdventureProtect);
        pWriter.WriteByte(state);
        return pWriter;
    }
}
