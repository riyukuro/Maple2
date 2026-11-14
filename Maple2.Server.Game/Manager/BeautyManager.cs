using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Serilog;

namespace Maple2.Server.Game.Manager;

public sealed class BeautyManager : IDisposable {
    private readonly GameSession session;
    private readonly ItemCollection items;
    private Item? previousHair;
    public BeautyManager(GameSession session) {
        this.session = session;
        using GameStorage.Request db = session.GameStorage.Context();
        ICollection<Item> saved = db.GetSavedHairs(session.CharacterId);
        // Styles saved under an older, larger base stay listed; OpenSlots counts by the rule, not by this size.
        items = new ItemCollection((short) Math.Max(Slots, saved.Count));
        foreach (Item item in saved.Where(item => items.Add(item).Count == 0)) {
            Log.Error("Failed to add saved hair:{Uid}", item.Uid);
        }
    }

    public void Dispose() {
        using GameStorage.Request db = session.GameStorage.Context();
        db.SaveItems(session.CharacterId, items.ToArray());
    }

    public void Load() {
        session.Send(BeautyPacket.StartList());
        session.Send(BeautyPacket.SaveSlots(session.Player.Value.Unlock.HairSlotExpand));
        session.Send(BeautyPacket.ListCount(items.Count));
        if (items.Count > 0) {
            session.Send(BeautyPacket.ListHair(items.OrderBy(hair => hair.CreationTime).ToList()));
            ;
        }
    }

    private int Slots {
        get {
            ConstantsTable constants = session.ServerTableMetadata.ConstantsTable;
            return BeautyRules.StyleSlots(constants.BeautyStyleDefaultSlotCount, session.Player.Value.Unlock.HairSlotExpand, constants.BeautyStyleMaxSlotCount);
        }
    }

    // The saved style slots the character owns, and the styles saved in them.
    public int SlotCount => Slots;
    public int SavedCount => items.Count;

    public short OpenSlots {
        get {
            ConstantsTable constants = session.ServerTableMetadata.ConstantsTable;
            return (short) BeautyRules.OpenStyleSlots(constants.BeautyStyleDefaultSlotCount, session.Player.Value.Unlock.HairSlotExpand, constants.BeautyStyleMaxSlotCount, items.Count);
        }
    }

    // The equipped hair. A saved style is a copy with its own uid, so the worn hair can be saved again; the client
    // sends the worn hair's uid each time and has no text for a style saved twice (uibeautyshopstyledialog.cpp:1407).
    public bool CanAddHair(long uid) {
        Item? hair = session.Item.GetOutfit(uid);
        return hair != null && hair.Type.IsHair;
    }

    // Beauty modes 17 and 19: one extension of the saved style slots, BeautyStyleExpandSlotCount1time slots
    // for BeautyStyleExpandSlotPrice merets, up to BeautyStyleMaxSlotCount extra slots (the client's own
    // test and price box, uibeautyshopstyledialog.cpp:986-1007). The client adds the slots on SaveSlots.
    public bool ExtendSlots() {
        ConstantsTable constants = session.ServerTableMetadata.ConstantsTable;
        short owned = session.Player.Value.Unlock.HairSlotExpand;
        long price = constants.BeautyStyleExpandSlotPrice;
        switch (BeautyRules.ExtendCheck(owned, constants.BeautyStyleExpandSlotCount1time, constants.BeautyStyleMaxSlotCount, session.Currency.Meret, price)) {
            case BeautyRules.ExtendStep.AtMax:
                session.Send(BeautyPacket.Error(BeautyError.s_beauty_msg_error_style_slot_extend_max));
                return false;
            case BeautyRules.ExtendStep.NoMeret:
                session.Send(BeautyPacket.Error(BeautyError.s_err_lack_merat_ask));
                return false;
        }
        int extended = BeautyRules.ExtendedSlots(owned, constants.BeautyStyleExpandSlotCount1time, constants.BeautyStyleMaxSlotCount);

        // The merets and the extension are stored together: the extension used to be kept in memory only, so a crash
        // before the next session save kept the merets taken and lost the slots.
        if (!session.Currency.TryTakeMeret(price, notify: false)) {
            session.Send(BeautyPacket.Error(BeautyError.s_err_lack_merat_ask));
            return false;
        }
        if (!session.TrySavePurchase(db => db.SaveHairSlotExpand(session.Player.Value, (short) extended))) {
            session.Currency.ReturnMeret(price);
            Log.Error("Style slot extension of {CharacterId} not stored; merets given back", session.CharacterId);
            // The style window's default code shows the system error box and closes (uibeautyshopstyledialog.cpp:1360).
            session.Send(BeautyPacket.Error(BeautyError.s_beauty_msg_error_code));
            return false;
        }
        session.Player.Value.Unlock.HairSlotExpand = (short) extended;
        items.Expand((short) Slots);
        session.Currency.NotifyMeretTaken(price);
        session.Send(BeautyPacket.SaveSlots((short) extended));
        return true;
    }

    public void AddHair(long uid, bool extended = false) {
        Item? hair = session.Item.GetOutfit(uid);
        if (hair == null || !hair.Type.IsHair) {
            return;
        }

        if (OpenSlots <= 0) {
            session.Send(BeautyPacket.Error(BeautyError.s_beauty_msg_error_style_slot_max));
            return;
        }

        Item? hairCopy = hair.Clone();
        hairCopy.Group = ItemGroup.SavedHair;
        using GameStorage.Request db = session.GameStorage.Context();
        hairCopy = db.CreateItem(session.CharacterId, hairCopy);
        if (hairCopy == null) {
            return;
        }
        items.Add(hairCopy);
        session.Send(BeautyPacket.SaveHair(hair, hairCopy, extended));
        session.ConditionUpdate(ConditionType.beauty_style_add, codeLong: hairCopy.Id);
    }

    public bool RemoveHair(long uid) {
        if (!items.Remove(uid, out Item? hair)) {
            return false;
        }

        session.Item.Inventory.Discard(hair);
        session.Send(BeautyPacket.DeleteHair(uid));
        return true;
    }

    public void SavePreviousHair(Item? prevHair) => previousHair = prevHair;

    public void SelectPreviousHair() {
        if (previousHair == null) {
            return;
        }

        // Making a copy due to previous hair being added to delete collection
        Item? previousHairCopy = previousHair.Clone();
        using GameStorage.Request db = session.GameStorage.Context();
        previousHairCopy = db.CreateItem(session.CharacterId, previousHairCopy);
        if (previousHairCopy == null) {
            return;
        }
        session.Item.Equips.EquipCosmetic(previousHairCopy, EquipSlot.HR);
    }

    public void ClearPreviousHair() => previousHair = null;

    // A random pick (sub-type 7) is waiting for its keep or return answer (sub-type 12).
    public bool HasPreviousHair => previousHair != null;

    public bool HasSavedCosmetic(long uid) => items.Get(uid) != null;

    // The hairs bought this session at a SaleBeautyShop price, with the shop whose sale lowered it: a save of one is
    // refused while that shop's sale still runs (BeautyHandler, error 25). Not stored: a hair bought before a relog saves.
    private readonly Dictionary<long, int> discountedHairs = [];

    public void MarkDiscounted(long uid, int shopId) {
        lock (discountedHairs) {
            discountedHairs[uid] = shopId;
        }
    }

    public int? DiscountedShop(long uid) {
        lock (discountedHairs) {
            return discountedHairs.TryGetValue(uid, out int shopId) ? shopId : null;
        }
    }

    // A new copy of saved style uid to wear (no uid yet), or null for a uid that is no saved style of one slot. The
    // caller stores it with the fee (BeautyHandler, sub-type 21).
    public Item? SavedCopy(long uid) {
        Item? cosmetic = items.Get(uid);
        if (cosmetic == null || cosmetic.Metadata.SlotNames.Length != 1) {
            return null;
        }

        // Clone() keeps the saved style's uid; the store gives the copy its own.
        Item copy = cosmetic.Clone();
        copy.Group = ItemGroup.Default;
        return copy;
    }
}
