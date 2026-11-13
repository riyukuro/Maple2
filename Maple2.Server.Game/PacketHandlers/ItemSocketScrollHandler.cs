using Maple2.Database.Storage;
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
using static Maple2.Model.Error.ItemSocketScrollError;

namespace Maple2.Server.Game.PacketHandlers;

public class ItemSocketScrollHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.ItemSocketScroll;

    private enum Command : byte {
        Unlock = 1,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required TableMetadataStorage TableMetadata { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Unlock:
                HandleUnlock(session, packet);
                return;
        }
    }

    private void HandleUnlock(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        long scrollUid = packet.ReadLong();

        lock (session.Item) {
            Item? item = session.Item.Inventory.Get(itemUid);
            if (item == null) {
                session.Send(ItemSocketScrollPacket.Error(s_itemsocket_scroll_error_invalid_target));
                return;
            }

            Item? scroll = session.Item.Inventory.Get(scrollUid);
            if (scroll?.Metadata.Function?.Type != ItemFunction.ItemSocketScroll || !int.TryParse(scroll.Metadata.Function.Parameters, out int scrollId)) {
                session.Send(ItemSocketScrollPacket.Error(s_itemsocket_scroll_error_invalid_scroll));
                return;
            }
            if (!TableMetadata.ItemSocketScrollTable.Entries.TryGetValue(scrollId, out ItemSocketScrollMetadata? metadata)) {
                session.Send(ItemSocketScrollPacket.Error(s_itemsocket_scroll_error_invalid_scroll));
                return;
            }

            ItemSocketScrollError error = IsCompatibleScroll(item, metadata);
            if (error != none) {
                session.Send(ItemSocketScrollPacket.Error(error));
                return;
            }

            // This should never happen
            if (item.Socket == null) {
                session.Send(ItemSocketScrollPacket.Error(s_itemsocket_scroll_error_server_fail_unlock_socket));
                return;
            }
            if (!session.Item.Inventory.Consume(scroll.Uid, 1)) {
                session.Send(ItemSocketScrollPacket.Error(s_itemsocket_scroll_error_server_fail_consume_scroll));
                return;
            }

            bool ruleChanged = Unlock(item, metadata);
            session.Send(ItemSocketScrollPacket.Unlock(item, true));
            if (ruleChanged) {
                // The unlock answer carries only the trade count (FUN_14062a230 sets it with SetTransferCount); the
                // item's update gives the client the rule without the Trade bit.
                session.Send(ItemInventoryPacket.UpdateItem(item));
            }
        }
    }

    // The scroll opens its socket count. A scroll with tradableCountDeduction (a bool in the client's
    // CItemSocketScrollData) makes the item untradable: UIItemSocketScrollDialog::FUN_140f66680 warns with
    // s_itemsocket_scroll_tradableCountDeduction_desc ("accessories with unlocked sockets are untradable"). The item
    // loses all its trades left, not one, and its trade rule bit: the client's tradable test (CItemTransfer
    // FUN_140635590, as CanListOnBlackMarket) passes with LimitTrade (0x10) and trades left, or else with the Trade
    // bit (4) alone, and it reads the rule from the item's transfer bytes. So a freely tradable accessory becomes
    // untradable too, as the warning says for any accessory (TradeRules.IsTradeable). True when the Trade bit went.
    public static bool Unlock(Item item, ItemSocketScrollMetadata metadata) {
        item.Socket!.UnlockSlots = metadata.SocketCount;
        if (metadata.TradableCountDeduction == 0 || item.Transfer == null) {
            return false;
        }
        item.Transfer.RemainTrades = 0;
        bool hadTrade = item.Transfer.Flag.HasFlag(TransferFlag.Trade);
        item.Transfer.Flag &= ~TransferFlag.Trade;
        return hadTrade;
    }

    // The client's checks in its order (FUN_140627450, itemsocketsystemutil.cpp:294): usePart 8, slot 5, rank 6,
    // level 7, no socket 3, nothing locked 4. The usePart test (FUN_140627930) passes when the scroll's part mask has
    // the item's kind, 2 for an item with a skinType and 1 for the rest. CItemSocketScrollData's ctor sets the mask
    // to 1 and its Read never stores the usePart it parses (exe 0x140624810 keeps the OR in a stack local), so the
    // client refuses every item with a skinType whatever the row says. The scroll can open no more than it has
    // (11) is the server's own check after the client's.
    private static ItemSocketScrollError IsCompatibleScroll(Item item, ItemSocketScrollMetadata metadata) {
        if (item.Metadata.Property.SkinType != 0) {
            return s_itemsocket_scroll_error_impossible_usepart;
        }
        if (!metadata.ItemTypes.Contains(item.Type.Type)) {
            return s_itemsocket_scroll_error_impossible_slot;
        }
        if (!metadata.Rarities.Contains(item.Rarity)) {
            return s_itemsocket_scroll_error_impossible_rank;
        }
        if (!ScrollRules.SocketLevelAllowed(item.Metadata.Limit.Level, metadata.MinLevel, metadata.MaxLevel)) {
            return s_itemsocket_scroll_error_impossible_level;
        }
        if (item.Socket == null || item.Socket.MaxSlots == 0) {
            return s_itemsocket_scroll_error_invalid_disable;
        }
        if (item.Socket.UnlockSlots >= item.Socket.MaxSlots) {
            return s_itemsocket_scroll_error_socket_unlock_all;
        }
        if (item.Socket.UnlockSlots >= metadata.SocketCount) {
            return s_itemsocket_scroll_error_already_socket_unlock;
        }

        return none;
    }
}
