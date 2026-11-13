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
using Maple2.Tools.Extensions;

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
            Refuse(session, ChangeAttributesScrollError.s_itemremake_scroll_error_server_fail_remake, itemUid);
            return;
        }

        lock (session.Item) {
            Item? scroll = session.Item.Inventory.Get(scrollUid, InventoryType.Misc);
            if (scroll == null || scroll.Metadata.Function?.Type != ItemFunction.ItemRemakeScroll) {
                Refuse(session, ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_scroll, itemUid);
                return;
            }

            Item? item = session.Item.Inventory.Get(itemUid, InventoryType.Gear) ?? session.Item.Inventory.Get(itemUid, InventoryType.Pets);
            if (item == null) {
                Refuse(session, ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_target_data, itemUid);
                return;
            }
            ChangeAttributesScrollError error = IsCompatibleScroll(item, scroll, out ItemRemakeScrollMetadata? itemRemakeScrollMetadata);
            if (error is not ChangeAttributesScrollError.none || itemRemakeScrollMetadata is null) {
                Refuse(session, error, itemUid);
                return;
            }

            ChangeAttributesScrollError lockError = CheckLocks(session, item, ingredientUid, locks, out Item? lockItem);
            if (lockError != ChangeAttributesScrollError.none) {
                Refuse(session, lockError, itemUid, $"{locks.Count} lock(s), ingredient {ingredientUid}");
                return;
            }

            // Clone the item so we can preview changes without modifying existing item.
            Item changeItem = item.Clone();
            if (changeItem.Stats == null) { // This should be impossible, but check again to make linter happy.
                Refuse(session, ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_target_stat, itemUid);
                return;
            }

            if (changeItem.Metadata.Option == null) { // This should be impossible, but check again to make linter happy.
                Refuse(session, ChangeAttributesScrollError.s_itemremake_scroll_error_impossible_item, itemUid);
                return;
            }

            // A failed roll answers on the scroll dialog's own packet (code 12, its "failed to modify" string); the
            // item dialog's error packet is not read by the scroll dialog. The locked options are added back.
            LockOption[] presets = ChangeAttributesHandler.LockOptions(locks);
            bool rolled = itemRemakeScrollMetadata.RollAttribute
                ? ItemStatsCalc.UpdateRandomOption(ref changeItem, presets)
                : ItemStatsCalc.UpdateFixedOption(ref changeItem, presets);
            if (!rolled) {
                Refuse(session, ChangeAttributesScrollError.s_itemremake_scroll_error_server_fail_remake, itemUid);
                return;
            }

            ChangeAttributesScrollError consumed = TakeCosts(session, scroll, lockItem);
            if (consumed != ChangeAttributesScrollError.none) {
                Refuse(session, consumed, itemUid);
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
    // Every refusal of a change is logged: the scroll dialog shows no text for most of its errors and goes back to its
    // first page.
    private void Refuse(GameSession session, ChangeAttributesScrollError error, long itemUid, string? detail = null) {
        Logger.Warning("ChangeAttributesScroll of item {ItemUid} by {CharacterId} refused: {Error} {Detail}",
            itemUid, session.CharacterId, error, detail ?? string.Empty);
        session.Send(ChangeAttributesScrollPacket.Error(error));
    }

    // The checks of the options the player keeps (UIItemRemakeScrollDialog, uiitemremakescrolldialog.cpp:470-520,
    // sends each as its kind and id): every kept option is one the item has, the lock item is held (for a pet the
    // candy in the ingredient slot), and it keeps that many.
    internal static ChangeAttributesScrollError CheckLocks(GameSession session, Item item, long ingredientUid,
                                                           List<ItemRemakeRules.Lock> locks, out Item? lockItem) {
        lockItem = null;
        if (locks.Count == 0) {
            return ChangeAttributesScrollError.none;
        }
        if (!ChangeAttributesHandler.HasLockedOptions(item, locks)) {
            return ChangeAttributesScrollError.s_itemremake_scroll_error_impossible_property;
        }
        lockItem = ChangeAttributesHandler.GetLockConsumeItem(session, item, ingredientUid);
        if (lockItem == null) {
            return ChangeAttributesScrollError.s_itemremake_error_server_fail_lack_lock_consume_item;
        }
        if (!ItemRemakeRules.LocksAllowed(locks.Count, ChangeAttributesHandler.LockLimit(item, lockItem))) {
            return ChangeAttributesScrollError.s_itemremake_scroll_error_server_fail_remake;
        }
        return ChangeAttributesScrollError.none;
    }

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

            if (Keep(session, item, session.ChangeAttributesItem.Stats)) {
                session.Stats.Refresh();
            }
            session.ChangeAttributesItem = null;
            session.Send(ChangeAttributesScrollPacket.SelectItem(item));
        }
    }

    // Puts the kept options on the item and tells whether the owner's totals must be recounted: here the summoned
    // pet, which stays in the Pets tab as the same item (PetHandler); the target comes from the bag, so worn gear
    // does not reach it. The dialog path does the same (ChangeAttributesHandler.CountsInStats). Before, a scroll
    // remake kept on a summoned pet left the old options in the totals until the next rebuild.
    internal static bool Keep(GameSession session, Item item, ItemStats result) {
        item.Stats![ItemStats.Type.Random] = result[ItemStats.Type.Random];
        return ChangeAttributesHandler.CountsInStats(session, item.Uid);
    }

    private ChangeAttributesScrollError IsCompatibleScroll(Item item, Item scroll, out ItemRemakeScrollMetadata? metadata) {
        metadata = null;
        if (!int.TryParse(scroll.Metadata.Function?.Parameters, out int remakeId)) {
            return ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_scroll_data;
        }
        if (!TableMetadata.ItemRemakeScrollTable.Entries.TryGetValue(remakeId, out metadata)) {
            return ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_scroll_data;
        }
        ChangeAttributesScrollError error = ScrollFits(item, metadata);
        if (error != ChangeAttributesScrollError.none) {
            return error;
        }
        ItemOption? random = null;
        if (item.Metadata.Option != null) {
            // Chosen here: a table the data lacks (KR has no 61000003 or 61000004) falls back to the item's RandomId.
            if (!TableMetadata.ItemOptionRandomTable.Options.TryGetValue(RandomTableId(item), item.Rarity, out random)) {
                TableMetadata.ItemOptionRandomTable.Options.TryGetValue(item.Metadata.Option.RandomId, item.Rarity, out random);
            }
        }
        return OptionsFit(item, random);
    }

    // The random table the client's test reads (CItemData::FUN_14053eff0, itemdata.cpp:1633): a pet's by its rarity
    // (2 to 61000001, 3 to 61000002, 4 to 61000003, 5 and 6 to 61000004; none for another rarity or a pet without
    // a constant option), any other item's own RandomId. The KR data has no 61000003 or 61000004; the caller then
    // falls back to the item's RandomId.
    internal static int RandomTableId(Item item) {
        if (item.Metadata.Option == null) {
            return 0;
        }
        if (!item.Type.IsPet) {
            return item.Metadata.Option.RandomId;
        }
        if (item.Metadata.Option.ConstantId == 0) {
            return 0;
        }
        return item.Rarity switch {
            2 => 61000001,
            3 => 61000002,
            4 => 61000003,
            5 or 6 => 61000004,
            _ => 0,
        };
    }

    // The armor's exclusive options (CItemOptionTable::FUN_1405f78f0, commonfiles/itemoptiondata_3.cpp:817-853): the
    // stats and special stats of which an armor's random options may hold one.
    private static readonly BasicAttribute[] ExclusiveBasics = [
        BasicAttribute.PhysicalAtk, BasicAttribute.MagicalAtk, BasicAttribute.Strength, BasicAttribute.Intelligence,
        BasicAttribute.Luck, BasicAttribute.Dexterity, BasicAttribute.Accuracy, BasicAttribute.AttackSpeed, BasicAttribute.Piercing,
    ];
    private static readonly SpecialAttribute[] ExclusiveSpecials = [
        SpecialAttribute.MagicalPiercing, SpecialAttribute.PhysicalPiercing, SpecialAttribute.NormalNpcDamage,
        SpecialAttribute.EliteNpcDamage, SpecialAttribute.BossNpcDamage, SpecialAttribute.ReduceCooldown,
    ];

    // The last check of the client's test (FUN_140616170, itemremakescrolldata.cpp:320-353), after the row's: the item's
    // random options must not be empty, every one must be in the random option table of the item and its rarity, and an
    // armor (FUN_14053d600) may hold at most one exclusive option; else 3. The table is the client's (RandomTableId) at
    // the item's rarity. An item without a table is refused (chosen here: the client reads the table unchecked).
    internal static ChangeAttributesScrollError OptionsFit(Item item, ItemOption? random) {
        if (item.Stats == null || random == null) {
            return ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_target_stat;
        }
        ItemStats.Option option = item.Stats[ItemStats.Type.Random];
        if (option.Basic.Count == 0 && option.Special.Count == 0) {
            return ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_target_stat;
        }
        if (option.Basic.Keys.Any(basic => !random.Entries.Any(entry => entry.BasicAttribute == basic))
            || option.Special.Keys.Any(special => !random.Entries.Any(entry => entry.SpecialAttribute == special))) {
            return ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_target_stat;
        }
        if (item.Type.IsArmor && option.Basic.Keys.Count(ExclusiveBasics.Contains) + option.Special.Keys.Count(ExclusiveSpecials.Contains) > 1) {
            return ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_target_stat;
        }
        return ChangeAttributesScrollError.none;
    }

    // The checks of the scroll's row, the client's test up to the option table (FUN_140616170, itemremakescrolldata.cpp:283-319; OptionsFit does the rest), in its
    // order and with its codes: an item with remakeDisable (7); no options (3); a row with an item list takes only those
    // ids (8), else a pet row (onlyPet) takes any pet (1) and another row the item types of its slot list (4); then the row's rarities (5), its levels (6) and its option value (addOpValue 0: 11). The row alone
    // decides: before, the attribute dialog's own gate (rarity 4 to 6, level 50, weapon, armor, accessory or pet) came
    // first, so the rank 3 pet rows (10000040 and 10000097) could never be used, and every row's refusal was 8.
    internal static ChangeAttributesScrollError ScrollFits(Item item, ItemRemakeScrollMetadata metadata) {
        if (item.Metadata.Property.RemakeDisable) {
            return ChangeAttributesScrollError.s_itemremake_scroll_error_impossible_property;
        }
        if (item.Stats == null) {
            return ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_target_stat;
        }
        if (metadata.ItemIds.Length > 0) {
            if (!metadata.ItemIds.Contains(item.Id)) {
                return ChangeAttributesScrollError.s_itemremake_scroll_error_impossible_item;
            }
        } else if (metadata.OnlyPet) {
            if (!item.Type.IsPet) {
                return ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_target;
            }
        } else if (!metadata.ItemTypes.Contains(item.Type.Type)) {
            return ChangeAttributesScrollError.s_itemremake_scroll_error_impossible_slot;
        }
        if (!metadata.Rarities.Contains(item.Rarity)) {
            return ChangeAttributesScrollError.s_itemremake_scroll_error_impossible_rank;
        }
        if (item.Metadata.Limit.Level < metadata.MinLevel || item.Metadata.Limit.Level > metadata.MaxLevel) {
            return ChangeAttributesScrollError.s_itemremake_scroll_error_impossible_level;
        }
        if (metadata.RollValueType == RollValueType.None) {
            return ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_scroll_data;
        }

        return ChangeAttributesScrollError.none;
    }
}
