namespace Maple2.Model.Validators;

public static class PetRules {
    // Whether a pet may be summoned here. The client refuses in a field whose usePet is 0 (MyPC::FUN_14152b2d0,
    // mypc.cpp:3275; the pet item's quick slot is greyed out there too, skillslot.cpp:1345) and for a pet item
    // whose required level is above the character's (mypc.cpp:3270).
    public static bool CanSummon(bool fieldUsePet, int requiredLevel, int level) {
        return fieldUsePet && requiredLevel <= level;
    }

    // The auto-potion window has two rows (uiitemautouseoptiondialog.cpp, DAT_1439ed000) and writes one entry per
    // row (Unk_PetOptions::Write); a longer list never comes from the client and is refused, not stored.
    public const int PotionRows = 2;

    public static bool IsPotionCount(int count) => count <= PotionRows;

    // What a summoned pet adds to its owner's stat on top of the pet item's own values: the item's constant
    // value times the pet's option factor, cut to an int, for every pet level above the first
    // (CEquipment::FUN_1411e76c0, equipment.cpp:3037-3040; FUN_1406198a0, itemremakescrolldata.cpp:933).
    public static int LevelBonus(int constantValue, float optionFactor, short petLevel) {
        return (int) (constantValue * (double) optionFactor + 1e-06) * (petLevel - 1);
    }

    // FieldAddPet's level: a summoned pet shows the level of its pet item (KMS2 sends 1 to 50 for the same
    // pet as it levels), a wild pet the level of its npc row.
    public static short FieldLevel(bool summoned, short? petLevel, short npcLevel) {
        return summoned && petLevel is > 0 ? petLevel.Value : npcLevel;
    }

    // The rarity of a captured pet from the wild pet's taming rank (1-100, FieldAddPet): the client's own grade of
    // that rank, the lua calc_TamingPetRank (3 from 95, 2 from 70, else 1) that picks the wild pet's grade effect.
    // The snares' texts (63100000 on) name normal, rare, exceptional and epic captures, epic at a very low chance;
    // no data gives that chance, so only the top rank (1 in 100) is epic. No capture is legendary.
    public const int TopTamingRank = 100;

    public static int CaptureRarity(int tamingRank) {
        if (tamingRank >= TopTamingRank) {
            return 4;
        }
        return tamingRank >= 95 ? 3 : tamingRank >= 70 ? 2 : 1;
    }

    // The pet taming mastery of a capture. No table or client code gives it (the client only names a gain the server
    // sends, scenestategame.cpp:3960), so the amount is the server's own: a snare craft's 30 (masteryreceipe type 11)
    // for a normal pet, doubled for each rarity above it.
    public const int CaptureMasteryBase = 30;

    public static int CaptureMastery(int rarity) {
        return CaptureMasteryBase << (Math.Clamp(rarity, 1, 4) - 1);
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
