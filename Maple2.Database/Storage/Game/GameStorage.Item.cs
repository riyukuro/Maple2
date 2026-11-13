using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Maple2.Database.Extensions;
using Maple2.Database.Model;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Item = Maple2.Model.Game.Item;
using PetConfig = Maple2.Model.Game.PetConfig;
using UgcItemLook = Maple2.Model.Game.UgcItemLook;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    // The item data an item row of the game DB is loaded with. None when the id has no item data, or when the
    // played client lacks the item (PlayedClientItems.CanCreate): the client crashes on the packet of such an
    // item, so it is left out of the inventory, storage, mail, market and buyback lists the way an item without
    // data is. Its row stays as it is, and the item is back once the data has it again.
    private bool TryGetItemMetadata(int itemId, [NotNullWhen(true)] out ItemMetadata? metadata) {
        return itemMetadata.TryGet(itemId, out metadata) && PlayedClientItems.CanCreate(metadata);
    }

    // The values each item object's row was last stored with by a save of this server (SaveItems), or was loaded with
    // (SeedLoaded), kept while the object lives: a save finding them unchanged does not write the row. A created item
    // or a Clone has none: its first save writes its row. Every other write of an item row (a split, a home's stack
    // join, a doll move, a mail, the black market) forgets what any object holds for that row (ForgetRows), so the next
    // save writes it: each row id has a write stamp (one of RowWrites' slots, set from the rising WriteClock at each
    // write), and kept values count only while it is the one they were kept at.
    // Two objects of one row (a home's cube and its copy) each bump the count with their saves, so neither skips a
    // write after the other's. Two saves of one object that overlap (two threads) keep nothing: Pending is the saves
    // in flight, and a save is skipped only with none; a keep whose count was moved meanwhile drops the values.
    private static readonly ConditionalWeakTable<Item, Stored> StoredRows = new();
    private static readonly long[] RowWrites = new long[1 << 18];
    private static long WriteClock;
    private static (IClrPropertyGetter Getter, ValueConverter? Converter)[]? ItemColumns;
    private static int LastModifiedColumn = -1;

    private sealed class Stored {
        public object?[]? Values;
        public long Uid;
        public long Stamp;
        public int Pending;
    }

    private static ref long RowWrite(long uid) => ref RowWrites[(int) ((ulong) uid * 0x9E3779B97F4A7C15UL >> 46)];

    // Moves row uid's stamp on: a stamp is never set twice to one value, so values kept at an earlier one no longer count.
    // The stamp only rises: a write that took its clock value before another one's stores it only while it is higher
    // (a plain store after a stall would set the slot back to an older value, which a load's clock may have passed).
    private static void BumpRow(long uid) {
        long next = Interlocked.Increment(ref WriteClock);
        ref long slot = ref RowWrite(uid);
        long current = Volatile.Read(ref slot);
        while (current < next) {
            long seen = Interlocked.CompareExchange(ref slot, next, current);
            if (seen == current) {
                return;
            }
            current = seen;
        }
    }

    // The owners whose item rows no other process writes while they are held (count of holds by owner id). A row
    // loaded here is seeded only when its owner is held: the RowWrites stamps are this process's, so a write by
    // another game server after the load would never reach the seed, and every save here would skip the row while
    // the DB keeps the other write (an item lost at the next login, or doubled). A session holds its account and
    // character from its entry when it took the account lock there (the save of the channel it came from holds the
    // lock until it is stored, and that session saves nothing after it); a session without the lock seeds nothing.
    // The ids of owners other than accounts and characters (pets, cubes, mails) are never held: their rows are
    // written by sessions and fields of other servers too (a home's doll, a mail sent). Account, character and item
    // ids are in separate ranges (the auto increment starts of their tables), so no other owner's id equals one held.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, int> HeldOwners = new();

    // Holds owners until the result is disposed (once).
    public static IDisposable HoldOwners(params long[] owners) {
        foreach (long owner in owners) {
            HeldOwners.AddOrUpdate(owner, 1, (_, count) => count + 1);
        }
        return new OwnersHold(owners);
    }

    private sealed class OwnersHold(long[] owners) : IDisposable {
        private int disposed;

        public void Dispose() {
            if (Interlocked.Exchange(ref disposed, 1) != 0) {
                return;
            }
            foreach (long owner in owners) {
                while (HeldOwners.TryGetValue(owner, out int count)) {
                    if (count <= 1 ? HeldOwners.TryRemove(new KeyValuePair<long, int>(owner, count)) : HeldOwners.TryUpdate(owner, count - 1, count)) {
                        break;
                    }
                }
            }
        }
    }

    // A test's step between a seeding load's query and its seeds (a write of a row the query read).
    internal static Action? AfterSeedingQuery;

    // Forgets what any item object holds for rows uids.
    private static void ForgetRows(IEnumerable<long> uids) {
        foreach (long uid in uids) {
            BumpRow(uid);
        }
    }

    public partial class Request {
        // Runs write, a write of rows uids, forgetting what any item object holds for them before it and again once it
        // is done: outside a transaction when write returns, in one when the transaction ends (a save between the write
        // and its commit may keep the values the commit then overwrites).
        private T WriteForgetting<T>(long[] uids, Func<T> write) {
            ForgetRows(uids);
            if (IsTransaction) {
                AfterEnd(() => ForgetRows(uids), () => ForgetRows(uids));
                return write();
            }
            try {
                return write();
            } finally {
                ForgetRows(uids);
            }
        }
    }

    public partial class Request {
        public Item? CreateItem(long ownerId, Item item) {
            Model.Item model = item;
            model.OwnerId = ownerId;
            model.Id = 0;
            Context.Item.Add(model);

            return Context.TrySaveChanges() ? ToItem(model) : null;
        }

        // The split-off part becomes a new row. The source row's lowered amount is written in the same
        // save, or after a crash before the next character save the database holds both the whole stack
        // and the split-off part. The caller lowers the in-memory amount after this returns.
        // The context does not track queries by default, so the source row is loaded tracked; an untracked
        // row's new amount would not be part of the save.
        public Item? SplitItem(long ownerId, Item item, int amount) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
            // A source row that is gone gives no piece: writing one anyway made an item from nothing.
            Model.Item? source = Context.Item.Find(item.Uid);
            if (source == null) {
                return null;
            }
            source.Amount = item.Amount - amount;

            Model.Item model = item;
            model.Amount = amount;
            model.OwnerId = ownerId;
            model.Slot = -1;
            model.Group = ItemGroup.Default;
            model.Id = 0;
            Context.Item.Add(model);

            return WriteForgetting([item.Uid], () => Context.TrySaveChanges()) ? ToItem(model) : null;
        }

        public List<Item>? CreateItems(long ownerId, params Item[] items) {
            var models = new Model.Item[items.Length];
            for (int i = 0; i < items.Length; i++) {
                models[i] = items[i];
                models[i].OwnerId = ownerId;
                models[i].Id = 0;
                Context.Item.Add(models[i]);
            }

            if (!Context.TrySaveChanges()) {
                return null;
            }

            return models.Select(ToItem).Where(item => item != null).ToList()!;
        }

        public Item? GetItem(long itemUid) {
            Seeding seeding = StartSeeding(tracking: true); // Find gives a tracked row as it is tracked
            Model.Item? model = Context.Item.Find(itemUid);
            AfterSeedingQuery?.Invoke();
            Item? item = ToItem(model);
            if (item != null) {
                SeedLoaded(seeding, item, model!);
            }
            return item;
        }

        public UgcItemLook? GetTemplate(long itemUid) {
            ItemSubType? model = Context.Item.Select(item => new {
                item.Id,
                item.SubType,
            }).FirstOrDefault(result => result.Id == itemUid)?.SubType;
            if (model is not ItemUgc ugcModel) {
                return null;
            }

            return ugcModel.Template;
        }

        public IDictionary<ItemGroup, List<Item>> GetItemGroups(long ownerId, params ItemGroup[] groups) {
            return LoadItems(Context.Item.Where(item => item.OwnerId == ownerId && groups.Contains(item.Group)))
                .GroupBy(item => item.Group)
                .ToDictionary(group => group.Key, group => group.ToList());
        }

        public IDictionary<ItemGroup, List<Item>> GetItemGroupsNoTracking(long ownerId, params ItemGroup[] groups) {
            return LoadItems(Context.Item.Where(item => item.OwnerId == ownerId && groups.Contains(item.Group)).AsNoTracking(), noTracking: true)
                .GroupBy(item => item.Group)
                .ToDictionary(group => group.Key, group => group.ToList());
        }

        public Dictionary<InventoryType, List<Item>> GetInventory(long characterId) {
            return LoadItems(Context.Item.Where(item => item.OwnerId == characterId && item.Group == ItemGroup.Default))
                .GroupBy(item => item.Inventory)
                .ToDictionary(group => group.Key, group => group.ToList());
        }

        public (long Mesos, short Expand) GetStorageInfo(long accountId) {
            ItemStorage? info = Context.ItemStorage.Find(accountId);
            if (info == null) {
                return (0, 0);
            }

            return (info.Meso, info.Expand);
        }

        public PetConfig GetPetConfig(long itemUid) {
            return Context.PetConfig.Find(itemUid) ?? new PetConfig();
        }

        public List<Item> GetStorage(long accountId) {
            return LoadItems(Context.Item.Where(item => item.OwnerId == accountId && item.Group == ItemGroup.Default));
        }

        public List<Item> GetSavedHairs(long characterId) {
            return LoadItems(Context.Item.Where(item => item.OwnerId == characterId && item.Group == ItemGroup.SavedHair));
        }

        // A mail's items. Mannequin items are keyed by cube uid, which another table's id (a mail's) can equal.
        public List<Item> GetAllItems(long ownerId) {
            return LoadItems(Context.Item.Where(item => item.OwnerId == ownerId && item.Group != ItemGroup.FittingDoll));
        }

        // The items of the rows query loads (rows without item data left out), each seeded with the values its row
        // was loaded with (SeedLoaded). All rows are read before any is seeded: a load that fails seeds nothing.
        private List<Item> LoadItems(IQueryable<Model.Item> query, bool noTracking = false) {
            Seeding seeding = StartSeeding(!noTracking && Context.ChangeTracker.QueryTrackingBehavior == QueryTrackingBehavior.TrackAll);
            List<Model.Item> models = query.ToList();
            AfterSeedingQuery?.Invoke();
            var items = new List<Item>(models.Count);
            foreach (Model.Item model in models) {
                Item? item = ToItem(model);
                if (item != null) {
                    SeedLoaded(seeding, item, model);
                    items.Add(item);
                }
            }
            return items;
        }

        // A load's start: the write clock read before its query, and which rows it gives may not be the DB's. In a
        // transaction any may be its own uncommitted writes; a tracking query gives a row this request tracks already
        // as it was loaded or changed then, not as the DB holds it now.
        private sealed record Seeding(long Clock, bool Fresh, HashSet<long>? Tracked);

        private Seeding StartSeeding(bool tracking) {
            long clock = Volatile.Read(ref WriteClock);
            if (Context.Database.CurrentTransaction != null) {
                return new Seeding(clock, false, null);
            }
            return new Seeding(clock, true, tracking ? TrackedItems().Keys.ToHashSet() : null);
        }

        // Keeps for a loaded item the values its row was loaded with, as a save had stored them: its first save
        // writes the row only when it changed since. Only when the row was not written since the load's query began
        // (its stamp is not past the clock read before it; a write after that moves the stamp past it, so values kept
        // at the stamp read here no longer count either), and only when a save of the item as loaded would write
        // exactly the row loaded: an item the conversion changed (RepairEnchant) is written. LastModified is left out:
        // every write sets it to the minimum time, and a row a save skips keeps its own. The times compare to the
        // second: the item holds whole seconds (CreationTime, ExpiryTime), the row the fraction the DB gave it at its
        // insert, which a write would cut off; it loads as the same second either way.
        private static bool SameStored(object? saved, object? loaded) {
            if (saved is DateTime savedTime && loaded is DateTime loadedTime) {
                return savedTime.Ticks / TimeSpan.TicksPerSecond == loadedTime.Ticks / TimeSpan.TicksPerSecond;
            }
            return Equals(saved, loaded);
        }

        private void SeedLoaded(Seeding seeding, Item item, Model.Item model) {
            if (!seeding.Fresh || seeding.Tracked?.Contains(model.Id) == true || !HeldOwners.ContainsKey(model.OwnerId)) {
                return;
            }
            long stamp = Volatile.Read(ref RowWrite(model.Id));
            if (stamp > seeding.Clock) {
                return;
            }
            object?[] loaded = RowValues(model);
            Model.Item saved = item;
            saved.OwnerId = model.OwnerId;
            object?[] row = RowValues(saved);
            for (int i = 0; i < row.Length; i++) {
                if (i != LastModifiedColumn && !SameStored(row[i], loaded[i])) {
                    return;
                }
            }
            Stored stored = StoredRows.GetValue(item, _ => new Stored());
            lock (stored) {
                if (stored.Pending == 0 && stored.Values == null) {
                    (stored.Values, stored.Uid, stored.Stamp) = (row, model.Id, stamp);
                }
            }
        }

        public bool SaveItems(long ownerId, params Item[] items) => SaveItems(ownerId, items, []);

        // The item rows this request's saves (SaveItems) have written, for the session's save log.
        public int ItemRowsWritten { get; private set; }

        // Saves |items| to ownerId and |discard| to owner 0 in one save: both are written or neither. An item whose row
        // would be written with the values it was last stored with (by this item object, see StoredRows) is not
        // written again: a session save wrote every row of the bag, storage and equips each time.
        public bool SaveItems(long ownerId, Item[] items, Item[] discard) {
            Dictionary<long, EntityEntry<Model.Item>>? tracked = null;
            var written = new List<Written>();
            bool saved = false;
            try {
                foreach ((Item item, long owner) in items.Select(item => (item, ownerId)).Concat(discard.Select(item => (item, 0L)))) {
                    if (item.Uid == 0) {
                        continue;
                    }

                    Model.Item model = item;
                    model.OwnerId = owner;
                    object?[] row = RowValues(model);
                    Stored stored = StoredRows.GetValue(item, _ => new Stored());
                    long stamp;
                    lock (stored) {
                        stamp = Volatile.Read(ref RowWrite(model.Id));
                        if (stored.Pending == 0 && stored.Values != null && stored.Uid == model.Id && stored.Stamp == stamp
                            && stored.Values.SequenceEqual(row)) {
                            continue;
                        }
                        stored.Pending++;
                    }
                    written.Add(new Written(stored, model.Id, row, stamp));
                    // A row this request created (CreateItem) is still tracked; updating a second instance of it threw,
                    // so a new item that merged in part into a stack was not saved and the add stopped half way. The
                    // tracked rows are listed once: each lookup ran DetectChanges over every tracked row.
                    tracked ??= TrackedItems();
                    if (tracked.Remove(model.Id, out EntityEntry<Model.Item>? entry)) {
                        entry.State = EntityState.Detached;
                    }
                    tracked[model.Id] = Context.Item.Update(model);
                }
                // Another write pending on this request is saved here as before.
                if (written.Count == 0 && !Context.ChangeTracker.HasChanges()) {
                    return true;
                }

                saved = Context.TrySaveChanges();
                ItemRowsWritten += written.Count;
                return saved;
            } finally {
                if (saved) {
                    KeepStoredRows(written);
                } else {
                    DropStoredRows(written);
                }
            }
        }

        private readonly record struct Written(Stored Stored, long Uid, object?[] Row, long Stamp);

        private Dictionary<long, EntityEntry<Model.Item>> TrackedItems() {
            bool detect = Context.ChangeTracker.AutoDetectChangesEnabled;
            Context.ChangeTracker.AutoDetectChangesEnabled = false;
            try {
                return Context.ChangeTracker.Entries<Model.Item>().ToDictionary(entry => entry.Entity.Id);
            } finally {
                Context.ChangeTracker.AutoDetectChangesEnabled = detect;
            }
        }

        // The rows written are known stored once the request's transaction commits; on a rollback (or a commit whose
        // answer was lost) they are not. In a transaction this request does not hold (MoveItem's) they are not known
        // either, so the next save writes them. A row is kept only when its write count is still the one read before
        // the write (no other write of it since, this object's or another's), and it moves on.
        private void KeepStoredRows(List<Written> written) {
            if (!IsTransaction && Context.Database.CurrentTransaction != null) {
                DropStoredRows(written);
                return;
            }
            AfterEnd(() => {
                foreach (Written write in written) {
                    lock (write.Stored) {
                        write.Stored.Pending--;
                        long next = Interlocked.Increment(ref WriteClock);
                        if (Interlocked.CompareExchange(ref RowWrite(write.Uid), next, write.Stamp) == write.Stamp) {
                            (write.Stored.Values, write.Stored.Uid, write.Stored.Stamp) = (write.Row, write.Uid, next);
                        } else {
                            write.Stored.Values = null;
                            BumpRow(write.Uid);
                        }
                    }
                }
            }, () => DropStoredRows(written));
        }

        // A write that may or may not be stored: nothing is kept for it, and what other objects keep for its row is
        // forgotten (the row may hold this write's values).
        private static void DropStoredRows(List<Written> written) {
            foreach (Written write in written) {
                lock (write.Stored) {
                    write.Stored.Pending--;
                    write.Stored.Values = null;
                }
                BumpRow(write.Uid);
            }
        }

        // The values item's row is written with: each column's value, the JSON columns as the text written.
        private object?[] RowValues(Model.Item model) {
            if (ItemColumns == null) {
                IProperty[] properties = Context.Model.FindEntityType(typeof(Model.Item))!.GetProperties().ToArray();
                LastModifiedColumn = Array.FindIndex(properties, property => property.Name == nameof(Model.Item.LastModified));
                ItemColumns = properties.Select(property => (property.GetGetter(), property.GetValueConverter())).ToArray();
            }
            var row = new object?[ItemColumns.Length];
            for (int i = 0; i < row.Length; i++) {
                (IClrPropertyGetter getter, ValueConverter? converter) = ItemColumns[i];
                object? value = getter.GetClrValue(model);
                row[i] = converter == null || value == null ? value : converter.ConvertToProvider(value);
            }
            return row;
        }

        // Saves item to toOwnerId only while its row is still owned by fromOwnerId in fromGroup, so a copy of the item
        // kept in memory by another server (a fitting doll loaded in two field instances) cannot move the row twice.
        // On a relational DB the test is one conditional update in the same transaction as the save; false when the
        // row has moved, or the save failed (nothing is changed then).
        public bool MoveItem(Item item, long fromOwnerId, ItemGroup fromGroup, long toOwnerId) {
            long uid = item.Uid;
            try {
                using IDbContextTransaction transaction = Context.Database.BeginTransaction();
                IQueryable<Model.Item> held = Context.Item.Where(row => row.Id == uid && row.OwnerId == fromOwnerId && row.Group == fromGroup);
                int rows = Context.Database.IsRelational()
                    ? held.ExecuteUpdate(update => update.SetProperty(row => row.OwnerId, toOwnerId))
                    : held.AsNoTracking().Count();
                if (rows != 1 || !SaveItems(toOwnerId, item)) {
                    return false; // the transaction is rolled back when it is disposed
                }

                transaction.Commit();
                return true;
            } catch (Exception ex) {
                Logger.LogError(ex, "Failed to move item {ItemUid} from {FromOwnerId} to {ToOwnerId}", uid, fromOwnerId, toOwnerId);
                return false;
            } finally {
                ForgetRows([uid]); // its own transaction has ended (see ForgetRows)
            }
        }

        // Puts a character's inventory item on the fitting doll of cube cubeUid, in the same transaction checking that the
        // cube is still placed on plot plotId. The row is not tested: the inventory is saved lazily, so the row of an
        // item just unequipped or taken from mail may not be the character's yet; the session's inventory holds it.
        public ItemMove PutOnFittingDoll(Item item, long cubeUid, long plotId) {
            try {
                using IDbContextTransaction transaction = Context.Database.BeginTransaction();
                if (!Context.UgcMapCube.Any(cube => cube.Id == cubeUid && cube.UgcMapId == plotId)) {
                    return ItemMove.TargetGone;
                }
                if (!SaveItems(cubeUid, item)) {
                    return ItemMove.Failed; // the transaction is rolled back when it is disposed
                }

                transaction.Commit();
                return ItemMove.Moved;
            } catch (Exception ex) {
                Logger.LogError(ex, "Failed to put item {ItemUid} on fitting doll {CubeUid}", item.Uid, cubeUid);
                return ItemMove.Failed;
            } finally {
                ForgetRows([item.Uid]);
            }
        }

        // Swaps items between the fitting doll of cube cubeUid and character characterId in one transaction and one
        // save: toDoll become the cube's, toCharacter the character's. The cube must still be placed on plot plotId
        // (TargetGone), and each of toCharacter must still be on the doll (SourceGone: the same home open on another
        // channel took it); then nothing is written. The rows of toDoll are not tested, as in PutOnFittingDoll.
        public ItemMove SwapFittingDoll(long cubeUid, long plotId, long characterId, IReadOnlyCollection<Item> toDoll,
                                        IReadOnlyCollection<Item> toCharacter) {
            try {
                using IDbContextTransaction transaction = Context.Database.BeginTransaction();
                if (!Context.UgcMapCube.Any(cube => cube.Id == cubeUid && cube.UgcMapId == plotId)) {
                    return ItemMove.TargetGone;
                }
                foreach (Item item in toCharacter) {
                    long uid = item.Uid;
                    IQueryable<Model.Item> held = Context.Item.Where(row => row.Id == uid && row.OwnerId == cubeUid && row.Group == ItemGroup.FittingDoll);
                    int rows = Context.Database.IsRelational()
                        ? held.ExecuteUpdate(update => update.SetProperty(row => row.OwnerId, characterId))
                        : held.AsNoTracking().Count();
                    if (rows != 1) {
                        return ItemMove.SourceGone; // the transaction is rolled back when it is disposed
                    }
                }
                ForgetRows(toDoll.Concat(toCharacter).Select(item => item.Uid));
                foreach ((Item item, long ownerId) in toDoll.Select(item => (item, cubeUid)).Concat(toCharacter.Select(item => (item, characterId)))) {
                    Model.Item model = item;
                    model.OwnerId = ownerId;
                    Context.Item.Update(model);
                }
                if (!Context.TrySaveChanges()) {
                    return ItemMove.Failed;
                }

                transaction.Commit();
                return ItemMove.Moved;
            } catch (Exception ex) {
                Logger.LogError(ex, "Failed to swap items with fitting doll {CubeUid}", cubeUid);
                return ItemMove.Failed;
            } finally {
                ForgetRows(toDoll.Concat(toCharacter).Select(item => item.Uid));
            }
        }

        // Deletes item uid from the fitting doll of cube cubeUid, only while its row is still on that cube (the same
        // home open on another channel may have taken it off): one conditional delete. False when the row has left the
        // cube or the delete failed (nothing is deleted then).
        public bool DeleteFittingDollItem(long uid, long cubeUid) {
            return WriteForgetting([uid], () => DeleteDollRow(uid, cubeUid));
        }

        private bool DeleteDollRow(long uid, long cubeUid) {
            try {
                IQueryable<Model.Item> held = Context.Item.Where(row => row.Id == uid && row.OwnerId == cubeUid && row.Group == ItemGroup.FittingDoll);
                if (Context.Database.IsRelational()) {
                    return held.ExecuteDelete() == 1;
                }
                Model.Item? row = held.FirstOrDefault();
                if (row == null) {
                    return false;
                }
                Context.Item.Remove(row);
                return Context.TrySaveChanges();
            } catch (Exception ex) {
                Logger.LogError(ex, "Failed to delete item {ItemUid} from fitting doll {CubeUid}", uid, cubeUid);
                return false;
            }
        }

        // The item object has no owner: the row keeps the one it has (written as 0 it was an unowned row, deleted at
        // the next world start, until a save of its owner wrote it back).
        public bool UpdateItem(Item item) {
            long? ownerId = Context.Item.Where(row => row.Id == item.Uid).Select(row => (long?) row.OwnerId).FirstOrDefault();
            if (ownerId is null) {
                return false;
            }
            Model.Item model = item;
            model.OwnerId = ownerId.Value;
            Context.Item.Update(model);

            return WriteForgetting([item.Uid], () => Context.TrySaveChanges());
        }

        // Delete all items that are unowned. (Character, account, etc.)
        public void DeleteUnownedItems() {
            int deletedCount = Context.Database.ExecuteSqlRaw("DELETE FROM `item` WHERE OwnerId = 0");
            if (deletedCount == 0) {
                return;
            }

            Logger.LogInformation("Deleted {Count} unowned items.", deletedCount);
        }

        // World start: mannequin item rows left on a cube that no longer exists (an older instance of the home deleted
        // the cube before plot saves gave such items back, or a cube removed on another channel during a put on).
        // A row counts only while its owner uid is nothing else: no cube on any plot or in a saved layout, no item
        // (a cube in storage), no mail, no character, no account. Each one is logged. Only with mail set, a row bound
        // to a character goes to that character in a system mail of its own, moved only while it is still on its old
        // owner uid. An unbound row is never mailed: the deleted cube row was its only link to a plot, so to an owner;
        // it stays, logged, for an admin. A mailed row leaves the doll group, so a second run finds it no more.
        public (int Orphaned, int Mailed) ReturnOrphanedDollItems(bool mail) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
            List<Model.Item> rows = Context.Item.AsNoTracking()
                .Where(row => row.Group == ItemGroup.FittingDoll && row.OwnerId != 0)
                .Where(row => !Context.UgcMapCube.Any(cube => cube.Id == row.OwnerId)
                              && !Context.UgcCubeLayout.Any(cube => cube.Id == row.OwnerId)
                              && !Context.Item.Any(item => item.Id == row.OwnerId)
                              && !Context.Mail.Any(other => other.Id == row.OwnerId)
                              && !Context.Character.Any(character => character.Id == row.OwnerId)
                              && !Context.Account.Any(account => account.Id == row.OwnerId))
                .OrderBy(row => row.Id)
                .ToList();
            if (rows.Count == 0) {
                return (0, 0);
            }

            foreach (Model.Item row in rows) {
                long bound = row.Transfer?.Binding?.CharacterId ?? 0;
                Logger.LogWarning("Fitting doll item {ItemUid} ({ItemId}) is on cube {CubeUid}, which no longer exists; bound to {CharacterId}",
                    row.Id, row.ItemId, row.OwnerId, bound);
            }
            int mailed = 0;
            if (mail) {
                foreach (Model.Item row in rows) {
                    long bound = row.Transfer?.Binding?.CharacterId ?? 0;
                    if (bound == 0 || !Context.Character.Any(character => character.Id == bound)) {
                        continue;
                    }
                    if (MailOrphanedDollRow(row, bound)) {
                        mailed++;
                        Logger.LogInformation("Mailed fitting doll item {ItemUid} to {CharacterId}", row.Id, bound);
                    }
                }
            }
            Logger.LogWarning("{Count} fitting doll items are on cubes that no longer exist; {Mailed} mailed (mailing is {Mode})",
                rows.Count, mailed, mail ? "on" : "off");
            return (rows.Count, mailed);
        }

        // One orphaned row into a mail to characterId: the mail and the row commit together or not at all.
        private bool MailOrphanedDollRow(Model.Item row, long characterId) {
            try {
                using IDbContextTransaction transaction = Context.Database.BeginTransaction();
                Model.Mail mail = DollItemMail(characterId);
                Context.Mail.Add(mail);
                if (!Context.TrySaveChanges() || !MoveDollRow(row, mail.Id) || !Context.TrySaveChanges()) {
                    return false; // the transaction is rolled back when it is disposed
                }
                transaction.Commit();
                return true;
            } catch (Exception ex) {
                Logger.LogError(ex, "Failed to mail fitting doll item {ItemUid}", row.Id);
                return false;
            } finally {
                ForgetRows([row.Id]);
                Context.ChangeTracker.Clear();
            }
        }

        public bool SaveStorageInfo(long accountId, long mesos, short expand) {
            ItemStorage? info = Context.ItemStorage.Find(accountId);
            if (info == null) {
                Context.Add(new ItemStorage {
                    AccountId = accountId,
                    Meso = mesos,
                    Expand = expand,
                });
            } else {
                info.Meso = mesos;
                info.Expand = expand;
                Context.ItemStorage.Update(info);
            }

            return Context.TrySaveChanges();
        }

        public bool SavePetConfig(long itemUid, PetConfig config) {
            Model.PetConfig? model = Context.PetConfig.Find(itemUid);
            if (model == null) {
                model = config;
                model.ItemUid = itemUid;

                Context.Add(model);
            } else {
                model = config;
                model.ItemUid = itemUid;

                Context.Update(model);
            }

            return Context.TrySaveChanges();
        }

        // Converts model to item if possible, otherwise returns null.
        private Item? ToItem(Model.Item? model) {
            if (model == null) {
                return null;
            }

            return game.TryGetItemMetadata(model.ItemId, out ItemMetadata? metadata) ? game.RepairEnchant(model.Convert(metadata)) : null;
        }
    }
}
