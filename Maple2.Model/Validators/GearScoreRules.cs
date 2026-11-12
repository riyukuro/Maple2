namespace Maple2.Model.Validators;

// The gear score as the client computes it for its own character window. The item level is CItem::UpdateItemLevel
// (commonfiles/item.cpp) with CItemData's base and enchant tables (commonfiles/itemdata.cpp), the sum over the worn
// gear is PC::vfunc_126 (client/ms2/pc.cpp) with the client script's calcOHItemAdditionalLevel and
// calcUnlimitedEnchantItemLevel. KMS2 sends the same numbers for the other players' lists.
public static class GearScoreRules {
    // One worn item for the sum: Hand 0 is any slot but the hands, 1 the right hand (slot 5), 2 the left hand (slot 4).
    public readonly record struct Worn(int Hand, bool Paired, int Level, int EnchantLevel);

    // The client's default for the constant BothHandGearScorePenaltyDiv.
    public const float DEFAULT_PENALTY_DIV = 4f;

    // The equip subtypes that are worn as a pair of one-hand weapons: dagger (31) and throwing star (34).
    public static bool IsPaired(int subType) => subType is 31 or 34;

    // CItemData::FUN_14053f430: the share of the item's equip subtype (the item data's subtype).
    public static double TypeFactor(int subType) {
        return subType switch {
            12 or 18 or 19 or 20 or 21 => 0.2,
            13 => 0.21,
            14 => 0.32,
            15 => 0.3,
            16 or 17 => 0.06,
            22 => 0.62,
            _ => 1.0,
        };
    }

    // The item's rank for the level: the item data's rank when it has one, else the item's own rarity.
    public static int Rank(int dataRank, int rarity) {
        return dataRank != 0 ? dataRank : rarity;
    }

    // CItemData::FUN_14053f0d0: the level without the enchant.
    public static int BaseLevel(int gearScore, int rank, int subType) {
        if (gearScore <= 0) {
            return 0;
        }
        double factor = TypeFactor(subType);
        if (gearScore < 70) {
            double result = (gearScore * 10 + (rank - 1) * 5) * factor;
            if (rank >= 4 && gearScore >= 50) {
                result = result * 2.0 * (rank - 3) + (gearScore - 50) * 100 * factor;
            }
            return (int) result;
        }

        double level = gearScore switch {
            70 => 5.2,
            80 => 19.537,
            81 => 71.334,
            82 => 80.0,
            90 => 61.35,
            _ => 0.0,
        };
        double rate = rank switch {
            4 => 0.558,
            5 => 1.2,
            6 => 1.9,
            _ => 0.0,
        };
        return (int) ((2.0 + level) * 1030.0 * rate * factor);
    }

    // CItemData::FUN_14053f390: the enchant's part. Below gear score 70 only an item whose data rank is 4 or more has one.
    public static int EnchantLevel(int gearScore, int dataRank, int baseLevel, int enchants) {
        if (gearScore < 70) {
            return dataRank >= 4 ? (int) (baseLevel * LowEnchantRate(enchants)) : 0;
        }
        return (int) (baseLevel * HighEnchantRate(enchants));
    }

    // CItemData::FUN_14053f4f0.
    private static double LowEnchantRate(int enchants) {
        return enchants switch {
            1 => 0.02, 2 => 0.04, 3 => 0.06, 4 => 0.08, 5 => 0.1,
            6 => 0.14, 7 => 0.18, 8 => 0.23, 9 => 0.29, 10 => 0.35,
            11 => 0.41, 12 => 0.47, 13 => 0.53, 14 => 0.59, 15 => 0.65,
            _ => 0.0,
        };
    }

    // CItemData::FUN_14053f620.
    private static double HighEnchantRate(int enchants) {
        return enchants switch {
            1 => 0.02, 2 => 0.04, 3 => 0.07, 4 => 0.1, 5 => 0.14,
            6 => 0.19, 7 => 0.25, 8 => 0.32, 9 => 0.4, 10 => 0.5,
            11 => 0.64, 12 => 0.84, 13 => 1.12, 14 => 1.5, 15 => 2.0,
            _ => 0.0,
        };
    }

    // The client script's calcUnlimitedEnchantItemLevel for limit break levels 1 to 99: 100 units, and per level
    // 1 unit below 20, 1.5 below 40, then 2, 2.5, 3, 3.5 and 4 per ten levels, 5 from 90. Its other two arguments
    // (the item's highest limit break level and its subtype) change nothing. The script fails from level 100.
    public static int LimitBreakLevel(int level) {
        if (level is <= 0 or >= 100) {
            return 0;
        }
        const double unit = 842.95818;
        double units = 100;
        for (int i = 1; i <= level; i++) {
            units += i switch {
                < 20 => 1.0,
                < 40 => 1.5,
                < 50 => 2.0,
                < 60 => 2.5,
                < 70 => 3.0,
                < 80 => 3.5,
                < 90 => 4.0,
                _ => 5.0,
            };
        }
        return (int) (unit * units);
    }

    // CItem::UpdateItemLevel: the item's level and the enchant's part of it.
    public static (int Level, int EnchantLevel) ItemLevel(int gearScore, int dataRank, int rarity, int subType, int enchants, int limitBreakLevel) {
        int baseLevel = BaseLevel(gearScore, Rank(dataRank, rarity), subType);
        int enchantLevel = EnchantLevel(gearScore, dataRank, baseLevel, enchants);
        if (limitBreakLevel > 0) {
            enchantLevel += LimitBreakLevel(limitBreakLevel);
        }
        return (enchantLevel + baseLevel, enchantLevel);
    }

    // The client script's calcOHItemAdditionalLevel for two paired one-hand weapons (dagger, throwing star): equal
    // levels count once, a lower level of 70% of the higher or more gives the mean, a lower one adds
    // low * (high - low) / (2 * high) to the lower level.
    public static int PairLevel(int first, int second) {
        int low = Math.Min(first, second);
        int high = Math.Max(first, second);
        if (low == high) {
            return high;
        }
        if (low == 0 || (double) low / high >= 0.7) {
            return (int) ((low + high) / 2.0);
        }
        return (int) (low + (double) low * (high - low) / (2.0 * high));
    }

    // PC::vfunc_126: the sum of the worn items' levels. The left hand slot's item (shield, spellbook) never counts
    // by itself; the right hand's counts in full with no paired weapon worn, divided by the constant
    // BothHandGearScorePenaltyDiv with one, and two paired weapons count as PairLevel.
    public static int Total(IEnumerable<Worn> worn, float penaltyDiv) {
        uint total = 0;
        int right = 0;
        int left = 0;
        int paired = 0;
        foreach (Worn item in worn) {
            switch (item.Hand) {
                case 1:
                    right = item.Level;
                    paired += item.Paired ? 1 : 0;
                    break;
                case 2:
                    left = item.Level;
                    paired += item.Paired ? 1 : 0;
                    break;
                default:
                    total += (uint) item.Level;
                    break;
            }
        }

        switch (paired) {
            case 0:
                total += (uint) right;
                break;
            case 2:
                total += (uint) PairLevel(right, left);
                break;
            default:
                // The client adds right / div in float; a constant of 0 would not give a number.
                total = penaltyDiv > 0 ? (uint) (total + right / penaltyDiv) : total + (uint) right;
                break;
        }
        return (int) total;
    }
}
