using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Maple2.Model.Game;

namespace Maple2.Server.Game.Manager.Items;

/// <remarks>
/// This class is thread-safe.
/// </remarks>
public class ItemCollection : IEnumerable<Item> {
    private readonly ReaderWriterLockSlim mutex;
    private readonly IDictionary<long, short> uidToSlot;
    private Item?[] items;

    public short OpenSlots => (short) (Size - Count);
    public short Size => (short) items.Length;
    public short Count { get; private set; }

    public ItemCollection(short size) {
        mutex = new ReaderWriterLockSlim();
        uidToSlot = new Dictionary<long, short>();
        items = new Item[size];

        Count = 0;
    }

    public Item? this[short slot] {
        get {
            mutex.EnterReadLock();
            try {
                return ValidSlot(slot) ? items[slot] : null;
            } finally {
                mutex.ExitReadLock();
            }
        }
        set {
            // Setting slot to null or Slot is already taken
            // Use `RemoveSlot()` to remove items.
            if (value == null || slot < 0 || slot >= Size || this[slot] != null) {
                return;
            }

            mutex.EnterUpgradeableReadLock();
            try {
                mutex.EnterWriteLock();
                try {
                    value.Slot = slot;
                    uidToSlot[value.Uid] = slot;
                    items[slot] = value;
                    Count++;
                } finally {
                    mutex.ExitWriteLock();
                }
            } finally {
                mutex.ExitUpgradeableReadLock();
            }
        }
    }

    /// <summary>
    /// Stacks the added item onto any available stacks
    /// <paramref name="add"/> will be modified to reflect the remaining amount.
    /// </summary>
    /// <param name="add">The item to stack</param>
    /// <param name="slot">The specific slot to stack on. If unspecified, attempt to stack on all possible items.</param>
    /// <returns>A list of items which were stacked on and their added amounts.</returns>
    public IList<(Item, int Added)> Stack(Item add, short slot = -1) {
        var result = new List<(Item, int)>();
        mutex.EnterReadLock();
        try {
            if (slot >= 0) {
                Item? item = items[slot];
                if (item == null) {
                    return result;
                }

                int added = StackItem(item, add);
                if (added > 0) {
                    result.Add((item, added));
                }

                return result;
            }

            foreach (Item item in GetInternalEnumerator()) {
                int added = StackItem(item, add);
                if (added == 0) {
                    continue;
                }

                result.Add((item, added));

                if (add.Amount <= 0) {
                    return result;
                }
            }
        } finally {
            mutex.ExitReadLock();
        }

        return result;
    }

    /// <summary>
    /// Inserts the added item into the first open slot.
    /// </summary>
    /// <param name="insert">The item to insert</param>
    /// <returns>true if the item was successfully inserted</returns>
    private bool Append(Item insert) {
        if (OpenSlots <= 0) {
            return false;
        }

        for (short i = 0; i < Size; i++) {
            if (this[i] != null) {
                continue;
            }

            this[i] = insert;
            insert.Slot = i;
            return true;
        }

        return false;
    }

    /// <summary>
    /// - If the added item has an existing slot, it will attempt to add to that slot.
    /// - If <paramref name="stack"/> is true, the item will first be stacked on existing
    /// items of the same type.
    /// - Any remaining amount will be added to the first open slot.
    ///
    /// If the item cannot be fully added, no action will take place.
    /// </summary>
    /// <param name="add">The item to add</param>
    /// <param name="stack">Whether or not to attempt stacking the item to add</param>
    /// <returns>A list of items which were stacked on and their added amounts.</returns>
    public IList<(Item, int Added)> Add(Item add, bool stack = false) {
        if (!Fits(add, stack)) {
            return [];
        }

        // Prefer item slot if specified
        if (ValidSlot(add.Slot) && this[add.Slot] == null) {
            this[add.Slot] = add;
            return [(add, add.Amount)];
        }

        IList<(Item, int Added)> result = new List<(Item, int)>();
        if (stack) {
            result = Stack(add);
            if (add.Amount <= 0) {
                return result;
            }
        }

        // All stacks are maxed out at this point, remaining items go in first open slot.
        if (Append(add)) {
            result.Add((add, add.Amount));
            return result;
        }

        return [];
    }

    public Item? Get(long uid) {
        short slot;
        mutex.EnterReadLock();
        try {
            if (!uidToSlot.TryGetValue(uid, out slot)) {
                return null;
            }
        } finally {
            mutex.ExitReadLock();
        }

        return this[slot];
    }

    public bool Contains(long uid) {
        mutex.EnterReadLock();
        try {
            return uidToSlot.ContainsKey(uid);
        } finally {
            mutex.ExitReadLock();
        }
    }

    public bool Remove(long uid, [NotNullWhen(true)] out Item? removed) {
        mutex.EnterUpgradeableReadLock();
        try {
            if (!uidToSlot.TryGetValue(uid, out short slot)) {
                removed = null;
                return false;
            }

            return RemoveSlot(slot, out removed);
        } finally {
            mutex.ExitUpgradeableReadLock();
        }
    }

    public bool RemoveSlot(short slot, [NotNullWhen(true)] out Item? removed) {
        removed = this[slot];
        if (removed == null) {
            return false;
        }

        mutex.EnterWriteLock();
        try {
            items[slot] = null;
            uidToSlot.Remove(removed.Uid);
            Count--;
            return true;
        } finally {
            mutex.ExitWriteLock();
        }
    }

    /// <summary>
    /// Moves the amounts of stackable items onto earlier stacks of the same item (same rules as <see cref="Stack"/>).
    /// </summary>
    /// <returns>The items emptied by the merge; they are already removed from this collection.</returns>
    public IList<Item> MergeStacks() {
        var emptied = new List<Item>();
        mutex.EnterWriteLock();
        try {
            for (int i = 0; i < items.Length; i++) {
                if (items[i] is not { } stackTo) continue;
                for (int j = i + 1; j < items.Length && stackTo.Amount < stackTo.Metadata.Property.SlotMax; j++) {
                    if (items[j] is not { } stackFrom || StackItem(stackTo, stackFrom) == 0 || stackFrom.Amount > 0) {
                        continue;
                    }
                    items[j] = null;
                    uidToSlot.Remove(stackFrom.Uid);
                    Count--;
                    emptied.Add(stackFrom);
                }
            }
        } finally {
            mutex.ExitWriteLock();
        }
        return emptied;
    }

    public void Sort() {
        mutex.EnterWriteLock();
        try {
            Array.Sort(items, SortItem);

            // Update the slot mapping
            uidToSlot.Clear();
            short i = 0;
            while (i < items.Length && items[i] is { } item) {
                item.Slot = i;
                uidToSlot[items[i]!.Uid] = i;
                i++;
            }
        } finally {
            mutex.ExitWriteLock();
        }
    }

    public bool Expand(short newSize) {
        if (newSize <= Size) {
            return false;
        }

        mutex.EnterWriteLock();
        try {
            Array.Resize(ref items, newSize);
            return true;
        } finally {
            mutex.ExitWriteLock();
        }
    }

    /// <summary>
    /// Simulates stacking an item on existing items in this collection without modification.
    /// Ignores any open slots.
    /// </summary>
    /// <param name="item">The item to attempt to add.</param>
    /// <param name="amount">The amount of item to add.</param>
    /// <returns>The remaining amount that could not be stacked.</returns>
    public int GetStackResult(Item item, int amount = -1) {
        int remaining = amount < 0 ? item.Amount : Math.Min(amount, item.Amount);
        mutex.EnterReadLock();
        try {
            foreach (Item existing in GetInternalEnumerator()) {
                if (!CanStack(existing, item)) continue;

                int available = existing.Metadata.Property.SlotMax - existing.Amount;
                remaining -= available;
                if (remaining <= 0) {
                    return 0;
                }
            }
        } finally {
            mutex.ExitReadLock();
        }

        return remaining;
    }

    // Whether Add(add, stack) takes the item: an open slot, or (with stack) stacks with room for all of it.
    public bool Fits(Item add, bool stack = false) {
        return OpenSlots > 0 || (stack && GetStackResult(add) <= 0);
    }

    // Whether Add(item, stack: true) of each of the items in turn takes all of them, changing nothing: an item goes
    // to its own slot when that slot is free, else on the stacks it can join (the bag's and the items placed before
    // it), and what is left takes an open slot.
    public bool TakesAll(IEnumerable<Item> adds) {
        mutex.EnterReadLock();
        try {
            var placed = new List<(Item Item, int Amount)>();
            var taken = new HashSet<short>();
            foreach (Item existing in GetInternalEnumerator()) {
                placed.Add((existing, existing.Amount));
                taken.Add(existing.Slot);
            }
            int open = OpenSlots;
            foreach (Item add in adds) {
                int remaining = add.Amount;
                if (!ValidSlot(add.Slot) || taken.Contains(add.Slot)) {
                    for (int i = 0; i < placed.Count && remaining > 0; i++) {
                        (Item stack, int amount) = placed[i];
                        int room = stack.Metadata.Property.SlotMax - amount;
                        if (room <= 0 || !SameStack(stack, add)) {
                            continue;
                        }
                        int added = Math.Min(room, remaining);
                        placed[i] = (stack, amount + added);
                        remaining -= added;
                    }
                    if (remaining <= 0) {
                        continue;
                    }
                }
                if (open <= 0) {
                    return false;
                }
                open--;
                placed.Add((add, remaining));
                if (ValidSlot(add.Slot) && taken.Add(add.Slot)) {
                    continue;
                }
                for (short slot = 0; slot < Size; slot++) {
                    if (taken.Add(slot)) {
                        break;
                    }
                }
            }
            return true;
        } finally {
            mutex.ExitReadLock();
        }
    }

    private bool ValidSlot(short slot) => slot >= 0 && slot < Size;

    // Enumerate array while ignoring nulls
    public IEnumerator<Item> GetEnumerator() {
        mutex.EnterReadLock();
        try {
            return items.Where(item => item != null)
                .Select(item => item!)
                .GetEnumerator();
        } finally {
            mutex.ExitReadLock();
        }
    }

    IEnumerator IEnumerable.GetEnumerator() {
        return GetEnumerator();
    }

    private IEnumerable<Item> GetInternalEnumerator() {
        foreach (Item? item in items) {
            if (item == null) continue;
            yield return item;
        }
    }

    private static int StackItem(Item stackTo, Item stackFrom) {
        if (!CanStack(stackTo, stackFrom)) {
            return 0;
        }

        int available = stackTo.Metadata.Property.SlotMax - stackTo.Amount;
        int added = Math.Min(available, stackFrom.Amount);
        stackFrom.Amount -= added;
        stackTo.Amount += added;

        return added;
    }

    private static bool CanStack(Item item, Item stack) {
        return item.Amount < item.Metadata.Property.SlotMax && SameStack(item, stack);
    }

    private static bool SameStack(Item item, Item stack) {
        return item.Id == stack.Id
               && item.Rarity == stack.Rarity
               && Equals(item.Transfer, stack.Transfer)
               // A merge keeps one item's lock state, so a locked and an unlocked item never merge.
               && item.IsLocked == stack.IsLocked && item.UnlockTime == stack.UnlockTime
               // A merge keeps one item's storage record (the character that stored a moveDisable item), so items
               // stored by two characters, or one with a record and one without, never merge.
               && Equals(item.Binding, stack.Binding)
               && item.ExpiryTime / 60 == stack.ExpiryTime / 60; // Expiry time is in seconds, convert to minutes
    }

    private static int SortItem(Item? x, Item? y) {
        if (x == null) return 1;
        if (y == null) return -1;

        int result = x.Id.CompareTo(y.Id);
        if (result != 0) return result;
        result = x.Rarity.CompareTo(y.Rarity);
        if (result != 0) return result;
        return x.Amount.CompareTo(y.Amount);
    }
}
