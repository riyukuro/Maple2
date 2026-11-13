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

    // The values each item object's row was last stored with by a save of this server (SaveItems), kept while the
    // object lives: a save finding them unchanged does not write the row. A write of the row by another way forgets
    // them, so the next save writes it. A new object (a load, a Clone) has none: its first save writes its row.
    private static readonly ConditionalWeakTable<Item, object?[]> StoredRows = new();
    private static (IClrPropertyGetter Getter, ValueConverter? Converter)[]? ItemColumns;

    private static void ForgetStoredRows(IEnumerable<Item> items) {
        foreach (Item item in items) {
            StoredRows.Remove(item);
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

            return Context.TrySaveChanges() ? ToItem(model) : null;
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
            Model.Item? model = Context.Item.Find(itemUid);
            if (model == null) {
                return null;
            }

            return game.TryGetItemMetadata(model.ItemId, out ItemMetadata? metadata) ? game.RepairEnchant(model.Convert(metadata)) : null;
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
            return Context.Item.Where(item => item.OwnerId == ownerId && groups.Contains(item.Group))
                .AsEnumerable()
                .GroupBy(item => item.Group)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(ToItem).Where(item => item != null).ToList()
                )!;
        }

        public IDictionary<ItemGroup, List<Item>> GetItemGroupsNoTracking(long ownerId, params ItemGroup[] groups) {
            return Context.Item.Where(item => item.OwnerId == ownerId && groups.Contains(item.Group))
                .AsNoTracking()
                .AsEnumerable()
                .GroupBy(item => item.Group)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(ToItem).Where(item => item != null).ToList()
                )!;
        }

        public Dictionary<InventoryType, List<Item>> GetInventory(long characterId) {
            return Context.Item.Where(item => item.OwnerId == characterId && item.Group == ItemGroup.Default)
                .AsEnumerable()
                .Select(ToItem)
                .Where(item => item != null)
                .GroupBy(item => item!.Inventory)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToList()
                )!;
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
            return Context.Item.Where(item => item.OwnerId == accountId && item.Group == ItemGroup.Default)
                .AsEnumerable()
                .Select(ToItem)
                .Where(item => item != null)
                .ToList()!;
        }

        public List<Item> GetSavedHairs(long characterId) {
            return Context.Item.Where(item => item.OwnerId == characterId && item.Group == ItemGroup.SavedHair)
                .AsEnumerable()
                .Select(ToItem)
                .Where(item => item != null)
                .ToList()!;
        }

        // A mail's items. Mannequin items are keyed by cube uid, which another table's id (a mail's) can equal.
        public List<Item> GetAllItems(long ownerId) {
            return Context.Item.Where(item => item.OwnerId == ownerId && item.Group != ItemGroup.FittingDoll)
                .AsEnumerable()
                .Select(ToItem)
                .Where(item => item != null)
                .ToList()!;
        }

        public bool SaveItems(long ownerId, params Item[] items) => SaveItems(ownerId, items, []);

        // Saves |items| to ownerId and |discard| to owner 0 in one save: both are written or neither. An item whose row
        // would be written with the values it was last stored with (by this item object, see StoredRows) is not
        // written again: a session save wrote every row of the bag, storage and equips each time.
        public bool SaveItems(long ownerId, Item[] items, Item[] discard) {
            Dictionary<long, EntityEntry<Model.Item>>? tracked = null;
            var written = new List<(Item Item, object?[] Row)>();
            foreach ((Item item, long owner) in items.Select(item => (item, ownerId)).Concat(discard.Select(item => (item, 0L)))) {
                if (item.Uid == 0) {
                    continue;
                }

                Model.Item model = item;
                model.OwnerId = owner;
                object?[] row = RowValues(model);
                if (StoredRows.TryGetValue(item, out object?[]? stored) && stored.SequenceEqual(row)) {
                    continue;
                }
                // A row this request created (CreateItem) is still tracked; updating a second instance of it threw,
                // so a new item that merged in part into a stack was not saved and the add stopped half way. The
                // tracked rows are listed once: each lookup ran DetectChanges over every tracked row.
                tracked ??= TrackedItems();
                if (tracked.Remove(model.Id, out EntityEntry<Model.Item>? entry)) {
                    entry.State = EntityState.Detached;
                }
                tracked[model.Id] = Context.Item.Update(model);
                written.Add((item, row));
            }
            // Another write pending on this request is saved here as before.
            if (written.Count == 0 && !Context.ChangeTracker.HasChanges()) {
                return true;
            }

            if (!Context.TrySaveChanges()) {
                return false;
            }
            KeepStoredRows(written);
            return true;
        }

        private Dictionary<long, EntityEntry<Model.Item>> TrackedItems() {
            bool detect = Context.ChangeTracker.AutoDetectChangesEnabled;
            Context.ChangeTracker.AutoDetectChangesEnabled = false;
            try {
                return Context.ChangeTracker.Entries<Model.Item>().ToDictionary(entry => entry.Entity.Id);
            } finally {
                Context.ChangeTracker.AutoDetectChangesEnabled = detect;
            }
        }

        // The rows written are known stored once the request's transaction commits (dropped on a rollback); in a
        // transaction this request does not hold (MoveItem's) they are not known, so the next save writes them.
        private void KeepStoredRows(List<(Item Item, object?[] Row)> written) {
            if (!IsTransaction && Context.Database.CurrentTransaction != null) {
                ForgetStoredRows(written.Select(entry => entry.Item));
                return;
            }
            AfterCommit(() => {
                foreach ((Item item, object?[] row) in written) {
                    StoredRows.AddOrUpdate(item, row);
                }
            });
        }

        // The values item's row is written with: each column's value, the JSON columns as the text written.
        private object?[] RowValues(Model.Item model) {
            ItemColumns ??= Context.Model.FindEntityType(typeof(Model.Item))!.GetProperties()
                .Select(property => (property.GetGetter(), property.GetValueConverter()))
                .ToArray();
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
                ForgetStoredRows(toDoll.Concat(toCharacter));
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
            }
        }

        // Deletes item uid from the fitting doll of cube cubeUid, only while its row is still on that cube (the same
        // home open on another channel may have taken it off): one conditional delete. False when the row has left the
        // cube or the delete failed (nothing is deleted then).
        public bool DeleteFittingDollItem(long uid, long cubeUid) {
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

        public bool UpdateItem(Item item) {
            ForgetStoredRows([item]);
            Model.Item model = item;
            Context.Item.Update(model);

            return Context.TrySaveChanges();
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
