using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.LuaFunctions;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class ItemDismantleHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.ItemDismantle;

    private enum Command : byte {
        Interface = 0,
        Stage = 1,
        Remove = 2,
        Confirm = 3,
        AutoAdd = 6,
    }

    private const int ONYX_ID = 40100023;
    private const int CHAOS_ONYX_ID = 40100024;

    // The item a tag pays out as, from the tag rows of the client's table/itemdata.xml (CItemDataTable::LoadTagItemIds).
    private static readonly Dictionary<string, int> TagItemIds = new() {
        ["SkinCrystal"] = 40100027,
        ["CrystalPiece"] = 40100001,
    };

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required ItemMetadataStorage ItemMetadata { private get; init; }
    public required TableMetadataStorage TableMetadata { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Interface:
                HandleInterface(session, packet);
                return;
            case Command.Stage:
                HandleStage(session, packet);
                return;
            case Command.Remove:
                HandleRemove(session, packet);
                return;
            case Command.Confirm:
                HandleConfirm(session);
                return;
            case Command.AutoAdd:
                HandleAutoAdd(session, packet);
                return;
        }
    }

    private void HandleInterface(GameSession session, IByteReader packet) {
        session.DismantleType = packet.ReadByte();
        session.DismantleOpened = session.DismantleType != 0;
        Array.Clear(session.DismantleStaging);
    }

    private void HandleStage(GameSession session, IByteReader packet) {
        if (!session.DismantleOpened) {
            return;
        }

        packet.ReadInt(); // -1
        long itemUid = packet.ReadLong();
        int amount = packet.ReadInt();
        Item? item = session.Item.Inventory.Get(itemUid);
        if (item == null || !DismantleRules.Breakable(item.Metadata.Limit.EnableBreak, item.Transfer?.DisableBreak == true, item.GachaDismantleId)) {
            return;
        }
        if (!DismantleRules.CanStage(amount, item.Amount, item.IsLocked, itemUid, session.Pet?.Pet.Uid ?? 0)) {
            return;
        }

        for (short slot = 0; slot < session.DismantleStaging.Length; slot++) {
            if (session.DismantleStaging[slot] != default) {
                if (session.DismantleStaging[slot].Uid == itemUid) {
                    return;
                }
                continue;
            }

            session.DismantleStaging[slot] = (item.Uid, amount);
            session.Send(ItemDismantlePacket.Stage(itemUid, slot, amount));
            break;
        }

        session.Send(ItemDismantlePacket.Preview(GetRewards(session)));
    }

    private void HandleRemove(GameSession session, IByteReader packet) {
        if (!session.DismantleOpened) {
            return;
        }

        long itemUid = packet.ReadLong();
        for (short slot = 0; slot < session.DismantleStaging.Length; slot++) {
            if (session.DismantleStaging[slot].Uid != itemUid) {
                continue;
            }

            session.DismantleStaging[slot] = default;
            session.Send(ItemDismantlePacket.Remove(itemUid));
            break;
        }

        session.Send(ItemDismantlePacket.Preview(GetRewards(session)));
    }

    private void HandleConfirm(GameSession session) {
        if (!session.DismantleOpened) {
            return;
        }

        lock (session.Item) {
            // The item can have changed since it was staged (locked, split, its pet summoned): drop those slots.
            for (short slot = 0; slot < session.DismantleStaging.Length; slot++) {
                (long uid, int amount) = session.DismantleStaging[slot];
                if (uid == 0) {
                    continue;
                }
                Item? item = session.Item.Inventory.Get(uid);
                if (item == null || !DismantleRules.CanStage(amount, item.Amount, item.IsLocked, uid, session.Pet?.Pet.Uid ?? 0)) {
                    session.DismantleStaging[slot] = default;
                }
            }

            // Nothing staged, or nothing left of it: the client's result 5 says so and resets the dialog's
            // pending break.
            if (Array.TrueForAll(session.DismantleStaging, staged => staged == default)) {
                session.Send(ItemDismantlePacket.Error(5));
                return;
            }

            // Room for every reward at its max before anything is broken. The client's result 3 shows
            // s_err_inventory ("Your inventory is full.") and keeps the staged items.
            if (!CanHoldRewards(session, GetRewards(session))) {
                session.Send(ItemDismantlePacket.Error(3));
                return;
            }

            var rewards = new Dictionary<int, (int Min, int Max)>();
            var broken = new List<int>();
            for (short slot = 0; slot < session.DismantleStaging.Length; slot++) {
                (long uid, int amount) = session.DismantleStaging[slot];
                Item? item = session.Item.Inventory.Get(uid);
                if (item == null) {
                    continue;
                }

                // If failed to consume, remove slot.
                if (!session.Item.Inventory.Consume(uid, amount)) {
                    session.DismantleStaging[slot] = default;
                    continue;
                }
                broken.Add(amount);

                foreach ((int id, int min, int max) in GetReward(item, amount)) {
                    (int Min, int Max) prev = rewards.GetValueOrDefault(id, (0, 0));
                    rewards[id] = (prev.Min + min, prev.Max + max);
                }
            }

            Array.Clear(session.DismantleStaging);

            var result = new Dictionary<int, int>();
            foreach ((int id, (int min, int max)) in rewards) {
                int total = DismantleRules.Roll(min, max, Random.Shared);
                result[id] = total;
                // An unknown reward id gives nothing (the loop used to spin on it forever).
                if (!ItemMetadata.TryGet(id, out ItemMetadata? rewardMetadata)) {
                    continue;
                }
                // One item per stack: the whole remainder was given on every pass, so a reward above
                // the stack size was paid more than once.
                foreach (int stack in DismantleRules.Stacks(total, rewardMetadata.Property.SlotMax)) {
                    Item? item = session.Field?.ItemDrop.CreateItem(id, amount: stack);
                    if (item == null) {
                        break;
                    }
                    // The room check counts each stack on its own, so a stack it misjudged goes by mail.
                    if (!session.Item.Inventory.Add(item, true)) {
                        session.Item.MailItem(item);
                    }
                }
            }

            session.Send(ItemDismantlePacket.Result(session.DismantleType, result));
            // Each item broken counts for item_break (74000011 "Dismantle 1 item", 10002700 dismantles 10).
            // KMS2 order: the items taken, the rewards, the result, then the trophy updates.
            foreach (int amount in broken) {
                session.ConditionUpdate(ConditionType.item_break, counter: amount);
            }
        }
    }

    private void HandleAutoAdd(GameSession session, IByteReader packet) {
        if (!session.DismantleOpened) {
            return;
        }

        // CItemBreakManager::SendItemDismantleMode6(type, rank): the type is the "type" member of the
        // dialog's m_bundleTypeDropdown row, an inventory tab (UIItemBreakDialog::OnLoadComplete keys
        // its row map by it). The dialog offers only equip, skin, gem and pet; rank is 1..6, "or below".
        byte tab = packet.ReadByte();
        byte rarity = packet.ReadByte();
        if (!InventoryTab.TryFromClient(tab, out InventoryType type)
                || type is not (InventoryType.Gear or InventoryType.Outfit or InventoryType.Gemstone or InventoryType.Pets)) {
            return;
        }

        IList<Item> items = session.Item.Inventory.Filter(item => item.Rarity <= rarity, type);
        var added = new List<(short Slot, long Uid, int Amount)>();
        for (short slot = 0, i = 0; slot < session.DismantleStaging.Length && i < items.Count; slot++) {
            if (session.DismantleStaging[slot] != default) {
                continue;
            }

            while (i < items.Count) {
                Item item = items[i++];
                if (session.DismantleStaging.Any(existing => existing.Uid == item.Uid)) {
                    continue;
                }

                // The same rule as Stage, for every type (pets had none before).
                if (item.IsLocked || !DismantleRules.Breakable(item.Metadata.Limit.EnableBreak, item.Transfer?.DisableBreak == true, item.GachaDismantleId)) {
                    continue;
                }

                switch (type) {
                    case InventoryType.Gear or InventoryType.Gemstone:
                        if (!item.Metadata.Limit.EnableBreak || item.Transfer?.DisableBreak == true) {
                            continue;
                        }
                        break;
                    case InventoryType.Outfit:
                        if (item.GachaDismantleId == 0) {
                            continue;
                        }
                        break;
                }

                session.DismantleStaging[slot] = (item.Uid, item.Amount);
                added.Add((slot, item.Uid, item.Amount));
                break;
            }
        }

        // One list packet (client mode 6) for the whole auto-add, as the client's reader for this request expects.
        session.Send(ItemDismantlePacket.StageList(added));
        session.Send(ItemDismantlePacket.Preview(GetRewards(session)));
    }

    private bool CanHoldRewards(GameSession session, IDictionary<int, (int Min, int Max)> rewards) {
        List<(int Id, int Amount)> stacks = DismantleRules.MaxRewardStacks(rewards.AsReadOnly(),
            id => ItemMetadata.TryGet(id, out ItemMetadata? metadata) ? metadata.Property.SlotMax : null);
        var items = new List<Item>();
        foreach ((int id, int amount) in stacks) {
            Item? item = session.Field?.ItemDrop.CreateItem(id, amount: amount);
            if (item == null) {
                continue;
            }
            // Currency, medals and furnishings go elsewhere (InventoryManager.Add) and take no slot.
            if (item.IsCurrency() || item.Type.IsMedal || item.Type.IsFurnishing) {
                continue;
            }
            items.Add(item);
        }
        return items.Count == 0 || session.Item.Inventory.CanAdd(items);
    }

    private IDictionary<int, (int Min, int Max)> GetRewards(GameSession session) {
        var rewards = new Dictionary<int, (int Min, int Max)>();
        foreach ((long uid, int amount) in session.DismantleStaging) {
            Item? item = session.Item.Inventory.Get(uid);
            if (item == null || item.Amount < amount) {
                continue;
            }

            foreach ((int id, int min, int max) in GetReward(item, amount)) {
                (int Min, int Max) prev = rewards.GetValueOrDefault(id, (0, 0));
                rewards[id] = (prev.Min + min, prev.Max + max);
            }
        }

        return rewards;
    }

    private IEnumerable<(int Id, int Min, int Max)> GetReward(Item item, int amount) {
        if (amount <= 0 || item.Amount < amount) {
            yield break;
        }

        if (TableMetadata.ItemBreakTable.Entries.TryGetValue(item.Id, out IReadOnlyList<ItemBreakTable.Ingredient>? ingredients)) {
            foreach (ItemBreakTable.Ingredient ingredient in ingredients) {
                yield return (ingredient.ItemId, ingredient.Amount * amount, ingredient.Amount * amount);
            }
        } else if (item.GachaDismantleId != 0 && TableMetadata.GachaInfoTable.Entries.TryGetValue(item.GachaDismantleId, out GachaInfoTable.Entry? gachaEntry)) {
            yield return (gachaEntry.CoinItemId, gachaEntry.CoinItemAmount, gachaEntry.CoinItemAmount);
        } else if (item.Metadata.Property.IsSkin) {
            // An outfit without a break table entry gives what the script's calcMeratSkinItemBreakReward gives
            // (the client lists it the same way); one with none cannot be staged in the client.
            (_, int count, int? rewardId, int? rewardAmount, _) = Lua.CalcMeratSkinItemBreakReward(
                (ushort) item.Metadata.Limit.Level, item.Rarity, 0, item.Metadata.Property.SkinType);
            if (count > 0 && rewardId is > 0 && rewardAmount is > 0) {
                yield return (rewardId.Value, rewardAmount.Value * amount, rewardAmount.Value * amount);
            }

            // Each opened socket gives back calcMeratSkinItemBreakRefundPercent of its unlock ingredient (the
            // client's ItemBreakCommon adds it to the reward list); the confirm sums it with a reward of the same item.
            int openSockets = item.Socket?.UnlockSlots ?? 0;
            if (openSockets > 0) {
                (_, string unlockTag, int unlockCount) = Lua.CalcItemSocketUnlockIngredient(0, item.Rarity,
                    (ushort) item.Metadata.Limit.Level, 0, item.Metadata.Property.SkinType);
                int refund = DismantleRules.SkinSocketRefund(unlockCount, (int) Lua.CalcMeratSkinItemBreakRefundPercent(), openSockets);
                if (refund > 0 && TagItemIds.TryGetValue(unlockTag, out int refundId)) {
                    yield return (refundId, refund * amount, refund * amount);
                }
            }
        } else {
            // Gear: onyx by the sell price, and the chaos onyx row, which KMS2 lists at 0 too
            // (DismantleRules.Onyx). The enchant level's part is not known.
            long[] prices = item.Metadata.Property.SellPrices;
            long price = prices.Length > 2 ? prices[2] : 0;
            bool oneHanded = item.Metadata.SlotNames.Contains(EquipSlot.OH);
            int onyx = DismantleRules.Onyx(price, item.Rarity, oneHanded) * amount;
            (int chaosMin, int chaosMax) = DismantleRules.ChaosOnyx(item.Rarity, oneHanded);
            yield return (ONYX_ID, onyx, onyx);
            yield return (CHAOS_ONYX_ID, chaosMin * amount, chaosMax * amount);
        }
    }
}
