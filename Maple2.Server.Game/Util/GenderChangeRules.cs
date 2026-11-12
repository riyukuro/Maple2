using Maple2.Model.Enum;

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
}
