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
            if (targetItem == null) {
                session.Send(ItemRepackPacket.Error(ItemRepackError.s_item_repacking_scroll_error_invalid_target));
                return;
            }

            if (!int.TryParse(scroll.Metadata.Function?.Parameters, out int scrollId) ||
                !session.TableMetadata.ItemRepackingScrollTable.Entries.TryGetValue(scrollId, out ItemRepackingScrollMetadata? scrollMetadata)) {
                session.Send(ItemRepackPacket.Error(ItemRepackError.s_item_repacking_scroll_error_invalid_scroll));
                return;
            }

            if (!ScrollRules.RepackUsable(targetItem.ExpiryTime, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), targetItem.Metadata.Property.PetId != 0)) {
                session.Send(ItemRepackPacket.Error((ItemRepackError) 2));
                return;
            }

            // The code is the first one CItemRePackingScrollTable::Check gives
            // (commonfiles/itemrepackingscrolldata.cpp:145-208), so it is the one the client would show itself.
            int code = ScrollRules.RepackCode(
                // The enchant record's flag is never cleared by this server (ItemEnchant.WriteTo sends it set).
                enchantOk: ScrollRules.RepackEnchantAllowed(targetItem.Enchant != null, true),
                scrollListed: ScrollRules.RepackScrollAllowed(targetItem.Metadata.Property.RepackScrollIds, scrollId),
                slotOk: ScrollRules.RepackSlotAllowed(scrollMetadata.IsPet, targetItem.Metadata.Property.PetId != 0, scrollMetadata.ItemTypes, (int) targetItem.Type.Type),
                rankOk: scrollMetadata.Rarities.Contains(targetItem.Rarity),
                levelOk: ScrollRules.RepackLevelAllowed(targetItem.Metadata.Limit.Level, scrollMetadata.MinLevel, scrollMetadata.MaxLevel),
                transferTypeOk: ScrollRules.RepackTransferTypeAllowed((int) targetItem.Metadata.Limit.TransferType),
                hasTransfer: targetItem.Transfer != null,
                tradesLeft: targetItem.Transfer?.RemainTrades ?? 0,
                bound: targetItem.Transfer?.Binding != null,
                repackLeft: ScrollRules.HasRepackLeft(targetItem.Metadata.Property.RepackCount, targetItem.Transfer?.RepackageCount ?? 0),
                locked: targetItem.IsLocked || targetItem.UnlockTime > DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            if (code != 0 || targetItem.Transfer == null) {
                session.Send(ItemRepackPacket.Error((ItemRepackError) code));
                return;
            }

            // Server rule, last: the client counts every stack of the scroll's item id.
            int need = Math.Max(1, targetItem.Metadata.Property.RepackConsumeCount);
            List<Item> scrolls = session.Item.Inventory.Find(scroll.Id).ToList();
            if (!ScrollRules.HasRepackScrolls(scrolls.Sum(stack => stack.Amount), need)) {
                session.Send(ItemRepackPacket.Error(ItemRepackError.s_item_repacking_scroll_error_server_fail_consume_scroll));
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
