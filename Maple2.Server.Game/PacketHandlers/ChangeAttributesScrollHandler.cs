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
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

public class ChangeAttributesScrollHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.ChangeAttributesScroll;

    private enum Command : byte {
        Change = 1,
        Select = 3,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required TableMetadataStorage TableMetadata { private get; init; }
    public required ItemStatsCalculator ItemStatsCalc { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Change:
                HandleChange(session, packet);
                return;
            case Command.Select:
                HandleSelect(session, packet);
                return;
        }
    }

    private void HandleChange(GameSession session, IByteReader packet) {
        long scrollUid = packet.ReadLong();
        long itemUid = packet.ReadLong();
        long ingredientUid = packet.ReadLong(); // the talent lock candy (UIItemRemakeScrollDialog m_nIngredientUid)
        packet.ReadByte();

        List<ItemRemakeRules.Lock>? locks = ItemRemakeRules.ReadLocks(packet);
        if (locks == null || !ItemRemakeRules.LocksAllowed(locks.Count, ItemRemakeRules.MaxLocks)) {
            session.Send(ChangeAttributesScrollPacket.Error(ChangeAttributesScrollError.s_itemremake_scroll_error_server_fail_remake));
            return;
        }

        lock (session.Item) {
            Item? scroll = session.Item.Inventory.Get(scrollUid, InventoryType.Misc);
            if (scroll == null || scroll.Metadata.Function?.Type != ItemFunction.ItemRemakeScroll) {
                session.Send(ChangeAttributesScrollPacket.Error(ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_scroll));
                return;
            }

            Item? item = session.Item.Inventory.Get(itemUid, InventoryType.Gear) ?? session.Item.Inventory.Get(itemUid, InventoryType.Pets);
            if (item == null) {
                session.Send(ChangeAttributesScrollPacket.Error(ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_target_data));
                return;
            }
            if (item.Stats == null) {
                session.Send(ChangeAttributesScrollPacket.Error(ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_target_stat));
                return;
            }

            ChangeAttributesScrollError error = IsCompatibleScroll(item, scroll, out ItemRemakeScrollMetadata? itemRemakeScrollMetadata);
            if (error is not ChangeAttributesScrollError.none || itemRemakeScrollMetadata is null) {
                session.Send(ChangeAttributesScrollPacket.Error(error));
                return;
            }

            Item? lockItem = null;
            if (locks.Count > 0) {
                if (!ChangeAttributesHandler.HasLockedOptions(item, locks)) {
                    session.Send(ChangeAttributesScrollPacket.Error(ChangeAttributesScrollError.s_itemremake_scroll_error_impossible_property));
                    return;
                }

                lockItem = ChangeAttributesHandler.GetLockConsumeItem(session, item, ingredientUid);
                if (lockItem == null) {
                    session.Send(ChangeAttributesScrollPacket.Error(ChangeAttributesScrollError.s_itemremake_error_server_fail_lack_lock_consume_item));
                    return;
                }
                if (!ItemRemakeRules.LocksAllowed(locks.Count, ChangeAttributesHandler.LockLimit(item, lockItem))) {
                    session.Send(ChangeAttributesScrollPacket.Error(ChangeAttributesScrollError.s_itemremake_scroll_error_server_fail_remake));
                    return;
                }
            }

            // Clone the item so we can preview changes without modifying existing item.
            Item changeItem = item.Clone();
            if (changeItem.Stats == null) { // This should be impossible, but check again to make linter happy.
                session.Send(ChangeAttributesScrollPacket.Error(ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_target_stat));
                return;
            }

            if (changeItem.Metadata.Option == null) { // This should be impossible, but check again to make linter happy.
                session.Send(ChangeAttributesScrollPacket.Error(ChangeAttributesScrollError.s_itemremake_scroll_error_impossible_item));
                return;
            }

            // A failed roll answers on the scroll dialog's own packet (code 12, its "failed to modify" string); the
            // item dialog's error packet is not read by the scroll dialog. The locked options are added back.
            LockOption[] presets = ChangeAttributesHandler.LockOptions(locks);
            bool rolled = itemRemakeScrollMetadata.RollAttribute
                ? ItemStatsCalc.UpdateRandomOption(ref changeItem, presets)
                : ItemStatsCalc.UpdateFixedOption(ref changeItem, presets);
            if (!rolled) {
                session.Send(ChangeAttributesScrollPacket.Error(ChangeAttributesScrollError.s_itemremake_scroll_error_server_fail_remake));
                return;
            }

            ChangeAttributesScrollError consumed = TakeCosts(session, scroll, lockItem);
            if (consumed != ChangeAttributesScrollError.none) {
                session.Send(ChangeAttributesScrollPacket.Error(consumed));
                return;
            }

            if (item.Metadata.Limit.TransferType is TransferType.BindPet) {
                item.Transfer?.Bind(session.Player.Value.Character);
                session.Send(ItemInventoryPacket.UpdateItem(item));
            }

            session.ChangeAttributesItem = changeItem;
            session.Send(ChangeAttributesScrollPacket.PreviewItem(session.ChangeAttributesItem));
        }
    }

    // Takes the scroll, then the lock item, or neither. |session.Item| is locked by the caller.
    internal static ChangeAttributesScrollError TakeCosts(GameSession session, Item scroll, Item? lockItem) {
        // The scroll's take fails for an expired scroll (it is not checked before), and then the lock item is not
        // taken. Before, the lock item was taken anyway and given back as a copy with its uid: with a stack of one its
        // row stayed on the bag's delete list, so once the item left the bag (stored, traded, mailed) the next save of
        // the bag deleted its row.
        if (!session.Item.Inventory.Consume(scroll.Uid, 1)) {
            return ChangeAttributesScrollError.s_itemremake_scroll_error_server_fail_consume_scroll;
        }
        if (lockItem != null && !session.Item.Inventory.Consume(lockItem.Uid, 1)) {
            // Not reached today: the lock item was found under the same lock, and its take refuses only an expired
            // item, which GetLockConsumeItem skips. The scroll comes back as a new item of its id and rarity (chosen
            // here: its copy would carry the uid of a row that may be marked to be deleted), by mail when the bag
            // refuses it.
            Item? back = session.Field?.ItemDrop.CreateItem(scroll.Id, scroll.Rarity, 1);
            if (back == null || !session.Item.AddOrMail(back, commit: true)) {
                Serilog.Log.Logger.Error("Remake scroll {ItemId} of {CharacterId} not given back", scroll.Id, session.CharacterId);
            }
            return ChangeAttributesScrollError.s_itemremake_error_server_fail_lack_lock_consume_item;
        }
        return ChangeAttributesScrollError.none;
    }

    private static void HandleSelect(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        if (session.ChangeAttributesItem == null || session.ChangeAttributesItem.Uid != itemUid) {
            session.Send(ChangeAttributesScrollPacket.Error(ChangeAttributesScrollError.s_itemremake_scroll_error_server_fail_apply_before_option));
            return;
        }

        lock (session.Item) {
            Item? item = session.Item.Inventory.Get(itemUid, InventoryType.Gear) ?? session.Item.Inventory.Get(itemUid, InventoryType.Pets);
            if (item == null) {
                session.Send(ChangeAttributesScrollPacket.Error(ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_target_data));
                return;
            }

            if (item.Stats == null || session.ChangeAttributesItem.Stats == null) {
                session.Send(ChangeAttributesScrollPacket.Error(ChangeAttributesScrollError.s_itemremake_scroll_error_server_fail_apply_before_option));
                return;
            }

            item.Stats[ItemStats.Type.Random] = session.ChangeAttributesItem.Stats[ItemStats.Type.Random];
            session.ChangeAttributesItem = null;
            session.Send(ChangeAttributesScrollPacket.SelectItem(item));
        }
    }

    private ChangeAttributesScrollError IsCompatibleScroll(Item item, Item scroll, out ItemRemakeScrollMetadata? metadata) {
        metadata = null;
        if (item.Rarity is < Constant.ChangeAttributesMinRarity or > Constant.ChangeAttributesMaxRarity) {
            return ChangeAttributesScrollError.s_itemremake_scroll_error_impossible_rank;
        }
        if (!item.Type.IsWeapon && item.Type is { IsArmor: false, IsAccessory: false, IsPet: false }) {
            return ChangeAttributesScrollError.s_itemremake_scroll_error_impossible_slot;
        }
        if (item.Metadata.Limit.Level < Constant.ChangeAttributesMinLevel) {
            return ChangeAttributesScrollError.s_itemremake_scroll_error_impossible_level;
        }

        // Validate scroll conditions
        if (!int.TryParse(scroll.Metadata.Function?.Parameters, out int remakeId)) {
            return ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_scroll_data;
        }
        if (!TableMetadata.ItemRemakeScrollTable.Entries.TryGetValue(remakeId, out metadata)) {
            return ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_scroll_data;
        }
        return ScrollFits(item, metadata);
    }

    // The checks of the scroll's row (the client's FUN_140616170, itemremakescrolldata.cpp:283): after the item
    // types, rarities and levels, a row without an option value (addOpValue 0) is invalid scroll data (error 11).
    internal static ChangeAttributesScrollError ScrollFits(Item item, ItemRemakeScrollMetadata metadata) {
        if (item.Metadata.Limit.Level < metadata.MinLevel || item.Metadata.Limit.Level > metadata.MaxLevel) {
            return ChangeAttributesScrollError.s_itemremake_scroll_error_impossible_item;
        }
        if (!metadata.Rarities.Contains(item.Rarity) || !metadata.ItemTypes.Contains(item.Type.Type) && !metadata.OnlyPet) {
            return ChangeAttributesScrollError.s_itemremake_scroll_error_impossible_item;
        }

        if (metadata.OnlyPet && !item.Type.IsPet) {
            return ChangeAttributesScrollError.s_itemremake_scroll_error_impossible_item;
        }
        if (metadata.RollValueType == RollValueType.None) {
            return ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_scroll_data;
        }

        return ChangeAttributesScrollError.none;
    }
}
