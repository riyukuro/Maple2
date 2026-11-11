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

    public override void Handle(GameSession session, IByteReader packet) {
        if (!GmCheck.Allow(session, OpCode, GmCreateRules.ItemPermission)) {
            return;
        }

        GmItemCreateRequest request = GmItemCreateRequest.Read(packet);
        if (session.Field == null) {
            return;
        }

        int rarity = request.Rarity;
        if (GmCreateRules.PlanItem(request.Amount, rarity, false, 1, 1).Verdict == GmCreateRules.ItemVerdict.Refuse) {
            // Amount <= 0 or a rank outside 1-6: refused, the client has no notice for it.
            Logger.Warning("RequestItemCreate: amount {Amount} rank {Rarity} refused, account {AccountId}", request.Amount, rarity, session.AccountId);
            return;
        }

        Item? item = session.Field.ItemDrop.CreateItem(request.ItemId, rarity);
        if (item == null) {
            // Unknown item id: refused, the client has no notice for it.
            Logger.Warning("RequestItemCreate: unknown item {ItemId} from account {AccountId}", request.ItemId, session.AccountId);
            return;
        }

        lock (session.Item) {
            GmCreateRules.ItemPlan plan = GmCreateRules.PlanItem(request.Amount, rarity, item.IsCurrency(),
                Math.Max(1, item.Metadata.Property.SlotMax), session.Item.Inventory.FreeSlots(item.Inventory));
            switch (plan.Verdict) {
                case GmCreateRules.ItemVerdict.Currency:
                    item.Amount = plan.Amount;
                    Give(session, item);
                    return;
                case GmCreateRules.ItemVerdict.Stack:
                    // One stack at most.
                    item.Amount = plan.Amount;
                    if (!session.Item.Inventory.CanAdd(item)) {
                        break;
                    }
                    Give(session, item);
                    return;
                case GmCreateRules.ItemVerdict.Singles:
                    // One slot per item: as many as there are free slots.
                    if (!Give(session, item)) {
                        return;
                    }
                    for (int i = 1; i < plan.Amount; i++) {
                        Item? next = session.Field.ItemDrop.CreateItem(request.ItemId, rarity);
                        if (next == null || !Give(session, next)) {
                            return;
                        }
                    }
                    return;
            }

            session.Item.Inventory.Discard(item);
            session.Send(ItemInventoryPacket.Error(ItemInventoryError.s_err_inventory));
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
