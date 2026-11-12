using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// Cash 0x00AD, read by the client's CCashMarketMan::OnCash (client/ms2/cashinventorybag.cpp). The server has no
// cash products and no cash balance, so the lists are empty and the balance is 0.
public static class CashPacket {
    private enum Command : byte {
        Products = 0,
        Balance = 1,
        Inventory = 5,
        Enabled = 9,
    }

    // The products on sale: a count, then one product each. None here.
    public static ByteWriter Products() {
        var pWriter = Packet.Of(SendOp.Cash);
        pWriter.Write<Command>(Command.Products);
        pWriter.WriteInt(0);

        return pWriter;
    }

    // Code 1 and the balance the window shows.
    public static ByteWriter Balance(int cash) {
        var pWriter = Packet.Of(SendOp.Cash);
        pWriter.Write<Command>(Command.Balance);
        pWriter.WriteInt(1);
        pWriter.WriteInt(cash);

        return pWriter;
    }

    // Code 1, the page asked for and the bought products of that page: a count, then one product each. None here.
    public static ByteWriter Inventory(int page) {
        var pWriter = Packet.Of(SendOp.Cash);
        pWriter.Write<Command>(Command.Inventory);
        pWriter.WriteInt(1);
        pWriter.WriteInt(page);
        pWriter.WriteInt(0);

        return pWriter;
    }

    // Sent once at login. With false the menu's cash shop button shows a notice and does not open the window.
    public static ByteWriter Enabled(bool enabled) {
        var pWriter = Packet.Of(SendOp.Cash);
        pWriter.Write<Command>(Command.Enabled);
        pWriter.WriteBool(enabled);

        return pWriter;
    }
}
