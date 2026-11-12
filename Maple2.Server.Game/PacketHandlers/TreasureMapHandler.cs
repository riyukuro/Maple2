using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

// UIItemTreasureMapDialog: a click on a treasure map in the dialog's bag asks what its appraise costs. The appraise
// itself is the plain item use (ItemUseHandler, item function TreasureMap).
public class TreasureMapHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.TreasureMap;

    private enum Command : byte {
        Cost = 2,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = (Command) packet.ReadByte();
        if (command != Command.Cost) {
            return;
        }

        long itemUid = packet.ReadLong();
        bool free = packet.ReadBool();

        Item? item = session.Item.Inventory.Get(itemUid);
        if (item == null) {
            return;
        }

        if (item.Metadata.Function?.Type != ItemFunction.TreasureMap
            || !TreasureMapRules.TryPick(item.Metadata.Function.Parameters, free, out _, out long cost)) {
            session.Send(ItemScriptPacket.TreasureMapCost(itemUid, TreasureMapRules.CannotAppraise));
            return;
        }

        session.Send(ItemScriptPacket.TreasureMapCost(itemUid, cost));
    }
}
