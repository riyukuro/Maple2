using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Serilog;

namespace Maple2.Server.Game.Manager;

public sealed class PetManager : IDisposable {
    private readonly GameSession session;
    private readonly FieldPet pet;
    private readonly ItemCollection items;

    public Item Pet => pet.Pet;
    private readonly PetConfig config;
    // The collection entry this summon made (pet id, rarity), sent once after the first load.
    private (int Id, short Rarity)? newCollection;

    private ItemPet ItemPet => Pet.Pet!;
    public int OwnerId => session.Player.ObjectId;
    // The summoned pet in the field: its object id (the picker a pet's pickup names), where the server holds it,
    // and how far it may trail its owner before it follows (the client's pet is that much behind the server's).
    public int ObjectId => pet.ObjectId;
    public Vector3 Position => pet.Position;
    public float FollowDistance => PetFollow.Distances(pet.Metadata.Distance).Follow;

    // The owner's state sync on the ground (FieldPlayer.OnStateSync): a pet summoned on the field entry goes there.
    public void OwnerLanded(Vector3 ground) => pet.OwnerLanded(ground, Environment.TickCount64);

    public PetManager(GameSession session, FieldPet pet) {
        this.session = session;
        this.pet = pet;

        short itemSlots = 0;
        if (session.ItemMetadata.TryGetPet(pet.Value.Id, out PetMetadata? metadata) && metadata.Type == 0) {
            itemSlots = (short) metadata.ItemSlots;
        }

        items = new ItemCollection(itemSlots);

        // AddOrUpdate Pet Collection
        bool firstCollect = !session.Player.Value.Unlock.Pets.TryGetValue(pet.Value.Id, out short rarity);
        if (firstCollect || rarity < Pet.Rarity) {
            session.Player.Value.Unlock.Pets[pet.Value.Id] = (short) Pet.Rarity;
            newCollection = (pet.Value.Id, (short) Pet.Rarity);
        }
        // A pet new to the album: 23100276 "Collect {0} pet(s) in the Album" (pet_collect, target = the least grade)
        // and 23100377 "Add ... to your Pet Album" (pet_first_collect, code = the pet item). A better grade of a pet
        // already in the album adds no pet.
        if (firstCollect) {
            session.ConditionUpdate(ConditionType.pet_collect, targetLong: Pet.Rarity);
            session.ConditionUpdate(ConditionType.pet_first_collect, codeLong: pet.Value.Id);
        }

        using GameStorage.Request db = session.GameStorage.Context();
        config = db.GetPetConfig(Pet.Uid);
        foreach (Item item in db.GetStorage(Pet.Uid)) {
            if (items.Add(item).Count == 0) {
                Log.Error("Failed to add storage item:{Uid}", item.Uid);
            }
        }

        session.Field?.Broadcast(PetPacket.Summon(pet));
    }

    // The owner got a monster's kill exp: the pet gets its share (PetFusionRules.KillExp) and the owner is
    // sent the new exp (ResponsePet 10), then the new level if it rose. It runs on the field thread and holds the
    // item lock, as the fusion stone use, fusion and training do, so a gain in the same tick as one of those is kept.
    // Nothing that holds the item lock waits on the field thread, and this takes no other lock inside it.
    // A running PetBattleExp event raises the share (the highest rate, as CGameRule keeps its rates).
    public void AddKillExp(int npcLevel, int npcClass) {
        long gain = PetFusionRules.ApplyBattleExp(PetFusionRules.KillExp(npcLevel, npcClass),
            EventSaleRules.HighestRate(session.FindEvent(GameEventType.PetBattleExp)));
        if (gain <= 0) {
            return;
        }

        lock (session.Item) {
            int maxLevel = session.ServerTableMetadata.ConstantsTable.PetMaxLevel;
            if (!PetFusionRules.IsTarget(pet.Metadata.Type, ItemPet.Level, maxLevel)) {
                return;
            }

            int oldLevel = ItemPet.Level;
            (int level, long exp) = PetFusionRules.Apply(session.TableMetadata.PetExpTable, ItemPet.Level, ItemPet.Exp, Pet.Rarity,
                Pet.Metadata.Limit.Level, gain, maxLevel);
            ItemPet.Level = (short) level;
            ItemPet.Exp = exp;
            session.Send(PetPacket.Fusion(OwnerId, Pet));
            if (level != oldLevel) {
                session.Send(PetPacket.LevelUp(OwnerId, Pet));
                session.Player.Stats.Refresh();
                SendItemUpdate();
            }
        }
    }

    // summoned: the owner just called the pet (not a field change). KMS2 then sends, after the pet's state,
    // the new collection entry when the summon made one and the "pet summoned" notice; the client prints the
    // dismissal line itself (CPetManager::OnPetUnsummoned) but not this one.
    public void Load(bool summoned = false) {
        foreach (ByteWriter packet in LoadPackets(OwnerId, ItemPet, config, summoned ? newCollection : null, summoned)) {
            session.Send(packet);
        }
        if (summoned) {
            newCollection = null;
        }
    }

    public static IEnumerable<ByteWriter> LoadPackets(int ownerId, ItemPet pet, PetConfig config, (int Id, short Rarity)? collection, bool summoned) {
        yield return PetPacket.Load(ownerId, pet, config);
        if (collection != null) {
            yield return PetPacket.AddCollection(collection.Value.Id, collection.Value.Rarity);
        }
        if (summoned) {
            yield return NoticePacket.Notice(NoticePacket.Flags.Message | NoticePacket.Flags.Alert, StringCode.s_pet_summon_on);
        }
    }

    public void LoadInventory() {
        lock (session.Item) {
            foreach (ByteWriter packet in LoadInventoryPackets(items.ToList())) {
                session.Send(packet);
            }
        }
    }

    // KMS2 answers the bag request with the reset (the client clears its slots on it; it only adds on the item
    // list), then the item list when the bag holds something.
    public static IEnumerable<ByteWriter> LoadInventoryPackets(ICollection<Item> bag) {
        yield return PetInventoryPacket.Reset();
        if (bag.Count > 0) {
            yield return PetInventoryPacket.Load(bag);
        }
    }

    public StringCode Add(long uid, short slot, int amount) {
        lock (session.Item) {
            Item? deposit = session.Item.Inventory.Get(uid);
            // The count is the client's: the inventory's Remove takes a negative one as the whole stack, and the
            // bag-full path below would then move more than fits.
            if (deposit == null || !StorageRules.IsCount(amount, deposit.Amount)) {
                return StringCode.s_item_err_invalid_count;
            }

            if (deposit.Pet != null) {
                return StringCode.s_pet_inventory_not_sendin_petitem;
            }

            // A survival match item stays in the inventory, as for storage (SurvivalItemRules, Chosen here).
            if (SurvivalItemRules.IsMatchOnly(deposit.Metadata)) {
                return StringCode.s_pet_inventory_not_sendin;
            }

            // The client does not ask with a summoned pet past its time (petmanager.cpp:533).
            if (Pet.IsExpired()) {
                return StringCode.s_empty_string;
            }

            if (items.OpenSlots == 0) {
                int remaining = items.GetStackResult(deposit, amount);
                if (amount == remaining) {
                    return StringCode.s_pet_inventory_not_sendin;
                }

                // Stack what we can and ignore the rest.
                amount -= remaining;
            }

            // The bag must take the whole count before anything leaves the inventory: an add the bag refused used to
            // drop the item taken out (items.Add returns nothing then), and the client is told it was not put in.
            Item probe = deposit.Clone();
            probe.Amount = amount;
            probe.Slot = slot;
            if (!items.TakesAll([probe])) {
                return StringCode.s_pet_inventory_not_sendin;
            }

            if (!session.Item.Inventory.Remove(uid, out deposit, amount)) {
                return StringCode.s_empty_string;
            }

            deposit.Slot = slot;
            IList<(Item, int Added)> result = items.Add(deposit, true);
            if (result.Count == 0) {
                // Not reached after TakesAll; the item goes back to the inventory rather than being lost.
                Log.Error("Pet bag refused deposit {Uid} of {CharacterId}; given back", deposit.Uid, session.CharacterId);
                deposit.Slot = -1;
                session.Item.Inventory.Add(deposit, commit: true);
                return StringCode.s_pet_inventory_not_sendin;
            }
            StoreDeposit(deposit, result.Select(entry => entry.Item1).ToArray());
            foreach ((Item item, int _) in result) {
                session.Send(deposit.Uid == item.Uid
                    ? PetInventoryPacket.Add(item)
                    : PetInventoryPacket.Update(item.Uid, item.Amount));
            }

            return StringCode.s_empty_string;
        }
    }

    // A deposit's rows are stored at once, in one transaction: the bag's stacks it went into, and the deposit's own row
    // at owner 0 when it merged away. Before, they waited for the unsummon: a part split off a stack sat at owner 0
    // until then (the split stores it so), and a stop before it lost that part.
    private void StoreDeposit(Item deposit, Item[] stacks) {
        using GameStorage.Request db = session.GameStorage.Context();
        db.BeginTransaction();
        if (db.SaveItems(Pet.Uid, stacks) && (deposit.Amount > 0 || db.SaveItems(0, deposit)) && db.Commit()) {
            return;
        }
        Log.Error("Pet bag deposit of {Uid} for {CharacterId} not stored; the unsummon stores the bag", deposit.Uid, session.CharacterId);
    }

    public StringCode Remove(long uid, short slot, int amount) {
        lock (session.Item) {
            Item? withdraw = items.Get(uid);
            if (withdraw == null || withdraw.Amount < amount) {
                return StringCode.s_item_err_invalid_count;
            }

            // The item leaves the pet's bag only when the owner's bag takes all of it.
            if (amount <= 0 || !session.Item.Inventory.CanAdd(withdraw, amount)) {
                return StringCode.s_err_inventory;
            }

            if (!RemoveInternal(uid, amount, out withdraw)) {
                return StringCode.s_empty_string;
            }

            withdraw.Slot = slot;
            session.Item.Inventory.Add(withdraw, commit: true);
            return StringCode.s_empty_string;
        }
    }

    /// <summary>
    /// Uses up pieces of an item in the pet's bag. The client casts an item's skill with the uid of the item it
    /// found, and it looks in the summoned pet's bag before the owner's (CPetManager::UseItemById).
    /// The item the pieces were taken from is given back in taken.
    /// </summary>
    public bool Consume(long uid, int amount, [NotNullWhen(true)] out Item? taken) {
        lock (session.Item) {
            switch (TakePieces(items, uid, amount, out Item? item)) {
                case PieceResult.Reduced:
                    session.Send(PetInventoryPacket.Update(uid, item!.Amount));
                    taken = item;
                    return true;
                case PieceResult.Removed:
                    session.Item.Inventory.Discard(item!);
                    session.Send(PetInventoryPacket.Remove(uid));
                    taken = item;
                    return true;
                default:
                    taken = null;
                    return false;
            }
        }
    }

    public bool Contains(long uid) => items.Contains(uid);

    public Item? Get(long uid) => items.Get(uid);

    internal enum PieceResult { None, Reduced, Removed }

    // Takes amount pieces off the item: the stack shrinks, or the item leaves the collection with its last piece.
    internal static PieceResult TakePieces(ItemCollection items, long uid, int amount, out Item? item) {
        item = items.Get(uid);
        if (item == null || amount <= 0 || item.IsExpired() || item.Amount < amount) {
            item = null;
            return PieceResult.None;
        }

        if (item.Amount > amount) {
            item.Amount -= amount;
            return PieceResult.Reduced;
        }

        return items.Remove(uid, out item) ? PieceResult.Removed : PieceResult.None;
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
                session.Send(PetInventoryPacket.Move(removeDst?.Uid ?? 0, srcSlot, uid, dstSlot));
            }

            return true;
        }
    }

    public void BadgeChanged(ItemBadge? badge) {
        pet.UpdateSkin(badge?.PetSkinId ?? 0);
    }

    public void Rename(string name) {
        if (!PetRules.IsName(name) || name == ItemPet.Name) {
            return;
        }

        int cost = PetRules.RenameCost(ItemPet.RenameRemaining, session.ServerTableMetadata.ConstantsTable.PetChangeNameMerat);
        if (cost > 0) {
            // The merets are held in memory, and the wallet and the pet's row with its new name stored in one
            // transaction. Before, the take and the name were left to the next session save.
            if (!session.Currency.TryTakeMeret(cost, notify: false)) {
                session.Send(PetPacket.Error(PetError.s_err_lack_merat_ask));
                return;
            }
            bool stored = session.TrySavePurchase(db => PacketHandlers.PetHandler.SavePetAs(session, db, Pet, item => {
                item.Pet!.Name = name;
                item.Pet.RenameRemaining = 0;
            }));
            if (!stored) {
                session.Currency.ReturnMeret(cost);
                session.Send(PetPacket.Error(PetError.s_common_error_unknown));
                return;
            }
            session.Currency.NotifyMeretTaken(cost);
        }

        ItemPet.Name = name;
        ItemPet.RenameRemaining = 0;

        session.Send(PetPacket.Rename(OwnerId, ItemPet));
        SendItemUpdate();
    }

    // The field's copies of the summoned pet (the owner's stat window counts its stats by its level) take the pet item
    // again (ResponsePet 21). Sent after a change to the summoned pet's level, name, evolution or options.
    public void SendItemUpdate() {
        session.Field?.Broadcast(PetPacket.ItemUpdate(OwnerId, Pet));
    }

    public static void SendItemUpdate(GameSession session, Item pet) {
        if (session.Pet is { } manager && manager.Pet.Uid == pet.Uid) {
            manager.SendItemUpdate();
        }
    }

    public void UpdatePotionConfig(PetPotionConfig[] potionConfig) {
        config.PotionConfig = potionConfig;

        session.Send(PetPacket.UpdatePotionConfig(OwnerId, config.PotionConfig));
    }

    public void UpdateLootConfig(PetLootConfig lootConfig) {
        config.LootConfig = lootConfig;

        session.Send(PetPacket.UpdateLootConfig(OwnerId, config.LootConfig));
    }

    /// <summary>
    /// Who gets the UnSummon packet. The client prints "You dismissed your pet." when its own player's pet is
    /// unsummoned, so a field change (the pet is summoned again in the next field) tells only the others.
    /// </summary>
    public static bool UnSummonToOwner(bool fieldChange) => !fieldChange;

    public void Dispose() => Dispose(false);

    /// <param name="fieldChange">The owner is leaving the field; the pet comes back in the next one.</param>
    // The pet's config and its bag, written in db's open transaction: the session's save (with the bag, after its
    // generation check) or the unsummon's own (SaveAlone).
    public bool Save(GameStorage.Request db) {
        lock (session.Item) {
            return db.SavePetConfig(Pet.Uid, config) & db.SaveItems(Pet.Uid, items.ToArray());
        }
    }

    public void Dispose(bool fieldChange) {
        // Not after the save before a move stored it (GameSession.MoveStored): the next server may have loaded the
        // pet's bag already, and a write from here would land after that load. Made outside the bag lock (Save takes
        // it): SaveAlone locks the account row first, as the session's save does before it takes the bag lock.
        if (!session.MoveStored) {
            session.SaveAlone("Pet", Save);
        }
        lock (session.Item) {
            // The bag goes with the pet item (GameStorage reads it at load); the client's compose and evolve tabs read
            // it too (s_err_cannot_compose_petitem_hasitem, s_err_cannot_evolution_petitem_hasitem).
            bool hasItems = items.Count > 0;
            if (ItemPet.HasItems != hasItems) {
                ItemPet.HasItems = hasItems;
                session.Send(ItemInventoryPacket.UpdateItem(Pet));
            }
        }

        session.Field?.RemovePet(pet.ObjectId);
        session.Field?.Broadcast(PetPacket.UnSummon(pet), UnSummonToOwner(fieldChange) ? null : session);
        session.Pet = null;
    }

    #region Internal (No Locks)
    private bool RemoveInternal(long uid, int amount, [NotNullWhen(true)] out Item? removed) {
        if (amount > 0) {
            Item? item = items.Get(uid);
            if (item == null || item.Amount < amount) {
                session.Send(NoticePacket.MessageBox(StringCode.s_item_err_invalid_count));
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

                session.Send(PetInventoryPacket.Update(uid, item.Amount));
                return true;
            }
        }

        // Full remove of item
        if (items.Remove(uid, out removed)) {
            session.Send(PetInventoryPacket.Remove(uid));
            return true;
        }

        return false;
    }
    #endregion
}
