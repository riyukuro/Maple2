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

    // The VIP goods shop (UIVIPGoodsShopDialog::vfunc_79, uivipgoodsshopdialog.cpp): mode 3 is the purchases left
    // of each limited goods (the dialog's list is cleared first, a goods with a limit and no entry or 0 is hidden),
    // mode 4 the goods just bought (the bought message box).
    public static ByteWriter LoadPackages(ICollection<(int GoodsId, int Remaining)> remaining) {
        var pWriter = Packet.Of(SendOp.PremiumClub);
        pWriter.WriteByte((byte) Command.LoadPackages);
        pWriter.WriteInt(remaining.Count);
        foreach ((int goodsId, int left) in remaining) {
            pWriter.WriteInt(goodsId);
            pWriter.WriteInt(left);
        }

        return pWriter;
    }

    public static ByteWriter PurchasePackage(int goodsId) {
        var pWriter = Packet.Of(SendOp.PremiumClub);
        pWriter.WriteByte((byte) Command.PurchasePackage);
        pWriter.WriteInt(goodsId);

        return pWriter;
    }
}
