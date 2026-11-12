using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

// Cash 0x006A: the cash shop window. When it opens the client asks for the balance (1), the products on sale (0)
// and page 1 of the bought products (5 + int page), in that order (UICashShopDialog::OnLoadComplete,
// client/ms2/uicashshopdialog.cpp:424); its page buttons send 5 again. Each gets its answer, as in KMS2. The
// server has no cash products, so the window lists none and the other requests (2 buy, 3 gift, 4, 6 and 7 for a
// bought product), which need a listed product, are read as far as their mode and logged.
public class CashHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.Cash;

    private enum Command : byte {
        Products = 0,
        Balance = 1,
        Inventory = 5,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Products:
                session.Send(CashPacket.Products());
                return;
            case Command.Balance:
                session.Send(CashPacket.Balance(0));
                return;
            case Command.Inventory:
                session.Send(CashPacket.Inventory(packet.ReadInt()));
                return;
            default:
                Logger.Debug("Cash {Command} from {CharacterId}: no cash products", (byte) command, session.CharacterId);
                return;
        }
    }
}
