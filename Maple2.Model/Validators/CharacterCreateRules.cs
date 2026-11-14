using Maple2.Model.Enum;

namespace Maple2.Model.Validators;

public static class CharacterCreateRules {
    // The job of a create as the client's own create reader takes it (FUN_140222590, commonenum.cpp:1431): the
    // eleven first jobs, 10 to 110. Beginner (1) is in job.xml but the client never creates one, and no other
    // value passes, so a forged job is refused before the job table is looked at.
    public static bool IsCreateJob(JobCode job) {
        return job is JobCode.Knight or JobCode.Berserker or JobCode.Wizard or JobCode.Priest or JobCode.Archer
            or JobCode.HeavyGunner or JobCode.Thief or JobCode.Assassin or JobCode.RuneBlader or JobCode.Striker
            or JobCode.SoulBinder;
    }

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

    // The equips of a create: no slot twice. No slot is required: the client sends only the equips it
    // shows, and which slots a job's create screen shows is not known to the server.
    public static bool IsCreateOutfit(IReadOnlyCollection<EquipSlot> slots) {
        return slots.Distinct().Count() == slots.Count;
    }

    // An account holds at most its own slot count, and never more than the server's.
    public static bool HasFreeSlot(int owned, int accountMax, int serverMax) {
        return owned >= 0 && owned < Math.Min(accountMax, serverMax);
    }
}
