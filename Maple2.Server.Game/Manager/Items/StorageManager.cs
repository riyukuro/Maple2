using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;
using Serilog;
using static Maple2.Model.Error.StorageInventoryError;

namespace Maple2.Server.Game.Manager.Items;

public sealed class StorageManager : IDisposable {
    private const int BATCH_SIZE = 10;

    private readonly GameSession session;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    private ConstantsTable Constants => session.ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    private readonly ItemCollection items;
    private long mesos;
    private short expand;

    public StorageManager(GameSession session) {
        this.session = session;

        using GameStorage.Request db = session.GameStorage.Context();
        (mesos, expand) = db.GetStorageInfo(session.AccountId);

        items = new ItemCollection((short) (Constant.BaseStorageCount + expand));

        foreach (Item item in db.GetStorage(session.AccountId)) {
            if (items.Add(item).Count == 0) {
                Log.Error("Failed to add storage item:{Uid}", item.Uid);
            }
        }
    }

    public void Dispose() {
        using GameStorage.Request db = session.GameStorage.Context();
        lock (session.Item) {
            db.SaveStorageInfo(session.AccountId, mesos, expand);
            db.SaveItems(session.AccountId, items.ToArray());

            session.Storage = null;
        }
    }

    public void Delete(long uid) {
        lock (session.Item) {
            Item? item = items.Get(uid);
            if (item == null) {
                return;
            }

            if (items.Remove(uid, out item)) {
                session.Item.Inventory.Discard(item);
                session.Send(StorageInventoryPacket.Remove(uid));
            }
        }
    }

    public void Load() {
        lock (session.Item) {
            session.Send(StorageInventoryPacket.Reset());
            session.Send(StorageInventoryPacket.SlotsExpanded(expand));
            session.Send(StorageInventoryPacket.UpdateMesos(mesos));
            session.Send(StorageInventoryPacket.SlotsUsed(items.Count));
            foreach (ImmutableList<Item> batch in items.Batch(BATCH_SIZE)) {
                session.Send(StorageInventoryPacket.Load(batch));
            }
        }
    }

    public void Deposit(long uid, short slot, int amount) {
        lock (session.Item) {
            Item? deposit = session.Item.Inventory.Get(uid);
            if (deposit == null || !StorageRules.IsCount(amount, deposit.Amount) || SurvivalItemRules.IsMatchOnly(deposit.Metadata)) {
                session.Send(StorageInventoryPacket.Error(s_item_err_invalid_count));
                return;
            }
            // The client keeps the pet that is out in the inventory and never asks.
            if (!StorageRules.CanDeposit(uid, session.Pet?.Pet.Uid ?? 0)) {
                return;
            }
            // Nor the score that is being played.
            if (StorageRules.IsPlayedScore(uid, session.Instrument?.Score?.Uid ?? 0)) {
                return;
            }

            // A moveDisable item carries the character that stored it: the client shows the name and lets only
            // that character take it out. It stacks only with what the same character stored.
            ItemBinding? storedBy = deposit.Metadata.Property.MoveDisable
                ? new ItemBinding(session.CharacterId, session.Player.Value.Character.Name)
                : null;
            if (items.OpenSlots == 0) {
                ItemBinding? held = deposit.Binding;
                deposit.Binding = storedBy;
                int remaining = items.GetStackResult(deposit, amount);
                deposit.Binding = held;
                if (amount == remaining) {
                    session.Send(StorageInventoryPacket.Error(s_item_err_store_full));
                    return;
                }

                // Stack what we can and ignore the rest.
                amount -= remaining;
            }

            if (!session.Item.Inventory.Remove(uid, out deposit, amount)) {
                return;
            }

            deposit.Slot = slot;
            deposit.Binding = storedBy;
            IList<(Item, int Added)> result = items.Add(deposit, true);

            foreach ((Item item, int _) in result) {
                session.Send(deposit.Uid == item.Uid
                    ? StorageInventoryPacket.Add(item)
                    : StorageInventoryPacket.Update(item.Uid, item.Amount));
            }
        }
    }

    public void Withdraw(long uid, short slot, int amount) {
        lock (session.Item) {
            Item? withdraw = items.Get(uid);
            if (withdraw == null || !StorageRules.IsCount(amount, withdraw.Amount)) {
                session.Send(StorageInventoryPacket.Error(s_item_err_invalid_count));
                return;
            }

            ItemBinding? bound = withdraw.Transfer?.Binding;
            if (bound != null && bound.CharacterId != session.CharacterId) {
                session.Send(StorageInventoryPacket.Error(s_item_err_binditem_store_out));
                return;
            }
            // A moveDisable item leaves with the character that stored it (the client tests the same before it
            // asks, store.cpp:633, with the same text).
            ItemBinding? storedBy = withdraw.Binding;
            if (!StorageRules.CanTakeOut(storedBy?.CharacterId ?? 0, session.CharacterId)) {
                session.Send(StorageInventoryPacket.Error(s_item_err_moveDisableitem_store_out));
                return;
            }

            // A failed add discards the item: with no room it must not leave the storage.
            if (!session.Item.Inventory.CanAdd(withdraw, amount)) {
                session.Send(ItemInventoryPacket.Error(Maple2.Model.Error.ItemInventoryError.s_err_inventory));
                return;
            }

            if (!RemoveInternal(uid, amount, out withdraw)) {
                return;
            }

            short storedSlot = withdraw.Slot;
            withdraw.Slot = slot;
            withdraw.Binding = null;
            if (!session.Item.Inventory.Add(withdraw, commit: true)) {
                // The bag did not take it (no field yet, or no such tab): it goes back into the storage.
                withdraw.Slot = storedSlot;
                withdraw.Binding = storedBy;
                foreach ((Item item, int _) in items.Add(withdraw, true)) {
                    session.Send(withdraw.Uid == item.Uid
                        ? StorageInventoryPacket.Add(item)
                        : StorageInventoryPacket.Update(item.Uid, item.Amount));
                }
            }
        }
    }

    public bool Move(long uid, short dstSlot) {
        lock (session.Item) {
            if (dstSlot < 0 || dstSlot >= items.Size) {
                return false;
            }

            if (items.Remove(uid, out Item? srcItem)) {
                short srcSlot = srcItem.Slot;
                if (items.RemoveSlot(dstSlot, out Item? removeDst)) {
                    items[srcSlot] = removeDst;
                }

                items[dstSlot] = srcItem;
                session.Send(StorageInventoryPacket.Move(removeDst?.Uid ?? 0, srcSlot, uid, dstSlot));
            }

            return true;
        }
    }

    // Deposit/Withdraw Mesos
    private bool CanDepositMeso(long amount) => 0 <= amount && amount <= StorageRules.MaxMesos - mesos;
    public void DepositMesos(long amount) {
        lock (session.Item) {
            if (!CanDepositMeso(amount)) {
                // More than the storage holds has its own text (s_store_err_deposit_max_money).
                session.Send(StorageInventoryPacket.Error(amount > 0
                    ? s_store_err_deposit_max_money
                    : s_store_err_deposit_invalid_money));
                return;
            }
            if (!StorageRules.IsRequestAmount(amount, Constants.StoreDepositMax)) {
                session.Send(StorageInventoryPacket.Error(s_store_err_deposit_invalid_money));
                return;
            }

            long negAmount = -amount;
            if (session.Currency.CanAddMeso(negAmount) != negAmount) {
                session.Send(StorageInventoryPacket.Error(s_store_err_deposit_invalid_money));
                return;
            }

            session.Currency.Meso -= amount;
            mesos += amount;
            session.Send(StorageInventoryPacket.UpdateMesos(mesos));
        }
        StoreMesoMove(amount);
    }

    private bool CanWithdrawMeso(long amount) => 0 <= amount && amount <= mesos;
    public void WithdrawMesos(long amount) {
        lock (session.Item) {
            if (!CanWithdrawMeso(amount) || !StorageRules.IsRequestAmount(amount, Constants.StoreWithdrawMax)) {
                session.Send(StorageInventoryPacket.Error(s_store_err_deposit_invalid_money));
                return;
            }

            if (session.Currency.CanAddMeso(amount) != amount) {
                session.Send(StorageInventoryPacket.Error(s_store_err_deposit_invalid_money));
                return;
            }

            mesos -= amount;
            session.Send(StorageInventoryPacket.UpdateMesos(mesos));
            session.Currency.Meso += amount;
        }
        StoreMesoMove(-amount);
    }

    // The storage's mesos are stored with the character's at each move, in one transaction (the storage used to be
    // stored only when it closed, so a crash after an autosave kept a withdrawal in both places or lost a deposit).
    // A move that is not stored is given back, so the session again holds what the database holds.
    private void StoreMesoMove(long deposited) {
        if (session.TrySavePurchase(db => db.SaveStorageInfo(session.AccountId, mesos, expand))) {
            return;
        }

        lock (session.Item) {
            // A withdrawal is taken back from the bag only as far as the bag still holds it.
            long back = deposited < 0 ? -Math.Min(-deposited, session.Currency.Meso) : deposited;
            mesos -= back;
            session.Currency.Meso += back;
            session.Send(StorageInventoryPacket.UpdateMesos(mesos));
            session.Send(StorageInventoryPacket.Error(s_store_err_code));
        }
    }

    public void Expand() {
        lock (session.Item) {
            short newSize = (short) (items.Size + Constant.InventoryExpandRowCount);
            // StoreExpandMaxSlotCount counts the added slots: the client offers the expansion while its slot
            // count is below the constant plus the 36 slots a storage starts with.
            if (!StorageRules.CanExpand(expand, Constant.InventoryExpandRowCount, Constants.StoreExpandMaxSlotCount)) {
                session.Send(StorageInventoryPacket.Error(s_store_err_expand_max));
                return;
            }
            if (session.Currency.Meret < Constants.StoreExpandPrice1Row) {
                session.Send(StorageInventoryPacket.Error(s_cannot_charge_merat));
                return;
            }

            if (!items.Expand(newSize)) {
                session.Send(StorageInventoryPacket.Error(s_store_err_code));
                return;
            }

            session.Currency.Meret -= Constants.StoreExpandPrice1Row;
            expand += Constant.InventoryExpandRowCount;

            // The client's reader of the expansion (CStore::Dispatch 0xd) sets the store's capacity; the items and
            // the mesos stay as they are. Dispatch 7 then shows the added slots in the open window with the expand
            // effect, so it comes second: its slot rebuild reads the capacity 0xd set.
            session.Send(StorageInventoryPacket.SlotsExpanded(expand));
            session.Send(StorageInventoryPacket.ExpandEffect());
        }
    }

    public void Sort() {
        lock (session.Item) {
            items.Sort();

            session.Send(StorageInventoryPacket.Reset());
            foreach (ImmutableList<Item> batch in items.Batch(BATCH_SIZE)) {
                session.Send(StorageInventoryPacket.Reload(batch));
            }
        }
    }

    #region Internal (No Locks)
    private bool RemoveInternal(long uid, int amount, [NotNullWhen(true)] out Item? removed) {
        if (amount > 0) {
            Item? item = items.Get(uid);
            if (item == null || item.Amount < amount) {
                session.Send(StorageInventoryPacket.Error(s_item_err_invalid_count));
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

                session.Send(StorageInventoryPacket.Update(uid, item.Amount));
                return true;
            }
        }

        // Full remove of item
        if (items.Remove(uid, out removed)) {
            session.Send(StorageInventoryPacket.Remove(uid));
            return true;
        }

        return false;
    }
    #endregion
}
