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
        Unknown5 = 7,
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
            case Command.Unknown5: // GM button of the pet remake page (GM level 50 or more)
                if (!GmCheck.Allow(session, OpCode, (int) command)) {
                    return;
                }
                HandleUnknown5(session, packet);
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
        packet.ReadLong();
        List<ItemRemakeRules.Lock> locks = ItemRemakeRules.ReadLocks(packet);
        if (!ItemRemakeRules.LocksAllowed(locks.Count)) {
            session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_impossible));
            return;
        }
        bool useLock = locks.Count == 1;
        bool isSpecialAttribute = useLock && locks[0].Special;
        short attribute = useLock ? locks[0].Attribute : (short) -1;

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
            if (useLock) {
                ItemStats.Option itemOption = item.Stats[ItemStats.Type.Random];
                if (isSpecialAttribute) {
                    if (!itemOption.Special.ContainsKey((SpecialAttribute) attribute)) {
                        session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_impossible));
                        return;
                    }
                } else {
                    if (!itemOption.Basic.ContainsKey((BasicAttribute) attribute)) {
                        session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_impossible));
                        return;
                    }
                }

                lockItem = GetLockConsumeItem(session, item);
                if (lockItem == null) {
                    // The dialog's string for a lock without a lock item (OnRemakeError code 9).
                    session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_fail_lack_lock_consume_item));
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

            // Randomize attributes.
            if (lockItem != null) {
                // Add back the locked attribute.
                if (isSpecialAttribute) {
                    if (!ItemStatsCalc.UpdateRandomOption(ref changeItem, new LockOption((SpecialAttribute) attribute, true))) {
                        session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_fail_apply_option));
                        return;
                    }
                } else {
                    if (!ItemStatsCalc.UpdateRandomOption(ref changeItem, new LockOption((BasicAttribute) attribute, true))) {
                        session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_fail_apply_option));
                        return;
                    }
                }
            } else {
                if (!ItemStatsCalc.UpdateRandomOption(ref changeItem)) {
                    session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_fail_apply_option));
                    return;
                }
            }

            if (!PayChange(session, item, ingredients, lockItem)) {
                return;
            }

            item.TimeChangedOption++;
            changeItem.TimeChangedOption++;
            session.ChangeAttributesItem = changeItem;
            session.Send(ChangeAttributesPacket.PreviewItem(session.ChangeAttributesItem));
            session.ConditionUpdate(ItemRemakeRules.RemakeCondition(item.Type.IsPet));
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
                long owned = session.Item.Inventory.Find(ItemTag.CrystalPiece).Sum(fragment => (long) fragment.Amount);
                (int fragments, meso) = ItemRemakeRules.FragmentFee(ingredients[index].Amount, owned);
                if (fragments > 0) {
                    ingredients[index] = new IngredientInfo(ItemTag.CrystalPiece, fragments);
                } else {
                    ingredients.RemoveAt(index);
                }
            }
            bool owns = ingredients.All(info => session.Item.Inventory.Find(info.Tag).Sum(material => (long) material.Amount) >= info.Amount);
            if (!owns || session.Currency.Meso < meso) {
                session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_lack_price));
                return false;
            }
            if (lockItem != null && lockItem.Amount < 1) {
                session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_fail_lack_lock_consume_item));
                return false;
            }
            if (meso > 0) {
                session.Currency.Meso -= meso;
            }
            if (!session.Item.Inventory.Consume(ingredients)) {
                session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_lack_price));
                return false;
            }
            if (lockItem != null && !session.Item.Inventory.Consume(lockItem.Uid, 1)) {
                session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_fail_lack_lock_consume_item));
                return false;
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
        }
    }

    private static void HandleUnknown5(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        packet.ReadLong(); // unknown
        bool useLock = packet.ReadBool();
        if (useLock) {
            byte index = packet.ReadByte();
            var attribute = (BasicAttribute) packet.ReadShort();
        }

        packet.ReadBool();
        packet.ReadInt();
    }

    private static void HandleForceFill(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        int count = packet.ReadInt();
    }

    // It needs to be epic or better armor and accessories at level 50 and above.
    private static bool IsValidItem(Item item) {
        if (item.Rarity is < Constant.ChangeAttributesMinRarity or > Constant.ChangeAttributesMaxRarity) {
            return false;
        }
        if (!item.Type.IsWeapon && !item.Type.IsArmor && !item.Type.IsAccessory && !item.Type.IsCombatPet) {
            return false;
        }
        if (item.Metadata.Limit.Level < Constant.ChangeAttributesMinLevel) {
            return false;
        }

        return true;
    }

    // The lock item the client asks for (ItemRemakeRules.LockTag): any inventory item with the lock tag of the
    // item's kind and, for an equip, the equip's rarity, that the character can use (ItemRemakeRules.LockItemUsable).
    // The first one found is taken.
    public static Item? GetLockConsumeItem(GameSession session, Item changeItem) {
        if (!IsValidItem(changeItem)) {
            return null;
        }

        ItemTag? lockTag = ItemRemakeRules.LockTag(changeItem.Type.IsArmor, changeItem.Type.IsWeapon,
            changeItem.Type.IsAccessory, changeItem.Type.IsCombatPet);
        if (lockTag == null) {
            return null;
        }
        int lockRarity = ItemRemakeRules.LockRarity(changeItem.Type.IsCombatPet, changeItem.Rarity);
        bool vip = session.Config.IsPremiumClubActive();
        return session.Item.Inventory.Filter(item => ItemRemakeRules.IsLockItem(lockTag.Value, lockRarity, item.Metadata.Property.Tag, item.Rarity)
            && ItemRemakeRules.LockItemUsable(item.IsExpired(), item.Metadata.Limit.RequireVip, vip, item.Binding?.CharacterId ?? 0, session.CharacterId),
            InventoryType.Misc).FirstOrDefault();
    }

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
