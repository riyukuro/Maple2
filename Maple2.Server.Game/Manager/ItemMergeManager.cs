using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Formulas;
using Maple2.Server.Game.LuaFunctions;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Serilog;

namespace Maple2.Server.Game.Manager;

public sealed class ItemMergeManager {

    private readonly GameSession session;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    private ConstantsTable Constants => session.ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    private readonly ILogger logger = Log.Logger.ForContext<ItemMergeManager>();

    private Item? upgradeItem;
    private Item? catalystItem;

    // A result rolled for an item that already had a merge option, until the player picks one of the two.
    // It lives with the session: after a relog the item keeps its old option.
    private sealed record PendingMerge(long ItemUid, int ItemId, int CrystalId, MergeRoll Roll);
    private PendingMerge? pending;

    public ItemMergeManager(GameSession session) {
        this.session = session;
    }

    private void Clear() {
        upgradeItem = null;
        catalystItem = null;
    }

    private Item? Find(long itemUid) {
        return session.Item.Inventory.Get(itemUid) ?? session.Item.Equips.Get(itemUid);
    }

    // The answer to the dialog's request on load.
    public void Info() {
        if (pending == null) {
            session.Send(ItemMergePacket.NoPendingChoice());
            return;
        }

        if (Find(pending.ItemUid) == null) {
            session.Send(ItemMergePacket.PendingItemMissing(pending.ItemId));
            pending = null;
            return;
        }

        session.Send(ItemMergePacket.PendingChoice(pending.ItemId, pending.ItemUid, pending.Roll));
    }

    public void Stage(long itemUid) {
        Clear();

        Item? item = session.Item.Inventory.Get(itemUid);
        if (item == null) {
            item = session.Item.Equips.Get(itemUid);
            if (item == null) {
                // TODO: Error
                return;
            }
        }
        upgradeItem = item;

        IList<Item> inventory = session.Item.Inventory.Filter(candidate => candidate.Metadata.Property.Type == 19);
        session.Send(ItemMergePacket.Stage(Materials(session.ServerTableMetadata.ItemMergeTable, item, inventory)));
    }

    // The materials offered for an item. The client shows the uids of mode 1 as they come
    // (UIItemMergeOptionDialog::FUN_1419d11b0, uiitemmergeoptiondialog.cpp:836) and asks its table by the
    // material's table id, its property param1 (FindMergeData, itemmergeoptiontable.cpp:674), not by the
    // item id. So every inventory item is tried as a material and the table is not walked by item id.
    public static List<long> Materials(ItemMergeTable table, Item item, IEnumerable<Item> inventory) {
        List<long> uids = [];
        foreach (Item material in inventory) {
            if (!material.IsExpired() && MergeSlot(table, item.Metadata, material.Metadata) != null) {
                uids.Add(material.Uid);
            }
        }
        return uids;
    }

    public void SelectCrystal(long itemUid, long crystalUid) {
        if (upgradeItem?.Uid != itemUid) {
            logger.Error("Item Merge upgrade item UID mismatch: {UpgradeItemUid} != {ItemUid}", upgradeItem?.Uid, itemUid);
            return;
        }

        Item? catalyst = session.Item.Inventory.Get(crystalUid);
        if (catalyst == null) {
            return;
        }

        catalystItem = catalyst;

        ItemMergeTable.Entry? mergeSlot = MergeSlot(session.ServerTableMetadata.ItemMergeTable, upgradeItem.Metadata, catalyst.Metadata);
        if (mergeSlot == null) {
            return;
        }

        session.Send(ItemMergePacket.Select(mergeSlot, ItemMerge.CostMultiplier(upgradeItem.Rarity)));

        if (!session.ScriptMetadata.TryGet(Constant.EmpowermentNpc, out ScriptMetadata? script) ||
            !script.States.TryGetValue(Constants.MergeSmithScriptID, out ScriptState? state)) {
            return;
        }

        CinematicEventScript[] eventScripts = state.Contents.First().Events;
        session.NpcScript = new NpcScriptManager(session, eventScripts);
    }

    public void Empower(long itemUid, long catalystUid) {
        // The error codes are the ones the client's own check gives (UIItemMergeOptionDialog::CheckMerge).
        if (pending != null) {
            session.Send(ItemMergePacket.Error(ItemMergeError.s_item_merge_option_error_unknown));
            return;
        }

        if (upgradeItem?.Uid != itemUid || Find(itemUid) == null || !IsFusionable(upgradeItem)) {
            session.Send(ItemMergePacket.Error(ItemMergeError.s_item_merge_option_error_invalid_mergeitem));
            return;
        }

        if (catalystItem?.Uid != catalystUid) {
            session.Send(ItemMergePacket.Error(ItemMergeError.s_item_merge_option_error_invalid_material));
            return;
        }

        ItemMergeTable.Entry? mergeSlot = MergeSlot(session.ServerTableMetadata.ItemMergeTable, upgradeItem.Metadata, catalystItem.Metadata);
        if (mergeSlot == null || mergeSlot.BasicOptions.Count + mergeSlot.SpecialOptions.Count == 0) {
            session.Send(ItemMergePacket.Error(ItemMergeError.s_item_merge_option_error_invalid_material));
            return;
        }

        int multiplier = ItemMerge.CostMultiplier(upgradeItem.Rarity);
        long totalMesoCost = mergeSlot.MesoCost * multiplier;
        if (session.Currency.CanAddMeso(-totalMesoCost) != -totalMesoCost) {
            session.Send(ItemMergePacket.Error(ItemMergeError.s_item_merge_option_error_lack_meso));
            return;
        }

        List<IngredientInfo> ingredients = [];
        foreach (ItemComponent component in mergeSlot.Materials) {
            ingredients.Add(new IngredientInfo(component.Tag, component.Amount * multiplier));
        }
        if (!session.Item.Inventory.Consume(ingredients)) {
            session.Send(ItemMergePacket.Error(ItemMergeError.s_item_merge_option_error_lack_material));
            return;
        }

        int crystalId = catalystItem.Id;
        if (!session.Item.Inventory.Consume(catalystItem.Uid, 1)) {
            session.Send(ItemMergePacket.Error(ItemMergeError.s_item_merge_option_error_invalid_merge_scroll));
            return;
        }

        session.Currency.Meso += -(mergeSlot.MesoCost * multiplier);

        MergeRoll roll = Roll(mergeSlot, Random.Shared);
        session.ConditionUpdate(ConditionType.item_merge_success);

        // An item that already has a merge option keeps it until the player has seen the new one and
        // picked (the client dialog waits in its choice phase after mode 4).
        if (upgradeItem.Stats is { HasMergeOption: true }) {
            pending = new PendingMerge(upgradeItem.Uid, upgradeItem.Id, crystalId, roll);
            session.Send(ItemMergePacket.Choice(roll));
            return;
        }

        Apply(upgradeItem, roll, crystalId);
    }

    // The player's pick between the old option and the pending one. Either way the client waits for the
    // result (mode 5) with the item as it is now.
    public void Choose(bool keepNew) {
        if (pending == null) {
            session.Send(ItemMergePacket.Error(ItemMergeError.s_item_merge_option_error_unknown));
            return;
        }

        PendingMerge result = pending;
        pending = null;
        Item? item = Find(result.ItemUid);
        if (item == null) {
            session.Send(ItemMergePacket.Error(ItemMergeError.s_item_merge_option_error_invalid_mergeitem));
            return;
        }

        if (keepNew) {
            Apply(item, result.Roll, result.CrystalId);
            return;
        }

        session.Send(ItemMergePacket.Empower(item));
        session.NpcScript?.EmpowerEvent();
    }

    private void Apply(Item item, MergeRoll roll, int crystalId) {
        item.Stats ??= new ItemStats();
        item.Stats.SetMergeOption(roll, crystalId);

        session.Send(ItemMergePacket.Empower(item));
        session.NpcScript?.EmpowerEvent();
        if (session.Item.Equips.Get(item.Uid) != null) {
            session.Stats.Refresh();
        }
    }

    // Client UIItemMergeRevertDialog: a revert item removes the merge option of an item. The checks and
    // their codes are the dialog's own (CheckItem, uiitemmergerevertdialog.cpp:283); the price is the
    // one the dialog shows, by the item's rarity and equip type. The client waits for mode 6.
    // The dialog takes the item from the inventory only (FindItemByUid of the inventory in onOk and in
    // OnItemDropped, uiitemmergerevertdialog.cpp:150 and :234), and the client's mode 6 reader updates
    // the inventory's item only (scenestategame.cpp, OnItemFusion), so an equipped item is no target.
    public void Revert(long revertItemUid, long itemUid) {
        if (pending != null) {
            session.Send(ItemMergePacket.Error(ItemMergeError.s_item_merge_option_error_unknown));
            return;
        }

        Item? revertItem = session.Item.Inventory.Get(revertItemUid);
        Item? item = RevertTarget(itemUid, uid => session.Item.Inventory.Get(uid));
        ItemMergeError error = CheckRevert(revertItem, item);
        if (error != ItemMergeError.ok || revertItem == null || item?.Stats == null) {
            session.Send(ItemMergePacket.Error(error == ItemMergeError.ok ? ItemMergeError.s_item_merge_option_error_unknown : error));
            return;
        }

        long price = RevertPrice(item);
        if (session.Currency.CanAddMeso(-price) != -price) {
            session.Send(ItemMergePacket.Error(ItemMergeError.s_item_merge_option_error_lack_meso));
            return;
        }

        if (!session.Item.Inventory.Consume(revertItem.Uid, 1)) {
            session.Send(ItemMergePacket.Error(ItemMergeError.s_item_merge_option_error_invalid_merge_scroll));
            return;
        }

        session.Currency.Meso -= price;
        item.Stats.ClearMergeOption();

        session.Send(ItemMergePacket.Remove(item));
    }

    // The revert's target: an item of the inventory. A uid of an equipped item finds nothing and the
    // check answers with the code of a missing item.
    public static Item? RevertTarget(long itemUid, Func<long, Item?> inventory) {
        return inventory(itemUid);
    }

    // The merge table's row for an item and a material, as the client finds it
    // (CItemMergeOptionTable::FindMergeData and GetMergeData, itemmergeoptiontable.cpp:674 and :434): the
    // material is of property type 19; the part is the item's equip subtype when the material's subtype is 1,
    // else 0; the item's level limit is 50 or more and not above the material's.
    // The row's partLimit list, when the ingest read one, must hold the item's equip subtype.
    // The table id is the material's property param1 (CItemData::GetMergeOptionId, itemdata.cpp:1197). The
    // item parser does not read it; the ingest takes it from the played client's item data when it has that
    // pack (ItemMergeTable.TableIds). Without it the table is asked by the material's item id and the ingest
    // copies the rows of some materials that share one.
    // The data folder's item xml is of another version than the played client's: its merge materials have
    // subtype 21 and 22 where the client's have 1 and 2. So a material that is not of subtype 1 and whose
    // table has no row of part 0 is asked by the item's equip subtype too (a table has rows by part or one
    // row of part 0, never both).
    public static ItemMergeTable.Entry? MergeSlot(ItemMergeTable table, ItemMetadata item, ItemMetadata material) {
        if (material.Property.Type != 19) {
            return null;
        }
        int tableId = table.TableIds != null && table.TableIds.TryGetValue(material.Id, out int id) ? id : material.Id;
        if (!table.Entries.TryGetValue(tableId, out Dictionary<int, ItemMergeTable.Entry>? parts)) {
            return null;
        }
        bool byPart = material.Property.SubType == 1 || !parts.ContainsKey(0);
        if (!parts.TryGetValue(byPart ? EquipSubtype(item) : 0, out ItemMergeTable.Entry? entry)) {
            return null;
        }
        if (entry.PartLimit is { Length: > 0 } && !entry.PartLimit.Contains(EquipSubtype(item))) {
            return null;
        }
        if (item.Limit.Level < 50 || item.Limit.Level > material.Limit.Level) {
            return null;
        }
        return entry;
    }

    // CItemData::GetEquipSubtype (itemdata.cpp:509): the item xml's property subtype when the property
    // type is 1, else 0. The client reads both from the item xml (itemdata.cpp:340). The server's item rows
    // come from the data folder's item xml, whose subtype is not the played client's (0 for most equips, 16
    // for some of every kind). In the played client's item data every item of property type 1 with an id of
    // 112xxxxx to 183xxxxx has the subtype id / 100000 - 100, and the ones below 112 have none, so the id
    // gives the client's value.
    public static int EquipSubtype(ItemMetadata metadata) {
        if (metadata.Property.Type != 1) {
            return 0;
        }
        int kind = metadata.Id / 100000;
        return kind is >= 112 and <= 183 ? kind - 100 : 0;
    }

    // ItemModelData::HasGroupType (itemdatastruct.cpp:2087): the first slot named OH answers for LH and
    // RH (and for nothing else); a slot before it that has the asked name answers yes.
    private static bool HasSlot(ItemMetadata metadata, EquipSlot slot) {
        foreach (EquipSlot name in metadata.SlotNames) {
            if (name == EquipSlot.OH) {
                return slot is EquipSlot.RH or EquipSlot.LH;
            }
            if (name == slot) {
                return true;
            }
        }
        return false;
    }

    // CItemData::FUN_14053e410 (itemdata.cpp:1348) with FUN_14053d460 (itemdata.cpp:893): 2 for the
    // equip subtypes 22, 50 to 56 and 80 to 83, and for an item of property type 23 whose slots give
    // both RH and LH; 1 for the others. The revert item's function parameter names the kind it works on.
    public static int RevertSlot(ItemMetadata metadata) {
        if (EquipSubtype(metadata) is 22 or >= 50 and <= 56 or >= 80 and <= 83) {
            return 2;
        }
        if (metadata.Property.Type == 23 && HasSlot(metadata, EquipSlot.RH) && HasSlot(metadata, EquipSlot.LH)) {
            return 2;
        }
        return 1;
    }

    // uiitemmergerevertdialog.cpp:209: the price by the item's rarity and equip subtype.
    public static long RevertPrice(Item item) {
        return Lua.CalcItemMergeRevertMeso(item.Rarity, EquipSubtype(item.Metadata));
    }

    // The client offers only items whose xml says fusionable to the merge dialog
    // (uiitemmergeoptiondialog.cpp:605 and :623) and the revert dialog refuses the others with code 3
    // (uiitemmergerevertdialog.cpp:283). A row without the field (older ingest) is let through.
    public static bool IsFusionable(Item item) {
        return item.Metadata.Property.Fusionable != false;
    }

    public static ItemMergeError CheckRevert(Item? revertItem, Item? item) {
        if (item == null || !IsFusionable(item)) {
            return ItemMergeError.s_item_merge_option_error_invalid_mergeitem;
        }
        if (revertItem?.Metadata.Function?.Type != ItemFunction.ItemMergeRevert) {
            return ItemMergeError.s_item_merge_option_error_invalid_merge_scroll;
        }
        if (revertItem.Rarity != item.Rarity) {
            return ItemMergeError.s_item_merge_option_error_invalid_revert_rank;
        }
        if (item.Stats is not { HasMergeOption: true }) {
            return ItemMergeError.s_item_merge_option_error_invalid_revert_option;
        }
        if (!int.TryParse(revertItem.Metadata.Function.Parameters, out int slot) || slot == 0) {
            return ItemMergeError.s_item_merge_option_error_invalid_merge_scroll;
        }
        if (slot != RevertSlot(item.Metadata)) {
            return ItemMergeError.s_item_merge_option_error_invalid_revert_slot;
        }
        return ItemMergeError.ok;
    }

    // One option of the slot, each as likely as the others, then one of its steps by weight and a value
    // inside the step, both ends included.
    public static MergeRoll Roll(ItemMergeTable.Entry mergeSlot, Random random) {
        int index = random.Next(mergeSlot.BasicOptions.Count + mergeSlot.SpecialOptions.Count);
        if (index < mergeSlot.BasicOptions.Count) {
            (BasicAttribute attribute, ItemMergeTable.Option option) = mergeSlot.BasicOptions.ElementAt(index);
            return new MergeRoll(attribute, SpecialAttribute.None, RollValue(option, random));
        } else {
            (SpecialAttribute attribute, ItemMergeTable.Option option) = mergeSlot.SpecialOptions.ElementAt(index - mergeSlot.BasicOptions.Count);
            return new MergeRoll(null, attribute, RollValue(option, random));
        }
    }

    private static int RollValue(ItemMergeTable.Option option, Random random) {
        int total = 0;
        for (int i = 0; i < option.Values.Length; i++) {
            total += Math.Max(option.Weights[i], 0);
        }
        if (total <= 0) {
            return option.Bounds().Min;
        }

        int pick = random.Next(total);
        for (int i = 0; i < option.Values.Length; i++) {
            pick -= Math.Max(option.Weights[i], 0);
            if (pick < 0) {
                ItemMergeTable.Range<int> step = option.Step(i);
                return random.Next(step.Min, Math.Max(step.Max, step.Min) + 1);
            }
        }
        return option.Bounds().Max;
    }
}
