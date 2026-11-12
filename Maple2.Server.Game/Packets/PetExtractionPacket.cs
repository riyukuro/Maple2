using Maple2.Model.Error;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// The pet extraction answer (0x108). The open UIPetExtractionScrollDialog reads it (vfunc_79,
// uipetextractionscrolldialog.cpp:224): the result byte; for 0 the pet's uid, the badge's uid and the badge's pet
// skin data (CGemPetSkin::vfunc_3, itemgem.cpp:887: the pet id). Both items must be in the inventory by then.
public static class PetExtractionPacket {
    public static ByteWriter Extracted(long petUid, long badgeUid, int petId) {
        var pWriter = Packet.Of(SendOp.PetSkinBadge);
        pWriter.WriteByte((byte) PetExtractionError.ok);
        pWriter.WriteLong(petUid);
        pWriter.WriteLong(badgeUid);
        pWriter.WriteInt(petId);
        return pWriter;
    }

    public static ByteWriter Error(PetExtractionError error) {
        var pWriter = Packet.Of(SendOp.PetSkinBadge);
        pWriter.WriteByte((byte) error);
        return pWriter;
    }
}
