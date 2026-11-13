using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Tools.Extensions;
using Serilog;

namespace Maple2.Server.Game.Manager.Items;

public class FurnishingManager {
    private static long _cubeIdCounter = Constant.FurnishingBaseId;
    public static long NextCubeId() => Interlocked.Increment(ref _cubeIdCounter);

    private readonly GameSession session;

    private readonly ItemCollection storage;
    private readonly ConcurrentDictionary<long, PlotCube> inventory;

    private readonly ILogger logger = Log.Logger.ForContext<FurnishingManager>();

    public FurnishingManager(GameStorage.Request db, GameSession session) {
        this.session = session;
        storage = new ItemCollection(Constant.FurnishingStorageMaxSlot);
        inventory = new ConcurrentDictionary<long, PlotCube>();

        // An account with no storage rows still loads its placed cubes (they can be picked up).
        List<Item> items = db.GetItemGroups(session.AccountId, ItemGroup.Furnishing).GetValueOrDefault(ItemGroup.Furnishing) ?? [];
        foreach (Item item in items) {
            if (storage.Add(item).Count == 0) {
                Log.Error("Failed to add furnishing:{Uid}", item.Uid);
            }
        }

        foreach (PlotCube cube in db.LoadCubesForOwner(session.AccountId)) {
            inventory[cube.Id] = cube;
        }
    }

    public void Load() {
        lock (session.Item) {
            Item[] items = storage.Where(item => item.Amount > 0).ToArray();
            session.Send(FurnishingStoragePacket.Count(items.Length));
            session.Send(FurnishingStoragePacket.StartList());
            foreach (Item item in items) {
                session.Send(FurnishingStoragePacket.Add(item));
            }
            session.Send(FurnishingStoragePacket.EndList());

            // FurnishingInventory
            session.Send(FurnishingInventoryPacket.StartList());
            foreach (PlotCube cube in inventory.Values) {
                session.Send(FurnishingInventoryPacket.Add(cube));
            }
            session.Send(FurnishingInventoryPacket.EndList());
        }
    }

    public Item? GetCube(long itemUid) {
        lock (session.Item) {
            return storage.Get(itemUid);
        }
    }

    public Item? GetItem(int itemId) {
        lock (session.Item) {
            return storage.FirstOrDefault(item => item.Id == itemId);
        }
    }

    /// <summary>
    /// Places a cube of the specified item uid at the requested location.
    /// If there are no amount remaining, we still keep the entry to allow reuse of the item uid.
    /// </summary>
    /// <param name="uid">Uid of the item to withdraw from</param>
    /// <param name="cube">The cube to be placed</param>
    /// <returns>Information about the withdrawn cube</returns>
    public bool TryAddCube(long uid, PlotCube cube) {
        const int amount = 1;
        lock (session.Item) {
            if (session.Field == null) {
                return false;
            }

            Item? item = storage.Get(uid);
            if (item == null || item.Amount < amount) {
                return false;
            }

            // We do not remove item from inventory even if it hits 0
            // this allows us to preserve the uid for later.
            item.Amount -= amount;
            // The placed cube keeps the item's expiry (HeldCube's third field).
            cube.ExpiryTime = item.ExpiryTime;
            session.Send(item.Amount > 0
                ? FurnishingStoragePacket.Update(item.Uid, item.Amount)
                : FurnishingStoragePacket.Remove(item.Uid));

            if (!AddInventory(cube)) {
                logger.Fatal("Failed to add cube: {CubeId} to inventory", cube.Id);
                throw new InvalidOperationException($"Failed to add cube: {cube.Id} to inventory");
            }

            return true;
        }
    }

    // False with the error the edit's answer carries (s_ugcmap_ok for none) when the cube is not paid.
    public bool PurchaseCube(FurnishingShopTable.Entry furnishingShopMetadata, out UgcMapError error) {
        error = UgcMapError.s_ugcmap_ok;
        if (!furnishingShopMetadata.Buyable) {
            return false;
        }

        if (furnishingShopMetadata.Price <= 0) {
            return true;
        }

        lock (session.Item) {
            long negAmount;
            switch (furnishingShopMetadata.FurnishingTokenType) {
                case FurnishingCurrencyType.Meso:
                    negAmount = -furnishingShopMetadata.Price;
                    if (session.Currency.CanAddMeso(negAmount) != negAmount) {
                        error = UgcMapError.s_err_ugcmap_not_enough_meso_balance;
                        return false;
                    }

                    session.Currency.Meso -= furnishingShopMetadata.Price;
                    break;
                case FurnishingCurrencyType.Meret:
                    negAmount = -furnishingShopMetadata.Price;
                    if (session.Currency.CanAddMeret(negAmount) != negAmount) {
                        error = UgcMapError.s_err_ugcmap_not_enough_merat_balance;
                        return false;
                    }

                    session.Currency.Meret -= furnishingShopMetadata.Price;
                    break;
            }
        }

        long mesos = HomeBuildRules.BuyCubeMesos(furnishingShopMetadata.FurnishingTokenType, furnishingShopMetadata.Price);
        if (mesos > 0) {
            session.ConditionUpdate(ConditionType.buy_cube, counter: mesos);
        }
        return true;
    }

    public long AddCube(HeldCube cube) {
        const int amount = 1;
        lock (session.Item) {
            int count = storage.Count;
            long itemUid = AddStorage(cube);
            if (itemUid == 0) {
                return 0;
            }

            // Sub-type 5 names the storage item by its uid (CSceneStateGame::OnFurnishingStorage), as KMS2 sends it.
            session.Send(FurnishingStoragePacket.Purchase(itemUid, amount));
            if (storage.Count != count) {
                session.Send(FurnishingStoragePacket.Count(storage.Count));
            }
            return itemUid;
        }
    }

    // TODO: NOTE - This should also be called for opening a furnishing box
    public long AddCube(int id) {
        const int amount = 1;
        lock (session.Item) {
            int count = storage.Count;
            long itemUid = AddStorage(id);
            if (itemUid == 0) {
                return 0;
            }

            session.Send(FurnishingStoragePacket.Purchase(itemUid, amount));
            if (storage.Count != count) {
                session.Send(FurnishingStoragePacket.Count(storage.Count));
            }
            return itemUid;
        }
    }

    public long AddCube(Item item) {
        lock (session.Item) {
            int count = storage.Count;
            long itemUid = AddStorage(item);
            if (itemUid == 0) {
                return 0;
            }

            session.Send(FurnishingStoragePacket.Purchase(itemUid, item.Amount));
            if (storage.Count != count) {
                session.Send(FurnishingStoragePacket.Count(storage.Count));
            }

            return itemUid;
        }
    }

    // Whether storage takes all these cubes (UgcMapPackageRules.StorageTakes).
    public bool StorageTakes(IEnumerable<UgcMapPackageRules.Stack> incoming) {
        lock (session.Item) {
            return UgcMapPackageRules.StorageTakes(storage.Select(item => new UgcMapPackageRules.Stack(item.Id, item.Template?.Url,
                item.Metadata.Housing?.MaidId > 0, item.Amount, item.Metadata.Property.SlotMax)), storage.OpenSlots, incoming);
        }
    }

    private static UgcMapPackageRules.Stack ToStack(Item item) =>
        new(item.Id, item.Template?.Url, item.Metadata.Housing?.MaidId > 0, item.Amount, item.Metadata.Property.SlotMax);

    // Whether storage takes every piece of these furnishing items, each item's amount as TakeMailed adds it (the
    // same rule InventoryManager.Add uses: all of them or none). A furnishing item makes no second stack in storage
    // (AddStorage), so more pieces than its stack size never fit, and each piece of a maid item takes a slot; such
    // amounts are refused before they are placed one by one.
    public bool Takes(IEnumerable<Item> items) => Takes(items.Select(item => (item, item.Amount)));

    public bool Takes(Item item, int amount) => Takes([(item, amount)]);

    private bool Takes(IEnumerable<(Item Item, int Amount)> items) {
        lock (session.Item) {
            var pieces = new List<UgcMapPackageRules.Stack>();
            foreach ((Item item, int amount) in items) {
                bool maidItem = item.Metadata.Housing?.MaidId > 0;
                if (amount <= 0 || amount > (maidItem ? storage.OpenSlots : Math.Max(1, item.Metadata.Property.SlotMax))) {
                    return false;
                }
                pieces.AddRange(Enumerable.Repeat(ToStack(item) with { Amount = 1 }, amount));
            }
            return StorageTakes(pieces);
        }
    }

    // Where the cubes deleted from a plot (GameStorage.Request.SaveCubes) go back: into furnishing storage as
    // AddStorage adds them one at a time (UgcMapPackageRules.Place), and the ones storage does not take by mail to
    // characterId. Chosen here: the mailed cubes of the same item and design share an item up to its stack size
    // (one mail each); a maid cube is mailed alone, with its maid. Nothing in storage changes here (ApplyReturn does,
    // once the save went through). Null when an item cannot be made (no field).
    public CubeReturn? PlanReturn(IReadOnlyList<PlotCube> cubes, long characterId) {
        lock (session.Item) {
            Item[] stored = storage.ToArray();
            List<UgcMapPackageRules.Placement> places = UgcMapPackageRules.Place(stored.Select(ToStack), storage.OpenSlots,
                cubes.Select(cube => new UgcMapPackageRules.Stack(cube.ItemId, cube.Template?.Url, cube.Metadata.Housing?.MaidId > 0, 1, cube.Metadata.Property.SlotMax)));
            var plan = new CubeReturn { AccountId = session.AccountId, CharacterId = characterId };
            var joins = new Dictionary<int, (int Amount, long MaidCubeId)>();
            var made = new Dictionary<int, CubeReturn.Add>();
            var mailed = new Dictionary<(int ItemId, string? Design), CubeReturn.Add>();
            for (int i = 0; i < cubes.Count; i++) {
                PlotCube cube = cubes[i];
                long maidCubeId = cube.Metadata.Housing?.MaidId > 0 ? cube.Id : 0;
                UgcMapPackageRules.Placement place = places[i];
                if (place.Kind == UgcMapPackageRules.PlaceKind.Join && place.Index < stored.Length) {
                    (int amount, long maid) = joins.TryGetValue(place.Index, out (int, long) join) ? join : (stored[place.Index].Amount, 0);
                    joins[place.Index] = (amount + 1, maidCubeId != 0 ? maidCubeId : maid);
                    continue;
                }
                if (place.Kind == UgcMapPackageRules.PlaceKind.Join) {
                    made[place.Index].Item.Amount++;
                    continue;
                }
                if (place.Kind == UgcMapPackageRules.PlaceKind.Mail && maidCubeId == 0
                    && mailed.TryGetValue((cube.ItemId, MaidRules.DesignKey(cube.Template?.Url)), out CubeReturn.Add? share)
                    && share.Item.Amount < Math.Max(1, cube.Metadata.Property.SlotMax)) {
                    share.Item.Amount++;
                    continue;
                }

                Item? item = session.Field?.ItemDrop.CreateItem(cube.ItemId);
                if (item == null) {
                    logger.Error("Cannot make item {ItemId} to give back cube {CubeId}", cube.ItemId, cube.Id);
                    return null;
                }
                item.Amount = 1;
                if (cube.Template != null) {
                    item.Template = cube.Template.Clone();
                }
                var add = new CubeReturn.Add(item, maidCubeId);
                if (place.Kind == UgcMapPackageRules.PlaceKind.NewStack) {
                    item.Group = ItemGroup.Furnishing;
                    made[place.Index] = add;
                    plan.NewStacks.Add(add);
                    continue;
                }
                plan.Mailed.Add(add);
                if (maidCubeId == 0) {
                    mailed[(cube.ItemId, MaidRules.DesignKey(cube.Template?.Url))] = add;
                }
            }
            foreach ((int index, (int amount, long maidCubeId)) in joins) {
                plan.Joins.Add(new CubeReturn.Join(stored[index], amount, maidCubeId));
            }
            return plan;
        }
    }

    // Puts what a save of plan wrote into storage and the client: the stacks' new amounts, the new storage items,
    // the moved maids, and the deleted cubes leave the furnishing inventory.
    public void ApplyReturn(CubeReturn plan, IEnumerable<long> deletedCubes) {
        lock (session.Item) {
            int count = storage.Count;
            foreach (CubeReturn.Join join in plan.Joins) {
                int previous = join.Stack.Amount;
                join.Stack.Amount = join.Amount;
                session.Send(previous == 0
                    ? FurnishingStoragePacket.Add(join.Stack)
                    : FurnishingStoragePacket.Update(join.Stack.Uid, join.Stack.Amount));
            }
            foreach (Item item in plan.Created) {
                if (storage.Add(item).Count <= 0) {
                    // Cannot happen: Place counted the open slots under this lock. The row is in storage at the next login.
                    logger.Error("Returned furnishing {Uid} has no storage slot", item.Uid);
                    continue;
                }
                session.Send(FurnishingStoragePacket.Add(item));
            }
            if (storage.Count != count) {
                session.Send(FurnishingStoragePacket.Count(storage.Count));
            }
            foreach (long cubeId in deletedCubes) {
                RemoveInventory(cubeId, out _);
            }
            foreach (Maid maid in plan.MovedMaids) {
                session.Send(UserMaidPacket.Add(maid));
                if (MaidRules.IsCrafting(maid)) {
                    session.Send(MaidCraftItemPacket.Add(maid));
                }
            }
            // A maid that left by mail: its cube is gone; the maid comes back when the mail is collected (TakeMailed).
            foreach (CubeReturn.Add add in plan.Mailed.Where(add => add.MaidCubeId != 0)) {
                session.Send(UserMaidPacket.Remove(add.MaidCubeId));
                session.Send(MaidCraftItemPacket.Remove(add.MaidCubeId));
            }
        }
    }

    // Drops cubes from the furnishing inventory that have no row (placed in memory by a save that failed).
    public void ForgetCubes(IEnumerable<long> cubeIds) {
        lock (session.Item) {
            foreach (long cubeId in cubeIds) {
                RemoveInventory(cubeId, out _);
            }
        }
    }

    // A furnishing item collected from mail goes into storage, all of it or none: false when storage does not take
    // every piece (the mail keeps it). A maid item takes the maid the mailed item holds (CubeReturn) along.
    public bool TakeMailed(Item item) {
        lock (session.Item) {
            bool maidItem = item.Metadata.Housing?.MaidId > 0;
            if (!Takes(item, item.Amount)) {
                return false;
            }
            int count = storage.Count;
            long itemUid = 0;
            int amount = item.Amount;
            var added = new List<long>(amount);
            for (int i = 0; i < amount; i++) {
                Item piece = item.Clone();
                piece.Amount = 1;
                itemUid = AddStorage(piece, item.Template);
                if (itemUid == 0) {
                    // Only a failed item write gets here (Takes checked the room under the lock). The pieces
                    // already stored are taken out again, so the caller, which keeps or mails the item, does not
                    // give them twice. The rows they touched are written back now: a row AddStorage created holds
                    // its piece in the database, which a crash before the next storage save gave next to the mail.
                    logger.Error("Furnishing {ItemId} from mail did not go into storage ({Added} of {Amount})", item.Id, i, amount);
                    for (int j = added.Count - 1; j >= 0; j--) {
                        TakePiece(added[j]);
                    }
                    Item[] undone = added.Distinct().Select(uid => storage.Get(uid)).OfType<Item>().ToArray();
                    if (undone.Length > 0) {
                        using GameStorage.Request db = session.GameStorage.Context();
                        if (!db.SaveItems(session.AccountId, undone)) {
                            logger.Error("Furnishing {ItemId}: the pieces taken back were not written", item.Id);
                        }
                    }
                    if (storage.Count != count) {
                        session.Send(FurnishingStoragePacket.Count(storage.Count));
                    }
                    return false;
                }
                added.Add(itemUid);
            }
            if (storage.Count != count) {
                session.Send(FurnishingStoragePacket.Count(storage.Count));
            }
            if (maidItem && item.Uid != 0) {
                using GameStorage.Request db = session.GameStorage.Context();
                Maid? moved = db.MoveMaid(item.Uid, itemUid);
                if (moved != null) {
                    session.Send(UserMaidPacket.Add(moved));
                    if (MaidRules.IsCrafting(moved)) {
                        session.Send(MaidCraftItemPacket.Add(moved));
                    }
                }
            }
            return true;
        }
    }

    // Takes one piece AddStorage put on a stored item back off it: the stack is one less, a stack of one is removed.
    private void TakePiece(long itemUid) {
        Item? stored = storage.Get(itemUid);
        if (stored == null) {
            return;
        }
        if (stored.Amount > 1) {
            stored.Amount--;
            session.Send(FurnishingStoragePacket.Update(stored.Uid, stored.Amount));
            return;
        }
        RemoveItem(itemUid);
    }

    // Returns the cube to storage: the uid of the storage item it went to, 0 when it was not in the inventory.
    public long RetrieveCube(long uid) {
        lock (session.Item) {
            if (!RemoveInventory(uid, out PlotCube? cube)) {
                return 0;
            }

            long itemUid = AddStorage(cube);
            if (itemUid == 0) {
                logger.Fatal("Failed to return cube: {CubeId} to storage", cube.Id);
                throw new InvalidOperationException($"Failed to return cube: {cube.Id} to storage");
            }

            return itemUid;
        }
    }

    // A cube with an expiry (one placed from a timed item; the server's own, HeldCube.ReadFrom keeps none from the
    // client) goes back with it, not with a new item's: it joins only a stack of the same expiry, or an empty one.
    private long AddStorage(HeldCube cube) {
        Item? item = session.Field?.ItemDrop.CreateItem(cube.ItemId);
        if (item == null) {
            return 0;
        }

        if (cube.ExpiryTime > 0) {
            item.ExpiryTime = cube.ExpiryTime;
            return AddStorage(item, cube.Template, keepExpiry: true);
        }
        return AddStorage(item, cube.Template);
    }

    private long AddStorage(int itemId) {
        Item? item = session.Field?.ItemDrop.CreateItem(itemId);
        if (item == null) {
            return 0;
        }

        return AddStorage(item);
    }

    public long AddStorage(Item item, UgcItemLook? template = null, bool keepExpiry = false) {
        const int amount = 1;

        lock (session.Item) {
            bool maidItem = item.Metadata.Housing?.MaidId > 0;
            Item? stored = keepExpiry
                // A stack of the same expiry first, else an empty one, which takes the expiry.
                ? storage.Where(existing => MaidRules.CanJoin(existing, item.Id, template?.Url, maidItem) &&
                                            (existing.ExpiryTime == item.ExpiryTime || existing.Amount == 0))
                    .OrderBy(existing => existing.ExpiryTime == item.ExpiryTime ? 0 : 1).FirstOrDefault()
                : storage.FirstOrDefault(existing => MaidRules.CanJoin(existing, item.Id, template?.Url, maidItem));
            if (stored == null) {
                if (storage.OpenSlots <= 0) {
                    logger.Error("Furnishing storage is full, cannot add item: {ItemId}", item.Id);
                    return 0;
                }
                item.Group = ItemGroup.Furnishing;
                // A designed cube keeps its design in storage (the stack is joined by it, and a cube placed from it
                // takes it).
                if (template != null) {
                    item.Template = template.Clone();
                }
                using GameStorage.Request db = session.GameStorage.Context();
                Item? newItem = db.CreateItem(session.AccountId, item);
                if (newItem == null) {
                    return 0;
                }

                if (storage.Add(newItem).Count <= 0) {
                    db.SaveItems(0, newItem);
                    return 0;
                }

                session.Send(FurnishingStoragePacket.Add(newItem));
                return newItem.Uid;
            }

            if (stored.Amount + amount > item.Metadata.Property.SlotMax) {
                return 0;
            }

            int previousAmount = stored.Amount;
            stored.Amount += amount;
            if (keepExpiry) {
                stored.ExpiryTime = item.ExpiryTime;
            }
            if (previousAmount == 0) {
                session.Send(FurnishingStoragePacket.Add(stored));
                return stored.Uid;
            }

            session.Send(FurnishingStoragePacket.Update(stored.Uid, stored.Amount));
            return stored.Uid;
        }
    }

    private bool AddInventory(PlotCube cube) {
        if (!inventory.TryAdd(cube.Id, cube)) {
            return false;
        }

        session.Send(FurnishingInventoryPacket.Add(cube));
        return true;
    }

    private bool RemoveInventory(long uid, [NotNullWhen(true)] out PlotCube? cube) {
        if (!inventory.Remove(uid, out cube)) {
            return false;
        }

        session.Send(FurnishingInventoryPacket.Remove(uid));
        return true;
    }

    public void RemoveItem(long itemUid) {
        lock (session.Item) {
            Item? item = storage.Get(itemUid);
            if (item is null) {
                return;
            }

            item.Amount = 0;

            session.Send(FurnishingStoragePacket.Remove(itemUid));
            // A stored maid item takes its maid with it.
            if (item.Metadata.Housing?.MaidId > 0) {
                using GameStorage.Request db = session.GameStorage.Context();
                if (db.DeleteMaid(itemUid)) {
                    session.Send(UserMaidPacket.Remove(itemUid));
                    session.Send(MaidCraftItemPacket.Remove(itemUid)); // its craft, if any, goes too
                }
            }
        }
    }

    public void SendStorageCount() {
        lock (session.Item) {
            session.Send(FurnishingStoragePacket.Count(storage.Count));
        }
    }

    public void Save(GameStorage.Request db) {
        lock (session.Item) {
            db.SaveItems(session.AccountId, storage.ToArray());
        }
    }
}
