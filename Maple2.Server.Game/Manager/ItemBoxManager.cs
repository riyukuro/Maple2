using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Serilog;
using ILogger = Serilog.ILogger;

namespace Maple2.Server.Game.Manager;

public class ItemBoxManager {
    private readonly GameSession session;
    public int BoxCount { get; private set; }

    private readonly ILogger logger = Log.Logger.ForContext<ItemBoxManager>();

    public ItemBoxManager(GameSession session) {
        this.session = session;
        BoxCount = 0;
    }

    public void Reset() {
        BoxCount = 0;
    }

    // Item id -> tick of the last use, for boxes with a cooldown (the client keeps the same map per item id).
    private readonly Dictionary<int, long> lastUseTicks = new();

    // The client measures the cooldown between its own sends, so a use may arrive a little early here.
    public const int CooldownSlackMs = 250;

    public static bool IsCooldownOver(long lastUseTick, long nowTick, int cooldownSeconds) {
        return nowTick - lastUseTick >= cooldownSeconds * 1000L - CooldownSlackMs;
    }

    public bool TryStartCooldown(int itemId, int cooldownSeconds, long nowTick) {
        if (cooldownSeconds <= 0) {
            return true;
        }
        if (lastUseTicks.TryGetValue(itemId, out long lastUseTick) && !IsCooldownOver(lastUseTick, nowTick, cooldownSeconds)) {
            return false;
        }

        lastUseTicks[itemId] = nowTick;
        return true;
    }

    // Each box opened counts for the openItemBox condition, with the box's item id as the code (quest 80020127
    // lists the Lucky Box 20302745).
    private ItemBoxError Counted(Item item, Func<ItemBoxError> open) {
        int before = BoxCount;
        ItemBoxError error = open();
        if (BoxCount > before) {
            session.ConditionUpdate(ConditionType.openItemBox, counter: BoxCount - before, codeLong: item.Id);
        }
        return error;
    }

    // selection: a select box's picked rows as the client sends them (ItemBoxRules.ParseSelection).
    public ItemBoxError Open(Item item, int count = 1, string selection = "") => Counted(item, () => OpenBox(item, count, selection));

    private ItemBoxError OpenBox(Item item, int count, string selection) {
        if (string.IsNullOrEmpty(item.Metadata.Function?.Parameters)) {
            logger.Error("Item {ItemId} has no parameters for item box function", item.Id);
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }
        if (item.Type.IsTranscendenceCrystal) {
            Dictionary<string, string> parameters = XmlParseUtil.GetParameters(item.Metadata.Function.Parameters);
            if (parameters.Count < 4) {
                logger.Error("Invalid parameters for item box: {ItemId}", item.Id);
                return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
            }
            if (!parameters.TryGetValue("globalDropBoxId", out string? globalDropBoxIdStr) || !int.TryParse(globalDropBoxIdStr, out int globalDropBoxId)) {
                globalDropBoxId = 0;
            }

            if (!parameters.TryGetValue("individualDropBoxId", out string? individualDropBoxIdStr) || !int.TryParse(individualDropBoxIdStr, out int individualDropBoxId)) {
                individualDropBoxId = 0;
            }
            return OpenItemBox(item, globalDropBoxId, 0, 0, individualDropBoxId, 1, count);
        }
        if (item.Metadata.Function.Type == ItemFunction.OpenItemBoxLimitedQuantity) {
            Dictionary<string, string> parameters = XmlParseUtil.GetParameters(item.Metadata.Function.Parameters);
            if (!parameters.TryGetValue("individualDropBoxId", out string? boxIdStr) || !int.TryParse(boxIdStr, out int boxId)) {
                logger.Error("Invalid parameters for item box: {ItemId}", item.Id);
                return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
            }
            return OpenItemBox(item, 0, 0, 0, boxId, 1, count);
        }
        int[] itemBoxParams = item.Metadata.Function.Parameters.Split(',').Select(int.Parse).ToArray();
        return item.Metadata.Function?.Type switch {
            ItemFunction.SelectItemBox => SelectItemBox(item, itemBoxParams[0], itemBoxParams[1], selection, count),
            ItemFunction.OpenItemBox => OpenItemBox(item, itemBoxParams[0], itemBoxParams[1], itemBoxParams[2], itemBoxParams[3], itemBoxParams.Length == 5 ? itemBoxParams[4] : 1, count),
            ItemFunction.OpenItemBoxWithKey => OpenItemBoxWithKey(item, itemBoxParams[0], itemBoxParams[1], itemBoxParams[2], itemBoxParams[5], count),
            ItemFunction.OpenGachaBox => OpenGachaBox(item, itemBoxParams[0], itemBoxParams.Length > 1 ? itemBoxParams[1] : 0, count),
            _ => LogAndReturnError(item.Metadata.Function?.Type.ToString()),
        };
    }

    // Adds an item that came out of the box. A drop row with isAnnounce is announced to the world with the box's
    // name and a link to the inventory item that took it; an item that goes to the mail instead is not announced.
    private bool AddFromBox(Item box, Item newItem) {
        Item? target = null;
        if (!session.Item.Inventory.Add(newItem, true, onAdded: added => target ??= added)) {
            return false;
        }
        if (ShouldAnnounce(newItem, target)) {
            ChatUtil.SendItemBoxAnnouncement(session, target!, box.Id);
        }
        return true;
    }

    // Currency, medals and furnishings do not become an inventory item (no target), so there is nothing to link.
    public static bool ShouldAnnounce(Item newItem, Item? target) {
        return newItem.AnnounceOnOpen && target is { Uid: > 0 };
    }

    private ItemBoxError LogAndReturnError(string? type) {
        logger.Error("Invalid box type: {Type}", type);
        return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
    }

    public ItemBoxError OpenLulluBox(Item item, int count = 1, bool autoPay = false) =>
        Counted(item, () => OpenLulluBoxes(item, count, autoPay));

    private ItemBoxError OpenLulluBoxes(Item item, int count, bool autoPay) {
        if (session.Field is null) return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;

        Dictionary<string, string> parameters = XmlParseUtil.GetParameters(item.Metadata.Function?.Parameters);

        // Get common dropbox
        if (!int.TryParse(parameters["commonBoxId"], out int commonBoxId)) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        // Get uncommon dropbox
        if (!int.TryParse(parameters["unCommonBoxId"], out int unCommonBoxId)) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        var boxIngredient = new IngredientInfo(Enum.Parse<ItemTag>(parameters["boxItemTag"]), 1);
        var keyIngredient = new IngredientInfo(Enum.Parse<ItemTag>(parameters["keyItemTag"]), 1);
        int.TryParse(parameters.GetValueOrDefault("boxPrice"), out int mesoCost);
        mesoCost = Math.Max(mesoCost, 0);
        var totalItems = new List<Item>();
        ItemBoxError error = ItemBoxError.ok;
        for (int startCount = 0; startCount < count; startCount++) {
            // Nothing is taken unless the key and the box (or its price) are all there and the roll gave items.
            int keys = (int) Math.Min(session.Item.Inventory.SpendableCount(keyIngredient.Tag), int.MaxValue);
            int boxes = (int) Math.Min(session.Item.Inventory.SpendableCount(boxIngredient.Tag), int.MaxValue);
            ItemBoxRules.LuluPay pay = ItemBoxRules.LuluPayment(keys, boxes, autoPay, session.Currency.Meso, mesoCost);
            if (pay == ItemBoxRules.LuluPay.None) {
                error = ItemBoxError.s_err_cannot_open_multi_itembox_inventory;
                break;
            }

            // common dropbox items
            ICollection<Item> items = session.Field.ItemDrop.GetIndividualDropItems(session, session.Player.Value.Character.Level, unCommonBoxId);

            // uncommon dropbox items
            items = items.Concat(session.Field.ItemDrop.GetIndividualDropItems(session, session.Player.Value.Character.Level, commonBoxId)).ToList();
            if (items.Count == 0) {
                error = ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
                break;
            }

            // The key and the box are taken in one step (both are tested before either is taken), so a box that
            // cannot be taken leaves the key and gives nothing.
            // A box bought for mesos: the price is taken before the key, without a packet, and given back when the
            // key cannot be taken. A take on another thread while the key leaves (its packet is sent) cannot leave
            // the key gone with the box unpaid.
            bool boxPaid = pay != ItemBoxRules.LuluPay.Meso;
            if (!boxPaid && !session.Currency.TryTakeMeso(mesoCost, notify: false)) {
                error = ItemBoxError.s_err_cannot_open_multi_itembox_inventory;
                break;
            }
            if (boxPaid && keyIngredient.Tag == boxIngredient.Tag) {
                if (!session.Item.Inventory.Consume([new IngredientInfo(keyIngredient.Tag, keyIngredient.Amount + boxIngredient.Amount)])) {
                    error = ItemBoxError.s_err_cannot_open_multi_itembox_inventory;
                    break;
                }
            } else if (!session.Item.Inventory.Consume(boxPaid ? [keyIngredient, boxIngredient] : [keyIngredient])) {
                if (!boxPaid) {
                    session.Currency.ReturnMeso(mesoCost, notify: false);
                }
                error = ItemBoxError.s_err_cannot_open_multi_itembox_inventory;
                break;
            }
            if (!boxPaid) {
                session.Currency.NotifyMeso();
            }

            foreach (Item newItem in items) {
                // A copy with the amount the box gave: a failed add leaves the item with only the rest it mails.
                totalItems.Add(newItem.Clone());
                if (!AddFromBox(item, newItem)) {
                    session.Item.MailItem(newItem);
                }
            }

            BoxCount++;
        }

        // The boxes opened before a stop still show their items.
        if (totalItems.Count > 0 || error == ItemBoxError.ok) {
            session.Send(ItemScriptPacket.LulluBox(totalItems));
        }
        return error;
    }

    public ItemBoxError OpenLulluBoxSimple(Item item, int count = 1) => Counted(item, () => OpenLulluBoxesSimple(item, count));

    private ItemBoxError OpenLulluBoxesSimple(Item item, int count) {
        if (session.Field is null) return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        Dictionary<string, string> parameters = XmlParseUtil.GetParameters(item.Metadata.Function?.Parameters);

        if (!int.TryParse(parameters["commonBoxId"], out int commonBoxId)) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        if (!session.ServerTableMetadata.IndividualDropItemTable.Entries.TryGetValue(commonBoxId, out IDictionary<int, IndividualDropItemTable.Entry>? entryDict)) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        if (entryDict.Count == 0) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        var keyItemTag = new IngredientInfo(Enum.Parse<ItemTag>(parameters["keyItemTag"]), 1);
        var totalItems = new List<Item>();
        ItemBoxError error = ItemBoxError.ok;
        for (int startCount = 0; startCount < count; startCount++) {
            if (session.Item.Inventory.SpendableCount(keyItemTag.Tag) < 1) {
                error = ItemBoxError.s_err_cannot_open_multi_itembox_inventory;
                break;
            }

            // common dropbox items; the key is taken only when the roll gave items
            ICollection<Item> items = session.Field.ItemDrop.GetIndividualDropItems(session, session.Player.Value.Character.Level, commonBoxId);
            if (items.Count == 0) {
                error = ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
                break;
            }

            if (!session.Item.Inventory.Consume([keyItemTag])) {
                error = ItemBoxError.s_err_cannot_open_multi_itembox_inventory;
                break;
            }

            foreach (Item newItem in items) {
                // A copy with the amount the box gave: a failed add leaves the item with only the rest it mails.
                totalItems.Add(newItem.Clone());
                if (!AddFromBox(item, newItem)) {
                    session.Item.MailItem(newItem);
                }
            }

            BoxCount++;
        }

        // The boxes opened before a stop still show their items.
        if (totalItems.Count > 0 || error == ItemBoxError.ok) {
            session.Send(ItemScriptPacket.LulluBox(totalItems));
        }
        return error;
    }

    // UIItemPetBoxDialog: each open takes one item with the tag of the xml parameter itemTag and rolls the boxes of
    // unCommonBoxId (the list the dialog previews as special) and, when present, commonBoxId.
    public ItemBoxError OpenPetBox(Item item, int count = 1) => Counted(item, () => OpenPetBoxes(item, count));

    private ItemBoxError OpenPetBoxes(Item item, int count) {
        if (session.Field is null) return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        Dictionary<string, string> parameters = XmlParseUtil.GetParameters(item.Metadata.Function?.Parameters);
        if (!parameters.TryGetValue("itemTag", out string? tagName) || !Enum.TryParse(tagName, out ItemTag tag) || tag == ItemTag.None) {
            logger.Error("Invalid parameters for pet box: {ItemId}", item.Id);
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        int[] unCommonBoxIds = ItemUseParameters.ParseIntArray(parameters.GetValueOrDefault("unCommonBoxId"));
        int[] commonBoxIds = ItemUseParameters.ParseIntArray(parameters.GetValueOrDefault("commonBoxId"));
        if (unCommonBoxIds.Length + commonBoxIds.Length == 0) {
            logger.Error("Invalid parameters for pet box: {ItemId}", item.Id);
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        var ingredient = new IngredientInfo(tag, 1);
        var results = new List<(Item Item, bool Special)>();
        for (int i = 0; i < count; i++) {
            if (!session.Item.Inventory.Consume([ingredient])) {
                break;
            }

            foreach (int boxId in unCommonBoxIds) {
                foreach (Item newItem in session.Field.ItemDrop.GetIndividualDropItems(session, session.Player.Value.Character.Level, boxId)) {
                    results.Add((newItem, true));
                }
            }
            foreach (int boxId in commonBoxIds) {
                foreach (Item newItem in session.Field.ItemDrop.GetIndividualDropItems(session, session.Player.Value.Character.Level, boxId)) {
                    results.Add((newItem, false));
                }
            }
            BoxCount++;
        }

        foreach ((Item newItem, _) in results) {
            if (!AddFromBox(item, newItem)) {
                session.Item.MailItem(newItem);
            }
        }

        // The dialog waits for this packet before it takes another click, so it is sent for an empty result too.
        session.Send(ItemScriptPacket.PetBox(results));
        return BoxCount > 0 ? ItemBoxError.ok : ItemBoxError.s_err_cannot_open_multi_itembox_inventory;
    }

    // The parameters are the select count and the individual drop box id; the client lists the box's rows and sends
    // the picked ones (ItemDropManager.SelectBoxRows).
    private ItemBoxError SelectItemBox(Item item, int selectCount, int boxId, string selection, int count = 1) {
        if (session.Field is null) return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        int[]? rows = ItemBoxRules.ParseSelection(selection, selectCount);
        if (rows == null) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }
        // check if box count is enough to open
        if (session.Item.Inventory.Find(item.Id, item.Rarity).Sum(box => box.Amount) < 1) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        if (!session.ServerTableMetadata.IndividualDropItemTable.Entries.TryGetValue(boxId, out IDictionary<int, IndividualDropItemTable.Entry>? boxEntry)) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        if (boxEntry.Count == 0) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        var error = ItemBoxError.ok;
        var ingredient = new ItemComponent(item.Id, item.Rarity, 1, ItemTag.None);
        for (int startCount = 0; startCount < count; startCount++) {
            ICollection<Item> itemList = session.Field.ItemDrop.GetSelectBoxItems(session, boxId, rows);
            if (itemList.Count == 0) {
                return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
            }
            if (!session.Item.Inventory.ConsumeItemComponents([ingredient])) {
                return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
            }
            foreach (Item newItem in itemList) {
                if (!AddFromBox(item, newItem)) {
                    session.Item.MailItem(newItem);
                    error = ItemBoxError.s_err_cannot_open_multi_itembox_inventory;
                }
            }
            BoxCount++;
            if (error != ItemBoxError.ok) {
                return error;
            }
        }
        return error;
    }

    private ItemBoxError OpenItemBox(Item item, int globalDropBoxId, int unknownId, int itemId, int individualDropBoxId, int itemRequiredAmount, int count = 1) {
        if (session.Field is null) return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        // unknownId is treated as a bool. if 1 = receive one item from each drop group, if 0 = receive all items from one drop group.
        // There are occasions where this number is higher than 1. No idea what it is as it's not a globaldropBoxId

        if (itemRequiredAmount > -1 && session.Item.Inventory.Find(item.Id, item.Rarity).Sum(box => box.Amount) < itemRequiredAmount) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        // Verify table exists before consuming items
        if (!session.ServerTableMetadata.IndividualDropItemTable.Entries.TryGetValue(individualDropBoxId, out IDictionary<int, IndividualDropItemTable.Entry>? entryDic)) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        Dictionary<int, IList<GlobalDropItemBoxTable.Group>>? globalDropEntries = [];
        if (globalDropBoxId > 0 && !session.ServerTableMetadata.GlobalDropItemBoxTable.DropGroups.TryGetValue(globalDropBoxId, out globalDropEntries)) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        if (entryDic.Count == 0 && globalDropEntries.Count == 0) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        var error = ItemBoxError.ok;
        var ingredient = new ItemComponent(item.Id, item.Rarity, itemRequiredAmount, ItemTag.None);
        for (int startCount = 0; startCount < count; startCount++) {
            if (!session.Item.Inventory.ConsumeItemComponents([ingredient])) {
                return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
            }

            // The item box special effects held (type 8) open a box of their item ids a second time, the box not
            // taken again.
            int opens = 1 + session.Buffs.TakeSpecialEffects(SpecialEffectRules.ItemBoxAgain,
                effect => SpecialEffectRules.ItemBoxApplies(effect, item.Id)).Count;
            for (int round = 0; round < opens; round++) {
                if (itemId > 0) {
                    // A fixed item that cannot be made does not cost the box its roll (it was skipped with the box taken).
                    Item? newItem = session.Field.ItemDrop.CreateItem(itemId, item.Metadata.Option?.ConstantId ?? 1);
                    if (newItem != null && !AddFromBox(item, newItem)) {
                        session.Item.MailItem(newItem);
                        error = ItemBoxError.s_err_cannot_open_multi_itembox_inventory;
                    }
                }

                IEnumerable<Item> itemList = session.Field.ItemDrop.GetGlobalDropItems(globalDropBoxId, session: session);

                itemList = itemList.Concat(session.Field.ItemDrop.GetIndividualDropItems(session, session.Player.Value.Character.Level, individualDropBoxId));
                foreach (Item newItem in itemList) {
                    if (!AddFromBox(item, newItem)) {
                        session.Item.MailItem(newItem);
                        error = ItemBoxError.s_err_cannot_open_multi_itembox_inventory;
                    }
                }
            }
            BoxCount++;
            if (error != ItemBoxError.ok) {
                return error;
            }
        }
        return error;
    }

    private ItemBoxError OpenItemBoxWithKey(Item item, int keyItemId, int keyAmountRequired, int itemId, int boxId, int count = 1) {
        if (session.Field is null) return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        if (keyItemId == 0) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        // check for keys
        if (session.Item.Inventory.Find(keyItemId).Sum(requiredItem => requiredItem.Amount) < keyAmountRequired) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        // Check if the boxId is in the drop table
        if (!session.ServerTableMetadata.IndividualDropItemTable.Entries.TryGetValue(boxId, out IDictionary<int, IndividualDropItemTable.Entry>? entryDict)) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        if (entryDict.Count == 0) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        var ingredients = new List<ItemComponent> {
            new ItemComponent(item.Id, item.Rarity, 1, ItemTag.None),
            new ItemComponent(keyItemId, -1, keyAmountRequired, ItemTag.None),
        };
        for (int startCount = 0; startCount < count; startCount++) {
            if (!session.Item.Inventory.ConsumeItemComponents(ingredients)) {
                return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
            }

            if (itemId > 0) {
                Item? newItem = session.Field.ItemDrop.CreateItem(itemId, item.Metadata.Option?.ConstantId ?? 1);
                if (newItem != null && !AddFromBox(item, newItem)) {
                    session.Item.MailItem(newItem);
                }
            }

            // Each box opened is rolled on its own (the roll was made once and its items given for every box).
            foreach (Item newItem in session.Field.ItemDrop.GetIndividualDropItems(session, session.Player.Value.Character.Level, boxId)) {
                if (!AddFromBox(item, newItem)) {
                    session.Item.MailItem(newItem);
                }
            }
            BoxCount++;
        }
        return ItemBoxError.ok;
    }

    // While GachaCapsuleMachine (id 266, 0x10a) is on, the client opens a capsule only while the bag also holds as many
    // of the machine item (the function's second parameter, when not 0) as capsules it opens: 1, or 10 for a multi open
    // (UIItemGachaBoxDialog::FUN_140c41700, uiitemgachaboxdialog.cpp:358, s_itemgacha_dialog_not_enough_capsulemachine).
    // The server refuses an open short of them; the machines are not taken, as the client source shows no take.
    public const string CapsuleMachineFeature = "GachaCapsuleMachine";

    private ItemBoxError OpenGachaBox(Item item, int gachaInfoId, int machineId, int count) {
        if (session.Field is null) return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        if (machineId != 0 && session.ServerTableMetadata.FeatureTable.IsOn(CapsuleMachineFeature)
                           && session.Item.Inventory.Find(machineId).Sum(machine => machine.Amount) < count) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }
        // Check if the gachaInfoId is in the drop table
        if (!session.TableMetadata.GachaInfoTable.Entries.TryGetValue(gachaInfoId, out GachaInfoTable.Entry? gachaEntry)) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        if (!session.ServerTableMetadata.IndividualDropItemTable.Entries.TryGetValue(gachaEntry.DropBoxId, out IDictionary<int, IndividualDropItemTable.Entry>? entryDict)) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        if (entryDict.Count == 0) {
            return ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
        }

        var totalItems = new List<Item>();
        var ingredient = new ItemComponent(item.Id, item.Rarity, 1, ItemTag.None);
        ItemBoxError error = ItemBoxError.ok;
        for (int startCount = 0; startCount < count; startCount++) {
            if (!session.Item.Inventory.ConsumeItemComponents([ingredient])) {
                error = ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail;
                break;
            }

            IEnumerable<Item> items = session.Field.ItemDrop.GetIndividualDropItems(session, session.Player.Value.Character.Level, gachaEntry.DropBoxId);
            foreach (Item createdItem in items) {
                createdItem.GachaDismantleId = gachaInfoId;
                // A copy with the amount the capsule gave: a failed add leaves the item with only the rest it mails.
                totalItems.Add(createdItem.Clone());
                if (!AddFromBox(item, createdItem)) {
                    session.Item.MailItem(createdItem);
                }
            }
            BoxCount++;
        }

        // The capsules opened before a stop still show their items.
        if (totalItems.Count > 0 || error == ItemBoxError.ok) {
            session.Send(ItemScriptPacket.Gacha(totalItems));
        }
        return error;
    }
}
