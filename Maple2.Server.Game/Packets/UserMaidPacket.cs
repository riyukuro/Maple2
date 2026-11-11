using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Tools.Extensions;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// The player's own maids (CUserMaidManager::OnPacket, client/ms2/maidmanager.cpp): the maid status window, the maid
// item tooltip and the craft windows read them. Update refreshes an open maid dialog (UpdateStatus) and, after a level
// up, the craft dialog's recipe list; Property refreshes the open maid dialog and the name over the maid npc.
public static class UserMaidPacket {
    private enum Command : byte {
        List = 0,
        Add = 1,
        Remove = 2,
        Update = 3,
        Property = 4,
    }

    public static ByteWriter List(ICollection<Maid> maids) {
        var pWriter = Packet.Of(SendOp.UserMaid);
        pWriter.WriteByte((byte) Command.List);
        pWriter.WriteInt(maids.Count);
        foreach (Maid maid in maids) {
            pWriter.WriteClass<Maid>(maid);
        }
        return pWriter;
    }

    public static ByteWriter Add(Maid maid) {
        var pWriter = Packet.Of(SendOp.UserMaid);
        pWriter.WriteByte((byte) Command.Add);
        pWriter.WriteClass<Maid>(maid);
        return pWriter;
    }

    public static ByteWriter Remove(long maidUid) {
        var pWriter = Packet.Of(SendOp.UserMaid);
        pWriter.WriteByte((byte) Command.Remove);
        pWriter.WriteLong(maidUid);
        return pWriter;
    }

    public static ByteWriter Update(Maid maid) {
        var pWriter = Packet.Of(SendOp.UserMaid);
        pWriter.WriteByte((byte) Command.Update);
        pWriter.WriteClass<Maid>(maid);
        return pWriter;
    }

    // The maid npc's object id.
    public static ByteWriter Property(int npcObjectId) {
        var pWriter = Packet.Of(SendOp.UserMaid);
        pWriter.WriteByte((byte) Command.Property);
        pWriter.WriteInt(npcObjectId);
        return pWriter;
    }
}
