using Maple2.Model;
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
using Maple2.Server.Game.Util;
using static Maple2.Model.Error.ChangeAttributesError;

namespace Maple2.Server.Game.PacketHandlers;

public class ChangeAttributesHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.ChangeAttributes;

    private enum Command : byte {
        Change = 0,
        Select = 2,
        KeepCurrent = 3,
        StageItem = 4,
        OpenDialog = 6,
        Debug = 7,
        ForceFill = 8,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
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
            case Command.Debug: // GM buttons of the remake dialog and the pet remake page (GM level 50 or more)
                if (!GmCheck.Allow(session, OpCode, (int) command)) {
                    return;
                }
                HandleDebug(packet);
                return;
            case Command.ForceFill: // GM buttons of the remake dialog (GM level 50 or more)
                if (!GmCheck.Allow(session, OpCode, (int) command)) {
                    return;
                }

                HandleForceFill(session, packet);
                return;
            case Command.KeepCurrent:
                HandleKeepCurrent(session, packet);
                return;
            case Command.StageItem:
                HandleStageItem(session, packet);
                return;
            case Command.OpenDialog:
                HandleOpenDialog(session, packet);
                return;
        }
    }

    private void HandleChange(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        long ingredientUid = packet.ReadLong(); // a pet's talent lock candy (TabContentsPetRemakeOption m_uidIngredient)
        List<ItemRemakeRules.Lock>? locks = ItemRemakeRules.ReadLocks(packet);
        if (locks == null || !ItemRemakeRules.LocksAllowed(locks.Count, ItemRemakeRules.MaxLocks)) {
            session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_impossible));
            return;
        }

        lock (session.Item) {
            // Validate item being changed + lock attributes/lock item if necessary.
            Item? item = session.Item.GetGear(itemUid)
                         ?? session.Item.Inventory.Get(itemUid, InventoryType.Pets);
            if (item == null) {
                session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_not_in_inven));
                return;
            }
            if (!IsValidItem(item)) {
                session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_impossible));
                return;
            }
            if (item.Stats == null) {
                session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_null_status));
                return;
            }

            Item? lockItem = null;
            if (locks.Count > 0) {
                if (!HasLockedOptions(item, locks)) {
                    session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_impossible));
                    return;
                }

                lockItem = GetLockConsumeItem(session, item, ingredientUid);
                if (lockItem == null) {
                    // The dialog's string for a lock without a lock item (OnRemakeError code 9).
                    session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_fail_lack_lock_consume_item));
                    return;
                }
                if (!ItemRemakeRules.LocksAllowed(locks.Count, LockLimit(item, lockItem))) {
                    session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_impossible));
                    return;
                }
            }

            // Calculate required catalysts, at the counts the dialog shows under a running SaleRemake event.
            List<IngredientInfo> ingredients = SaleIngredients(session, item, GetIngredients(item));

            // Clone the item so we can preview changes without modifying existing item.
            Item changeItem = item.Clone();
            if (changeItem.Stats == null) { // This should be impossible, but check again to make linter happy.
                session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_null_status));
                return;
            }

            // Randomize attributes, adding back the locked ones.
            if (!ItemStatsCalc.UpdateRandomOption(ref changeItem, LockOptions(locks))) {
                session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_fail_apply_option));
                return;
            }

            if (!PayChange(session, item, ingredients, lockItem)) {
                return;
            }

            item.TimeChangedOption++;
            changeItem.TimeChangedOption++;
            session.ChangeAttributesItem = changeItem;
            session.Send(ChangeAttributesPacket.PreviewItem(session.ChangeAttributesItem));
            session.ConditionUpdate(ItemRemakeRules.RemakeCondition(item.Type.IsPet));
            session.ConditionUpdate(ItemRemakeRules.RecordCondition(item.Type.IsPet), counter: item.TimeChangedOption);
        }
    }

    // Takes the price of a change. A pet's lacking crystal fragments are paid in meso; nothing is taken unless
    // everything can be, and KMS2 takes the meso before the materials.
    internal static bool PayChange(GameSession session, Item item, List<IngredientInfo> ingredients, Item? lockItem) {
        lock (session.Item) {
            ingredients = [.. ingredients];
            long meso = 0;
            // Pets and gear alike: fragments lacking are paid in meso (ItemRemakeRules.FragmentFee).
            int index = ingredients.FindIndex(info => info.Tag == ItemTag.CrystalPiece);
            if (index >= 0) {
                long owned = session.Item.Inventory.SpendableCount(ItemTag.CrystalPiece);
                (int fragments, meso) = ItemRemakeRules.FragmentFee(ingredients[index].Amount, owned);
                if (fragments > 0) {
                    ingredients[index] = new IngredientInfo(ItemTag.CrystalPiece, fragments);
                } else {
                    ingredients.RemoveAt(index);
                }
            }
            bool owns = ingredients.All(info => session.Item.Inventory.SpendableCount(info.Tag) >= info.Amount);
            if (!owns || session.Currency.Meso < meso) {
                session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_lack_price));
                return false;
            }
            // The lock item as its consume tests it (in the bag, not expired, one left), so a lock item that cannot be
            // taken refuses before anything is. Before, the fee and the materials went and the lock item's consume
            // then refused.
            if (lockItem != null && !LockItemTakeable(session, lockItem)) {
                session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_fail_lack_lock_consume_item));
                return false;
            }
            // Checked and taken in one step, so a spend elsewhere cannot leave the fee unpaid; given back when an
            // item cannot be taken.
            if (meso > 0 && !session.Currency.TryTakeMeso(meso)) {
                session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_lack_price));
                return false;
            }
            // The lock item (one) before the materials, so a refusal after the fee loses no material.
            if (lockItem != null && !session.Item.Inventory.Consume(lockItem.Uid, 1)) {
                session.Currency.ReturnMeso(meso);
                session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_fail_lack_lock_consume_item));
                return false;
            }
            if (!session.Item.Inventory.Consume(ingredients)) {
                session.Currency.ReturnMeso(meso);
                session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_lack_price));
                return false;
            }
            // A pet's change binds the pet: the pet page asks first when the pet has a binding rule and no binding
            // (TabContentsPetRemakeOption FUN_140b95280, tabcontentspetremakeoption.cpp:952-965,
            // s_msg_transfer_bind_pet_remakeoption), as the scroll dialog does for a pet row. Gear is not bound here
            // (the item dialog does not ask).
            if (item.Type.IsPet) {
                session.Item.Bind(item);
            }
            return true;
        }
    }

    internal static List<IngredientInfo> GetIngredients(Item item) {
        var ingredients = new List<IngredientInfo>();
        (string tag1Str, int amount1, string tag2Str, int amount2, string tag3Str, int amount3) = item.Type.IsPet
            ? Lua.CalcGetPetRemakeIngredient(item.TimeChangedOption, item.Rarity, 0)
            : Lua.CalcGetItemRemakeIngredientNew(item.Type.Type, item.TimeChangedOption, item.Rarity, item.Metadata.Limit.Level);
        if (Enum.TryParse<ItemTag>(tag1Str, out ItemTag tag1)) {
            ingredients.Add(new IngredientInfo(tag1, amount1));
        }
        if (Enum.TryParse<ItemTag>(tag2Str, out ItemTag tag2)) {
            ingredients.Add(new IngredientInfo(tag2, amount2));
        }
        if (Enum.TryParse<ItemTag>(tag3Str, out ItemTag tag3)) {
            ingredients.Add(new IngredientInfo(tag3, amount3));
        }

        return ingredients;
    }

    // The ingredient counts a running SaleRemake event or the character's fee attribute leaves, the higher of the
    // two (EventSaleRules.RemakeDiscount). The staged item's reply carries the full counts: the client lowers
    // them itself (EventSaleRules.RemakeIngredientCount).
    internal static List<IngredientInfo> SaleIngredients(GameSession session, Item item, List<IngredientInfo> ingredients) {
        int saleRate = EventSaleRules.RemakeSaleRate(session.FindEvent(GameEventType.SaleRemake)
                .Select(gameEvent => gameEvent.Metadata.Data).OfType<SaleRemake>(),
            EventSaleRules.RemakeSaleType(item.Type.Group, item.Type.Type, item.Type.IsPet));
        SpecialAttribute fee = item.Type.IsPet ? SpecialAttribute.ReducePetRemakeOptionFee : SpecialAttribute.ReduceRemakeOptionFee;
        int rate = EventSaleRules.RemakeDiscount(saleRate, session.Player.Stats.Values[fee].Total);
        if (rate <= 0) {
            return ingredients;
        }
        float limit = session.ServerTableMetadata.ConstantsTable.IngredientFeeReduceLimit;
        return ingredients.Select(ingredient => new IngredientInfo(ingredient.Tag,
            EventSaleRules.RemakeIngredientCount(ingredient.Amount, rate, limit))).ToList();
    }

    private static void HandleSelect(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        if (session.ChangeAttributesItem == null || session.ChangeAttributesItem.Uid != itemUid) {
            session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_fail_apply_option));
            return;
        }

        lock (session.Item) {
            Item? item = session.Item.GetGear(itemUid)
                         ?? session.Item.Inventory.Get(itemUid, InventoryType.Pets);
            if (item == null) {
                session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_not_in_inven));
                return;
            }

            if (item.Stats == null || session.ChangeAttributesItem.Stats == null) {
                session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_null_status));
                return;
            }

            item.Stats[ItemStats.Type.Random] = session.ChangeAttributesItem.Stats[ItemStats.Type.Random];
            session.ChangeAttributesItem = null;
            session.Send(ChangeAttributesPacket.SelectItem(item));
            if (CountsInStats(session, itemUid)) {
                session.Stats.Refresh();
            }
        }
    }

    // The client puts the chosen options on the worn copy and recounts the equipment's stats (UIItemRemakeOptionDialog
    // FUN_1412a1650, unit_14129cfa0_itemremakeoptiondialog.cpp:877-894: SetItem, then FUN_1411e7660). A summoned pet's
    // options count in the owner's totals too (StatsManager.AddPet). Before, the totals kept the old options until the
    // next rebuild.
    internal static bool CountsInStats(GameSession session, long itemUid) {
        return session.Item.Equips.Gear.Values.Any(gear => gear.Uid == itemUid) || session.Pet?.Pet.Uid == itemUid;
    }

    // RemakeOptionDebug::vfunc_1 (itemremakescrolldata.cpp:453+, 0x1406175b0): the RemakeOption of mode 0 (i64 uid,
    // i64, the locks) then u8 kind and i32 value. Writers: unit_14129cfa0_itemremakeoptiondialog.cpp:438 (kind 1,
    // value 300) and :480 (kind 2), tabcontentspetremakeoption.cpp:1028 (kind 0) and :1062 (kind 2). The server does
    // nothing with it.
    internal static void HandleDebug(IByteReader packet) {
        packet.ReadLong(); // item uid
        packet.ReadLong();
        ItemRemakeRules.ReadLocks(packet);
        packet.ReadByte(); // kind
        packet.ReadInt(); // value
    }

    private static void HandleForceFill(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        int count = packet.ReadInt();
    }

    // It needs to be epic or better armor and accessories at level 50 and above.
    internal static bool IsValidItem(Item item) {
        // The pet page (CItem::FUN_140526670, item.cpp:1887-1905) takes any pet (CItemData::IsPetItem, item type 11:
        // the combat and the storage pets) from rarity 3, with no level limit; then remakeDisable, and a pet with no
        // random option is refused (GetGroupOptionCount == 0, result 6, :1905). A pet without stats is left to the
        // caller's null-status answer.
        if (item.Type.IsPet) {
            return item.Rarity is >= Constant.ChangeAttributesPetMinRarity and <= Constant.ChangeAttributesMaxRarity
                && !item.Metadata.Property.RemakeDisable
                && (item.Stats == null || item.Stats[ItemStats.Type.Random].Count > 0);
        }
        if (item.Rarity is < Constant.ChangeAttributesMinRarity or > Constant.ChangeAttributesMaxRarity) {
            return false;
        }
        // The item page takes armor and accessories only, no weapon (CItem::FUN_1405264c0, item.cpp:1850-1854:
        // equip subtypes 13-17 and 22, CItemData::FUN_14053d600 itemdata.cpp:958, or 12 and 18-21, FUN_14053d5c0
        // itemdata.cpp:943; result 8).
        if (!item.Type.IsArmor && !item.Type.IsAccessory) {
            return false;
        }
        if (item.Metadata.Limit.Level < Constant.ChangeAttributesMinLevel) {
            return false;
        }
        // The client refuses an item with remakeDisable on both pages (CItem::FUN_1405264c0, item.cpp:1862, and
        // FUN_140526670 above, :1903; result 2).
        if (item.Metadata.Property.RemakeDisable) {
            return false;
        }

        return true;
    }

    // The lock item the client asks for (ItemRemakeRules.LockTag): an inventory item with the lock tag of the
    // item's kind and, for an equip, the equip's rarity, that the character can use (ItemRemakeRules.LockItemUsable).
    // Every tab is searched: a gear lock item (type 4) is in Misc, a pet candy (type 15) in Catalyst (TabOfType,
    // the client's FUN_14053e530). For gear the first one found is taken. For a pet it is the talent lock candy the player chose
    // (ingredientUid), which sets how many talents stay (LockLimit).
    public static Item? GetLockConsumeItem(GameSession session, Item changeItem, long ingredientUid) {
        if (!IsValidItem(changeItem)) {
            return null;
        }

        ItemTag? lockTag = ItemRemakeRules.LockTag(changeItem.Type.IsArmor, changeItem.Type.IsWeapon,
            changeItem.Type.IsAccessory, changeItem.Type.IsPet);
        if (lockTag == null) {
            return null;
        }
        int lockRarity = ItemRemakeRules.LockRarity(changeItem.Type.IsPet, changeItem.Rarity);
        bool vip = session.Config.IsPremiumClubActive();
        bool pet = lockTag == ItemTag.LockItemOptionPet;
        return session.Item.Inventory.Filter(item => ItemRemakeRules.IsLockItem(lockTag.Value, lockRarity, item.Metadata.Property.Tag, item.Rarity)
            && ItemRemakeRules.LockItemUsable(item.IsExpired(), item.Metadata.Limit.RequireVip, vip, item.Transfer?.Binding?.CharacterId ?? 0, session.CharacterId)
            && (!pet || (item.Uid == ingredientUid && ItemRemakeRules.CandyLocks(item.Metadata.Function?.Type, item.Metadata.Function?.Parameters) > 0)),
            null).FirstOrDefault();
    }

    // How many options the lock item keeps: one for gear, the candy's count for a pet (ItemRemakeRules.LocksAllowed).
    // What the lock item's Consume(uid, 1) needs: the same item in the bag, not expired, at least one.
    private static bool LockItemTakeable(GameSession session, Item lockItem) {
        Item? held = session.Item.Inventory.Get(lockItem.Uid);
        return held != null && !held.IsExpired() && held.Amount >= 1;
    }

    internal static int LockLimit(Item changeItem, Item lockItem) {
        if (!changeItem.Type.IsPet) {
            return 1;
        }
        int options = changeItem.Stats?[ItemStats.Type.Random].Count ?? 0;
        return ItemRemakeRules.PetLockLimit(
            ItemRemakeRules.CandyLocks(lockItem.Metadata.Function?.Type, lockItem.Metadata.Function?.Parameters), options);
    }

    // Every kept option is one the item has, and none is kept twice.
    internal static bool HasLockedOptions(Item item, List<ItemRemakeRules.Lock> locks) {
        if (item.Stats == null || locks.Distinct().Count() != locks.Count) {
            return false;
        }
        ItemStats.Option option = item.Stats[ItemStats.Type.Random];
        return locks.All(kept => kept.Special
            ? option.Special.ContainsKey((SpecialAttribute) kept.Attribute)
            : option.Basic.ContainsKey((BasicAttribute) kept.Attribute));
    }

    internal static LockOption[] LockOptions(List<ItemRemakeRules.Lock> locks) =>
        locks.Select(kept => kept.Special
            ? new LockOption((SpecialAttribute) kept.Attribute, true)
            : new LockOption((BasicAttribute) kept.Attribute, true)).ToArray();

    // Client TabContentsPetRemakeOption / UIItemRemakeOptionDialog: the player keeps the current
    // options instead of the new ones (kind 1 item, 2 pet). Only the pending result in the session
    // is dropped; nothing saved changes. The client sends no item uid and expects no reply here.
    private static void HandleKeepCurrent(GameSession session, IByteReader packet) {
        byte kind = packet.ReadByte();
        session.ChangeAttributesItem = null;
    }

    // Client UIItemRemakeOptionDialog::SendChangeAttributesMode4: an item put in the remake slot.
    // The reply (kind 4) carries the ingredients of the next change, which the dialog shows as the cost.
    private void HandleStageItem(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();

        lock (session.Item) {
            Item? item = session.Item.GetGear(itemUid)
                         ?? session.Item.Inventory.Get(itemUid, InventoryType.Pets);
            if (item == null) {
                session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_not_in_inven));
                return;
            }
            if (!IsValidItem(item)) {
                session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_impossible));
                return;
            }

            session.Send(ChangeAttributesPacket.StageItem(item.Uid, GetIngredients(item)));
        }
    }

    // Client UIItemRemakeOptionDialog::OnLoadComplete (kind 1) and the pet tab reset (kind 2). The client waits
    // for the reply (kind 6): either nothing is pending, or the change the player has not chosen yet.
    private static void HandleOpenDialog(GameSession session, IByteReader packet) {
        byte kind = packet.ReadByte();

        lock (session.Item) {
            Item? pending = session.ChangeAttributesItem;
            // The item dialog (kind 1) and the pet tab (kind 2) each resume only their own kind of item.
            if (pending == null || pending.Type.IsPet != (kind == 2)) {
                session.Send(ChangeAttributesPacket.NoPending());
                return;
            }

            Item? item = session.Item.GetGear(pending.Uid)
                         ?? session.Item.Inventory.Get(pending.Uid, InventoryType.Pets);
            if (item == null) {
                session.ChangeAttributesItem = null;
                session.Send(ChangeAttributesPacket.PendingItemGone(pending.Id));
                return;
            }

            session.Send(ChangeAttributesPacket.PendingItem(pending));
        }
    }
}
