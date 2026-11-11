using System.Numerics;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

public static class MinimapPingPacket {
    // CSceneStateGame::OnMinimapPing: the pinging character's id, the ping kind and the position.
    // The client draws it with the character's name on the minimap if the field allows pings.
    public static ByteWriter Ping(long characterId, byte kind, Vector3 position) {
        var pWriter = Packet.Of(SendOp.MinimapPing);
        pWriter.WriteLong(characterId);
        pWriter.WriteByte(kind);
        pWriter.WriteFloat(position.X);
        pWriter.WriteFloat(position.Y);
        pWriter.WriteFloat(position.Z);

        return pWriter;
    }
}
