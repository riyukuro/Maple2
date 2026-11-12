using System.Globalization;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

// Pet fusion (RequestPet mode 12, TabContentsPetCompose in the client). A pet below the top pet level gains
// exp from other pets and from pet exp books (string tag PetEXP). Every number here comes from the client:
// the script (calc_petEnchantExp, calc_petEnchantCost, calc_petEnchantBookCost, calc_petExp),
// table/petexp.xml and the constants PetMaxLevel and DailyPetEnchantMaxCount.
public static class PetFusionRules {
    // The window takes at most 12 fodder units (FUN_140cb5c90); a book stack adds one unit per click.
    public const int MaxFodders = 12;

    public enum FodderKind {
        None,
        Pet, // a pet whose pet row has a type other than 0 (the window's pet cards)
        Book, // an item with string tag PetEXP (the window's candidates)
    }

    public enum FodderError {
        None,
        Missing,
        Target, // the pet being fused
        Summoned,
        NotFodder,
        Count, // a pet sent more than once, or more books than held
        HasItems, // s_err_cannot_compose_petitem_hasitem
        Locked, // s_itemlock_invalid_petcompose
        TooMany, // more than 12 units
        Full, // s_pet_not_enchant: the units taken already reach the top pet level
    }

    // The fusion window lists a pet if its pet row has a type other than 0 (FUN_140cb5600); it only adds
    // fodders while the pet is below the top pet level (constant PetMaxLevel, the client's m_petClass).
    public static bool IsTarget(int petType, int petLevel, int petMaxLevel) {
        return petType != 0 && petLevel < petMaxLevel;
    }

    // The pet training kit's target (UIItemPetTrainingDialog FUN_140e57ed0): a pet item with a pet data row of type 1,
    // below the top level (s_pet_training_dialog_invalid_item, s_pet_training_dialog_max_level otherwise).
    public static bool IsTrainingTarget(int petType, int petLevel, int petMaxLevel) {
        return petType == 1 && petLevel < petMaxLevel;
    }

    // The kit's exp: offsetExp of its parameters (`<v offsetExp="1500" />`); the dialog does nothing for 0.
    public static long TrainingExp(IReadOnlyDictionary<string, string> parameters) {
        return parameters.TryGetValue("offsetExp", out string? text)
            && uint.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint value) ? value : 0;
    }

    public static FodderKind KindOf(Item fodder, int? petType) {
        if (fodder.Pet != null) {
            return petType is not null and not 0 ? FodderKind.Pet : FodderKind.None;
        }
        return fodder.Metadata.Property.Tag == ItemTag.PetEXP ? FodderKind.Book : FodderKind.None;
    }

    // The checks of RebuildPool and FUN_140cb5c90 that do not depend on the other fodders, in the window's
    // order. "amount" is the count sent for this uid: 1 for a pet, at most the stack for a book
    // (GetAvailableCount).
    public static FodderError CheckFodder(Item target, Item? fodder, FodderKind kind, int amount, long summonedUid, long now) {
        if (fodder == null) {
            return FodderError.Missing;
        }
        if (fodder.Uid == target.Uid) {
            return FodderError.Target;
        }
        if (fodder.Uid == summonedUid) {
            return FodderError.Summoned;
        }
        if (kind == FodderKind.None) {
            return FodderError.NotFodder;
        }
        if (amount < 1 || (kind == FodderKind.Pet && amount != 1) || (kind == FodderKind.Book && amount > fodder.Amount)) {
            return FodderError.Count;
        }
        if (fodder.Pet?.HasItems == true) {
            return FodderError.HasItems;
        }
        if (fodder.IsLocked || fodder.UnlockTime > now) {
            return FodderError.Locked;
        }
        return FodderError.None;
    }

    // Before each unit the window works out the level the units taken so far give, and refuses the unit once
    // that reaches the top level; so only the last unit can go over it.
    public static FodderError CheckUnit(int unitsTaken, int predictedLevel, int petMaxLevel) {
        if (unitsTaken >= MaxFodders) {
            return FodderError.TooMany;
        }
        if (predictedLevel >= petMaxLevel) {
            return FodderError.Full;
        }
        return FodderError.None;
    }

    // One pet fodder (FUN_140cb4ed0): calc_petEnchantExp with the fusion count before it. The caller adds 1
    // to the count after each pet fodder; books do not count.
    public static (long Base, long Bonus) PetExp(Item target, Item fodder, int count, int bonusLimit) {
        return LuaFunctions.Lua.CalcPetEnchantExp(target.Rarity, fodder.Rarity, fodder.Pet?.Level ?? 0, fodder.Pet?.Exp ?? 0,
            count, bonusLimit, fodder.Metadata.Limit.Level);
    }

    // One pet exp book: the first value of its function parameter (AddPetEXP "3000"), read as unsigned.
    public static long BookExp(ItemMetadataFunction? function) {
        if (function == null || string.IsNullOrWhiteSpace(function.Parameters)) {
            return 0;
        }
        string first = function.Parameters.Split(',')[0].Trim();
        return uint.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint value) ? value : 0;
    }

    // The exp a summoned pet gets when its owner gets a monster's kill exp. KMS2 gives a level 1 pet
    // level * 12 / 100 (rounded down) for a class 1 monster of level 23 to 50 and level * 48 / 100 for a
    // class 3 one; the rule is not in the client. Chosen here: other classes below 3 count as class 1, above
    // 3 as class 3, and the pet's own level does not change the amount.
    public static long KillExp(int npcLevel, int npcClass) {
        return npcLevel <= 0 ? 0 : (long) npcLevel * (npcClass >= 3 ? 48 : 12) / 100;
    }

    // The level and exp after a gain (FUN_140cb5300). A pet at the top level does not change. Each level
    // takes the exp table/petexp.xml asks for (CExpTable::GetPetExp); reaching the top level sets the exp to 0.
    public static (int Level, long Exp) Apply(PetExpTable table, int level, long exp, int rarity, int levelLimit, long gain, int petMaxLevel) {
        if (level >= petMaxLevel) {
            return (level, exp);
        }

        exp = gain < 0 && exp <= Math.Abs(gain) ? 0 : exp + gain;
        int up = 0;
        long need = table.Need(level, rarity, levelLimit);
        while (need <= exp) {
            exp -= need;
            up++;
            need = table.Need(level + up, rarity, levelLimit);
        }

        if (up > 0) {
            level += up;
            if (level >= petMaxLevel) {
                level = petMaxLevel;
                exp = 0;
            }
        }
        return (level, exp);
    }

    // The meso of one fodder unit, before the sale: calc_petEnchantCost for a pet, calc_petEnchantBookCost
    // for a book (PetComposePetEnchant, the cost line of the window).
    public static long UnitCost(Item target, Item fodder, FodderKind kind) {
        return kind == FodderKind.Pet
            ? LuaFunctions.Lua.CalcPetEnchantCost(target.Rarity, fodder.Rarity, fodder.Pet?.Level ?? 0, fodder.Pet?.Exp ?? 0)
            : LuaFunctions.Lua.CalcPetEnchantBookCost(target.Rarity);
    }

    // CGameRule::ApplyPetComposeSale: a PetComposeSale event rate in 1/10000; 0 leaves the cost.
    public static long ApplySale(long cost, int rate) {
        if (rate == 0) {
            return cost;
        }
        double factor = 1.0 - (uint) rate / 10000.0;
        return (uint) ((uint) cost * factor + 1e-06);
    }

    // The daily fusion count: 0 once a daily reset has passed since it was stored, or when no time is stored
    // (the client's PC::IsDailyCountExpired keeps the same count at +0x11f4/+0x11f8).
    public static int DailyCount(int stored, long storedTime, long lastReset) {
        return storedTime == 0 || storedTime < lastReset ? 0 : stored;
    }

    // The count and time the login packet gives the client: the stored pair while it is from today, else a
    // count of 0 from now.
    public static (int Count, long Time) LoginCount(int stored, long storedTime, long lastReset, long now) {
        return storedTime == 0 || storedTime < lastReset ? (0, now) : (stored, storedTime);
    }

    // The server's daily reset runs at local midnight (WorldServer.StartDailyReset).
    public static long LastResetTime() {
        return new DateTimeOffset(DateTime.Today).ToUnixTimeSeconds();
    }
}
