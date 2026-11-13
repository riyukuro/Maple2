using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Serilog;

namespace Maple2.Server.Game.Manager;

public class ItemEnchantManager {
    private const int MaxRate = 100;
    private const int MaxFodderRate = 30;
    private const int MaxExp = 10000;
    private const int ChargeRate = 1;

    private static readonly int[] RequireFodder = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 2, 3, 3, 4];
    private static readonly int[] GainExp = [10000, 10000, 10000, 5000, 5000, 5000, 2500, 2500, 2500, 2000, 3334, 2000, 2000, 1250, 1250];
    private static readonly int[] SuccessRate = [100, 100, 100, 95, 90, 80, 70, 60, 50, 40, 30, 20, 15, 10, 5];
    private static readonly int[] FodderRate = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 10, 7, 5, 4, 2];
    private static readonly int[] FailCharge = [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 3, 4, 4, 5];

    private static readonly IngredientInfo[][] PeachyCost = new IngredientInfo[15][];

    // TODO: Dynamic catalysts
    static ItemEnchantManager() {
        PeachyCost[0] = Build(802, 4, 20); // 1x
        PeachyCost[1] = Build(802, 4, 20); // 1x
        PeachyCost[2] = Build(802, 4, 20); // 1x
        PeachyCost[3] = Build(579, 2, 15); // 2x
        PeachyCost[4] = Build(611, 2, 16); // 2x
        PeachyCost[5] = Build(872, 4, 21); // 2x
        PeachyCost[6] = Build(604, 2, 15); // 4x
        PeachyCost[7] = Build(826, 3, 21); // 4x
        PeachyCost[8] = Build(1133, 3, 29); // 4x
        PeachyCost[9] = Build(2238, 6, 31); // 5x
        PeachyCost[10] = Build(4828, 13, 78); // 3x
        PeachyCost[11] = Build(6148, 16, 66); // 5x
        PeachyCost[12] = Build(7347, 19, 79); // 5x
        PeachyCost[13] = Build(6374, 17, 69); // 8x
        PeachyCost[14] = Build(7784, 20, 84); // 8x

        IngredientInfo[] Build(int onyx, int chaosOnyx, int crystalFragment) {
            return [
                new IngredientInfo(ItemTag.Onix, onyx),
                new IngredientInfo(ItemTag.ChaosOnix, chaosOnyx),
                new IngredientInfo(ItemTag.CrystalPiece, crystalFragment),
            ];
        }
    }

    private readonly GameSession session;
    private readonly ILogger logger = Log.Logger.ForContext<ItemEnchantManager>();

    public EnchantType Type { get; private set; }

    private Item? upgradeItem;
    private readonly List<IngredientInfo> catalysts;
    private readonly Dictionary<long, Item> fodders;
    private readonly Dictionary<BasicAttribute, BasicOption> attributeDeltas;
    private readonly EnchantRates rates;
    private int fodderWeight;
    private int useCharges;
    private EnchantResult enchantResult;
    // The last try (result, level after it, grade) for the dialog's result event (talk event 3), which comes after the
    // stage is spent: at the last grade nothing stays staged.
    // The last Ophelia try (its result, the level after the roll, the grade) for the result line of talk event 3;
    // the roll's Reset clears the staged item before that event comes. A new stage drops it.
    private (EnchantResult Result, int Level, short Rarity)? lastTry;
    // The item of the last stage: the dialog's retry button asks for it again by a talk event alone.
    private EnchantType retryType;
    private long retryUid;

    // Limit break values only
    private long mesoCost;
    private Item? limitBreakItemUpgrade;
    private bool upgraded;

    public ItemEnchantManager(GameSession session) {
        this.session = session;

        catalysts = [];
        fodders = new Dictionary<long, Item>();
        attributeDeltas = new Dictionary<BasicAttribute, BasicOption>();
        rates = new EnchantRates();
    }

    public void Reset() {
        Type = EnchantType.None;
        upgradeItem = null;
        catalysts.Clear();
        fodders.Clear();
        attributeDeltas.Clear();
        rates.Clear();
        fodderWeight = 0;
        useCharges = 0;
        enchantResult = EnchantResult.None;
        mesoCost = 0;
        limitBreakItemUpgrade = null;
        upgraded = false;
    }

    public bool StageItem(EnchantType enchantType, long itemUid) {
        if (enchantType is not (EnchantType.Ophelia or EnchantType.Peachy)) {
            return false;
        }

        Item? item = session.Item.GetGear(itemUid);
        // The client's own test (itemenchantlimittable.cpp:185): an item at the last grade or limit broken
        // is not staged. Without it the grade passed the last row of every table.
        if (item == null || !ItemEnchantRules.CanEnchant(item.Metadata.Limit.EnableEnchant, item.Enchant?.Enchants ?? 0, item.LimitBreak?.Level ?? 0)) {
            session.Send(ItemEnchantPacket.Error(ItemEnchantError.s_itemenchant_invalid_item));
            NpcTalkEvent(ScriptEventType.EnchantFail, ItemEnchantError.s_itemenchant_invalid_item);
            return false;
        }

        Reset();
        lastTry = null;
        Type = enchantType;
        upgradeItem = item;
        retryType = enchantType;
        retryUid = itemUid;
        int minFodder = Prepare();
        // KMS2 order: the staged item, then the smith's line.
        session.Send(ItemEnchantPacket.StageItem(Type, upgradeItem, catalysts, attributeDeltas, rates, minFodder));
        NpcTalkEvent(ScriptEventType.EnchantSelect);
        return true;
    }

    // The staged item's cost, rate and options for its next grade, with no fodder and no charges chosen.
    // Returns the fodder the grade needs.
    private int Prepare() {
        catalysts.Clear();
        fodders.Clear();
        attributeDeltas.Clear();
        rates.Clear();
        fodderWeight = 0;
        useCharges = 0;
        if (upgradeItem == null) {
            return 0;
        }

        // Ensure this item has an Enchant field set.
        upgradeItem.Enchant ??= new ItemEnchant();
        int enchants = Math.Clamp(upgradeItem.Enchant.Enchants, 0, 14);

        foreach (IngredientInfo ingredient in GetRequiredCatalysts()) {
            catalysts.Add(ingredient);
        }
        foreach ((BasicAttribute attribute, BasicOption option) in GetEnchant(session, upgradeItem).BasicOptions) {
            attributeDeltas[attribute] = option;
        }

        switch (Type) {
            case EnchantType.Ophelia:
                rates.Success = SuccessRate[enchants];
                break;
            case EnchantType.Peachy:
                rates.Success = MaxRate;
                break;
        }
        return RequireFodder[enchants];
    }

    public bool UpdateFodder(long itemUid, bool add) {
        if (upgradeItem == null) {
            return false;
        }

        int enchants = Math.Clamp(upgradeItem.Enchant?.Enchants ?? 0, 0, 14);
        if (FodderRate[enchants] <= 0) {
            return false; // If adding fodder does not improve rate, restrict it.
        }

        if (add) {
            // Prevent adding more fodder if it won't help.
            if (Type is EnchantType.Ophelia && rates.Total >= MaxRate) {
                NpcTalkEvent(ScriptEventType.EnchantFail, ItemEnchantError.max_fodder);
                return false;
            }

            // Can't go over 30% rate with fodders
            if (Type is EnchantType.Ophelia && fodders.Count >= RequireFodder[enchants] && rates.Success + rates.Fodder >= MaxFodderRate) {
                NpcTalkEvent(ScriptEventType.EnchantFail, ItemEnchantError.max_fodder);
                return false;
            }

            // Cannot add the same fodder twice.
            if (fodders.ContainsKey(itemUid)) {
                return false;
            }

            Item? item = session.Item.Inventory.Get(itemUid);
            if (item == null) {
                session.Send(ItemEnchantPacket.Error(ItemEnchantError.s_itemenchant_lack_ingredient));
                NpcTalkEvent(ScriptEventType.EnchantFail, ItemEnchantError.s_itemenchant_lack_ingredient);
                return false;
            }
            if (!IsValidFodder(upgradeItem, item)) {
                logger.Debug("Can't add fodder, invalid item: {Id}", item.Id);
                return false;
            }

            fodders.Add(itemUid, item);
            fodderWeight += GetFodderWeight(upgradeItem, item);
        } else {
            if (!fodders.Remove(itemUid, out Item? removed)) {
                return false;
            }

            fodderWeight -= GetFodderWeight(upgradeItem, removed);
        }

        switch (Type) {
            case EnchantType.Ophelia:
                int extra = fodderWeight - RequireFodder[enchants];
                int maxFodder = Math.Max(0, MaxFodderRate - rates.Success);
                rates.Fodder = Math.Clamp(extra * FodderRate[enchants], 0, maxFodder);

                // Recompute charges in case we are over max.
                SetCharges(useCharges);
                session.Send(ItemEnchantPacket.UpdateCharges(fodders.Keys, useCharges, fodderWeight, rates));
                break;
            case EnchantType.Peachy:
                session.Send(ItemEnchantPacket.UpdateFodder(fodders.Keys, fodderWeight));
                break;
        }
        return true;
    }

    // GM debug buttons: the item's stored fail charges are filled or emptied.
    public bool DebugFailCharges(long itemUid, bool fill) {
        // The enchant window also lists worn gear, so look in the bag and in the equips.
        Item? item = session.Item.GetGear(itemUid);
        if (item == null) {
            return false;
        }

        item.Enchant ??= new ItemEnchant();
        item.Enchant.Charges = Util.DebugButtonRules.FailCharges(fill, MaxRate, ChargeRate);
        session.Send(ItemInventoryPacket.UpdateItem(item));
        if (upgradeItem?.Uid == itemUid && useCharges > item.Enchant.Charges) {
            SetCharges(item.Enchant.Charges);
            session.Send(ItemEnchantPacket.UpdateCharges(fodders.Keys, useCharges, fodderWeight, rates));
        }
        return true;
    }

    public bool UpdateCharges(int count) {
        if (upgradeItem == null || count < 0 || count > upgradeItem.Enchant?.Charges) {
            return false;
        }

        SetCharges(count);
        session.Send(ItemEnchantPacket.UpdateCharges(fodders.Keys, useCharges, fodderWeight, rates));
        return true;
    }

    // GM debug button (RequestItemEnchant 15, no body): the staged enchant succeeds at once, nothing is consumed.
    public bool DebugForceEnchant() {
        if (upgradeItem == null || !Util.DebugButtonRules.ForceEnchantAllowed(upgradeItem.Enchant?.Enchants ?? 0)) {
            return false;
        }

        return Enchant(upgradeItem.Uid, force: true);
    }

    // GM debug buttons (RequestItemEnchant 16): the ingredients of the staged enchant are put in the bag.
    public bool DebugFillIngredients(long itemUid, int requested) {
        if (upgradeItem == null || upgradeItem.Uid != itemUid) {
            return false;
        }

        foreach (IngredientInfo ingredient in catalysts) {
            int held = session.Item.Inventory.Filter(item => item.Metadata.Property.Tag == ingredient.Tag).Sum(item => item.Amount);
            // The enchant cost names a tag, not an item: take the first material item that carries the tag.
            ItemMetadata? material = null;
            for (int id = Util.DebugButtonRules.MaterialIdFirst; id <= Util.DebugButtonRules.MaterialIdLast && material == null; id++) {
                if (session.ItemMetadata.TryGet(id, out ItemMetadata? metadata) && metadata.Property.Tag == ingredient.Tag) {
                    material = metadata;
                }
            }
            if (material == null) {
                logger.Debug("Enchant debug fill: no material item with tag {Tag}", ingredient.Tag);
                continue;
            }

            // The amount comes from the packet: no more than a few full stacks of the material.
            int amount = Util.DebugButtonRules.FillAmount(requested, ingredient.Amount, held, material.Property.SlotMax);
            if (amount <= 0) {
                continue;
            }

            Item? give = session.Field?.ItemDrop.CreateItem(material.Id, amount: amount);
            if (give == null) {
                continue;
            }

            session.Item.Inventory.Add(give, true);
        }
        return true;
    }

    public bool Enchant(long itemUid, bool force = false) {
        Item? item = session.Item.GetGear(itemUid);
        if (item == null || upgradeItem == null || upgradeItem.Uid != item.Uid) {
            return false;
        }

        upgradeItem.Enchant ??= new ItemEnchant();
        // The staged item may have reached the last grade by the enchant before this one.
        if (!ItemEnchantRules.CanEnchant(upgradeItem.Metadata.Limit.EnableEnchant, upgradeItem.Enchant.Enchants, upgradeItem.LimitBreak?.Level ?? 0)) {
            session.Send(ItemEnchantPacket.Error(ItemEnchantError.s_itemenchant_invalid_item));
            NpcTalkEvent(ScriptEventType.EnchantFail, ItemEnchantError.s_itemenchant_invalid_item);
            return false;
        }
        int enchants = Math.Clamp(upgradeItem.Enchant.Enchants, 0, 14);
        if (!force && fodderWeight < RequireFodder[enchants]) {
            // The client shows a line for error 3 (OnItemEnchant mode 12, scenestategame.cpp:6196); the
            // smith's line alone needs an open talk script.
            session.Send(ItemEnchantPacket.Error(ItemEnchantError.s_itemenchant_lack_ingredient));
            NpcTalkEvent(ScriptEventType.EnchantFail, ItemEnchantError.not_enough_fodder);
            return false;
        }

        if (!force && !ConsumeMaterial()) {
            session.Send(ItemEnchantPacket.Error(ItemEnchantError.s_itemenchant_lack_ingredient));
            NpcTalkEvent(ScriptEventType.EnchantFail, ItemEnchantError.s_itemenchant_lack_ingredient);
            return false;
        }

        void ApplyEnchantStats() {
            // GetBasicOptions() again to ensure rates match those in table.
            // This *MUST* be called before incrementing Enchants.
            foreach ((BasicAttribute attribute, BasicOption option) in GetEnchant(session, upgradeItem).BasicOptions) {
                if (upgradeItem.Enchant.BasicOptions.TryGetValue(attribute, out BasicOption existing)) {
                    upgradeItem.Enchant.BasicOptions[attribute] = option + existing;
                } else {
                    upgradeItem.Enchant.BasicOptions[attribute] = option;
                }
            }
            upgradeItem.Enchant.Enchants++;
        }

        switch (Type) {
            case EnchantType.Ophelia:
                float roll = Random.Shared.NextSingle() * MaxRate;
                int totalRate = rates.Total;
                enchantResult = force || roll < totalRate ? EnchantResult.Success : EnchantResult.Fail;
                logger.Debug("Enchant result: {Roll} / {Total} = {Result}", roll, totalRate, enchantResult.ToString());

                if (enchantResult == EnchantResult.Success) {
                    ApplyEnchantStats();

                    // KMS2 order: the result, then the quest and trophy updates.
                    session.Send(ItemEnchantPacket.Success(upgradeItem, attributeDeltas));
                    session.ConditionUpdate(ConditionType.enchant_result, codeLong: (int) EnchantResult.Success, targetLong: upgradeItem.Enchant.Enchants);
                    ChatUtil.SendEnchantAnnouncement(session, upgradeItem);
                } else {
                    upgradeItem.Enchant.Charges += FailCharge[enchants];
                    session.Send(ItemEnchantPacket.Failure(upgradeItem, FailCharge[enchants]));
                    session.ConditionUpdate(ConditionType.enchant_result, codeLong: (int) EnchantResult.Fail, targetLong: upgradeItem.Enchant.Enchants);
                }

                lastTry = (enchantResult, upgradeItem.Enchant.Enchants, (short) upgradeItem.Rarity);

                // What was staged is spent: a request that follows without a new stage pays the cost of the
                // item's grade as it is now, at that grade's rate, with no fodder and no charges. The last grade
                // has no next cost (the tables end with it): nothing stays staged.
                if (ItemEnchantRules.CanEnchant(upgradeItem.Metadata.Limit.EnableEnchant, upgradeItem.Enchant.Enchants, upgradeItem.LimitBreak?.Level ?? 0)) {
                    Prepare();
                } else {
                    Reset();
                }
                return true;
            case EnchantType.Peachy:
                upgradeItem.Enchant.EnchantExp += force ? MaxExp : GainExp[enchants];
                if (upgradeItem.Enchant.EnchantExp >= MaxExp) {
                    upgradeItem.Enchant.EnchantExp = 0;
                    ApplyEnchantStats();

                    session.Send(ItemEnchantPacket.Success(upgradeItem, attributeDeltas));
                    session.Send(ItemEnchantPacket.UpdateExp(itemUid, 0));
                    session.ConditionUpdate(ConditionType.enchant_result, codeLong: (int) EnchantResult.Success, targetLong: upgradeItem.Enchant.Enchants);
                    ChatUtil.SendEnchantAnnouncement(session, upgradeItem);
                    Reset();
                } else {
                    // Just clear the fodder as the same details are reused.
                    fodders.Clear();
                    fodderWeight = 0;

                    session.Send(ItemEnchantPacket.UpdateExp(itemUid, upgradeItem.Enchant.EnchantExp));
                }

                return true;
            default:
                return false;
        }
    }

    // session.Item must be locked before calling.
    // Consumes materials and charges for this upgrade.
    private bool ConsumeMaterial() {
        if (upgradeItem?.Enchant == null) {
            return false;
        }

        lock (session.Item) {
            // Validate charges, fodder, catalyst
            if (upgradeItem.Enchant.Charges < useCharges) {
                return false;
            }
            foreach ((long fodderUid, _) in fodders) {
                Item? item = session.Item.Inventory.Get(fodderUid);
                if (item == null) {
                    session.Send(ItemEnchantPacket.Error(ItemEnchantError.s_itemenchant_lack_ingredient));
                    return false;
                }
            }

            // Missing Crystal Fragments are paid in mesos, as the client's ingredient list shows (s_itemenchant_fee).
            long fee = 0;
            List<IngredientInfo> consume = new(catalysts.Count);
            foreach (IngredientInfo catalyst in catalysts) {
                int amount = catalyst.Amount;
                if (catalyst.Tag == ItemTag.CrystalPiece) {
                    int held = session.Item.Inventory.Find(ItemTag.CrystalPiece).Sum(item => item.Amount);
                    fee = ItemEnchantRules.CrystalFragmentShortfallFee(amount, held);
                    if (fee > 0) {
                        amount = held;
                    }
                }
                if (amount > 0) {
                    consume.Add(new IngredientInfo(catalyst.Tag, amount));
                }
            }
            if (fee > 0 && (!LimitBreakRules.CanPay(session.Currency.Meso, fee) || session.Currency.CanAddMeso(-fee) != -fee)) {
                session.Send(ItemEnchantPacket.Error(ItemEnchantError.s_itemenchant_lack_ingredient));
                return false;
            }

            // Note: All validation should be above this point.
            if (!session.Item.Inventory.Consume(consume)) {
                session.Send(ItemEnchantPacket.Error(ItemEnchantError.s_itemenchant_lack_ingredient));
                return false;
            }
            if (fee > 0) {
                session.Currency.Meso -= fee;
            }

            foreach ((long fodderUid, _) in fodders) {
                if (!session.Item.Inventory.Consume(fodderUid, 1)) {
                    logger.Fatal("Failed to consume item {ItemUid}", fodderUid);
                    throw new InvalidOperationException($"Fatal: Consuming item: {fodderUid}");
                }
            }

            upgradeItem.Enchant.Charges -= useCharges;

            return true;
        }
    }

    private IEnumerable<IngredientInfo> GetRequiredCatalysts() {
        if (upgradeItem == null) {
            yield break;
        }

        int enchants = upgradeItem.Enchant?.Enchants ?? 0;
        IEnumerable<IngredientInfo> costs = Type == EnchantType.Peachy ? PeachyCost[enchants] : Core.Formulas.Enchant.GetEnchantCost(upgradeItem);
        foreach (IngredientInfo cost in costs) {
            yield return cost;
        }
    }

    public static ItemEnchant GetEnchant(GameSession session, Item upgradeItem, int target = -1) {
        return GetEnchant(session.ServerTableMetadata.EnchantOptionTable, upgradeItem, target);
    }

    public static ItemEnchant GetEnchant(EnchantOptionTable table, Item upgradeItem, int target = -1) {
        int enchantLevel = Math.Clamp(target >= 0 ? target : (upgradeItem.Enchant?.Enchants ?? 0) + 1, 1, 15);
        var itemEnchant = new ItemEnchant();
        EnchantOptionMetadata? entry = table.Entries.Values.FirstOrDefault(
            e => e.Slot == upgradeItem.Type.Type &&
                 e.EnchantLevel == enchantLevel &&
                 e.Rarity == upgradeItem.Rarity &&
                 e.MinLevel <= upgradeItem.Metadata.Limit.Level &&
                 e.MaxLevel >= upgradeItem.Metadata.Limit.Level);
        if (entry == null) {
            return itemEnchant;
        }
        foreach (BasicAttribute attribute in entry.Attributes) {
            // The client reads an enchant option as rate * 1000 (BaseOption::GetRate).
            itemEnchant.BasicOptions[attribute] = new BasicOption(entry.Rate);
        }
        itemEnchant.Enchants = enchantLevel;
        return itemEnchant;
    }

    // Prevent user from using more charges than needed
    private void SetCharges(int count) {
        // Charges are only relevant to Ophelia.
        if (Type != EnchantType.Ophelia) {
            useCharges = 0;
            return;
        }

        int rateWithoutCharges = Math.Clamp(rates.Total - rates.Charge, 0, MaxRate);
        int maxCharges = (int) Math.Ceiling((MaxRate - rateWithoutCharges) / (float) ChargeRate);
        useCharges = Math.Clamp(count, 0, maxCharges);
        rates.Charge = Math.Clamp(useCharges * ChargeRate, 0, MaxRate);
    }

    private static int GetFodderWeight(Item item, Item fodder) {
        if (!item.Type.IsDagger && !item.Type.IsThrowingStar) {
            return 1;
        }

        // For Dagger/ThrowingStar, Toad's Toolkit is valued as 2 fodder.
        return IsValidToolkit(item, fodder) ? 2 : 1;
    }

    private static bool IsValidFodder(Item item, Item fodder) {
        return fodder.Id == item.Id && fodder.Rarity == item.Rarity || IsValidToolkit(item, fodder);
    }

    private static bool IsValidToolkit(Item item, Item fodder) {
        return fodder.Metadata.Property.Tag switch {
            ItemTag.EnchantJockerItemNormal => item.Rarity == 1,
            ItemTag.EnchantJockerItemRare => item.Rarity == 2,
            ItemTag.EnchantJockerItemElite => item.Rarity == 3,
            ItemTag.EnchantJockerItemExcellent => item.Rarity == 4,
            ItemTag.EnchantJockerItemLegendary => item.Rarity == 5,
            ItemTag.EnchantJockerItemEpic => item.Rarity == 6,
            _ => false,
        };
    }

    public void NpcTalkEvent(ScriptEventType scriptEventType, ItemEnchantError error = ItemEnchantError.s_itemenchant_unknown_err) {
        if (scriptEventType == ScriptEventType.EnchantComplete && lastTry is { } done) {
            session.NpcScript?.EnchantResultScript(scriptEventType, done.Result, error, done.Level, done.Rarity);
            lastTry = null;
            Reset();
            return;
        }
        session.NpcScript?.EnchantResultScript(scriptEventType, enchantResult, error, upgradeItem?.Enchant?.Enchants ?? 0, (short) (upgradeItem?.Rarity ?? 0));
        if (scriptEventType == ScriptEventType.EnchantComplete) {
            Reset();
        }
    }

    // The dialog's retry button (UIItemEnchantDialog::OnRetry, uiitemenchantdialog.cpp:781) clears its slots
    // and sends talk event 4. KMS2 stages the same item again: the client resets its cost, rate and the listed
    // copies on the stage packet only (OnEnchantInfoPacket), not on a result.
    public bool Retry() {
        if (retryUid == 0) {
            return false;
        }
        Item? item = session.Item.GetGear(retryUid);
        if (item == null || !ItemEnchantRules.CanEnchant(item.Metadata.Limit.EnableEnchant, item.Enchant?.Enchants ?? 0, item.LimitBreak?.Level ?? 0)) {
            return false;
        }
        return StageItem(retryType, retryUid);
    }

    #region Limit Break
    public bool StageLimitBreakItem(long itemUid) {
        // Must happen due to this function triggering automatically upon upgrading. Without it, the animation does not play.
        if (upgraded) {
            upgraded = false;
            return false;
        }
        Item? item = session.Item.GetGear(itemUid);
        // A new item has neither record yet: it counts as level 0 and enchant 0.
        if (item == null || !LimitBreakRules.IsItem(item.Metadata.Property.LimitBreakMaxLevel,
                item.LimitBreak?.Level ?? 0, item.Enchant?.Enchants ?? 0)) {
            session.Send(LimitBreakPacket.Error(LimitBreakError.s_unlimited_enchant_err_invalid_item));
            return false;
        }

        item.LimitBreak ??= new ItemLimitBreak();
        if (LimitBreakRules.IsMaxLevel(item.Metadata.Property.LimitBreakMaxLevel, item.LimitBreak.Level)) {
            session.Send(LimitBreakPacket.Error(LimitBreakError.s_unlimited_enchant_err_max_unlimited_grade));
            return false;
        }

        Reset();
        upgradeItem = item;

        foreach (IngredientInfo ingredient in Core.Formulas.LimitBreak.GetCatalysts(item.LimitBreak.Level)) {
            catalysts.Add(ingredient);
        }

        mesoCost = Core.Formulas.LimitBreak.MesoCost(item.LimitBreak.Level);

        limitBreakItemUpgrade = item.Clone();

        if (!session.ServerTableMetadata.UnlimitedEnchantOptionTable.Entries.TryGetValue(limitBreakItemUpgrade.Type.Type, out Dictionary<int, UnlimitedEnchantOptionTable.Option>? levelDictionary) ||
            !levelDictionary.TryGetValue(limitBreakItemUpgrade.LimitBreak!.Level + 1, out UnlimitedEnchantOptionTable.Option? optionMetadata)) {
            session.Send(LimitBreakPacket.Error(LimitBreakError.s_unlimited_enchant_err_invalid_item));
            return false;
        }

        foreach ((BasicAttribute attribute, int value) in optionMetadata.Values) {
            if (limitBreakItemUpgrade.LimitBreak.BasicOptions.TryGetValue(attribute, out BasicOption existing)) {
                limitBreakItemUpgrade.LimitBreak.BasicOptions[attribute] = existing + new BasicOption(value);
            } else {
                limitBreakItemUpgrade.LimitBreak.BasicOptions[attribute] = new BasicOption(value);
            }
        }
        foreach ((BasicAttribute attribute, float rate) in optionMetadata.Rates) {
            if (limitBreakItemUpgrade.LimitBreak.BasicOptions.TryGetValue(attribute, out BasicOption existing)) {
                limitBreakItemUpgrade.LimitBreak.BasicOptions[attribute] = existing + new BasicOption(rate);
            } else {
                limitBreakItemUpgrade.LimitBreak.BasicOptions[attribute] = new BasicOption(rate);
            }
        }
        foreach ((SpecialAttribute attribute, int value) in optionMetadata.SpecialValues) {
            if (limitBreakItemUpgrade.LimitBreak.SpecialOptions.TryGetValue(attribute, out SpecialOption existing)) {
                limitBreakItemUpgrade.LimitBreak.SpecialOptions[attribute] = existing + new SpecialOption(value);
            } else {
                limitBreakItemUpgrade.LimitBreak.SpecialOptions[attribute] = new SpecialOption(value);
            }
        }
        foreach ((SpecialAttribute attribute, float rate) in optionMetadata.SpecialRates) {
            if (limitBreakItemUpgrade.LimitBreak.SpecialOptions.TryGetValue(attribute, out SpecialOption existing)) {
                limitBreakItemUpgrade.LimitBreak.SpecialOptions[attribute] = existing + new SpecialOption(rate);
            } else {
                limitBreakItemUpgrade.LimitBreak.SpecialOptions[attribute] = new SpecialOption(rate);
            }
        }

        limitBreakItemUpgrade.LimitBreak.Level++;
        if (limitBreakItemUpgrade.Enchant != null) {
            limitBreakItemUpgrade.Enchant.Enchants = 0;
        }
        // The newest KMS2 build has no meso field: the fee is a fifth ingredient, crystal fragments (the fee in
        // thousands), and the client shows 1000 meso for each fragment the player lacks.
        session.Send(LimitBreakPacket.StageItem(item, limitBreakItemUpgrade,
            [.. catalysts, new IngredientInfo(ItemTag.CrystalPiece, LimitBreakRules.Fragments(mesoCost))]));

        return true;
    }

    public bool LimitBreakEnchant(long itemUid) {
        Item? item = session.Item.GetGear(itemUid);
        if (item == null || upgradeItem == null || limitBreakItemUpgrade?.LimitBreak == null ||
            !LimitBreakRules.IsItem(item.Metadata.Property.LimitBreakMaxLevel,
                item.LimitBreak?.Level ?? 0, item.Enchant?.Enchants ?? 0) ||
            !LimitBreakRules.IsStaged(upgradeItem.Uid, limitBreakItemUpgrade.LimitBreak.Level, item.Uid, item.LimitBreak?.Level ?? 0)) {
            session.Send(LimitBreakPacket.Error(LimitBreakError.s_unlimited_enchant_err_invalid_item));
            return false;
        }

        item.LimitBreak ??= new ItemLimitBreak();
        if (LimitBreakRules.IsMaxLevel(item.Metadata.Property.LimitBreakMaxLevel, item.LimitBreak.Level)) {
            session.Send(LimitBreakPacket.Error(LimitBreakError.s_unlimited_enchant_err_max_unlimited_grade));
            return false;
        }

        if (!ConsumeLimitBreakMaterial()) {
            return false;
        }

        upgradeItem.LimitBreak = limitBreakItemUpgrade.LimitBreak.Clone();
        if (upgradeItem.Enchant != null) {
            upgradeItem.Enchant.Enchants = 0;
        }
        session.Send(LimitBreakPacket.LimitBreak(upgradeItem));
        if (upgradeItem.LimitBreak.Level == upgradeItem.Metadata.Property.LimitBreakMaxLevel) {
            session.Send(LimitBreakPacket.Error(LimitBreakError.s_unlimited_enchant_err_max_unlimited_grade));
        }
        session.ConditionUpdate(ConditionType.unlimited_enchant);
        upgraded = true;
        return true;
    }

    private bool ConsumeLimitBreakMaterial() {
        if (upgradeItem?.LimitBreak == null) {
            return false;
        }

        lock (session.Item) {
            // The fee is paid in the crystal fragments the player owns, and in meso for the rest (the client's
            // fee text). Nothing is taken unless everything can be; KMS2 takes the meso before the materials.
            long owned = session.Item.Inventory.Find(ItemTag.CrystalPiece).Sum(item => (long) item.Amount);
            (int fragments, long meso) = LimitBreakRules.Fee(mesoCost, owned);
            if (!LimitBreakRules.CanPay(session.Currency.Meso, meso) || session.Currency.CanAddMeso(-meso) != -meso) {
                session.Send(LimitBreakPacket.Error(LimitBreakError.s_unlimited_enchant_err_lack_meso));
                return false;
            }
            foreach (IngredientInfo catalyst in catalysts) {
                if (session.Item.Inventory.Find(catalyst.Tag).Sum(item => (long) item.Amount) < catalyst.Amount) {
                    session.Send(LimitBreakPacket.Error(LimitBreakError.s_unlimited_enchant_err_lack_ingredient));
                    return false;
                }
            }

            if (meso > 0) {
                session.Currency.Meso -= meso;
            }
            List<IngredientInfo> materials = [.. catalysts];
            if (fragments > 0) {
                materials.Add(new IngredientInfo(ItemTag.CrystalPiece, fragments));
            }
            if (!session.Item.Inventory.Consume(materials)) {
                throw new InvalidOperationException("Failed to consume limit break materials after validating");
            }
            return true;
        }
    }
    #endregion
}
