using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

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
        UnbindItem = 9, // networkservice.cpp:520: i64 doll uid, i64 item uid
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
            case Command.PutOff:
                Send(session, PutOff(session, dollUid, (EquipSlot) packet.ReadByte()));
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
                Item? item = session.Item.Inventory.Get(itemUid);
                if (item == null || (item.ExpiryTime > 0 && item.ExpiryTime <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())) {
                    return FittingDollRules.Error.InvalidItem;
                }
                if (!FittingDollRules.SlotFits(slot, item.Metadata.SlotNames)) {
                    return FittingDollRules.Error.InvalidSlot;
                }
                FittingDollRules.Error error = FittingDollRules.CanWear(doll.Data, item.Metadata.Property.IsSkin, item.Metadata.Limit.Gender);
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
                item.Group = ItemGroup.FittingDoll;
                item.Slot = (short) slot;
                using (GameStorage.Request db = session.GameStorage.Context()) {
                    db.SaveItems(doll.Uid, item);
                }

                foreach (Item worn in displaced) {
                    var wornSlot = (EquipSlot) worn.Slot;
                    doll.Items.Remove(wornSlot);
                    session.Field?.Broadcast(FittingDollPacket.PutOff(doll, wornSlot));
                    ToInventoryOrMail(session, worn);
                }
                doll.Items[slot] = item;
                session.Field?.Broadcast(FittingDollPacket.PutOn(doll, item));
                return FittingDollRules.Error.None;
            }
        }
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
                if (!doll.Items.TryGetValue(slot, out Item? item)) {
                    return FittingDollRules.Error.InvalidItem;
                }
                // The client refuses a take-off into a full inventory the same way (FittingDollPC::FUN_14121c9d0).
                if (session.Item.Inventory.FreeSlots(item.Inventory) <= 0) {
                    return FittingDollRules.Error.InventoryFull;
                }

                item.Group = ItemGroup.Default;
                item.Slot = -1;
                if (!session.Item.Inventory.Add(item, commit: true)) {
                    item.Group = ItemGroup.FittingDoll;
                    item.Slot = (short) slot;
                    return FittingDollRules.Error.InventoryFull;
                }
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
                List<Item> items;
                using (GameStorage.Request db = owner.GameStorage.Context()) {
                    items = db.GetItemGroups(cubeUid, ItemGroup.FittingDoll).TryGetValue(ItemGroup.FittingDoll, out List<Item>? held) ? held : [];
                }
                if (doll != null) {
                    foreach (EquipSlot slot in doll.Items.Keys.ToList()) {
                        doll.Items.Remove(slot);
                        owner.Field?.Broadcast(FittingDollPacket.PutOff(doll, slot));
                    }
                }
                foreach (Item item in items) {
                    ToInventoryOrMail(owner, item);
                }
            }
        }
    }

    private static void ToInventoryOrMail(GameSession owner, Item item) {
        item.Group = ItemGroup.Default;
        item.Slot = -1;
        if (owner.Field != null && owner.Item.Inventory.FreeSlots(item.Inventory) > 0 && owner.Item.Inventory.Add(item, commit: true)) {
            return;
        }
        if (!owner.Item.MailHeldItem(item)) {
            Serilog.Log.Logger.Error("Failed to return fitting doll item {ItemUid} to {CharacterId}", item.Uid, owner.CharacterId);
        }
    }
}
