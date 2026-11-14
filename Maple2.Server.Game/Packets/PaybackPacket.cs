using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// SendOp 0x012D, PaybackManagerClient::OnPaybackPacket (client/ms2/paybackmanagerclient.cpp); the client reads it only
// with feature 0x109 (Payback) on.
public static class PaybackPacket {
    private enum Command : byte {
        Status = 0,
        Products = 1,
    }

    // The month's adventure levels gained and the merets spent on the payback products (FUN_1407a9710 reads two
    // ints); an open payback dialog redraws its share from them, and the client shuts it when the payback is off.
    public static ByteWriter Status(int levels, long merat) {
        var pWriter = Packet.Of(SendOp.Payback);
        pWriter.WriteByte((byte) Command.Status);
        pWriter.WriteInt(levels);
        pWriter.WriteInt((int) Math.Clamp(merat, 0, int.MaxValue));
        return pWriter;
    }

    // The payback products, each as the meret market sends a product (CMeratMarketProduct's reader); the dialog's buy
    // button bills the first one on sale.
    public static ByteWriter Products(ICollection<PremiumMarketItem> products) {
        var pWriter = Packet.Of(SendOp.Payback);
        pWriter.WriteByte((byte) Command.Products);
        pWriter.WriteInt(products.Count);
        foreach (PremiumMarketItem product in products) {
            pWriter.WriteMarketItem(product);
        }
        return pWriter;
    }
}
