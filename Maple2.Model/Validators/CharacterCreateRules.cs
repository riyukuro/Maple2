using Maple2.Model.Enum;

namespace Maple2.Model.Validators;

public static class CharacterCreateRules {
    // The create packet carries the gender as one byte; the client only ever sends 0 or 1.
    public static bool IsCreateGender(Gender gender) {
        return gender is Gender.Male or Gender.Female;
    }

    // What create and the name change item both refuse before the name is looked up: a blank name, and a
    // name the ban-word list matches.
    public static bool IsBlankName(string name) {
        return string.IsNullOrWhiteSpace(name);
    }

    public static bool IsAllowedName(string name, Func<string, bool> isBanned) {
        return !IsBlankName(name) && !isBanned(name);
    }

    // The equips of a create: no slot twice, and hair, face and clothes are there.
    public static bool IsCreateOutfit(IReadOnlyCollection<EquipSlot> slots) {
        return slots.Distinct().Count() == slots.Count
               && slots.Contains(EquipSlot.HR) && slots.Contains(EquipSlot.FA) && slots.Contains(EquipSlot.CL);
    }

    // An account holds at most its own slot count, and never more than the server's.
    public static bool HasFreeSlot(int owned, int accountMax, int serverMax) {
        return owned >= 0 && owned < Math.Min(accountMax, serverMax);
    }
}
