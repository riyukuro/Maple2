using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Tools.Extensions;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// The maids of the field (CFieldMaidManager::OnPacket, client/ms2/maidmanager.cpp). The client finds an npc's maid
// here by the maid uid of the npc's FieldAddNpc packet.
public static class FieldMaidPacket {
    private enum Command : byte {
        Add = 0,
        Remove = 1,
    }

    public static ByteWriter Add(Maid maid) {
        var pWriter = Packet.Of(SendOp.FieldMaid);
        pWriter.WriteByte((byte) Command.Add);
        pWriter.WriteClass<Maid>(maid);
        return pWriter;
    }

    public static ByteWriter Remove(long maidUid) {
        var pWriter = Packet.Of(SendOp.FieldMaid);
        pWriter.WriteByte((byte) Command.Remove);
        pWriter.WriteLong(maidUid);
        return pWriter;
    }
}
