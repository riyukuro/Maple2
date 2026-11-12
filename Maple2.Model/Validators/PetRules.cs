namespace Maple2.Model.Validators;

public static class PetRules {
    // Whether a pet may be summoned here. The client refuses in a field whose usePet is 0 (MyPC::FUN_14152b2d0,
    // mypc.cpp:3275; the pet item's quick slot is greyed out there too, skillslot.cpp:1345) and for a pet item
    // whose required level is above the character's (mypc.cpp:3270).
    public static bool CanSummon(bool fieldUsePet, int requiredLevel, int level) {
        return fieldUsePet && requiredLevel <= level;
    }

    // What a summoned pet adds to its owner's stat on top of the pet item's own values: the item's constant
    // value times the pet's option factor, cut to an int, for every pet level above the first
    // (CEquipment::FUN_1411e76c0, equipment.cpp:3037-3040; FUN_1406198a0, itemremakescrolldata.cpp:933).
    public static int LevelBonus(int constantValue, float optionFactor, short petLevel) {
        return (int) (constantValue * (double) optionFactor + 1e-06) * (petLevel - 1);
    }

    // RequestPet 16 (another player's pet): the popup names the pet the owner has summoned; a pet that was put
    // away or switched since then is not shown.
    public static bool IsShownPet(long? summonedUid, long requestedUid) {
        return requestedUid != 0 && summonedUid == requestedUid;
    }

    // RequestPet 4 (rename): the client's name box sends a name that is not empty and is shorter than 32 characters.
    public const int MaxNameLength = 31;

    public static bool IsName(string name) {
        return name.Length > 0 && name.Length <= MaxNameLength;
    }

    // The client also runs its pet name filter; the server runs its ban-word list.
    public static bool IsAllowedName(string name, Func<string, bool> isBanned) {
        return IsName(name) && !string.IsNullOrWhiteSpace(name) && !isBanned(name);
    }

    // A name the client's box can send but the server's ban-word list refuses. The client's own filter
    // passed it, so the player has seen no error yet and the server has to show one.
    public static bool IsBannedName(string name, Func<string, bool> isBanned) {
        return IsName(name) && !string.IsNullOrWhiteSpace(name) && isBanned(name);
    }

    // The client asks for no merets while the pet has a rename left, and for the pet rename price after that.
    public static int RenameCost(short renameRemaining, int price) {
        return renameRemaining > 0 ? 0 : price;
    }

    // pet_taming ("Pacify a hungry monster", 70600025): the feeding that fills a wild pet's taming gauge. The
    // capture is its own step (a trap, pet_catch_*), so a gauge that was already full counts no more.
    public static bool Pacified(int pointBefore, int pointAfter, int maxPoint) {
        return pointBefore < maxPoint && pointAfter >= maxPoint;
    }
}
