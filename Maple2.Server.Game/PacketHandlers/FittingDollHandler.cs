using System.Collections.Concurrent;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

// FittingDoll 0x0042, the home mannequin window. Only the owner of the mannequin's plot dresses it
// (s_fittingdoll_error_invalid_owner); results go to the field as FittingDoll 0x7B modes 6 and 7, refusals to the
// sender as mode 0xb.
public class FittingDollHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.FittingDoll;

    private enum Command : byte {
        PutOn = 2, // InvenSlot::FUN_141405a60 (invenslot.cpp:675): i64 doll uid, i64 item uid, u8 slot
        Transform = 3, // UIFittingDollDialog (uifittingdolldialog.cpp:180): i64 doll uid
        PutOff = 7, // fittingdollslot_2.cpp:369: i64 doll uid, u8 slot
        Destroy = 9, // networkservice.cpp:520, uimessagebox.cpp:1433 and mypc.cpp:4095: i64 doll uid, i64 item uid
        ReturnAll = 10, // mypc.cpp:4118, before the doll is recalled: i64 doll uid
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = (Command) packet.ReadByte();
        long dollUid = packet.ReadLong();
        switch (command) {
            case Command.PutOn:
                long itemUid = packet.ReadLong();
                var slot = (EquipSlot) packet.ReadByte();
                Send(session, PutOn(session, dollUid, itemUid, slot));
                return;
            case Command.Transform:
                Send(session, Transform(session, dollUid));
                return;
            case Command.PutOff:
                Send(session, PutOff(session, dollUid, (EquipSlot) packet.ReadByte()));
                return;
            case Command.Destroy:
                Send(session, Destroy(session, dollUid, packet.ReadLong()));
                return;
            case Command.ReturnAll:
                FieldFittingDoll? doll = session.Field?.GetFittingDoll(dollUid);
                if (doll == null) {
                    Send(session, FittingDollRules.Error.InvalidDoll);
                } else if (doll.Plot.OwnerId != session.AccountId) {
                    Send(session, FittingDollRules.Error.InvalidOwner);
                } else {
                    ReturnItems(session, dollUid);
                }
                return;
            default:
                Logger.Debug("FittingDoll {Command} for doll {DollUid} not handled", command, dollUid);
                return;
        }
    }

    private static void Send(GameSession session, FittingDollRules.Error error) {
        if (error != FittingDollRules.Error.None) {
            session.Send(FittingDollPacket.Error(error));
        }
    }

    private static FittingDollRules.Error PutOn(GameSession session, long dollUid, long itemUid, EquipSlot slot) {
        FieldFittingDoll? doll = session.Field?.GetFittingDoll(dollUid);
        if (doll == null) {
            return FittingDollRules.Error.InvalidDoll;
        }
        if (doll.Plot.OwnerId != session.AccountId) {
            return FittingDollRules.Error.InvalidOwner;
        }

        lock (session.Item) {
            lock (doll.Lock) {
                if (doll.Removed) {
                    return FittingDollRules.Error.InvalidDoll;
                }
                Reload(session, doll);
                Item? item = session.Item.Inventory.Get(itemUid);
                if (item == null || (item.ExpiryTime > 0 && item.ExpiryTime <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())) {
                    return FittingDollRules.Error.InvalidItem;
                }
                if (!FittingDollRules.SlotFits(slot, item.Metadata.SlotNames)) {
                    return FittingDollRules.Error.InvalidSlot;
                }
                FittingDollRules.Error error = FittingDollRules.CanWear(doll.Data, item.Metadata.Property.IsSkin,
                    item.Metadata.Limit.Gender, FittingDollRules.BoundToOther(item.Transfer?.Binding, session.CharacterId));
                if (error != FittingDollRules.Error.None) {
                    return error;
                }

                // The items taken off go to the inventory; the item put on frees its own slot there.
                List<Item> displaced = FittingDollRules.Displaced(slot, item.Metadata.SlotNames, doll.Worn());
                foreach (IGrouping<InventoryType, Item> group in displaced.GroupBy(worn => worn.Inventory)) {
                    int free = session.Item.Inventory.FreeSlots(group.Key) + (group.Key == item.Inventory ? 1 : 0);
                    if (group.Count() > free) {
                        return FittingDollRules.Error.InventoryFull;
                    }
                }

                if (!session.Item.Inventory.Remove(itemUid, out item)) {
                    return FittingDollRules.Error.InvalidItem;
                }
                short inventorySlot = item.Slot;
                item.Group = ItemGroup.FittingDoll;
                item.Slot = (short) slot;
                ItemMove moved;
                using (GameStorage.Request db = session.GameStorage.Context()) {
                    moved = db.PutOnFittingDoll(item, doll.Uid, doll.Plot.Id);
                }
                if (moved != ItemMove.Moved) {
                    // Nothing was written: the item goes back where it was and the doll is unchanged. TargetGone is a
                    // doll whose cube was removed (and saved) in another field instance of this home.
                    item.Group = ItemGroup.Default;
                    item.Slot = inventorySlot;
                    if (!session.Item.AddOrMail(item)) {
                        Serilog.Log.Logger.Error("Failed to put item {ItemUid} back after a failed fitting doll save", item.Uid);
                    }
                    return moved == ItemMove.TargetGone ? FittingDollRules.Error.InvalidDoll : FittingDollRules.Error.InvalidItem;
                }

                foreach (Item worn in displaced) {
                    var wornSlot = (EquipSlot) worn.Slot;
                    doll.Items.Remove(wornSlot);
                    session.Field?.Broadcast(FittingDollPacket.PutOff(doll, wornSlot));
                    ToInventoryOrMail(session, doll.Uid, worn);
                }
                doll.Items[slot] = item;
                session.Field?.Broadcast(FittingDollPacket.PutOn(doll, item));
                return FittingDollRules.Error.None;
            }
        }
    }

    // Swaps the doll's items with the character's worn items of the doll's kind (gear or outfit). The client tests
    // the same before it asks (UIFittingDollDialog::CanWearEquipment): every doll item must be one the character may
    // wear, and every worn item one the doll may. The swap is one save: on any failure nothing moves, in memory or
    // in the DB, and the sender gets s_fittingdoll_failswap.
    private static FittingDollRules.Error Transform(GameSession session, long dollUid) {
        FieldFittingDoll? doll = session.Field?.GetFittingDoll(dollUid);
        if (doll == null) {
            return FittingDollRules.Error.InvalidDoll;
        }
        if (doll.Plot.OwnerId != session.AccountId) {
            return FittingDollRules.Error.InvalidOwner;
        }

        lock (session.Item) {
            lock (doll.Lock) {
                if (doll.Removed) {
                    return FittingDollRules.Error.InvalidDoll;
                }
                Reload(session, doll);
                bool outfit = doll.Data.Type != 0;
                ConcurrentDictionary<EquipSlot, Item> equips = outfit ? session.Item.Equips.Outfit : session.Item.Equips.Gear;

                var dollSide = new List<FittingDollRules.SwapEntry<Item>>();
                foreach ((EquipSlot slot, Item item) in doll.Items) {
                    bool moves = !FittingDollRules.BodyPart(item.Metadata.SlotNames);
                    if (moves) {
                        FittingDollRules.Error error = CanWearFromDoll(session, item);
                        if (error != FittingDollRules.Error.None) {
                            return error;
                        }
                    }
                    dollSide.Add(new(item, slot, item.Metadata.SlotNames, moves));
                }
                var characterSide = new List<FittingDollRules.SwapEntry<Item>>();
                foreach ((EquipSlot slot, Item item) in equips) {
                    ItemMetadataProperty property = item.Metadata.Property;
                    bool moves = FittingDollRules.Swaps(doll.Data, property.IsSkin, property.SubType, item.Metadata.SlotNames);
                    if (moves) {
                        FittingDollRules.Error error = FittingDollRules.CanWear(doll.Data, property.IsSkin, item.Metadata.Limit.Gender);
                        if (error != FittingDollRules.Error.None) {
                            return error;
                        }
                    }
                    characterSide.Add(new(item, slot, item.Metadata.SlotNames, moves));
                }

                (List<Item> toCharacter, List<Item> toDoll, bool partial) = FittingDollRules.Swap(dollSide, characterSide);
                if (toCharacter.Count == 0 && toDoll.Count == 0) {
                    session.Send(FittingDollPacket.Transform(doll, FittingDollRules.TransformResult.Nothing));
                    return FittingDollRules.Error.None;
                }

                // Changed only here, so a failed save can put each item back as it was.
                var before = toCharacter.Concat(toDoll).Select(item => (item, item.Group, item.Slot, item.Transfer?.Clone())).ToList();
                var dollSlots = toCharacter.ToDictionary(item => item.Uid, item => (EquipSlot) item.Slot);
                var bound = new List<Item>();
                Character character = session.Player.Value.Character;
                foreach (Item item in toCharacter) {
                    // Wearing binds, as an equip does (EquipManager.Equip).
                    if ((item.Metadata.Limit.TransferType is TransferType.BindOnEquip or TransferType.BindOnLoot) && item.Transfer?.Bind(character) == true) {
                        bound.Add(item);
                    }
                    item.Group = outfit ? ItemGroup.Outfit : ItemGroup.Gear;
                    item.Slot = (short) FittingDollRules.WornSlot((EquipSlot) item.Slot, item.Metadata.SlotNames);
                }
                foreach (Item item in toDoll) {
                    item.Group = ItemGroup.FittingDoll;
                }

                ItemMove moved;
                using (GameStorage.Request db = session.GameStorage.Context()) {
                    moved = db.SwapFittingDoll(doll.Uid, doll.Plot.Id, session.CharacterId, toDoll, toCharacter);
                }
                if (moved != ItemMove.Moved) {
                    foreach ((Item item, ItemGroup group, short slot, ItemTransfer? transfer) in before) {
                        item.Group = group;
                        item.Slot = slot;
                        item.Transfer = transfer;
                    }
                    if (moved == ItemMove.TargetGone) {
                        return FittingDollRules.Error.InvalidDoll;
                    }
                    if (moved == ItemMove.SourceGone) {
                        Reload(session, doll); // the doll was changed in another field instance of this home
                    }
                    return FittingDollRules.Error.FailSwap;
                }

                foreach (Item item in toDoll) {
                    equips.TryRemove((EquipSlot) item.Slot, out _);
                    session.Field?.Broadcast(EquipPacket.UnequipItem(session.Player, item));
                    session.Buffs.RemoveItemBuffs(item);
                }
                foreach (Item item in toCharacter) {
                    doll.Items.Remove(dollSlots[item.Uid]);
                    session.Field?.Broadcast(FittingDollPacket.PutOff(doll, dollSlots[item.Uid]));
                }
                foreach (Item item in toCharacter) {
                    equips[(EquipSlot) item.Slot] = item;
                    session.Field?.Broadcast(EquipPacket.EquipItem(session.Player, item, EquipKind.Gear));
                }
                foreach (Item item in bound) {
                    session.Send(ItemBindPacket.For(item, inBag: false));
                }
                foreach (Item item in toDoll) {
                    doll.Items[(EquipSlot) item.Slot] = item;
                    session.Field?.Broadcast(FittingDollPacket.PutOn(doll, item));
                }
                if (toCharacter.Any(item => item.Template != null)) {
                    session.ConditionUpdate(ConditionType.change_ugc_equip);
                }
                session.Stats.Refresh();
                session.Quest?.UpdateEquipped();
                session.Send(FittingDollPacket.Transform(doll, partial ? FittingDollRules.TransformResult.Partial : FittingDollRules.TransformResult.Done));
                return FittingDollRules.Error.None;
            }
        }
    }

    // A doll item the character is to wear (FittingDollPC::FUN_14121c870): one bound to another character is
    // refused as in a put; one the character may not use (Unk_Inventory::GetItemUseError: expired, gender, job,
    // level) is s_fittingdoll_Invaliditem_limit.
    private static FittingDollRules.Error CanWearFromDoll(GameSession session, Item item) {
        if (FittingDollRules.BoundToOther(item.Transfer?.Binding, session.CharacterId)) {
            return FittingDollRules.Error.InvalidBinding;
        }
        Gender gender = item.Metadata.Limit.Gender;
        if (EquipManager.ValidateEquipItem(session, item) != StringCode.s_empty_string
            || (gender != Gender.All && gender != session.Player.Value.Character.Gender)) {
            return FittingDollRules.Error.ItemLimit;
        }
        return FittingDollRules.Error.None;
    }

    private static FittingDollRules.Error PutOff(GameSession session, long dollUid, EquipSlot slot) {
        FieldFittingDoll? doll = session.Field?.GetFittingDoll(dollUid);
        if (doll == null) {
            return FittingDollRules.Error.InvalidDoll;
        }
        if (doll.Plot.OwnerId != session.AccountId) {
            return FittingDollRules.Error.InvalidOwner;
        }

        lock (session.Item) {
            lock (doll.Lock) {
                if (doll.Removed) {
                    return FittingDollRules.Error.InvalidDoll;
                }
                Reload(session, doll);
                if (!doll.Items.TryGetValue(slot, out Item? item)) {
                    return FittingDollRules.Error.InvalidItem;
                }
                // Another character of the account may not take an item bound to the character that put it on.
                if (FittingDollRules.BoundToOther(item.Transfer?.Binding, session.CharacterId)) {
                    return FittingDollRules.Error.InvalidBinding;
                }
                // The client refuses a take-off into a full inventory the same way (FittingDollPC::FUN_14121c9d0).
                if (session.Item.Inventory.FreeSlots(item.Inventory) <= 0) {
                    return FittingDollRules.Error.InventoryFull;
                }

                doll.Items.Remove(slot);
                session.Field?.Broadcast(FittingDollPacket.PutOff(doll, slot));
                return ToInventoryOrMail(session, doll.Uid, item) ? FittingDollRules.Error.None : FittingDollRules.Error.InvalidItem;
            }
        }
    }

    // Destroys a doll item bound to a character outside the plot owner's account, which no recall can give back to
    // anyone the owner plays. The client asks only for such an item (Unk_Inventory::FUN_140a66200, inventory.cpp:1661,
    // reasons 4 and 5: the binding's character is not in the account's character list): after the owner confirms
    // s_strDestroyDollItem on a take-off (uimessagebox.cpp:1433), and for each such item before a recall (mypc.cpp:4095,
    // then mode 10). It needs feature FittingDollEquipAll (0xd4), on in KR. The row is deleted only while it is still
    // on the cube; the field then gets the take-off (0x7B mode 7), as the client has no other answer for it.
    private FittingDollRules.Error Destroy(GameSession session, long dollUid, long itemUid) {
        FieldFittingDoll? doll = session.Field?.GetFittingDoll(dollUid);
        if (doll == null) {
            return FittingDollRules.Error.InvalidDoll;
        }
        if (doll.Plot.OwnerId != session.AccountId) {
            return FittingDollRules.Error.InvalidOwner;
        }

        lock (session.Item) {
            lock (doll.Lock) {
                if (doll.Removed) {
                    return FittingDollRules.Error.InvalidDoll;
                }
                Reload(session, doll);
                (EquipSlot slot, Item? item) = doll.Items.FirstOrDefault(entry => entry.Value.Uid == itemUid);
                if (item == null) {
                    return FittingDollRules.Error.InvalidItem;
                }
                long bound = item.Transfer?.Binding?.CharacterId ?? 0;
                if (bound == 0) {
                    return FittingDollRules.Error.InvalidItem; // unbound: taken off as any item
                }
                using (GameStorage.Request db = session.GameStorage.Context()) {
                    // A character of the account gets the item back with a take-off or a recall. A character that
                    // no longer exists is outside the account.
                    if (db.GetCharacterAccounts([bound]).TryGetValue(bound, out long accountId) && accountId == session.AccountId) {
                        return FittingDollRules.Error.InvalidBinding;
                    }
                    if (!db.DeleteFittingDollItem(item.Uid, doll.Uid)) {
                        Reload(session, doll); // taken off by the same home on another channel
                        return FittingDollRules.Error.InvalidItem;
                    }
                }

                Logger.Information("Account {AccountId} destroyed fitting doll {DollUid} item {ItemId} uid {ItemUid} bound to character {CharacterId}",
                    session.AccountId, doll.Uid, item.Id, item.Uid, bound);
                doll.Items.Remove(slot);
                session.Field?.Broadcast(FittingDollPacket.PutOff(doll, slot));
                return FittingDollRules.Error.None;
            }
        }
    }

    // Gives the owner back every item on the doll with this cube uid: to the inventory, or by mail when it is full
    // or the owner is not in a field. Used before a recall (mode 10) and when the cube is removed.
    public static void ReturnItems(GameSession owner, long cubeUid) {
        FieldFittingDoll? doll = owner.Field?.GetFittingDoll(cubeUid);
        lock (owner.Item) {
            lock (doll?.Lock ?? new object()) {
                List<Item> items = Rows(owner, cubeUid);
                if (doll != null) {
                    foreach (EquipSlot slot in doll.Items.Keys.ToList()) {
                        doll.Items.Remove(slot);
                        owner.Field?.Broadcast(FittingDollPacket.PutOff(doll, slot));
                    }
                }
                foreach (Item item in items) {
                    ToInventoryOrMail(owner, cubeUid, item);
                }
            }
        }
    }

    private static List<Item> Rows(GameSession session, long cubeUid) {
        using GameStorage.Request db = session.GameStorage.Context();
        return db.GetItemGroups(cubeUid, ItemGroup.FittingDoll).TryGetValue(ItemGroup.FittingDoll, out List<Item>? held) ? held : [];
    }

    // The DB rows are the authority: the same home open on another channel may have changed them. Entries that left
    // the cube are dropped, and new or changed ones shown, before the doll is changed here.
    private static void Reload(GameSession session, FieldFittingDoll doll) {
        var rows = new Dictionary<EquipSlot, Item>();
        foreach (Item row in Rows(session, doll.Uid)) {
            rows[(EquipSlot) row.Slot] = row;
        }
        foreach ((EquipSlot slot, Item held) in doll.Items.ToList()) {
            if (!rows.TryGetValue(slot, out Item? row) || row.Uid != held.Uid) {
                doll.Items.Remove(slot);
                session.Field?.Broadcast(FittingDollPacket.PutOff(doll, slot));
            }
        }
        foreach ((EquipSlot slot, Item row) in rows) {
            bool shown = doll.Items.ContainsKey(slot);
            doll.Items[slot] = row;
            if (!shown) {
                session.Field?.Broadcast(FittingDollPacket.PutOn(doll, row));
            }
        }
    }

    // Moves the row off the doll to the owner, only while it is still on this cube; false when another instance of
    // the field already took it (the memory entry was stale and nothing is given).
    private static bool ToInventoryOrMail(GameSession owner, long cubeUid, Item item) {
        item.Group = ItemGroup.Default;
        item.Slot = -1;
        // A recall or a removed cube gives an item bound to another character of the account back to that character:
        // the row becomes its own, in its inventory at its next login (an inventory save never deletes rows it does
        // not hold).
        ItemBinding? binding = item.Transfer?.Binding;
        bool toOther = FittingDollRules.BoundToOther(binding, owner.CharacterId);
        using (GameStorage.Request db = owner.GameStorage.Context()) {
            if (!db.MoveItem(item, cubeUid, ItemGroup.FittingDoll, toOther ? binding!.CharacterId : owner.CharacterId)) {
                return false;
            }
        }
        if (toOther) {
            return true;
        }
        if (owner.Field != null && owner.Item.Inventory.FreeSlots(item.Inventory) > 0 && owner.Item.Inventory.Add(item, commit: true)) {
            return true;
        }
        if (!owner.Item.MailHeldItem(item)) {
            // The row is the character's now (group Default), so it is in the inventory at the next login.
            Serilog.Log.Logger.Error("Failed to return fitting doll item {ItemUid} to {CharacterId}", item.Uid, owner.CharacterId);
        }
        return true;
    }
}
