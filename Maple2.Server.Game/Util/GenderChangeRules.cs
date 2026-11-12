using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

// ItemUse ChangeGender (the gender voucher). The client asks with a confirm box and sends
// RequestItemUse with an empty string. It refuses by itself while a worn item outside the look
// slots is limited to one gender.
public static class GenderChangeRules {
    // The slots KMS2 takes off and fills again with items of the new gender.
    public static readonly EquipSlot[] LookSlots = [EquipSlot.HR, EquipSlot.FA, EquipSlot.FD, EquipSlot.ER];

    public static Gender Other(Gender gender) => gender == Gender.Male ? Gender.Female : Gender.Male;

    public static bool IsLookSlot(EquipSlot slot) => Array.IndexOf(LookSlots, slot) >= 0;

    // The client's own refusal: a worn item that is not a look item and is limited to one gender.
    public static bool BlocksChange(EquipSlot slot, Gender limit) => !IsLookSlot(slot) && limit != Gender.All;

    // The default item of a look slot for the new gender: the first one limited to that gender,
    // else the first one for both. 0 when the list has none.
    public static int PickDefault(IEnumerable<(int Id, Gender Limit)> defaults, Gender gender) {
        int any = 0;
        foreach ((int id, Gender limit) in defaults) {
            if (limit == gender) {
                return id;
            }
            if (limit == Gender.All && any == 0) {
                any = id;
            }
        }
        return any;
    }

    // The client's own refusal while engaged, married or in a filed divorce (ChangeGender @0x140d22dd0 allows
    // wedding states 0 and 5 only).
    public static bool WeddingAllows(MaritalStatus status) => status is MaritalStatus.Single or MaritalStatus.Divorced;

    // Hair and face are always replaced; a face decoration or an ear only when one is worn.
    public static bool Replaces(EquipSlot slot, bool worn) => slot is EquipSlot.HR or EquipSlot.FA || (worn && IsLookSlot(slot));

    // The whole change. Every step that can fail comes first and leaves nothing behind: the new look items are
    // stored (unstore takes back the stored ones when a later one or the voucher fails), then the voucher is
    // consumed. Only then the worn look items are replaced in the outfit tab (takeOff gets each old one) and the
    // gender flips. A new item keeps the colour of the item it replaces. False when nothing was changed.
    public static bool Apply(Character character, IDictionary<EquipSlot, Item> outfit, IEnumerable<(EquipSlot Slot, ItemMetadata Metadata)> picks,
                             Func<Item, Item?> store, Action<Item> unstore, Func<bool> consume, Action<Item> takeOff) {
        var looks = new List<(EquipSlot Slot, Item Look)>();
        bool stored = true;
        foreach ((EquipSlot slot, ItemMetadata metadata) in picks) {
            outfit.TryGetValue(slot, out Item? old);
            if (!Replaces(slot, old != null)) {
                continue;
            }
            var look = new Item(metadata);
            if (old?.Appearance != null && look.Appearance != null) {
                look.Appearance.Color = old.Appearance.Color;
            }
            Item? created = store(look);
            if (created == null) {
                stored = false;
                break;
            }
            looks.Add((slot, created));
        }

        if (!stored || !looks.Exists(look => look.Slot == EquipSlot.HR) || !looks.Exists(look => look.Slot == EquipSlot.FA) || !consume()) {
            foreach ((EquipSlot _, Item look) in looks) {
                unstore(look);
            }
            return false;
        }

        foreach ((EquipSlot slot, Item look) in looks) {
            if (outfit.Remove(slot, out Item? old)) {
                takeOff(old);
            }
            look.Group = ItemGroup.Outfit;
            look.Slot = (short) slot;
            outfit[slot] = look;
        }
        character.Gender = Other(character.Gender);
        return true;
    }
}
