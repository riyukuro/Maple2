using System.Diagnostics;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model;
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

            // The client sends no material for an item with a skin type and exactly the feature's count for any
            // other (ItemSocketRules.UnlockMaterials), each a different item of its list.
            int skinType = item.Metadata.Property.SkinType;
            if (materialUids.Length != ItemSocketRules.UnlockMaterials(skinType)) {
                session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_invalid_target_ingredient_count));
                return;
            }
            if (materialUids.Distinct().Count() != materialUids.Length) {
                session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_invalid_target_ingredient));
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

                // The client's list (EnableSocketUnlockFunction::Matches) leaves out the item itself and an item with
                // a locked gem; an item with other gems is sent after s_itemsocketsystem_socketunlock_msg_ask_unlock_
                // ingredient_in_gemstone, whose text says its gems go with it. It leaves out an expired item
                // (CItem::FUN_140528980) and a locked one (CItem::FUN_140528e20: locked, or unlocking until UnlockTime).
                if (material.Uid == item.Uid || !IsSocketMaterialEqual(item, material) || HasLockedGem(material)
                    || material.IsExpired() || material.IsLocked || material.UnlockTime > DateTimeOffset.UtcNow.ToUnixTimeSeconds()) {
                    session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_invalid_target_ingredient));
                    return;
                }
            }

            SocketPrice? price = SocketPrice.Check(session, [SocketUnlockIngredient(session, item)]);
            if (price == null || !price.Take(session)) {
                session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_lack_price));
                return;
            }

            foreach (long materialUid in materialUids) {
                if (!session.Item.Inventory.Consume(materialUid, amount: 1)) {
                    Logger.Fatal("Failed to consume material: {ItemUid}", materialUid);
                    session.Send(ItemSocketPacket.Error(code: 1, error: s_itemsocketsystem_error_server_default));
                    return;
                }
            }

            // A skin socket unlock binds the item to the character: the client asks first for an unbound item with no
            // open socket (s_itemsocketsystem_socketunlock_msg_ask_unlock_skin_first, "change it to a character-bound
            // state"). The answer carries the item's transfer, so the client takes the binding from it.
            if (skinType != 0) {
                item.Transfer?.BindAnyway(session.Player.Value.Character);
            }

            item.Socket.UnlockSlots++;
            session.Send(ItemSocketPacket.UnlockSocket(true, item));
            session.ConditionUpdate(ConditionType.socket_unlock, targetLong: item.Socket.UnlockSlots);
            session.ConditionUpdate(ConditionType.socket_unlock_success);
            session.ConditionUpdate(ConditionType.socket_unlock_try);
        }

    }

    // The unlock's catalyst (GetSocketUnlockCost, the same rule as the Lua), lowered by the ReduceGameItemSocketUnlockFee
    // attribute (SA 179) on an item with no skin type (FUN_140627ac0), held to IngredientFeeReduceLimit and at least 1
    // (FUN_140627b80, the same math as a change of attributes' ingredient count).
    private static IngredientInfo SocketUnlockIngredient(GameSession session, Item equip) {
        int skinType = equip.Metadata.Property.SkinType;
        (int _, string tag, int amount) =
            Lua.CalcItemSocketUnlockIngredient(0, equip.Rarity, (ushort) equip.Metadata.Limit.Level, 0, skinType);
        int rate = skinType > 0 ? 0
            : EventSaleRules.RemakeDiscount(0, session.Player.Stats.Values[SpecialAttribute.ReduceGameItemSocketUnlockFee].Total);
        int count = EventSaleRules.RemakeIngredientCount(amount, rate, session.ServerTableMetadata.ConstantsTable.IngredientFeeReduceLimit);
        return new IngredientInfo(Enum.Parse<ItemTag>(tag), count);
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

                if (!TakeGemstoneUpgradePrice(result.Value.entry, null)) {
                    return;
                }

                if (!RollGemstoneUpgrade(result.Value.entry)) {
                    // The gem stays as it was; the client stores the gem the answer carries in the slot.
                    session.Send(ItemSocketPacket.UpgradeGemstone(itemUid, (byte) slot, false, gemUid, result.Value.gem));
                    session.ConditionUpdate(ConditionType.gemstone_upgrade_try);
                    session.ConditionUpdate(ConditionType.gemstone_upgrade_fail);
                    return;
                }

                result.Value.gem.ItemId = result.Value.entry.NextItemId;
                session.Send(ItemSocketPacket.UpgradeGemstone(itemUid, (byte) slot, true, gemUid, result.Value.gem));
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

                if (!TakeGemstoneUpgradePrice(result.Value.entry, session.Item.Inventory.Get(gemUid, InventoryType.Gemstone))) {
                    return;
                }

                if (!RollGemstoneUpgrade(result.Value.entry)) {
                    // The gem stays in the inventory as it was.
                    Item? kept = session.Item.Inventory.Get(gemUid, InventoryType.Gemstone);
                    if (kept != null) {
                        session.Send(ItemSocketPacket.UpgradeGemstone(itemUid, false, kept));
                    }
                    session.ConditionUpdate(ConditionType.gemstone_upgrade_try);
                    session.ConditionUpdate(ConditionType.gemstone_upgrade_fail);
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
        bool RollGemstoneUpgrade(GemstoneUpgradeTable.Entry entry) {
            return ItemSocketRules.GemstoneUpgradeSucceeds(GemstoneUpgradeRate(session, entry), Random.Shared.Next(100));
        }

        // The client's request (FUN_140f76970): each ingredient's count lowered by the sale, the owned count leaving
        // out the gem itself, crystal fragments short paid in mesos, else s_itemsocketsystem_error_lack_price.
        bool TakeGemstoneUpgradePrice(GemstoneUpgradeTable.Entry entry, Item? gem) {
            SocketPrice? price = SocketPrice.Check(session, GemstoneUpgradeCosts(session, entry), gem);
            if (price == null || !price.Take(session)) {
                session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_lack_price));
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

        GemstoneUpgradeTable.Entry? entry;
        if (itemUid != 0) {
            entry = CheckUpgradeGemstoneOnItem(session, itemUid, slot)?.Item2;
        } else {
            entry = CheckUpgradeGemstoneInInventory(session, gemUid)?.Item1;
        }
        if (entry == null) {
            return;
        }

        // The page shows this rate (s_itemsocketsystem_success_prob); the upgrade rolls the same one.
        session.Send(ItemSocketPacket.StageUpgradeGemstone(itemUid, slot, gemUid, GemstoneUpgradeRate(session, entry)));
    }

    // The upgrade's sale (FUN_140627a20): the higher of the SaleGemStoneUpgrade game rule and the ReduceGemstoneUpgradeFee
    // attribute (SA 172), none for a row whose equipPart mask has bit 8; each count is then lowered as an unlock's is
    // (FUN_140627b80). Chosen here: the server keeps no SaleGemStoneUpgrade event data, so only the attribute applies.
    private static IEnumerable<SocketCost> GemstoneUpgradeCosts(GameSession session, GemstoneUpgradeTable.Entry entry) {
        int rate = (entry.EquipPart & 8) != 0 ? 0
            : EventSaleRules.RemakeDiscount(0, session.Player.Stats.Values[SpecialAttribute.ReduceGemstoneUpgradeFee].Total);
        float limit = session.ServerTableMetadata.ConstantsTable.IngredientFeeReduceLimit;
        // An ingredient named by id is a copy of that item's rank (GetItemRank, commonfiles/itemdata.cpp:2084, the rarity
        // FUN_140f6dc70 builds it with); FUN_140f6e580 counts it with Unk_Inventory::FUN_140a5f180 (client/ms2/inventory.cpp:782-803),
        // which skips a copy of another rarity when the rank is not 0. A tag ingredient counts at any rarity.
        return entry.Ingredients.Select(ingredient =>
            new SocketCost(ingredient.ItemTag, ingredient.ItemId, EventSaleRules.RemakeIngredientCount(ingredient.Amount, rate, limit),
                ingredient.ItemTag == ItemTag.None && session.ItemMetadata.TryGet(ingredient.ItemId, out ItemMetadata? metadata)
                    ? metadata.Property.Rank ?? 0 : 0));
    }

    private static int GemstoneUpgradeRate(GameSession session, GemstoneUpgradeTable.Entry entry) {
        ConstantsTable constants = session.ServerTableMetadata.ConstantsTable;
        return ItemSocketRules.GemstoneUpgradeRate(entry.Level, entry.EquipPart, constants.GemStoneProbList, constants.SkinGemStoneProbList);
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
            // The socketed gem's binding is the gem's trade binding (the bag gem's writer sends Transfer.Binding in the
            // same place), and its whole transfer is kept for the take-off.
            ItemTransfer? transfer = gem.Transfer?.Clone();
            var itemGemstone = new ItemGemstone(gem.Id, transfer?.Binding?.Clone(), gem.Stats?.Clone(), gem.IsLocked, gem.UnlockTime, transfer);
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
            if (gem != null) {
                RestoreGemstone(gem, itemGemstone, session.Player.Value.Character);
            }
            SocketPrice? price = null;
            switch (GemstoneUnequipRules.TakeOut(gem != null, gem != null && session.Item.Inventory.CanAdd(gem),
                        () => TakeGemstoneUnequipMesos(itemGemstone, item.Metadata.Property.SkinType, out price))) {
                case GemstoneUnequipRules.Result.NoGemstone:
                    session.Send(ItemSocketPacket.Error(11, error: s_itemsocketsystem_error_server_default));
                    return;
                case GemstoneUnequipRules.Result.NoRoom:
                    session.Send(ItemInventoryPacket.Error(Maple2.Model.Error.ItemInventoryError.s_err_inventory));
                    return;
                case GemstoneUnequipRules.Result.NotPaid:
                    return; // the price check sent its own error
            }

            // The mesos are taken, then the gem goes to the bag, then the fragments (checked under this lock).
            if (!session.Item.Inventory.Add(gem!, true)) {
                price!.ReturnMesos(session);
                session.Send(ItemInventoryPacket.Error(Maple2.Model.Error.ItemInventoryError.s_err_inventory));
                return;
            }
            if (!price!.TakeItems(session)) {
                Logger.Fatal("Failed to take the take-off price of gem {ItemUid} after its check", gem!.Uid);
            }

            item.Socket!.Sockets[socketSlot] = null;
            session.Send(ItemSocketPacket.UnequipGemstone(item.Uid, socketSlot, gem.Uid));
            session.ConditionUpdate(ConditionType.gemstone_putoff);
            session.ConditionUpdate(ItemSocketRules.GemstonePutOffCondition(item.Metadata.Property.SkinType));
        }

        #region Local Function
        // The gem taken out is the gem that went in: its trade count, binding and lock, not a fresh item's (a fresh
        // transfer unbound a bound gem and reset its trades). Its stats are the fresh gem's, rolled for its id: a gem
        // upgraded in the socket changed only its id. A gem socketed before its transfer was kept gets back its lock,
        // and is bound to the character when its rule binds (Bind in the flag): its record's binding was the bag
        // gem's storage binding, not a trade binding, so a bound gem cannot be told from an unbound one, and binding
        // never makes a gem looser than it went in.
        static void RestoreGemstone(Item gem, ItemGemstone socketed, Character character) {
            if (socketed.Transfer != null) {
                gem.Transfer = socketed.Transfer.Clone();
            } else if (gem.Transfer != null && gem.Transfer.Flag.HasFlag(TransferFlag.Bind)) {
                gem.Transfer.BindAnyway(character);
            }
            gem.IsLocked = socketed.IsLocked;
            gem.UnlockTime = socketed.UnlockTime;
        }

        // The client's price (FUN_1406284f0 with the gem's level and the item's skin type: free for an outfit).
        bool TakeGemstoneUnequipMesos(ItemGemstone gemstone, int skinType, out SocketPrice? price) {
            price = null;
            if (!TableMetadata.GemstoneUpgradeTable.Entries.TryGetValue(gemstone.ItemId, out GemstoneUpgradeTable.Entry? entry)) {
                session.Send(ItemSocketPacket.Error(code: 12, error: s_itemsocketsystem_error_server_default));
                return false;
            }

            (string tag, int amount) = Lua.CalcGetGemStonePutOffPrice(Constant.GemstoneGrade, (ushort) entry.Level, skinType);
            price = SocketPrice.Check(session, [new IngredientInfo(Enum.Parse<ItemTag>(tag), amount)]);
            if (price == null || !price.TakeMesos(session)) {
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

    private static bool HasLockedGem(Item material) {
        return material.Socket != null && material.Socket.Sockets.Any(gem => gem is { IsLocked: true });
    }

    private enum SocketState { None, Locked, Used, Empty }
    private static SocketState CheckSocketSlot(ItemSocket? itemSocket, byte slot) {
        // Sockets holds the open sockets only (its length is UnlockSlots), so a slot from UnlockSlots up is locked.
        if (itemSocket == null || slot >= itemSocket.MaxSlots) {
            return SocketState.None;
        }
        if (slot >= itemSocket.UnlockSlots) {
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

    // A socket page's price (the unlock, the gem upgrade and the take-off). The client lets mesos make up crystal
    // fragments (ItemTagKey 96, CrystalPiece) the player is short of, 1000 for each, and sends the request when the
    // mesos cover them (FUN_140526320; RequestUnlock, FUN_140f76970, FUN_140f71530); any other tag must be held in
    // full. Checked first under lock(session.Item), so the items it takes stay held; the mesos are taken first (the
    // one step another thread can change) and given back once if a later step fails.
    // One cost item: by its tag when it has one, else by its item id (GetOwnedItemCount).
    // Rarity (0: any) holds a cost by id to copies of that rarity.
    private readonly record struct SocketCost(ItemTag Tag, int ItemId, int Amount, int Rarity = 0) {
        public SocketCost(IngredientInfo ingredient) : this(ingredient.Tag, 0, ingredient.Amount) { }

        public bool Matches(Item item) => Tag != ItemTag.None ? item.Metadata.Property.Tag == Tag
            : ItemId != 0 && item.Id == ItemId && (Rarity == 0 || item.Rarity == Rarity);
    }

    private sealed class SocketPrice {
        public const long MesosPerChip = 1000;

        private readonly (long Uid, int Amount)[] picks;
        public long Mesos { get; }

        private SocketPrice((long, int)[] picks, long mesos) {
            this.picks = picks;
            Mesos = mesos;
        }

        public static SocketPrice? Check(GameSession session, IEnumerable<IngredientInfo> price) {
            return Check(session, price.Select(ingredient => new SocketCost(ingredient)), null);
        }

        // The stacks each cost is taken from, picked now and taken under the same lock. The excluded item (the gem
        // being upgraded) counts one less, as the client counts it (CItem::vfunc_4 with SetExcluded). Only crystal
        // fragments (FUN_140526320, string tag 0x60) may be short, paid at 1000 mesos each.
        public static SocketPrice? Check(GameSession session, IEnumerable<SocketCost> price, Item? excluded) {
            var picks = new List<(long, int)>();
            var used = new Dictionary<long, int>();
            long mesos = 0;
            foreach (SocketCost cost in price) {
                if (cost.Amount <= 0) {
                    continue;
                }
                int remaining = cost.Amount;
                foreach (Item item in session.Item.Inventory.Filter(item => item.IsSpendable() && cost.Matches(item))) {
                    int free = item.Amount - used.GetValueOrDefault(item.Uid) - (excluded != null && item.Uid == excluded.Uid ? 1 : 0);
                    int take = Math.Min(free, remaining);
                    if (take <= 0) {
                        continue;
                    }
                    used[item.Uid] = used.GetValueOrDefault(item.Uid) + take;
                    picks.Add((item.Uid, take));
                    remaining -= take;
                    if (remaining == 0) {
                        break;
                    }
                }
                if (remaining == 0) {
                    continue;
                }
                if (cost.Tag != ItemTag.CrystalPiece) {
                    return null;
                }
                mesos += remaining * MesosPerChip;
            }
            if (session.Currency.Meso < mesos) {
                return null;
            }
            return new SocketPrice(picks.ToArray(), mesos);
        }

        public bool TakeMesos(GameSession session) {
            return Mesos == 0 || session.Currency.TryTakeMeso(Mesos);
        }

        public void ReturnMesos(GameSession session) {
            session.Currency.ReturnMeso(Mesos);
        }

        // The stacks checked, still held under the same lock.
        public bool TakeItems(GameSession session) {
            foreach ((long uid, int amount) in picks) {
                if (!session.Item.Inventory.Consume(uid, amount)) {
                    Serilog.Log.Fatal("Failed to consume socket price item {ItemUid} x{Amount}", uid, amount);
                    return false;
                }
            }
            return true;
        }

        // Mesos, then items; the mesos come back when the items cannot be taken.
        public bool Take(GameSession session) {
            if (!TakeMesos(session)) {
                return false;
            }
            if (!TakeItems(session)) {
                ReturnMesos(session);
                return false;
            }
            return true;
        }
    }
}
