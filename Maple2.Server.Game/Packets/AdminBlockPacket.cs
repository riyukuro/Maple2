using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Packets;

// CBlockManager::OnBlockPacket (blockmanager.cpp:85). The client keeps one record per kind (CBlockChecker::Set) and
// clears them only at character select.
public static class AdminBlockPacket {
    private enum Command : byte {
        Load = 0,
        Remove = 1,
        SetExpiry = 2,
    }

    // Each record replaces the client's record of its kind. A Penalty (kind 2) shows s_admin_block_velma_add, a
    // Citation (kind 4) s_admin_block_velma_ugc_add, kind 10 s_admin_block_cash_out.
    public static ByteWriter Load(ICollection<AccountBlock> blocks) {
        var pWriter = Packet.Of(SendOp.AdminBlock);
        pWriter.WriteByte((byte) Command.Load);
        pWriter.WriteInt(blocks.Count);
        foreach (AccountBlock block in blocks) {
            pWriter.WriteClass<AccountBlock>(block);
        }

        return pWriter;
    }

    public static ByteWriter Remove(long id) {
        var pWriter = Packet.Of(SendOp.AdminBlock);
        pWriter.WriteByte((byte) Command.Remove);
        pWriter.WriteLong(id);

        return pWriter;
    }

    // The client looks the record up by the id cut to an int and sets its expiry. A negative delta (seconds) shows
    // the "reduced by" line for a Penalty (s_admin_block_velma_endtime_dec) or a Citation.
    public static ByteWriter SetExpiry(long id, int kind, long expiryTime, int delta) {
        var pWriter = Packet.Of(SendOp.AdminBlock);
        pWriter.WriteByte((byte) Command.SetExpiry);
        pWriter.WriteLong(id);
        pWriter.WriteInt(kind);
        pWriter.WriteLong(expiryTime);
        pWriter.WriteInt(delta);

        return pWriter;
    }
}
