using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

// Pet evolution (RequestPet modes 17 and 18, TabContentsPetEvolution in the client). A pet at the top pet
// level gains evolution points by giving up pets of its own kind and rarity; once it has the points
// table/petevolution.xml asks for its level limit and rarity, it evolves to the next rarity. Every number
// here comes from that table and the script (calc_petEvolutionCost, calc_petEvolutionResultLevel).
public static class PetEvolveRules {
    // The window takes at most 12 fodder pets (TabContentsPetEvolution::FUN_140acbe00).
    public const int MaxFodders = 12;

    public enum FodderError {
        None,
        Missing,
        Target, // the pet being evolved
        Summoned,
        Duplicate,
        OtherKind, // another pet id
        OtherRarity,
        HasItems, // s_err_cannot_evolution_petitem_hasitem
        Locked, // s_itemlock_invalid_petevolution
        Full, // s_err_cannot_evolution_consume: the points already reach the need
    }

    // The evolution window lists a pet only if its pet row has a type other than 0 and the pet is at the top
    // pet level (constant PetMaxLevel, the client's m_petClass) (RebuildPetCards).
    public static bool IsTarget(int petType, int petLevel, int petMaxLevel) {
        return petType != 0 && petMaxLevel > 0 && petLevel == petMaxLevel;
    }

    // The points the next evolution needs (CExpTable::GetPetEvolution). 0: this rarity does not evolve.
    public static int Need(PetEvolutionTable table, int levelLimit, int rarity) {
        return table.Need(levelLimit, rarity);
    }

    // A fodder adds its own evolution points, and at least 1 (GetSelectedPointTotal).
    public static int FodderPoints(int fodderPoints) {
        return Math.Max(fodderPoints, 1);
    }

    // The checks the window makes before a pet joins the fodder list (RebuildPool and FUN_140acbe00), in
    // the window's order. "total" is the target's points plus the fodders taken before this one: a fodder
    // is refused once that reaches the need, so only the last fodder can go over it.
    public static FodderError CheckFodder(Item target, Item? fodder, long summonedUid, long total, int need, long now) {
        if (fodder?.Pet == null) {
            return FodderError.Missing;
        }
        if (fodder.Uid == target.Uid) {
            return FodderError.Target;
        }
        if (fodder.Uid == summonedUid) {
            return FodderError.Summoned;
        }
        if (fodder.Metadata.Property.PetId != target.Metadata.Property.PetId) {
            return FodderError.OtherKind;
        }
        if (fodder.Rarity != target.Rarity) {
            return FodderError.OtherRarity;
        }
        if (fodder.Pet.HasItems) {
            return FodderError.HasItems;
        }
        if (fodder.IsLocked || fodder.UnlockTime > now) {
            return FodderError.Locked;
        }
        if (total >= need) {
            return FodderError.Full;
        }
        return FodderError.None;
    }

    // The meso the window shows for the fodders: calc_petEvolutionCost by each fodder's rarity.
    public static long Cost(IEnumerable<int> fodderRarities) {
        return fodderRarities.Sum(rarity => LuaFunctions.Lua.CalcPetEvolutionCost(rarity));
    }

    // The window sends mode 17 only when the points reach a need above 0 (OnPetEnchant), and the next rarity
    // must have a result level in the script.
    public static bool CanEvolve(long points, int need, int resultLevel) {
        return need > 0 && points >= need && resultLevel > 0;
    }

    // The evolved pet keeps its random option group and gets the constant and static groups of its new rarity
    // (the window's preview copies group 2 into a fresh item, FUN_140ac9e20 / CopyGroup2From).
    public static ItemStats? EvolvedStats(ItemStats? rolled, ItemStats? old) {
        if (rolled == null) {
            return old;
        }
        if (old != null) {
            rolled[ItemStats.Type.Random] = old[ItemStats.Type.Random];
        }
        return rolled;
    }

    // The value the GM command "/petevolutionpoint" stores: never below 0, and not above the points the
    // pet's next evolution needs when that number is known (null or 0: not known, no upper bound).
    public static int ClampPoints(long requested, int? needed = null) {
        if (requested <= 0) {
            return 0;
        }

        long max = needed is > 0 ? needed.Value : int.MaxValue;
        return (int) Math.Min(requested, max);
    }
}
