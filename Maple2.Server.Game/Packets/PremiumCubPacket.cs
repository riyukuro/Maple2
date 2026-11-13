using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

public static class PremiumCubPacket {
    private enum Command : byte {
        Activate = 0,
        LoadItems = 1,
        ClaimItem = 2,
        LoadPackages = 3,
        PurchasePackage = 4,
    }

    public static ByteWriter Activate(int objectId, long expiration) {
        var pWriter = Packet.Of(SendOp.PremiumClub);
        pWriter.WriteByte((byte) Command.Activate);
        pWriter.WriteInt(objectId);
        pWriter.WriteLong(expiration);

        return pWriter;
    }

    public static ByteWriter LoadItems(IList<int> itemIds) {
        var pWriter = Packet.Of(SendOp.PremiumClub);
        pWriter.WriteByte((byte) Command.LoadItems);
        pWriter.WriteInt(itemIds.Count);
        foreach (int itemId in itemIds) {
            pWriter.WriteInt(itemId);
        }

        return pWriter;
    }

    public static ByteWriter ClaimItem(int benefitId) {
        var pWriter = Packet.Of(SendOp.PremiumClub);
        pWriter.WriteByte((byte) Command.ClaimItem);
        pWriter.WriteInt(benefitId);

        return pWriter;
    }

    // The client reads only the sub-type for 3 and 4: UIVIPDialog::FUN_140fb87d0 (uivipdialog.cpp:234) handles 1 and 2 only.
    public static ByteWriter LoadPackages() {
        var pWriter = Packet.Of(SendOp.PremiumClub);
        pWriter.WriteByte((byte) Command.LoadPackages);

        return pWriter;
    }

    public static ByteWriter PurchasePackage() {
        var pWriter = Packet.Of(SendOp.PremiumClub);
        pWriter.WriteByte((byte) Command.PurchasePackage);

        return pWriter;
    }
}
