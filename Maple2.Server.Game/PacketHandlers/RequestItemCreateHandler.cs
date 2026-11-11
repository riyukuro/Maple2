using Maple2.Model;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

// The client's GM item create window. GM only. The item goes into the inventory by the normal add
// path; the client has no reply of its own for this packet.
public class RequestItemCreateHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.RequestItemCreate;

    private const int MaxRarity = 6;

    public override void Handle(GameSession session, IByteReader packet) {
        if (!GmCheck.Allow(session, OpCode)) {
            return;
        }

        GmItemCreateRequest request = GmItemCreateRequest.Read(packet);
        if (session.Field == null || request.Amount <= 0) {
            return;
        }

        int rarity = Math.Clamp((int) request.Rarity, 1, MaxRarity);
        Item? item = session.Field.ItemDrop.CreateItem(request.ItemId, rarity);
        if (item == null) {
            // Unknown item id: refused, the client has no notice for it.
            Logger.Warning("RequestItemCreate: unknown item {ItemId} from account {AccountId}", request.ItemId, session.AccountId);
            return;
        }

        lock (session.Item) {
            if (item.IsCurrency()) {
                item.Amount = request.Amount;
                Give(session, item);
                return;
            }

            int slotMax = Math.Max(1, item.Metadata.Property.SlotMax);
            int freeSlots = session.Item.Inventory.FreeSlots(item.Inventory);
            if (slotMax > 1) {
                // One stack at most.
                item.Amount = Math.Min(request.Amount, slotMax);
                if (!session.Item.Inventory.CanAdd(item)) {
                    session.Item.Inventory.Discard(item);
                    session.Send(ItemInventoryPacket.Error(ItemInventoryError.s_err_inventory));
                    return;
                }
                Give(session, item);
                return;
            }

            if (freeSlots <= 0) {
                session.Item.Inventory.Discard(item);
                session.Send(ItemInventoryPacket.Error(ItemInventoryError.s_err_inventory));
                return;
            }

            // One slot per item: as many as there are free slots.
            int amount = Math.Min(request.Amount, freeSlots);
            if (!Give(session, item)) {
                return;
            }
            for (int i = 1; i < amount; i++) {
                Item? next = session.Field.ItemDrop.CreateItem(request.ItemId, rarity);
                if (next == null || !Give(session, next)) {
                    return;
                }
            }
        }
    }

    private static bool Give(GameSession session, Item item) {
        if (session.Item.Inventory.Add(item, true)) {
            return true;
        }

        session.Item.Inventory.Discard(item);
        session.Send(ItemInventoryPacket.Error(ItemInventoryError.s_err_inventory));
        return false;
    }
}
