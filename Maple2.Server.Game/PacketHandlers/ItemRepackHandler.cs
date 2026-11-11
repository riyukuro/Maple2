using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class ItemRepackHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.ItemRepack;

    private enum Command : byte {
        Commit = 1,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Commit:
                HandleCommit(session, packet);
                return;
        }
    }

    private static void HandleCommit(GameSession session, IByteReader packet) {
        long scrollUid = packet.ReadLong();
        long targetItemUid = packet.ReadLong();

        lock (session.Item) {
            Item? scroll = session.Item.Inventory.Get(scrollUid);
            if (scroll == null || scroll.Metadata.Function?.Type != ItemFunction.ItemRePackingScroll) {
                session.Send(ItemRepackPacket.Error(ItemRepackError.s_item_repacking_scroll_error_invalid_scroll));
                return;
            }

            Item? targetItem = session.Item.Inventory.Get(targetItemUid);
            if (targetItem == null || targetItem.Transfer == null || targetItem.Binding != null) {
                session.Send(ItemRepackPacket.Error(ItemRepackError.s_item_repacking_scroll_error_invalid_target));
                return;
            }

            // CItemRePackingScrollTable::Check order: enchant state, transfer type, trades left,
            // (binding, above), repacks left, lock.
            // The enchant record's flag is never cleared by this server (ItemEnchant.WriteTo sends it set).
            if (!ScrollRules.RepackEnchantAllowed(targetItem.Enchant != null, true)) {
                session.Send(ItemRepackPacket.Error(ItemRepackError.s_item_repacking_scroll_error_remain_count));
                return;
            }

            if (!ScrollRules.RepackTransferTypeAllowed((int) targetItem.Metadata.Limit.TransferType)) {
                session.Send(ItemRepackPacket.Error(ItemRepackError.s_item_repacking_scroll_error_not_limit_tradable_item));
                return;
            }

            if (targetItem.Transfer.Flag.HasFlag(TransferFlag.LimitTrade) && targetItem.Transfer.RemainTrades > 0) {
                session.Send(ItemRepackPacket.Error(ItemRepackError.s_item_repacking_scroll_error_tradable_item));
                return;
            }

            if (!ScrollRules.HasRepackLeft(targetItem.Metadata.Property.RepackCount, targetItem.Transfer.RepackageCount)) {
                session.Send(ItemRepackPacket.Error(ItemRepackError.s_item_repacking_scroll_error_remain_count));
                return;
            }

            // The client refuses a locked item and one whose unlock is still running.
            if (targetItem.IsLocked || targetItem.UnlockTime > DateTimeOffset.UtcNow.ToUnixTimeSeconds()) {
                session.Send(ItemRepackPacket.Error(ItemRepackError.s_item_repacking_scroll_error_not_unlock_item));
                return;
            }

            // The client counts every stack of the scroll's item id.
            int need = Math.Max(1, targetItem.Metadata.Property.RepackConsumeCount);
            List<Item> scrolls = session.Item.Inventory.Find(scroll.Id).ToList();
            if (!ScrollRules.HasRepackScrolls(scrolls.Sum(stack => stack.Amount), need)) {
                session.Send(ItemRepackPacket.Error(ItemRepackError.s_item_repacking_scroll_error_server_fail_consume_scroll));
                return;
            }

            if (!int.TryParse(scroll.Metadata.Function?.Parameters, out int scrollId) ||
                !ScrollRules.RepackScrollAllowed(targetItem.Metadata.Property.RepackScrollIds, scrollId) ||
                !session.TableMetadata.ItemRepackingScrollTable.Entries.TryGetValue(scrollId, out ItemRepackingScrollMetadata? scrollMetadata)) {
                session.Send(ItemRepackPacket.Error(ItemRepackError.s_item_repacking_scroll_error_invalid_target));
                return;
            }

            if (!scrollMetadata.Rarities.Contains(targetItem.Rarity)) {
                session.Send(ItemRepackPacket.Error(ItemRepackError.s_item_repacking_scroll_error_impossible_rank));
                return;
            }

            if (!ScrollRules.RepackLevelAllowed(targetItem.Metadata.Limit.Level, scrollMetadata.MinLevel, scrollMetadata.MaxLevel)) {
                session.Send(ItemRepackPacket.Error(ItemRepackError.s_item_repacking_scroll_error_impossible_level));
                return;
            }

            if (!ScrollRules.RepackSlotAllowed(scrollMetadata.IsPet, targetItem.Metadata.Property.PetId != 0, scrollMetadata.ItemTypes, (int) targetItem.Type.Type)) {
                session.Send(ItemRepackPacket.Error(ItemRepackError.s_item_repacking_scroll_error_impossible_slot));
                return;
            }

            // The scrolls are charged before the item changes; the stack the client named goes first.
            int remaining = need;
            foreach (Item stack in scrolls.OrderByDescending(stack => stack.Uid == scroll.Uid)) {
                int take = Math.Min(remaining, stack.Amount);
                if (!session.Item.Inventory.Consume(stack.Uid, take)) {
                    session.Send(ItemRepackPacket.Error(ItemRepackError.s_item_repacking_scroll_error_server_fail_consume_scroll));
                    return;
                }
                remaining -= take;
                if (remaining <= 0) {
                    break;
                }
            }

            targetItem.Transfer.RemainTrades++;
            targetItem.Transfer.RepackageCount++;
            session.Send(ItemRepackPacket.Commit(targetItem));
        }
    }
}
