using Maple2.Model.Enum;

namespace Maple2.Model.Validators;

public static class CharacterCreateRules {
    // The create packet carries the gender as one byte; the client only ever sends 0 or 1.
    public static bool IsCreateGender(Gender gender) {
        return gender is Gender.Male or Gender.Female;
    }

    // An account holds at most its own slot count, and never more than the server's.
    public static bool HasFreeSlot(int owned, int accountMax, int serverMax) {
        return owned >= 0 && owned < Math.Min(accountMax, serverMax);
    }
}
