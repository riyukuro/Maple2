using System.Diagnostics;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.LuaFunctions;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;
using static Maple2.Model.Error.ItemSocketError;

namespace Maple2.Server.Game.PacketHandlers;

public class ItemSocketHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.ItemSocketSystem;

    private enum Command : byte {
        UnlockSocket = 0,
        StageUnlockSocket = 2,
        UpgradeGemstone = 4,
        StageUpgradeGemstone = 6,
        EquipGemstone = 8,
        UnequipGemstone = 10,
        TransferSocket = 12,
        Unknown14 = 14,
        Unknown15 = 15,
        Unknown16 = 16,
        StageTransferSocket = 17,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required ItemMetadataStorage ItemMetadata { private get; init; }
    public required TableMetadataStorage TableMetadata { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.UnlockSocket:
                HandleUnlockSocket(session, packet);
                return;
            case Command.StageUnlockSocket:
                HandleStageUnlockSocket(session, packet);
                return;
            case Command.UpgradeGemstone:
                HandleUpgradeGemstone(session, packet);
                return;
            case Command.StageUpgradeGemstone:
                HandleStageUpgradeGemstone(session, packet);
                return;
            case Command.EquipGemstone:
                HandleEquipGemstone(session, packet);
                return;
            case Command.UnequipGemstone:
                HandleUnequipGemstone(session, packet);
                return;
            case Command.TransferSocket:
                HandleTransferSocket(session, packet);
                return;
            case Command.StageTransferSocket:
                HandleDebugTransferSocket(packet);
                return;
        }
    }

    private void HandleUnlockSocket(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        byte count = packet.ReadByte();
        long[] materialUids = new long[count];
        for (int i = 0; i < count; i++) {
            materialUids[i] = packet.ReadLong();
        }

        lock (session.Item) {
            Item? item = session.Item.GetSocketItem(itemUid);
            if (item == null) {
                session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_invalid_target));
                return;
            }

            if (item.Socket == null || item.Socket.UnlockSlots >= item.Socket.MaxSlots) {
                session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_socket_unlock_all));
                return;
            }

            foreach (long materialUid in materialUids) {
                // The client offers ingredients from inventory tabs 0 and 1 (gear and outfit).
                Item? material = ItemSocketRules.Inventories.Select(type => session.Item.Inventory.Get(materialUid, type))
                    .FirstOrDefault(found => found != null);
                if (material == null) {
                    session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_invalid_target));
                    return;
                }

                if (!IsSocketMaterialEqual(item, material) || !AllSocketsEmpty(material)) {
                    session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_invalid_target_ingredient));
                    return;
                }
            }

            if (!ConsumeSocketUnlockIngredients(item)) {
                return;
            }

            foreach (long materialUid in materialUids) {
                if (!session.Item.Inventory.Consume(materialUid, amount: 1)) {
                    Logger.Fatal("Failed to consume material: {ItemUid}", materialUid);
                    session.Send(ItemSocketPacket.Error(code: 1, error: s_itemsocketsystem_error_server_default));
                    return;
                }
            }

            item.Socket.UnlockSlots++;
            session.Send(ItemSocketPacket.UnlockSocket(true, item));
            session.ConditionUpdate(ConditionType.socket_unlock, targetLong: item.Socket.UnlockSlots);
            session.ConditionUpdate(ConditionType.socket_unlock_success);
            session.ConditionUpdate(ConditionType.socket_unlock_try);
        }

        #region Local Function
        bool ConsumeSocketUnlockIngredients(Item equip) {
            (int _, string tag, int amount) =
                Lua.CalcItemSocketUnlockIngredient(0, equip.Rarity, (ushort) equip.Metadata.Limit.Level, 0, equip.Metadata.Property.SkinType);

            var ingredient = new IngredientInfo(Enum.Parse<ItemTag>(tag), amount);
            if (!session.Item.Inventory.Consume([ingredient])) {
                session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_lack_price));
                return false;
            }

            return true;
        }
        #endregion
    }

    private void HandleStageUnlockSocket(GameSession session, IByteReader packet) {
        long zero = packet.ReadLong(); // 0
        byte max = packet.ReadByte(); // 0xFF (-1/255)
        Debug.Assert(zero == 0 && max == byte.MaxValue);

        long itemUid = packet.ReadLong();

        Item? item = session.Item.GetSocketItem(itemUid);
        if (item == null) {
            // Any reply lifts the page's wait; the client shows this message for an item it cannot find itself.
            session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_invalid_target));
            return;
        }

        session.Send(ItemSocketPacket.StageUnlockSocket(item, 100f));
    }

    private void HandleUpgradeGemstone(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        sbyte slot = packet.Read<sbyte>(); // -1 if not equipped
        long gemUid = packet.ReadLong();   // this has a client-generated uid when if equipped

        lock (session.Item) {
            if (itemUid != 0) {
                (ItemGemstone gem, GemstoneUpgradeTable.Entry entry)? result = CheckUpgradeGemstoneOnItem(session, itemUid, slot);
                if (result == null) {
                    return;
                }

                if (!ConsumeGemstoneUpgradeIngredients(result.Value.entry)) {
                    return;
                }

                result.Value.gem.ItemId = result.Value.entry.NextItemId;
                session.Send(ItemSocketPacket.UpgradeGemstone(itemUid, true, gemUid, result.Value.gem));
                session.ConditionUpdate(ConditionType.gemstone_upgrade, targetLong: result.Value.entry.Level + 1);
                session.ConditionUpdate(ConditionType.gemstone_upgrade_try);
                session.ConditionUpdate(ConditionType.gemstone_upgrade_success);
            } else {
                (GemstoneUpgradeTable.Entry entry, ItemMetadata upgrade)? result = CheckUpgradeGemstoneInInventory(session, gemUid);
                if (result == null) {
                    return;
                }

                if (session.Item.Inventory.FreeSlots(InventoryType.Gemstone) < 1) {
                    session.Send(ItemSocketPacket.Error(code: 5, error: s_itemsocketsystem_error_server_default));
                    return;
                }

                if (!ConsumeGemstoneUpgradeIngredients(result.Value.entry)) {
                    return;
                }

                if (!session.Item.Inventory.Remove(gemUid, out Item? gem, amount: 1)) {
                    session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_invalid_target_gemstone));
                    return;
                }

                Item upgradeGem = gem.Mutate(result.Value.upgrade);
                if (!session.Item.Inventory.Add(upgradeGem, true)) {
                    Logger.Fatal("Failed to add upgraded gem {ItemUid} to inventory", upgradeGem.Uid);
                    session.Send(ItemSocketPacket.Error(code: 6, error: s_itemsocketsystem_error_server_default));
                    return;
                }

                session.Send(ItemSocketPacket.UpgradeGemstone(itemUid, true, upgradeGem));
                session.ConditionUpdate(ConditionType.gemstone_upgrade, targetLong: result.Value.entry.Level + 1);
                session.ConditionUpdate(ConditionType.gemstone_upgrade_try);
                session.ConditionUpdate(ConditionType.gemstone_upgrade_success);
            }
        }

        #region Local Function
        bool ConsumeGemstoneUpgradeIngredients(GemstoneUpgradeTable.Entry entry) {
            IngredientInfo[] ingredients = entry.Ingredients.Select(ingredient => new IngredientInfo(ingredient.ItemTag, ingredient.Amount)).ToArray();
            if (!session.Item.Inventory.Consume(ingredients)) {
                session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_invalid_target_ingredient_count));
                return false;
            }

            return true;
        }
        #endregion
    }

    private void HandleStageUpgradeGemstone(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        sbyte slot = packet.Read<sbyte>(); // -1 if not equipped
        long gemUid = packet.ReadLong();   // this has a client-generated uid when if equipped

        if (itemUid != 0) {
            if (CheckUpgradeGemstoneOnItem(session, itemUid, slot) == null) {
                return;
            }
        } else {
            if (CheckUpgradeGemstoneInInventory(session, gemUid) == null) {
                return;
            }
        }

        session.Send(ItemSocketPacket.StageUpgradeGemstone(itemUid, slot, gemUid, 100f));
    }

    private void HandleEquipGemstone(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        long gemUid = packet.ReadLong();
        byte socketSlot = packet.ReadByte();

        lock (session.Item) {
            Item? item = session.Item.GetSocketItem(itemUid);
            if (item == null) {
                session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_invalid_target));
                return;
            }
            Item? gem = session.Item.Inventory.Get(gemUid, InventoryType.Gemstone);
            if (gem == null) {
                session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_invalid_target_gemstone));
                return;
            }

            SocketState state = CheckSocketSlot(item.Socket, socketSlot);
            switch (state) {
                case SocketState.None:
                    session.Send(ItemSocketPacket.Error(code: 8, error: s_itemsocketsystem_error_server_default));
                    return;
                case SocketState.Locked:
                    session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_socket_lock));
                    return;
                case SocketState.Used:
                    session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_socket_used));
                    return;
            }

            Debug.Assert(state == SocketState.Empty);
            if (!session.Item.Inventory.Consume(gemUid, amount: 1)) {
                session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_invalid_target_gemstone));
                return;
            }

            // item.Socket cannot be null after CheckSocketSlot()
            var itemGemstone = new ItemGemstone(gem.Id, gem.Binding, gem.Stats, gem.IsLocked, gem.UnlockTime);
            item.Socket!.Sockets[socketSlot] = itemGemstone;
            session.Send(ItemSocketPacket.EquipGemstone(item.Uid, socketSlot, itemGemstone));
            session.ConditionUpdate(ConditionType.gemstone_puton);
            session.ConditionUpdate(ItemSocketRules.GemstonePutOnCondition(item.Metadata.Property.SkinType));
        }
    }

    private void HandleUnequipGemstone(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        byte socketSlot = packet.ReadByte();

        lock (session.Item) {
            Item? item = session.Item.GetSocketItem(itemUid);
            if (item == null) {
                session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_invalid_target));
                return;
            }

            SocketState state = CheckSocketSlot(item.Socket, socketSlot);
            switch (state) {
                case SocketState.None:
                    session.Send(ItemSocketPacket.Error(code: 10, error: s_itemsocketsystem_error_server_default));
                    return;
                case SocketState.Locked:
                    session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_socket_lock));
                    return;
                case SocketState.Empty:
                    session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_socket_empty));
                    return;
            }

            Debug.Assert(state == SocketState.Used);
            // item.Socket cannot be null after CheckSocketSlot()
            // itemGemstone cannot be null if SocketState.Used
            ItemGemstone itemGemstone = item.Socket!.Sockets[socketSlot]!;
            // The price is taken only for a gemstone that exists and has a slot to go to.
            Item? gem = session.Field?.ItemDrop.CreateItem(itemGemstone.ItemId, rarity: Constant.GemstoneGrade);
            switch (GemstoneUnequipRules.TakeOut(gem != null, gem != null && session.Item.Inventory.CanAdd(gem),
                        () => ConsumeGemstoneUnequipIngredients(itemGemstone))) {
                case GemstoneUnequipRules.Result.NoGemstone:
                    session.Send(ItemSocketPacket.Error(11, error: s_itemsocketsystem_error_server_default));
                    return;
                case GemstoneUnequipRules.Result.NoRoom:
                    session.Send(ItemInventoryPacket.Error(Maple2.Model.Error.ItemInventoryError.s_err_inventory));
                    return;
                case GemstoneUnequipRules.Result.NotPaid:
                    return; // the price check sent its own error
            }

            if (!session.Item.Inventory.Add(gem!, true)) {
                return; // Failed to add to inventory
            }

            item.Socket!.Sockets[socketSlot] = null;
            session.Send(ItemSocketPacket.UnequipGemstone(item.Uid, socketSlot, gem.Uid));
            session.ConditionUpdate(ConditionType.gemstone_putoff);
            session.ConditionUpdate(ItemSocketRules.GemstonePutOffCondition(item.Metadata.Property.SkinType));
        }

        #region Local Function
        bool ConsumeGemstoneUnequipIngredients(ItemGemstone gemstone) {
            if (!TableMetadata.GemstoneUpgradeTable.Entries.TryGetValue(gemstone.ItemId, out GemstoneUpgradeTable.Entry? entry)) {
                session.Send(ItemSocketPacket.Error(code: 12, error: s_itemsocketsystem_error_server_default));
                return false;
            }

            (string tag, int amount) = Lua.CalcGetGemStonePutOffPrice(Constant.GemstoneGrade, (ushort) entry.Level, 0);
            var ingredient = new IngredientInfo(Enum.Parse<ItemTag>(tag), amount);
            if (!session.Item.Inventory.Consume([ingredient])) {
                session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_lack_price));
                return false;
            }

            return true;
        }
        #endregion
    }

    private (ItemGemstone, GemstoneUpgradeTable.Entry)? CheckUpgradeGemstoneOnItem(GameSession session, long itemUid, sbyte slot) {
        ItemSocket? itemSocket = session.Item.GetSocketItem(itemUid)?.Socket;
        if (itemSocket == null) {
            session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_invalid_target));
            return null;
        }
        if (slot < 0 || slot >= itemSocket.UnlockSlots) {
            session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_server_default));
            return null;
        }

        ItemGemstone? gem = itemSocket.Sockets[slot];
        if (gem == null) {
            session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_socket_empty));
            return null;
        }

        if (!TableMetadata.GemstoneUpgradeTable.Entries.TryGetValue(gem.ItemId, out GemstoneUpgradeTable.Entry? entry)) {
            session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_invalid_target_ingredient));
            return null;
        }
        if (entry.NextItemId == 0) {
            session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_gemstone_maxlevel));
            return null;
        }

        return (gem, entry);
    }

    private (GemstoneUpgradeTable.Entry, ItemMetadata)? CheckUpgradeGemstoneInInventory(GameSession session, long gemUid) {
        Item? gem = session.Item.Inventory.Get(gemUid, InventoryType.Gemstone);
        if (gem == null) {
            session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_invalid_target_gemstone));
            return null;
        }

        if (!TableMetadata.GemstoneUpgradeTable.Entries.TryGetValue(gem.Id, out GemstoneUpgradeTable.Entry? entry)) {
            session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_invalid_target_ingredient));
            return null;
        }
        if (entry.NextItemId == 0) {
            session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_gemstone_maxlevel));
            return null;
        }
        if (!ItemMetadata.TryGet(entry.NextItemId, out ItemMetadata? upgrade)) {
            session.Send(ItemSocketPacket.Error(code: 4, error: s_itemsocketsystem_error_server_default));
            return null;
        }

        return (entry, upgrade);
    }

    private static bool IsSocketMaterialEqual(Item item, Item material) {
        return item.Id == material.Id
               && item.Rarity == material.Rarity
               && item.Socket != null
               && material.Socket != null
               && item.Socket.UnlockSlots == material.Socket.UnlockSlots;
    }

    private static bool AllSocketsEmpty(Item material) {
        return material.Socket == null || material.Socket.Sockets.All(gem => gem == null);
    }

    private enum SocketState { None, Locked, Used, Empty }
    private static SocketState CheckSocketSlot(ItemSocket? itemSocket, byte slot) {
        if (itemSocket == null || slot > itemSocket.MaxSlots) {
            return SocketState.None;
        }
        if (slot > itemSocket.UnlockSlots) {
            return SocketState.Locked;
        }
        return itemSocket.Sockets[slot] != null ? SocketState.Used : SocketState.Empty;
    }

    // Socket transfer (client ItemSocketSystemPageSocketTransfer::RequestTransfer, mode 12 with the source and target
    // uids): the open, empty sockets of one outfit move to an outfit of the same socket count that has none open
    // (FUN_140628200). The client lists sources from the outfit tab and the equipped outfits, targets from the outfit
    // tab only, and pays GetSocketTransferCost. The target is sent back with its socket transfer flag (m_b42) cleared,
    // so it cannot pass the sockets on again.
    private void HandleTransferSocket(GameSession session, IByteReader packet) {
        long sourceItemUid = packet.ReadLong();
        long targetItemUid = packet.ReadLong();

        lock (session.Item) {
            Item? source = session.Item.GetOutfit(sourceItemUid);
            Item? target = session.Item.Inventory.Get(targetItemUid, InventoryType.Outfit);
            if (source == null || target == null || source.Uid == target.Uid
                || !CanTransferSockets(source, target, TableMetadata.ItemSocketTable)) {
                session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_invalid_target));
                return;
            }

            int cost = ItemSocketRules.TransferCost(source.Socket!.UnlockSlots, source.Metadata.Property.SkinType);
            if (cost > 0 && !session.Item.Inventory.Consume([new IngredientInfo(ItemSocketRules.TransferCostTag, cost)])) {
                session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_lack_price));
                return;
            }

            byte open = source.Socket.UnlockSlots;
            source.Socket.UnlockSlots = 0;
            target.Socket!.UnlockSlots = open;
            if (target.Transfer != null) {
                target.Transfer.SocketTransfer = false;
            }
            session.Send(ItemSocketPacket.TransferSocket(source, target));
        }
    }

    // 17 is the client's debug transfer (OnDebugTransfer, shown only at GM level 0x32 and up); it is not run.
    private void HandleDebugTransferSocket(IByteReader packet) {
        long sourceItemUid = packet.ReadLong();
        long targetItemUid = packet.ReadLong();
        Logger.Debug("ItemSocket debug transfer from {SourceItemUid} to {TargetItemUid} not run", sourceItemUid, targetItemUid);
    }

    // FUN_140628200: the source may give its sockets, the target may take them, and both hold the same number.
    internal static bool CanTransferSockets(Item source, Item target, ItemSocketTable table) {
        // The client has no transfer page without feature 0xa7 (ItemSocketTransfer01).
        if (!table.SocketTransfer) {
            return false;
        }
        return CanGiveSockets(source, table) && CanTakeSockets(target, table)
               && source.Socket!.MaxSlots == target.Socket!.MaxSlots;
    }

    // FUN_140627e80: some sockets open, none filled, and not an item from a gacha that can be dismantled (CItem m_dc).
    internal static bool CanGiveSockets(Item item, ItemSocketTable table) {
        return MaySocketTransfer(item, table) && item.Socket!.UnlockSlots > 0 && FilledSockets(item.Socket) == 0
               && item.GachaDismantleId <= 0;
    }

    // FUN_140628050: no socket open (nor filled).
    internal static bool CanTakeSockets(Item item, ItemSocketTable table) {
        return MaySocketTransfer(item, table) && item.Socket!.UnlockSlots == 0 && FilledSockets(item.Socket) == 0;
    }

    // What both sides ask: CItemData::HasSkinType, CItemTransfer::CanTransferSocket (m_b42 and m_b41) and
    // CItem::FUN_140529560 (an equipment item of a socket subtype; not a timed item without a socketDataId).
    private static bool MaySocketTransfer(Item item, ItemSocketTable table) {
        // FUN_140529560 first asks feature 0x2e (ItemSocketSystem).
        if (!table.SocketSystem) {
            return false;
        }
        ItemMetadataProperty property = item.Metadata.Property;
        if (property.Type != 1 || property.SkinType == 0 || item.Socket == null) {
            return false;
        }
        if (item.Transfer?.SocketTransfer != true) {
            return false;
        }
        ItemSocketMetadata? row = null;
        if (property.SocketId != 0 && !table.Entries.TryGetValue(property.SocketId, item.Rarity, out row)) {
            table.Entries.TryGetValue(property.SocketId, 0, out row);
        }
        if (!ItemSocketRules.TableAllowsTransfer(property.SocketId, row?.MaxCount, row?.InitialCount)) {
            return false;
        }
        // The client's item data subtype (GearScoreRules.SubType: the id's digits), not the server pack's, which
        // is 0 or 16 for most equipment (the Northern Fur Earrings 11200084: 0 there, 12 in the client).
        if (!ItemSocketRules.HasSocketSubtype(GearScoreRules.SubType(item.Id), property.SkinType)) {
            return false;
        }
        return item.ExpiryTime <= 0 || property.SocketId != 0;
    }

    private static int FilledSockets(ItemSocket socket) => socket.Sockets.Count(gem => gem != null);
}
