using System.Diagnostics.CodeAnalysis;
using Maple2.Database.Extensions;
using Maple2.Database.Model;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Microsoft.EntityFrameworkCore;
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
            Model.Item? source = Context.Item.Find(item.Uid);
            if (source != null) {
                source.Amount = item.Amount - amount;
            }

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

        public bool SaveItems(long ownerId, params Item[] items) {
            var models = new Model.Item[items.Length];
            for (int i = 0; i < items.Length; i++) {
                if (items[i].Uid == 0) {
                    continue;
                }

                models[i] = items[i];
                models[i].OwnerId = ownerId;
                Context.Item.Update(models[i]);
            }

            return Context.TrySaveChanges();
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

        public bool UpdateItem(Item item) {
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
