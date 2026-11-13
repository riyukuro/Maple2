using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;
using Serilog;
using static Maple2.Model.Error.ItemInventoryError;

namespace Maple2.Server.Game.Manager.Items;

public class InventoryManager {
    private const int BATCH_SIZE = 10;

    private readonly GameSession session;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    private ConstantsTable Constants => session.ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    private readonly Dictionary<InventoryType, ItemCollection> tabs;
    private readonly List<Item> delete;

    private readonly ILogger logger = Log.Logger.ForContext<InventoryManager>();

    public InventoryManager(GameStorage.Request db, GameSession session) {
        this.session = session;
        tabs = new Dictionary<InventoryType, ItemCollection>();
        foreach (InventoryType type in Enum.GetValues<InventoryType>()) {
            session.Player.Value.Unlock.Expand.TryGetValue(type, out short expand);
            tabs[type] = new ItemCollection((short) (BaseSize(type) + expand));
        }

        delete = [];
        var overflow = new List<Item>();
        foreach ((InventoryType type, List<Item> load) in db.GetInventory(session.CharacterId)) {
            if (!tabs.TryGetValue(type, out ItemCollection? items)) continue;
            foreach (Item item in load) {
                if (items.Add(item).Count != 0) continue;
                overflow.Add(item);
            }
        }
        MailOverflow(overflow);
    }

    // An item row the tab has no slot for at load: the tab is taken from the item data (Item.Inventory), so an ingest
    // that puts an item in another tab (PlayedClientItems.ClientType) can bring more items to a tab than it has slots,
    // and the client shows no item past the tab's size. Such a row went to owner 0 at the next save, which lost the
    // item. It now moves to a system mail of its own, as a reward that finds the bag full (ItemManager.MailItem); the
    // login's mail count (MailManager.Notify) shows it. When the mail is not made the row stays with the character,
    // out of the bag, and the next load tries again.
    private void MailOverflow(List<Item> overflow) {
        if (overflow.Count == 0) {
            return;
        }
        // Another request: the rows loaded above are tracked by the caller's context.
        using GameStorage.Request db = session.GameStorage.Context();
        foreach (Item item in overflow) {
            short slot = item.Slot;
            item.Slot = -1;
            var mail = new Mail {
                Type = MailType.System,
                ReceiverId = session.CharacterId,
                Content = "50000000", // id from string/en/systemmailcontentna.xml
            };
            mail.Items.Add(item);
            if (db.CreateMail(mail) is { } created) {
                logger.Warning("Item {ItemUid} ({ItemId}) found no slot in inventory {InventoryType}: mailed ({MailId})",
                    item.Uid, item.Id, item.Inventory, created.Id);
            } else {
                item.Slot = slot;
                logger.Error("Item {ItemUid} ({ItemId}) found no slot in inventory {InventoryType} and could not be mailed",
                    item.Uid, item.Id, item.Inventory);
            }
        }
    }

    private short BaseSize(InventoryType type) {
        return type switch {
            InventoryType.Gear => Constants.bagSlotTabGameCount[0],
            InventoryType.Outfit => Constants.bagSlotTabSkinCount[0],
            InventoryType.Mount => Constants.bagSlotTabSummonCount[0],
            InventoryType.Catalyst => Constants.bagSlotTabMaterialCount[0],
            InventoryType.FishingMusic => Constants.bagSlotTabLifeCount[0],
            InventoryType.Quest => Constants.bagSlotTabQuestCount[0],
            InventoryType.Gemstone => Constants.bagSlotTabGemCount[0],
            InventoryType.Misc => Constants.bagSlotTabMiscCount[0],
            InventoryType.LifeSkill => Constants.bagSlotTabMasteryCount[0],
            InventoryType.Pets => Constants.bagSlotTabPetCount[0],
            InventoryType.Consumable => Constants.bagSlotTabActiveSkillCount[0],
            InventoryType.Currency => Constants.bagSlotTabCoinCount[0],
            InventoryType.Badge => Constants.bagSlotTabBadgeCount[0],
            InventoryType.Lapenshard => Constants.bagSlotTabLapenShardCount[0],
            InventoryType.Fragment => Constants.bagSlotTabPieceCount[0],
            _ => throw new ArgumentOutOfRangeException($"Invalid InventoryType: {type}"),
        };
    }

    private short MaxExpandSize(InventoryType type) {
        return type switch {
            InventoryType.Gear => Constants.bagSlotTabGameCount[1],
            InventoryType.Outfit => Constants.bagSlotTabSkinCount[1],
            InventoryType.Mount => Constants.bagSlotTabSummonCount[1],
            InventoryType.Catalyst => Constants.bagSlotTabMaterialCount[1],
            InventoryType.FishingMusic => Constants.bagSlotTabLifeCount[1],
            InventoryType.Quest => Constants.bagSlotTabQuestCount[1],
            InventoryType.Gemstone => Constants.bagSlotTabGemCount[1],
            InventoryType.Misc => Constants.bagSlotTabMiscCount[1],
            InventoryType.LifeSkill => Constants.bagSlotTabMasteryCount[1],
            InventoryType.Pets => Constants.bagSlotTabPetCount[1],
            InventoryType.Consumable => Constants.bagSlotTabActiveSkillCount[1],
            InventoryType.Currency => Constants.bagSlotTabCoinCount[1],
            InventoryType.Badge => Constants.bagSlotTabBadgeCount[1],
            InventoryType.Lapenshard => Constants.bagSlotTabLapenShardCount[1],
            InventoryType.Fragment => Constants.bagSlotTabPieceCount[1],
            _ => throw new ArgumentOutOfRangeException($"Invalid InventoryType: {type}"),
        };
    }

    public void Load() {
        lock (session.Item) {
            foreach ((InventoryType type, ItemCollection items) in tabs) {
                session.Send(ItemInventoryPacket.Reset(type));
                session.Send(ItemInventoryPacket.ExpandCount(type, items.Size - BaseSize(type)));
                // Load items for above tab
                foreach (ImmutableList<Item> batch in items.Batch(BATCH_SIZE)) {
                    session.Send(ItemInventoryPacket.Load(batch));
                }
            }
        }
    }

    public bool Move(long uid, short dstSlot) {
        lock (session.Item) {
            if (dstSlot < 0) {
                session.Send(ItemInventoryPacket.Error(s_item_err_Invalid_slot));
                return false;
            }

            ItemCollection? items = tabs.Values.FirstOrDefault(collection => collection.Contains(uid));
            if (items == null || dstSlot >= items.Size) {
                return false;
            }

            // Attempt to stack
            Item? srcItem = items.Get(uid);
            if (srcItem != null) {
                IList<(Item, int)> results = items.Stack(srcItem, dstSlot);
                if (results.Count > 0) {
                    (Item item, int _) = results.First();
                    if (srcItem.Amount == 0) {
                        items.Remove(uid, out _);
                        Discard(srcItem);

                        session.Send(ItemInventoryPacket.Remove(uid));
                    } else {
                        session.Send(ItemInventoryPacket.UpdateAmount(srcItem.Uid, srcItem.Amount));
                    }
                    session.Send(ItemInventoryPacket.UpdateAmount(item.Uid, item.Amount));

                    return true;
                }
            }

            if (items.Remove(uid, out srcItem)) {
                short srcSlot = srcItem.Slot;
                if (items.RemoveSlot(dstSlot, out Item? removeDst)) {
                    items[srcSlot] = removeDst;
                }

                items[dstSlot] = srcItem;

                session.Send(ItemInventoryPacket.Move(removeDst?.Uid ?? 0, srcSlot, uid, dstSlot));
            }

            return true;
        }
    }

    // onAdded: called with each inventory item that took part of the amount (a new row or a stack it merged into).
    public bool Add(Item add, bool notifyNew = false, bool commit = false, Action<Item>? onAdded = null) {
        lock (session.Item) {
            if (session.Field is null) {
                logger.Warning("Tried to add item while not in a field");
                return false;
            }

            if (add.IsCurrency()) {
                AddCurrency(add);
                session.Item.Inventory.Discard(add);
                return true;
            }

            if (add.Type.IsMedal) {
                session.Survival.AddMedal(add);
                session.Item.Inventory.Discard(add);
                return true;
            }

            if (add.Type.IsFurnishing) {
                session.Item.Furnishing.AddStorage(add, add.Template);
                session.Item.Inventory.Discard(add);
                return true;
            }

            if (!tabs.TryGetValue(add.Inventory, out ItemCollection? items)) {
                session.Send(ItemInventoryPacket.Error(s_item_err_not_active_tab));
                return false;
            }

            // Unified logic for stack splitting (works for both SlotMax == 1 and SlotMax > 1)
            int slotMax = Math.Max(1, add.Metadata.Property.SlotMax);
            if (add.Amount > slotMax) {
                int totalAmount = add.Amount;

                // 1. Fill partially filled stacks first
                foreach (Item existing in items.Where(x => x.Id == add.Id && x.Rarity == add.Rarity && x.Amount < slotMax && !x.IsExpired())) {
                    int canFill = slotMax - existing.Amount;
                    int toFill = Math.Min(canFill, totalAmount);
                    if (toFill <= 0) continue;
                    totalAmount -= toFill;
                    Item? itemToStack = session.Field.ItemDrop.CreateItem(add.Id, add.Rarity, toFill);
                    if (itemToStack is null) return false;
                    if (!Add(itemToStack, notifyNew, commit, onAdded)) {
                        logger.Error("Failed to add partial stack during multi-step stacking");
                        return false;
                    }
                }

                int fullStacks = totalAmount / slotMax;
                int remainder = totalAmount % slotMax;
                int stacksNeeded = fullStacks + (remainder > 0 ? 1 : 0);

                if (items.OpenSlots < stacksNeeded) {
                    session.Send(ItemInventoryPacket.Error(s_err_inventory));
                    return false;
                }

                // 2. Add full stacks
                for (int i = 0; i < fullStacks; i++) {
                    Item? stackItem = session.Field.ItemDrop.CreateItem(add.Id, add.Rarity);
                    if (stackItem is null) return false;
                    stackItem.Amount = slotMax;
                    if (!Add(stackItem, notifyNew, commit, onAdded)) return false;
                }

                // 3. Add remainder stack
                if (remainder > 0) {
                    Item? remainderItem = session.Field.ItemDrop.CreateItem(add.Id, add.Rarity);
                    if (remainderItem is null) return false;
                    remainderItem.Amount = remainder;
                    if (!Add(remainderItem, notifyNew, commit, onAdded)) return false;
                }

                return true;
            }

            if (add.Metadata.Limit.TransferType is TransferType.BindOnLoot) {
                add.Transfer?.Bind(session.Player.Value.Character);
            }

            // A new item gets its row only when it fits: a row created first stayed with the character at slot -1
            // when the bag was full, and the caller then mailed the item as another new row (a reward on a full
            // bag existed twice in the database).
            if (add.Uid == 0 && !items.Fits(add, stack: true)) {
                session.Send(ItemInventoryPacket.Error(s_err_inventory));
                return false;
            }

            using GameStorage.Request db = session.GameStorage.Context();

            bool justCreated = false;

            // If we are adding an item without a Uid, it needs to be created in db.
            if (add.Uid == 0) {
                // Slot MUST be -1 so we don't add directly to a slot.
                add.Slot = -1;

                Item? newAdd = db.CreateItem(session.CharacterId, add);
                if (newAdd == null) {
                    logger.Error("Failed to create item in database");
                    return false;
                }

                add = newAdd;
                justCreated = true;
            }

            IList<(Item, int Added)> result = items.Add(add, stack: true);
            if (result.Count == 0) {
                // A failed add leaves an item that already had a row as it was (ItemCollection.Add changes nothing
                // when it fails): its row stays with its owner, a mail or a storage, and the caller keeps it.
                // Discarding it here moved the row to owner 0, so an attachment collected into a full bag was lost.
                if (justCreated) {
                    Discard(add, commit);
                }
                session.Send(ItemInventoryPacket.Error(s_err_inventory));
                return false;
            }

            if (add.Amount == 0) {
                Discard(add, commit);
            }

            if (commit) {
                // The stacks the item merged into changed too. An item merged away completely was already
                // discarded above and is not saved back to the character. A just-created item was written
                // with its full amount; when part of it merged into stacks, save its lowered amount as well,
                // or the merged part exists twice in the database until the next character save.
                List<Item> changed = result.Select(entry => entry.Item1).Where(item => item.Uid != add.Uid).ToList();
                if (add.Amount > 0 && (!justCreated || changed.Count > 0)) {
                    changed.Add(add);
                }
                if (changed.Count > 0) {
                    db.SaveItems(session.CharacterId, changed.ToArray());
                }
            }

            foreach ((Item item, int added) in result) {
                session.Send(item.Uid == add.Uid
                    ? ItemInventoryPacket.Add(add)
                    : ItemInventoryPacket.UpdateAmount(item.Uid, item.Amount));
                onAdded?.Invoke(item);

                if (notifyNew) {
                    session.Send(ItemInventoryPacket.NotifyNew(item.Uid, added));
                }
                session.ConditionUpdate(ConditionType.item_collect, codeLong: item.Id);
                session.ConditionUpdate(ConditionType.item_add, counter: item.Amount, codeLong: item.Id);
                session.Achievement.Update(ConditionType.item_exist, count: item.Amount, targetLong: session.Field?.MapId ?? 0, codeLong: item.Id);
                session.Quest.UpdateHeld(item.Id);
            }

            return true;
        }
    }

    private void AddCurrency(Item add) {
        switch (add.Id) {
            case 90000001 or 90000002 or 90000003:
                session.Currency.Meso += add.Amount;
                break;
            // case 90000011: // Meret (Secondary)
            // case 90000015: // GameMeret (Secondary)
            case 90000016: // EventMeret (Secondary)
            case 90000020: // RedMeret
            case 90000004: // Meret
                session.Currency.Meret += add.Amount;
                break;
            case 90000006: // ValorToken
                session.Currency[CurrencyType.ValorToken] += add.Amount;
                break;
            case 90000008: // ExperienceOrb
                session.Exp.AddExp(ExpType.expDrop, additionalExp: add.Amount);
                break;
            case 90000009: // SpiritOrb
                session.Stats.Values[BasicAttribute.Spirit].Add(add.Amount);
                session.Send(StatsPacket.Update(session.Player, BasicAttribute.Spirit));
                break;
            case 90000010: // StaminaOrb
                session.Stats.Values[BasicAttribute.Stamina].Add(add.Amount);
                session.Send(StatsPacket.Update(session.Player, BasicAttribute.Stamina));
                break;
            case 90000013: // Rue
                session.Currency[CurrencyType.Rue] += add.Amount;
                break;
            case 90000014: // HaviFruit
                session.Currency[CurrencyType.HaviFruit] += add.Amount;
                break;
            case 90000017: // Treva
                session.Currency[CurrencyType.Treva] += add.Amount;
                break;
            case 90000027: // MesoToken
                session.Currency[CurrencyType.MesoToken] += add.Amount;
                break;
            // case 90000005: // DungeonKey
            // case 90000007: // Karma
            // case 90000012: // Unknown (BookIcon)
            // case 90000018: // ShadowFragment
            // case 90000019: // DistinctPaul
            case 90000021: // GuildFunds: the guild's fund, added by the World (none without a guild)
                session.Guild.AddExpFund(0, add.Amount);
                break;
            case 90000022: // ReverseCoin
                session.Currency[CurrencyType.ReverseCoin] += add.Amount;
                break;
            case 90000023: // MentorPoint
                session.Currency[CurrencyType.MentorToken] += add.Amount;
                break;
            case 90000024: // MenteePoint
                session.Currency[CurrencyType.MenteeToken] += add.Amount;
                break;
            case 90000025: // StarPoint
                session.Currency[CurrencyType.StarPoint] += add.Amount;
                break;
                // case 90000026: // Unknown (Blank)
        }
    }

    public bool CanAdd(Item item) {
        lock (session.Item) {
            if (tabs.TryGetValue(item.Inventory, out ItemCollection? items)) {
                return items.OpenSlots > 0 || items.GetStackResult(item) == 0;
            }

            return false;
        }
    }

    // Room for <paramref name="amount"/> pieces of the item: a free slot, or stacks that take all of them.
    public bool CanAdd(Item item, int amount) {
        lock (session.Item) {
            return tabs.TryGetValue(item.Inventory, out ItemCollection? items)
                   && StorageRules.HasRoom(items.OpenSlots, items.GetStackResult(item, amount));
        }
    }

    // Room for the whole amount of the item: what the stacks do not take fits the open slots at SlotMax each,
    // so Add takes all of it or (before it changes anything) none. Currency, medals and furnishings need no slot.
    public bool HasRoomFor(Item item) {
        if (item.IsCurrency() || item.Type.IsMedal || item.Type.IsFurnishing) {
            return true;
        }
        lock (session.Item) {
            return tabs.TryGetValue(item.Inventory, out ItemCollection? items)
                   && items.OpenSlots >= StorageRules.SlotsNeeded(items.GetStackResult(item), item.Metadata.Property.SlotMax);
        }
    }

    // Whether Add of each of the items in turn takes all of them. Currency, medals and furnishings need no slot.
    public bool TakesAll(IEnumerable<Item> adds) {
        lock (session.Item) {
            foreach (IGrouping<InventoryType, Item> group in adds
                         .Where(item => !item.IsCurrency() && !item.Type.IsMedal && !item.Type.IsFurnishing)
                         .GroupBy(item => item.Inventory)) {
                if (!tabs.TryGetValue(group.Key, out ItemCollection? items) || !items.TakesAll(group)) {
                    return false;
                }
            }
            return true;
        }
    }

    public bool CanAdd(ICollection<Item> items) {
        lock (session.Item) {
            // Group items by inventory type
            Dictionary<InventoryType, List<Item>> itemsByType = items.GroupBy(item => item.Inventory)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach ((InventoryType inventoryType, List<Item> typeItems) in itemsByType) {
                if (!tabs.TryGetValue(inventoryType, out ItemCollection? collection)) {
                    return false;
                }

                short availableSlots = collection.OpenSlots;

                foreach (Item item in typeItems) {
                    int stackResult = collection.GetStackResult(item);

                    if (stackResult == 0) {
                        // Item can be fully stacked, no slots needed
                        continue;
                    }

                    // Need a new slot
                    if (availableSlots <= 0) {
                        return false;
                    }
                    availableSlots--;
                }
            }

            return true;
        }
    }

    // notify false: a full remove without its ItemInventory packet; the caller sends it.
    public bool Remove(long uid, [NotNullWhen(true)] out Item? removed, int amount = -1, bool notify = true) {
        lock (session.Item) {
            return RemoveInternal(uid, amount, out removed, notify);
        }
    }

    public bool Consume(long uid, int amount = -1) {
        lock (session.Item) {
            return ConsumeInternal(uid, amount);
        }
    }

    public bool Consume(ICollection<IngredientInfo> ingredients) {
        lock (session.Item) {
            // Build this index so we don't need to find materials twice.
            Dictionary<ItemTag, IList<Item>> ingredientsByTag = ingredients.ToDictionary(
                entry => entry.Tag,
                entry => Filter(item => item.Metadata.Property.Tag == entry.Tag && !item.IsExpired())
            );

            // Validate
            foreach (IngredientInfo info in ingredients) {
                int remaining = info.Amount;
                foreach (Item ingredient in ingredientsByTag[info.Tag]) {
                    remaining -= ingredient.Amount;
                    if (remaining <= 0) {
                        break;
                    }
                }

                if (remaining > 0) {
                    return false;
                }
            }

            // Consume
            foreach (IngredientInfo info in ingredients) {
                int remaining = info.Amount;
                foreach (Item ingredient in ingredientsByTag[info.Tag]) {
                    int consume = Math.Min(remaining, ingredient.Amount);
                    if (!ConsumeInternal(ingredient.Uid, consume)) {
                        Log.Error("Failed to consume ingredient {ItemUid}", ingredient.Uid);
                        return false;
                    }

                    remaining -= consume;
                    if (remaining <= 0) {
                        break;
                    }
                }
            }

            return true;
        }
    }

    public bool ConsumeItemComponents(IReadOnlyList<ItemComponent> components, int quantityMultiplier = 1) {
        lock (session.Item) {

            // Check for components
            Dictionary<int, IList<Item>> materialsById = components.ToDictionary(
                ingredient => ingredient.ItemId,
                ingredient => Filter(item => !item.IsExpired() && item.Id == ingredient.ItemId && (ingredient.Rarity < 0 || item.Rarity == ingredient.Rarity))
            );
            var materialsByTag = new Dictionary<ItemTag, IList<Item>>();
            foreach (ItemComponent ingredient in components) {
                if (materialsByTag.TryGetValue(ingredient.Tag, out IList<Item>? value)) {
                    foreach (Item item in session.Item.Inventory.Find(ingredient.ItemId, ingredient.Rarity)) {
                        value.Add(item);
                    }
                } else {
                    materialsByTag.Add(ingredient.Tag, session.Item.Inventory.Find(ingredient.ItemId, ingredient.Rarity).ToList());
                }
            }

            foreach (ItemComponent ingredient in components) {
                int remaining = ingredient.Amount * quantityMultiplier;
                if (ingredient.Tag != ItemTag.None) {
                    foreach (Item material in materialsByTag[ingredient.Tag]) {
                        remaining -= material.Amount;
                        if (remaining <= 0) {
                            break;
                        }
                    }
                } else {
                    foreach (Item material in materialsById[ingredient.ItemId]) {
                        remaining -= material.Amount;
                        if (remaining <= 0) {
                            break;
                        }
                    }
                }

                if (remaining > 0) {
                    return false;
                }
            }

            foreach (ItemComponent ingredient in components) {
                int remainingIngredients = ingredient.Amount * quantityMultiplier;
                if (ingredient.Tag != ItemTag.None) {
                    foreach (Item material in materialsByTag[ingredient.Tag]) {
                        int consume = Math.Min(remainingIngredients, material.Amount);
                        if (!session.Item.Inventory.Consume(material.Uid, consume)) {
                            Log.Error("Failed to consume item uid: {ItemUid}, item id: {ItemId}", material.Uid, material.Id);
                            return false;
                        }

                        remainingIngredients -= consume;
                        if (remainingIngredients <= 0) {
                            break;
                        }
                    }
                } else {
                    foreach (Item material in materialsById[ingredient.ItemId]) {
                        int consume = Math.Min(remainingIngredients, material.Amount);
                        if (!session.Item.Inventory.Consume(material.Uid, consume)) {
                            Log.Error("Failed to consume item uid: {ItemUid}, item id: {ItemId}", material.Uid, material.Id);
                            return false;
                        }

                        remainingIngredients -= consume;
                        if (remainingIngredients <= 0) {
                            break;
                        }
                    }
                }
            }
        }
        return true;
    }

    public void Sort(InventoryType type, bool removeExpired = false, bool mergeStacks = false) {
        lock (session.Item) {
            if (!tabs.TryGetValue(type, out ItemCollection? items)) {
                session.Send(ItemInventoryPacket.Error(s_item_err_not_active_tab));
                return;
            }

            if (removeExpired) {
                // A locked item is kept even when expired: the lock is the player's guard against losing it.
                List<Item> toRemove = items.Where(item => item.IsExpired() && !item.IsLocked).ToList();
                foreach (Item item in toRemove) {
                    if (items.Remove(item.Uid, out Item? removed)) {
                        Discard(removed);
                    }
                }
            }

            if (mergeStacks) {
                // Emptied stacks are deleted at the inventory save; they were merged, not destroyed, so no
                // item_destroy. As in KMS2, an emptied stack is removed and a changed one updated before the
                // tab is reloaded.
                Dictionary<long, int> amounts = items.ToDictionary(item => item.Uid, item => item.Amount);
                foreach (Item emptied in items.MergeStacks()) {
                    delete.Add(emptied);
                    session.Send(ItemInventoryPacket.Remove(emptied.Uid));
                }
                foreach (Item item in items.Where(item => amounts[item.Uid] != item.Amount)) {
                    session.Send(ItemInventoryPacket.UpdateAmount(item.Uid, item.Amount));
                }
            }

            items.Sort();

            session.Send(ItemInventoryPacket.Reset(type));
            foreach (ImmutableList<Item> batch in items.Batch(BATCH_SIZE)) {
                session.Send(ItemInventoryPacket.LoadTab(type, batch));
            }
        }
    }

    // The Meret expansion of the bag window (charge: true) costs InventoryExpandPrice1Row and ends with
    // ExpandComplete, which shows s_msg_expand_inven_complete. An expansion coupon (item function ExpandInven) costs
    // nothing: the client asks with the plain item-use box, no price in it, and the ItemUse answer shows the same
    // message, so no ExpandComplete is sent for it.
    public bool Expand(InventoryType type, int expandRowCount = Constant.InventoryExpandRowCount, bool charge = true) {
        // if expandRowCount is not divisible by 6, return false
        if (expandRowCount % 6 != 0) {
            return false;
        }

        lock (session.Item) {
            if (!tabs.TryGetValue(type, out ItemCollection? items)) {
                session.Send(ItemInventoryPacket.Error(s_item_err_not_active_tab));
                return false;
            }

            session.Player.Value.Unlock.Expand.TryGetValue(type, out short expanded);
            short newExpand = (short) (expanded + expandRowCount);
            if (newExpand > MaxExpandSize(type)) {
                // There is client side validation for this, but if the server side limits mismatch, use this error.
                session.Send(NoticePacket.MessageBox(StringCode.s_inventory_err_expand_max));
                return false;
            }

            if (charge && session.Currency.Meret < Constants.InventoryExpandPrice1Row) {
                session.Send(ItemInventoryPacket.Error(s_cannot_charge_merat));
                return false;
            }

            if (!items.Expand((short) (BaseSize(type) + newExpand))) {
                return false;
            }

            if (charge) {
                session.Currency.Meret -= Constants.InventoryExpandPrice1Row;
            }
            session.Player.Value.Unlock.Expand[type] = newExpand;

            session.Send(ItemInventoryPacket.ExpandCount(type, newExpand));
            if (charge) {
                session.Send(ItemInventoryPacket.ExpandComplete());
            }
            return true;
        }
    }

    public short FreeSlots(InventoryType type) {
        lock (session.Item) {
            return !tabs.TryGetValue(type, out ItemCollection? items) ? (short) 0 : items.OpenSlots;
        }
    }

    public short TotalSlots(InventoryType type) {
        lock (session.Item) {
            return !tabs.TryGetValue(type, out ItemCollection? items) ? (short) 0 : items.Size;
        }
    }

    public Item? Get(long uid, InventoryType? type = null) {
        lock (session.Item) {
            if (type != null) {
                return tabs[(InventoryType) type].Get(uid);
            }

            return tabs.Values.FirstOrDefault(collection => collection.Contains(uid))?.Get(uid);
        }
    }

    // Takes the survival match items (SurvivalItemRules.IsMatchOnly) out of the inventory with the normal remove packet,
    // for the death drop at the body (SurvivalGameField). Chosen here.
    public List<Item> TakeSurvivalItems() {
        lock (session.Item) {
            var taken = new List<Item>();
            foreach (Item item in tabs.Values.SelectMany(tab => tab.Where(item => SurvivalItemRules.IsMatchOnly(item.Metadata))).ToList()) {
                if (RemoveInternal(item.Uid, -1, out Item? removed)) {
                    taken.Add(removed);
                }
            }
            return taken;
        }
    }

    // Deletes the survival match items (SurvivalItemRules) on entering a field whose survival element calls for it,
    // with the normal remove packet and one log line per item. Chosen here.
    public int ClearSurvivalItems(MapMetadataSurvival? survival, int mapId) {
        string? reason = SurvivalItemRules.ClearReason(survival);
        if (reason == null) {
            return 0;
        }
        lock (session.Item) {
            int count = 0;
            foreach (Item item in tabs.Values.SelectMany(tab => tab.Where(item => SurvivalItemRules.IsMatchOnly(item.Metadata))).ToList()) {
                int amount = item.Amount;
                if (!RemoveInternal(item.Uid, -1, out Item? removed)) {
                    continue;
                }
                if (removed.Uid != 0) {
                    delete.Add(removed);
                }
                count++;
                logger.Information("Survival item deleted: character {CharacterId}, item {ItemId}, uid {ItemUid}, amount {Amount}, map {MapId}: {Reason}",
                    session.CharacterId, removed.Id, removed.Uid, amount, mapId, reason);
            }
            return count;
        }
    }

    public IList<Item> Filter(Func<Item, bool> condition, InventoryType? type = null) {
        lock (session.Item) {
            if (type != null) {
                return tabs[(InventoryType) type].Where(condition).ToList();
            }

            return tabs.Values.SelectMany(tab => tab.Where(condition)).ToList();
        }
    }

    public IEnumerable<Item> Find(int id, int rarity = -1) {
        lock (session.Item) {
            if (!session.ItemMetadata.TryGet(id, out ItemMetadata? metadata)) {
                yield break;
            }

            InventoryType type = metadata.Inventory();
            if (!tabs.TryGetValue(type, out ItemCollection? items)) {
                session.Send(ItemInventoryPacket.Error(s_item_err_not_active_tab));
                yield break;
            }

            foreach (Item item in items) {
                if (item.Id != id) continue;
                if (rarity != -1 && item.Rarity != rarity) continue;
                if (item.IsExpired()) continue;

                yield return item;
            }
        }
    }

    public IEnumerable<Item> Find(ItemTag itemTag) {
        lock (session.Item) {
            foreach ((InventoryType type, ItemCollection items) in tabs) {
                foreach (Item item in items) {
                    if (item.IsExpired()) continue;
                    if (item.Metadata.Property.Tag != itemTag) continue;
                    yield return item;
                }
            }
        }
    }

    public void Clear(InventoryType tab) {
        lock (session.Item) {
            if (!tabs.TryGetValue(tab, out ItemCollection? items)) {
                session.Send(ItemInventoryPacket.Error(s_item_err_not_active_tab));
                return;
            }

            foreach (Item item in items) {
                Remove(item.Uid, out _, item.Amount);
                Discard(item);
            }
        }
    }

    #region Internal (No Locks)
    private bool RemoveInternal(long uid, int amount, [NotNullWhen(true)] out Item? removed, bool notify = true) {
        ItemCollection? items = tabs.Values.FirstOrDefault(collection => collection.Contains(uid));
        if (items == null || amount == 0) {
            removed = null;
            return false;
        }

        if (amount > 0) {
            Item? item = items.Get(uid);
            if (item == null || item.Amount < amount) {
                session.Send(ItemInventoryPacket.Error(s_item_err_invalid_count));
                removed = null;
                return false;
            }

            // Otherwise, we would just do a full remove.
            if (item.Amount > amount) {
                using GameStorage.Request db = session.GameStorage.Context();
                removed = db.SplitItem(0, item, amount);
                if (removed == null) {
                    return false;
                }
                item.Amount -= amount;

                session.Send(ItemInventoryPacket.UpdateAmount(uid, item.Amount));
                session.Quest.UpdateHeld(item.Id);
                return true;
            }
        }

        // Full remove of item
        if (items.Remove(uid, out removed)) {
            if (notify) {
                session.Send(ItemInventoryPacket.Remove(uid));
            }
            session.Quest.UpdateHeld(removed.Id);
            return true;
        }

        return false;
    }

    private bool ConsumeInternal(long uid, int amount, bool commit = false) {
        ItemCollection? items = tabs.Values.FirstOrDefault(collection => collection.Contains(uid));
        if (items == null || amount == 0) {
            return false;
        }

        if (amount > 0) {
            Item? item = items.Get(uid);
            if (item == null || item.IsExpired() || item.Amount < amount) {
                return false;
            }

            // Otherwise, we would just do a full remove.
            if (item.Amount > amount) {
                item.Amount -= amount;

                session.Send(ItemInventoryPacket.UpdateAmount(uid, item.Amount));
                session.Quest.UpdateHeld(item.Id);
                return true;
            }
        }

        // Full remove of item
        if (items.Remove(uid, out Item? removed)) {
            Discard(removed, commit);
            session.Send(ItemInventoryPacket.Remove(uid));
            session.Quest.UpdateHeld(removed.Id);
            return true;
        }

        return false;
    }
    #endregion

    public void Discard(Item item, bool commit = false) {
        // Only discard items that need to be saved to DB.
        if (item.Uid == 0) {
            return;
        }

        if (commit) {
            lock (session.Item) {
                using GameStorage.Request db = session.GameStorage.Context();
                db.SaveItems(0, item);
            }
        } else {
            delete.Add(item);
        }
        lock (session.Item) {
            if (item.Type is { IsSkin: false, IsHair: false, IsDecal: false, IsEar: false, IsFace: false }) {
                session.ConditionUpdate(ConditionType.item_destroy, codeLong: item.Id);
            }
        }
    }

    public void Save(GameStorage.Request db) {
        lock (session.Item) {
            db.SaveItems(0, delete.ToArray());
            foreach (ItemCollection tab in tabs.Values) {
                db.SaveItems(session.CharacterId, tab.ToArray());
            }
        }
    }

    public string Print(InventoryType type) {
        lock (session.Item) {
            if (!tabs.TryGetValue(type, out ItemCollection? items)) {
                return $"Inventory {type} not found.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Inventory {type}:");
            foreach (Item item in items) {
                sb.AppendLine($"- {item.Id} [{item.Metadata.Name}] (Amount: {item.Amount}, Slot: {item.Slot}, Expiry: {item.ExpiryTime}, Rarity: {item.Rarity}, Tag: {item.Metadata.Property.Tag})");
            }
            sb.AppendLine($"Total Items: {items.Count}, Open Slots: {items.OpenSlots}, Size: {items.Size}");
            return sb.ToString();
        }
    }
}
