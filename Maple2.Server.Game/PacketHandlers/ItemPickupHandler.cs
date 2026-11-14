using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

public class ItemPickupHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.RequestItemPickup;

    public override void Handle(GameSession session, IByteReader packet) {
        int objectId = packet.ReadInt();
        // CDropItem::SendRequestItemPickup (dropitem.cpp:936): a bool, then the picker's object id when a pet picks.
        int pickerId = packet.ReadBool() ? packet.ReadInt() : 0;

        if (session.Field == null) {
            return;
        }

        // Ensure item exists.
        if (!session.Field.TryGetItem(objectId, out FieldItem? fieldItem)) {
            return;
        }

        // A refused pickup of a drop that stays is answered, so the client asks again later.
        if (fieldItem.ReceiverId != 0 && fieldItem.ReceiverId != session.CharacterId) {
            session.Send(ItemPickupPacket.Refuse(objectId));
            return;
        }

        // The client asks only for a drop in its picker's range: the player, or the summoned pet it names.
        if (!InReach(session, fieldItem, pickerId)) {
            Logger.Debug("Pickup of drop {ObjectId} out of reach", objectId);
            session.Send(ItemPickupPacket.Refuse(objectId));
            return;
        }

        // Currency items are handled differently
        if (fieldItem.Value.IsCurrency()) {
            // Remove objectId from Field, make sure item still exists (multiple looters)
            if (!session.Field.PickupItem(session.Player, objectId, out Item? item)) {
                return;
            }

            // A score orb is no item: its amount goes to the open score board.
            if (session.Field.TakeScoreOrb(item)) {
                return;
            }

            session.Item.Inventory.Add(item);
            return;
        }

        lock (session.Item) {
            // Room for the whole amount (Add takes all of it or none): a free slot alone does not hold a drop of
            // more than SlotMax next to stacks that do not take it.
            if (!session.Item.Inventory.HasRoomFor(fieldItem.Value)) {
                session.Send(ItemPickupPacket.Refuse(objectId));
                return;
            }

            // Remove objectId from Field, make sure item still exists (multiple looters)
            if (!session.Field.PickupItem(session.Player, objectId, out Item? item)) {
                return;
            }

            session.ConditionUpdate(ConditionType.item_pickup, counter: item.Amount, codeLong: item.Id);

            // Items with Pick up effects are not added to player inventory.
            if (item.Metadata.AdditionalEffects.Any(buff => buff.PickUpEffect)) {
                foreach (ItemMetadataAdditionalEffect additionalEffect in item.Metadata.AdditionalEffects.Where(buff => buff.PickUpEffect)) {
                    session.Player.Buffs.AddBuff(session.Player, session.Player, additionalEffect.Id, additionalEffect.Level, session.Field.FieldTick);
                }
                return;
            }

            item.Slot = -1;
            if (!session.Item.Inventory.Add(item, true)) {
                // Only a failed item write gets here (HasRoomFor checked the bag or furnishing storage): what Add did
                // not deliver (the item keeps that amount) is put back on the ground at the player, for them.
                session.Field.DropItem(session.Player.Position, session.Player.Rotation, item, owner: session.Player, characterId: session.CharacterId);
                return;
            }
            if (item.Metadata.Limit.TransferType == TransferType.BindOnLoot) {
                session.Item.Bind(item);
            }
        }
    }

    private static bool InReach(GameSession session, FieldItem fieldItem, int pickerId) {
        float reach = PickupRules.Reach(session.ServerTableMetadata.ConstantsTable.DropItemMaxLength);
        if (PickupRules.InReach(session.Player.Position, fieldItem.Position, reach)) {
            return true;
        }
        PetManager? pet = session.Pet;
        return pet != null && pickerId != 0 && pickerId == pet.ObjectId
               && PickupRules.InReach(pet.Position, fieldItem.Position, reach + pet.FollowDistance);
    }
}
