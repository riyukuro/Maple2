using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

public class MesoPickupHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.RequestMesoPickup;

    public override void Handle(GameSession session, IByteReader packet) {
        if (session.Field == null) {
            return;
        }

        byte count = packet.ReadByte();
        for (byte i = 0; i < count; i++) {
            int objectId = packet.ReadInt();
            // Check that the item being looted is mesos
            if (!session.Field.TryGetItem(objectId, out FieldItem? fieldItem)) {
                continue;
            }

            // A drop that stays (no meso, or another player's) is refused, so the client can ask for it again.
            if (!fieldItem.Value.IsMeso() || (fieldItem.ReceiverId != 0 && fieldItem.ReceiverId != session.CharacterId)) {
                session.Send(ItemPickupPacket.Refuse(objectId));
                continue;
            }

            // The client lists only mesos within DropMoneyMaxLength of the player (WorldService::CollectDropItems,
            // worldservice_1.cpp:6386); a pet's meso pickup comes as RequestItemPickup.
            if (!PickupRules.InReach(session.Player.Position, fieldItem.Position, PickupRules.Reach(session.ServerTableMetadata.ConstantsTable.DropMoneyMaxLength))) {
                Logger.Debug("Meso pickup of drop {ObjectId} out of reach", objectId);
                session.Send(ItemPickupPacket.Refuse(objectId));
                continue;
            }

            // If item is mesos, attempt to loot it
            if (session.Field.PickupItem(session.Player, objectId, out Item? item)) {
                session.Item.Inventory.Add(item);
                session.ConditionUpdate(ConditionType.meso, item.Amount);
            }
        }
    }
}
