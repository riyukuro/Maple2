using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Model.Validators;

// The home mannequin (FittingDoll 0x42 / 0x7B). The result codes are the ones the client's doll notice reads
// (FUN_14121b840, client/ms2/fittingdollslot_1.cpp:201), each naming its s_fittingdoll_ string.
public static class FittingDollRules {
    // The client asks to destroy a doll item (mode 9) only while FittingDollEquipAll (id 212, 0xd4) is on: the
    // s_strDestroyDollItem box (uimessagebox.cpp:1196) and the recall's ask (fittingdollslot_2.cpp:325) check it.
    public const string EquipAllFeature = "FittingDollEquipAll";

    public enum Error {
        None = 0,
        InventoryFull = 1, // s_fittingdoll_InventoryFull
        InvalidOwner = 2, // s_fittingdoll_error_invalid_owner
        ItemIsSkin = 3, // s_fittingdoll_InvalidItemType_Skin: an outfit on an equipment doll
        ItemIsEquip = 4, // s_fittingdoll_InvalidItemType_Equip: equipment on an outfit doll
        ItemRule = 5, // s_fittingdoll_InvalidItem_rule: a doll item the character's equip rules refuse
        ItemLimit = 6, // s_fittingdoll_Invaliditem_limit: a doll item the character may not use (level, job, gender, expired)
        Male = 7, // s_fittingdoll_Invaliditem_male
        Female = 8, // s_fittingdoll_Invaliditem_female
        InvalidItem = 0xc, // s_fittingdoll_InvalidItem
        InvalidDoll = 0xd, // s_fittingdoll_Invalid_doll
        InvalidSlot = 0x10, // s_fittingdoll_Invalid_slot
        InvalidBinding = 0x12, // s_fittingdoll_Invalid_binding
        FailSwap = 0x14, // s_fittingdoll_failswap
    }

    // FittingDoll 0x7B mode 8, the answer to a swap (CSceneStateGame::OnFittingDoll): Done and Partial name
    // s_fittingdoll_done_all and s_fittingdoll_done_partially and play the swap pose on the character and the doll;
    // Nothing shows nothing. Any other value is an assert in the client.
    public enum TransformResult {
        Done = 0x17,
        Partial = 0x18,
        Nothing = 0x19,
    }

    // Hair, face, face decoration and ears (CItemData::FUN_14053d550: the model's first slot): body parts, which
    // neither side of a swap moves.
    public static bool BodyPart(IReadOnlyList<EquipSlot> itemSlots) =>
        itemSlots.Count > 0 && itemSlots[0] is EquipSlot.HR or EquipSlot.FA or EquipSlot.FD or EquipSlot.ER;

    // Whether a worn item of the character goes onto the doll in a swap (UIFittingDollDialog::CanWearEquipment,
    // uifittingdolldialog.cpp:411): it is a skin exactly when the doll is an outfit doll, is no body part, and its
    // subtype is not 0x13 or 0x15 on an outfit doll (CItemData::FUN_14053c810) and not 0 on an equipment doll
    // (FUN_14053c870).
    public static bool Swaps(FunctionCubeMetadata.FittingDollData doll, bool isSkin, int subType, IReadOnlyList<EquipSlot> itemSlots) {
        bool outfitDoll = doll.Type != 0;
        if (isSkin != outfitDoll || BodyPart(itemSlots)) {
            return false;
        }
        return outfitDoll ? subType is not (0x13 or 0x15) : subType != 0;
    }

    // One side of a swap: the item, the slot it is in, the slots its model has, and whether it would move.
    public readonly record struct SwapEntry<T>(T Item, EquipSlot Slot, IReadOnlyCollection<EquipSlot> ItemSlots, bool Moves);

    // Which items a swap moves. Each item that would move, moves, unless it would overlap an item that stays on the
    // other side (an item on the character that is not swapped keeps its slots); an item held back then stays too,
    // which can hold back others, until nothing changes. Partial when an item that would move is held back.
    public static (List<T> ToCharacter, List<T> ToDoll, bool Partial) Swap<T>(IReadOnlyList<SwapEntry<T>> doll, IReadOnlyList<SwapEntry<T>> character) {
        bool[] dollMoves = doll.Select(entry => entry.Moves).ToArray();
        bool[] characterMoves = character.Select(entry => entry.Moves).ToArray();
        bool changed = true;
        while (changed) {
            changed = HoldBack(doll, dollMoves, character, characterMoves) | HoldBack(character, characterMoves, doll, dollMoves);
        }
        bool partial = doll.Where((entry, i) => entry.Moves && !dollMoves[i]).Any()
                       || character.Where((entry, i) => entry.Moves && !characterMoves[i]).Any();
        return (doll.Where((_, i) => dollMoves[i]).Select(entry => entry.Item).ToList(),
            character.Where((_, i) => characterMoves[i]).Select(entry => entry.Item).ToList(), partial);
    }

    // Holds back each moving item of |from| that overlaps an item staying on |to|; true when one was held back.
    private static bool HoldBack<T>(IReadOnlyList<SwapEntry<T>> from, bool[] fromMoves, IReadOnlyList<SwapEntry<T>> to, bool[] toMoves) {
        var kept = to.Where((_, i) => !toMoves[i]).SelectMany(entry => Occupies(entry.Slot, entry.ItemSlots)).ToHashSet();
        bool changed = false;
        for (int i = 0; i < from.Count; i++) {
            if (fromMoves[i] && Occupies(from[i].Slot, from[i].ItemSlots).Any(kept.Contains)) {
                fromMoves[i] = false;
                changed = true;
            }
        }
        return changed;
    }

    // Whether an item fits the doll, as the client tests it before it sends a put (FittingDollPC::FUN_14121c910,
    // fittingdollslot_1.cpp:490): the item is a skin exactly when the doll is an outfit doll (type not 0), and the
    // item's gender limit allows the doll's gender.
    // The client tests no binding itself; result 0x12 shows s_fittingdoll_Invalid_binding ("belongs to another
    // character"), so the server refuses an item bound to another character, not one bound to the one dressing the doll.
    public static Error CanWear(FunctionCubeMetadata.FittingDollData doll, bool isSkin, Gender itemGender, bool boundToOther = false) {
        if (boundToOther) {
            return Error.InvalidBinding;
        }
        bool outfitDoll = doll.Type != 0;
        if (isSkin != outfitDoll) {
            return outfitDoll ? Error.ItemIsEquip : Error.ItemIsSkin;
        }
        if (itemGender != Gender.All && itemGender != doll.Gender) {
            return doll.Gender == Gender.Male ? Error.Male : Error.Female;
        }
        return Error.None;
    }

    // Whether an item is bound to a character other than characterId (a binding to character 0 binds no one).
    public static bool BoundToOther(ItemBinding? binding, long characterId) =>
        binding != null && binding.CharacterId != 0 && binding.CharacterId != characterId;

    // The character whose binding keeps characterId from taking the item, as the client finds it before it offers a
    // destroy (FUN_1406344b0, itemsocketsystemutil.cpp:2550): the item's own binding to another character (kind 1),
    // else, when a gem in an unlocked slot is bound to another character, the binding of the last bound gem
    // (CItemSocket::GetGemBindOwner takes the last one, which may be characterId's own: kind 2). 0 when nothing binds
    // the item to someone else.
    public static (long CharacterId, bool Gem) BindingOwner(Item item, long characterId) {
        ItemBinding? binding = item.Transfer?.Binding;
        if (BoundToOther(binding, characterId)) {
            return (binding!.CharacterId, false);
        }
        ItemGemstone?[] gems = item.Socket?.Sockets ?? [];
        if (!gems.Any(gem => gem != null && gem.ItemId != 0 && BoundToOther(gem.Binding, characterId))) {
            return (0, false);
        }
        long owner = gems.LastOrDefault(gem => gem != null && gem.ItemId != 0 && gem.Binding is { CharacterId: not 0 })!.Binding!.CharacterId;
        return (owner, true);
    }

    // Mannequins work only in the indoor home. An outdoor plot can be forfeited or expire without its cubes being
    // removed one by one, which would leave the items on a cube that no longer exists.
    public static bool Allowed(int plotMapId) => plotMapId == Constant.DefaultHomeMapId;

    // Whether the slot the client names is one of the item's (InvenSlot::FUN_141405a60, invenslot.cpp:675: a slot the
    // item's model has no group for is refused with 0x10). A one-handed weapon goes to either hand.
    public static bool SlotFits(EquipSlot slot, IReadOnlyCollection<EquipSlot> itemSlots) {
        if (slot is EquipSlot.SK or EquipSlot.OH or EquipSlot.ER or EquipSlot.Unknown) {
            return false;
        }
        if (itemSlots.Contains(EquipSlot.OH)) {
            return slot is EquipSlot.LH or EquipSlot.RH;
        }
        return itemSlots.Contains(slot);
    }

    // The slot an item from the doll is worn in: a doll takes an overall or a two-handed weapon in any of its slots
    // (SlotFits), but the character's equips key it by its first slot, the only one EquipManager.Equip accepts and
    // the one a later equip tests for overlap; a one-handed weapon keeps its hand.
    public static EquipSlot WornSlot(EquipSlot slot, IReadOnlyList<EquipSlot> itemSlots) =>
        itemSlots.Count == 0 || itemSlots.Contains(EquipSlot.OH) ? slot : itemSlots[0];

    // The slots an item takes on the doll: all of its slots for a two-handed weapon or an overall, else the one it is in.
    public static IEnumerable<EquipSlot> Occupies(EquipSlot slot, IReadOnlyCollection<EquipSlot> itemSlots) =>
        itemSlots.Count > 1 && !itemSlots.Contains(EquipSlot.OH) ? itemSlots.Append(slot).Distinct() : [slot];

    // The doll items an item put in slot takes off: those whose slots overlap the new item's, as equipping does.
    public static List<T> Displaced<T>(EquipSlot slot, IReadOnlyCollection<EquipSlot> itemSlots,
            IEnumerable<(EquipSlot Slot, IReadOnlyCollection<EquipSlot> ItemSlots, T Item)> worn) {
        var taken = Occupies(slot, itemSlots).ToHashSet();
        return worn.Where(entry => Occupies(entry.Slot, entry.ItemSlots).Any(taken.Contains)).Select(entry => entry.Item).ToList();
    }
}
