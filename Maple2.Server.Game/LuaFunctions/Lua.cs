using System.Diagnostics.CodeAnalysis;
using Maple2.Model.Enum;
using Maple2.Server.Core.Constants;

// ReSharper disable HeuristicUnreachableCode
#pragma warning disable CS0162 // Unreachable code detected

namespace Maple2.Server.Game.LuaFunctions;

public static class Lua {
    private const string LocaleKr = "KR";
    private const string LocaleCn = "CN";
    private const string LocaleNa = "NA";
    private const string LocaleJp = "JP";
    private const string LocaleTh = "TH";
    private const string LocaleTw = "TW";

    // The script branch the formulas follow: the LOCALE setting. Tests set it to check each branch.
    internal static string Locale { get; set; } = Target.LOCALE;

    private const int SlotWeapon = 0;
    private const int SlotArmor = 1;
    private const int SlotAcc = 2;

    private const int RankNormal = 1;
    private const int RankRare = 2;
    private const int RankElite = 3;
    private const int RankExcellent = 4;
    private const int RankLegendary = 5;
    private const int RankEpic = 6;

    private const string ItemRemakeRedcrystalTag = "RedCrystal";
    private const string ItemRemakeBluecrystalTag = "BlueCrystal";
    private const string ItemRemakeGreencrystalTag = "GreenCrystal";
    private const string ItemRemakeCrystalChipTag = "CrystalPiece";
    private const string ItemRemakeMetaCellTag = "MetaCell";

    private const long BlackmarketRegisterDepositMax = 100000;
    private const int BlackmarketRegisterDepositPercent = 1;
    private const float BlackmarketRegisterDepositRate = BlackmarketRegisterDepositPercent / 100f;

    private const float UpgradeFactor = 0.06f;
    private const float CorrectCritical = 0.015f;


    // The client's own critical damage (luautil.cpp CLua::FUN_140674a70(value, rate, bonus)) scales the critical damage
    // by max(1 - rate, 0) before the clamp; the client always passes rate 0 (its character info), so the rate being the
    // target's critical damage resistance is the server's reading.
    public static float CalcCritDamage(float critDamage, int mode = 0, float resistance = 0) {
        float rate = Math.Max(1 - resistance, 0);
        if (mode == 14) {
            return Math.Clamp(1 + (critDamage / 1000 * rate), 1, 5);
        }

        return Math.Clamp(1 + (critDamage / 1000 * rate), 1, 2.5f);
    }

    public static float CalcPlayerCritRate(int jobCode, long luk, long critRate, long critResistance, int finalCapV, int mode) {
        float criticalMajorValue = GetCriticalMajorValue(jobCode);

        float finalCritRate = luk * criticalMajorValue;
        finalCritRate += critRate * 5.3f;
        finalCritRate /= critResistance * 2;

        finalCritRate *= CorrectCritical;

        if (mode == 14) {
            return Math.Clamp(finalCritRate, 0, 0.9f);
        }

        return Math.Clamp(finalCritRate, 0, 0.4f);
    }

    public static float CalcNpcCritRate(float luk, float critRate, float critResistance) {
        float result = critRate * 5.3f;
        result /= critResistance * 2;
        result *= CorrectCritical;

        return Math.Clamp(result, 0, 0.5f);
    }

    public static (int, int) CalcItemLevel(int gearScore, int grade, int itemType, int enchantLevel, int limitBreakLevel) {
        double result2;
        float result;

        double local2;
        enchantLevel = Math.Clamp(enchantLevel, (int) 0, (int) 15);
        double local3 = 0;
        double local4 = 0;

        if (gearScore > 0) {
            float local1;
            if (limitBreakLevel < 60) {
                if (Locale == LocaleKr) {
                    local2 = 10 * gearScore;
                    result2 = Math.Max(grade - 1, 0) * 5;
                    local3 = local2 + result2;
                } else {
                    if (Locale == LocaleCn) {
                        if (grade > 3 && gearScore >= 50) {
                            local2 = GetItemLevelRankCoefficient(itemType);
                            if (local2 > 0) {
                                if (grade >= 5) {
                                    local1 = 100;
                                    local2 = 10 * gearScore;
                                    result2 =
                                        Math.Max(grade - 1, 0) * 5;
                                    local3 = local2 + result2;
                                    result2 = GetItemLevelCoefficient(itemType);
                                    local3 *= result2;
                                    local3 *= 2;
                                    result2 = Math.Max(grade - 3, 1);
                                    local3 *= result2;
                                    result2 =
                                        Math.Max(gearScore - 50, 0) * local1 * GetItemLevelCoefficient(itemType);
                                    local3 += result2;
                                } else {
                                    local2 = 10 * gearScore;
                                    result2 =
                                        Math.Max(grade - 1, 0) * 5;
                                    local3 = local2 + result2;
                                    result2 = GetItemLevelCoefficient(itemType);
                                    local3 *= result2;
                                    local3 *= 2;
                                    result2 = Math.Max(grade - 3, 1);
                                    local3 *= result2;
                                }
                            } else if (
                                itemType is 12 or 18 or 19 or 20
                            ) {
                                if (grade >= 5) {
                                    local1 = 100;
                                    local2 = 10 * gearScore;
                                    result2 =
                                        Math.Max(grade - 2, 0) * 5;
                                    local3 = local2 + result2;
                                    result2 = GetItemLevelCoefficient(itemType);
                                    local3 *= result2;
                                    local3 *= 2;
                                    result2 = Math.Max(grade - 4, 1);
                                    local3 *= result2;
                                    result2 = Math.Max(gearScore - 50, 0) * local1 * GetItemLevelCoefficient(itemType);
                                    local3 += result2;
                                } else {
                                    local2 = 10 * gearScore;
                                    result2 =
                                        Math.Max(grade - 1, 0) * 5;
                                    local3 = local2 + result2;
                                    result2 = GetItemLevelCoefficient(itemType);
                                    local3 *= result2;
                                }
                            } else {
                                local2 = 10 * gearScore;
                                result2 =
                                    Math.Max(grade - 1, 0) * 5;
                                local3 = local2 + result2;
                                result2 = GetItemLevelCoefficient(itemType);
                                local3 *= result2;
                            }
                        } else {
                            local2 = 10 * gearScore;
                            result2 =
                                Math.Max(grade - 1, 0) * 5;
                            local3 = local2 + result2;
                            result2 = GetItemLevelCoefficient(itemType);
                            local3 *= result2;
                        }
                    } else if (grade > 3 && gearScore >= 50) {
                        local2 = GetItemLevelRankCoefficient(itemType);
                        if (local2 > 0) {
                            local2 = GetLevelScoreFactorNa(gearScore);
                            local2 = 2 + local2;
                            local2 *= 1030;
                            result2 = GetRankScoreFactor(grade);
                            local2 *= result2;
                            result2 = GetItemLevelCoefficient(itemType);
                            local3 = local2 * result2;
                        } else if (
                            itemType is 12 or 18 or 19 or 20
                        ) {
                            local2 = GetLevelScoreFactorNa(gearScore);
                            local2 = 2 + local2;
                            local2 *= 1030;
                            result2 = GetRankScoreFactor(grade);
                            local2 *= result2;
                            result2 = GetItemLevelCoefficient(itemType);
                            local3 = local2 * result2;
                        } else {
                            local2 = 10 * gearScore;
                            result2 =
                                Math.Max(grade - 1, 0) * 5;
                            local3 = local2 + result2;
                            result2 = GetItemLevelCoefficient(itemType);
                            local3 *= result2;
                        }
                    } else {
                        local2 = 10 * gearScore;
                        result2 =
                            Math.Max(grade - 1, 0) * 5;
                        local3 = local2 + result2;
                        result2 = GetItemLevelCoefficient(itemType);
                        local3 *= result2;
                    }
                }
                local4 = local3;
            } else if (limitBreakLevel < 70) {
                if (grade > 3 && gearScore >= 50) {
                    if (Locale != LocaleKr) {
                        if (Locale == LocaleCn) {
                            // Empty block
                        }
                        local2 = GetItemLevelRankCoefficient(itemType);
                        if (local2 > 0) {
                            local1 = 100;
                            local2 = 10 * gearScore;
                            result2 =
                                Math.Max(grade - 1, 0) * 5;
                            local3 = local2 + result2;
                            result2 = GetItemLevelCoefficient(itemType);
                            local3 *= result2;
                            local3 *= 2;
                            result2 = Math.Max(grade - 3, 1);
                            local3 *= result2;
                            result2 = Math.Max(gearScore - 50, 0) * local1 * GetItemLevelCoefficient(itemType);
                            local3 += result2;
                        } else if (
                            itemType is 12 or 18 or 19 or 20
                        ) {
                            local1 = 100;
                            local2 = 10 * gearScore;
                            result2 =
                                Math.Max(grade - 2, 0) * 5;
                            local3 = local2 + result2;
                            result2 = GetItemLevelCoefficient(itemType);
                            local3 *= result2;
                            local3 *= 2;
                            result2 = Math.Max(grade - 3, 1);
                            local3 *= result2;
                            result2 =
                                Math.Max(gearScore - 50, 0) * local1 *
                                GetItemLevelCoefficient(itemType);
                            local3 += result2;
                        } else {
                            local2 = 10 * gearScore;
                            result2 =
                                Math.Max(grade - 1, 0) * 5;
                            local3 = local2 + result2;
                            result2 = GetItemLevelCoefficient(itemType);
                            local3 *= result2;
                        }
                    } else {
                        local2 = GetItemLevelRankCoefficient(itemType);
                        if (local2 > 0) {
                            local2 = GetLevelScoreFactorNa(gearScore);
                            local2 = 2 + local2;
                            local2 *= 1030;
                            result2 = GetRankScoreFactor(grade);
                            local2 *= result2;
                            result2 = GetItemLevelCoefficient(itemType);
                            local3 = local2 * result2;
                        } else if (
                            itemType is 12 or 18 or 19 or 20
                        ) {
                            local2 = GetLevelScoreFactorNa(gearScore);
                            local2 = 2 + local2;
                            local2 *= 1030;
                            result2 = GetRankScoreFactor(grade);
                            local2 *= result2;
                            result2 = GetItemLevelCoefficient(itemType);
                            local3 = local2 * result2;
                        } else {
                            local2 = 10 * gearScore;
                            result2 =
                                Math.Max(grade - 1, 0) * 5;
                            local3 = local2 + result2;
                            result2 = GetItemLevelCoefficient(itemType);
                            local3 *= result2;
                        }
                    }
                } else if (grade > 3 && gearScore >= 50) {
                    local2 = GetRankScoreFactor(grade);

                    result2 = GetItemLevelRankCoefficient(itemType);
                    if (result2 > 0) {
                        if (Locale == LocaleKr) {
                            result2 = GetLevelScoreFactorKr(gearScore);
                            result2 = 2 + result2;
                            result2 *= 1030;
                            result2 *= local2;
                            result = GetItemLevelCoefficient(itemType);
                            local3 = result2 * result;
                        } else {
                            if (Locale == LocaleCn) {
                                result2 = GetLevelScoreFactorCn(gearScore);
                                result2 = 2 + result2;
                                result2 *= 1030;
                                result2 *= local2;
                                result = GetItemLevelCoefficient(itemType);
                                local3 = result2 * result;
                            } else {
                                result2 = GetLevelScoreFactorNa(gearScore);
                                result2 = 2 + result2;
                                result2 *= 1030;
                                result2 *= local2;
                                result = GetItemLevelCoefficient(itemType);
                                local3 = result2 * result;
                            }
                        }
                    } else if (itemType is 12 or 18 or 19 or 20) {
                        if (Locale == LocaleKr) {
                            result2 = GetLevelScoreFactorKr(gearScore);
                            result2 = 2 + result2;
                            result2 *= 1030;
                            result2 *= local2;
                            result = GetItemLevelCoefficient(itemType);
                            local3 = result2 * result;
                        } else {
                            if (Locale == LocaleCn) {
                                result2 = GetLevelScoreFactorCn(gearScore);
                                result2 = 2 + result2;
                                result2 *= 1030;
                                result2 *= local2;
                                result = GetItemLevelCoefficient(itemType);
                                local3 = result2 * result;
                            } else {
                                result2 = GetLevelScoreFactorNa(gearScore);
                                result2 = 2 + result2;
                                result2 *= 1030;
                                result2 *= local2;
                                result = GetItemLevelCoefficient(itemType);
                                local3 = result2 * result;
                            }
                        }
                    } else {
                        local2 = 10 * gearScore;
                        result2 = Math.Max(grade - 1, 0) * 5;
                        result = 0;
                        local3 = local2 + result2;
                        result = GetItemLevelCoefficient(itemType);
                        local3 *= result;
                    }
                } else {
                    local2 = 10 * gearScore;
                    result2 = Math.Max(grade - 1, 0) * 5;
                    local3 = local2 + result2;
                    result2 = GetItemLevelCoefficient(itemType);
                    local3 *= result2;
                }
                local4 = local3;
            }
        }
        if (limitBreakLevel < 60) {
            if (Locale == LocaleKr) {
                if (itemType is 22 or > 29) {
                    local3 = local4 * 2;
                } else {
                    local3 = local4;
                }
            } else {
                local3 = local4;
            }
        } else if (limitBreakLevel < 70) {
            if (Locale == LocaleKr) {
                if (grade == 4) {
                    local3 = local4 * 1.2;
                } else {
                    local3 = local4;
                }
            } else {
                if (Locale == LocaleCn) {
                    if (grade == 4 && gearScore > 65) {
                        local3 = local4 * 1.2;
                    } else {
                        local3 = local4;
                    }
                } else {
                    local3 = local4;
                }
            }
        }

        if (limitBreakLevel < 60) {
            if (Locale == LocaleKr) {
                if (enchantLevel < 10) {
                    double temp = 8 * Math.Pow(enchantLevel, 2) + 4 * enchantLevel;
                    temp *= 3.5;
                    temp /= 21;
                    local2 = (float) Math.Round(temp, 0);
                } else if (enchantLevel >= 10) {
                    local2 = (float) (140 + 70 * (enchantLevel - 10));
                } else {
                    local2 = 0;
                }
                if (itemType is 22 or > 29) {
                    result2 = local2 * 2;
                } else {
                    result2 = local2;
                }
            } else {
                if (Locale == LocaleCn) {
                    if (grade >= 5) {
                        result = GetAdditionalItemLevelCoefficientR5(enchantLevel);
                    } else {
                        result = GetAdditionalItemLevelCoefficient(enchantLevel);
                    }
                    local2 = local3 * result;
                    result2 = local2;
                } else if (grade >= 4) {
                    result = GetAdditionalItemLevelCoefficientNa(enchantLevel);
                    local2 = local3 * result;
                } else {
                    result = GetAdditionalItemLevelCoefficient(enchantLevel);
                    local2 = local3 * result;
                }
                result2 = local2;
            }
        } else if (limitBreakLevel < 70) {
            if (grade >= 4) {
                if (Locale != LocaleKr) {
                    if (Locale == LocaleCn) {
                        // Empty block
                    }
                    result = GetAdditionalItemLevelCoefficientR5(enchantLevel);
                    local2 = local3 * result;
                } else {
                    result = GetAdditionalItemLevelCoefficientNa(enchantLevel);
                    local2 = local3 * result;
                }
            } else {
                result = GetAdditionalItemLevelCoefficient(enchantLevel);
                local2 = local3 * result;
            }
            result2 = local2;
        } else if (limitBreakLevel < 80) {
            // calcItemLevel: KR and CN take the per-grade coefficients, other regions the grade >= 4 split.
            if (Locale == LocaleKr || Locale == LocaleCn) {
                if (grade == 4) {
                    if (gearScore < 70) {
                        result = GetAdditionalItemLevelCoefficientR5(enchantLevel);
                        local2 = local3 * result;
                    } else {
                        result = GetAdditionalItemLevelCoefficientL70(enchantLevel);
                        local2 = local3 * result;
                    }
                } else if (grade > 4) {
                    result = GetAdditionalItemLevelCoefficientNa(enchantLevel);
                    local2 = local3 * result;
                } else {
                    result = GetAdditionalItemLevelCoefficient(enchantLevel);
                    local2 = local3 * result;
                }
            } else if (grade >= 4) {
                result = GetAdditionalItemLevelCoefficientNa(enchantLevel);
                local2 = local3 * result;
            } else {
                result = GetAdditionalItemLevelCoefficient(enchantLevel);
                local2 = local3 * result;
            }
            result2 = local2;
        } else if (grade >= 4) {
            result = GetAdditionalItemLevelCoefficientNa(enchantLevel);
            local2 = local3 * result;
            result2 = local2;
        } else {
            result = GetAdditionalItemLevelCoefficient(enchantLevel);
            local2 = local3 * result;
            result2 = local2;
        }
        // Each branch above sets result2; the KR one doubles it for item types 22 and above 29.
        result = (float) local3;
        return ((int, int)) (result, result2);
    }

    public static (int, string, int) CalcItemSocketUnlockIngredient(int type, int grade, int levelLimit, int p4, int skinType) {
        string tag = "SkinCrystal";
        int amount;

        if (skinType == 0) {
            if (Locale == LocaleKr) {
                levelLimit = Math.Min(56, levelLimit);
            } else {
                levelLimit = Math.Min(50, levelLimit);
            }

            int temp = Math.Max(levelLimit - 50, 0);
            amount = 200 + (temp * 20);
            int temp1 = Math.Max(grade - 3, 1);
            amount *= temp1;

            return (1, ItemRemakeCrystalChipTag, amount);
        }
        if (skinType > 0) {
            if (Locale == LocaleJp) {
                tag = "CrystalPiece";
                amount = 100;
            } else {
                amount = 690;
            }

            return (1, tag, amount);
        }

        // Default return in case none of the conditions are met
        return (0, string.Empty, 0);
    }

    public static (string, int) CalcGetGemStonePutOffPrice(int grade, ushort level, int skinType) {
        if (skinType > 0) {
            return (ItemRemakeCrystalChipTag, 0);
        }

        int amount;

        if (Locale == LocaleCn) {
            if (level <= 7) {
                int temp = 100 * level;
                temp *= level;
                amount = temp + 100;
            } else {
                amount = (int) Math.Round((double) (200 * level * level - 4800), -3);
            }
        } else {
            amount = 8 + (level * 2);
        }

        return (ItemRemakeCrystalChipTag, amount);
    }

    public static int CalcResolvePenaltyPrice(ushort level, int penaltyCount, int mode) {
        var variables = new {
            A = 2.7296,
            B = -12.841,
            C = 262.03,
        };

        double localA = variables.A;
        double localB = Math.Pow(level, 2);
        localA *= localB;
        localB = variables.B;
        localB *= level;
        localA += localB;
        localB = variables.C;
        double penalty = localA + localB;

        if (mode == 0) {
            return (int) Math.Floor(penalty + 0.5);
        }
        if (mode == 1) {
            return (int) Math.Floor((penalty * 1.1) + 0.5);
        }

        return 0;
    }

    public static int CalcRevivalPrice(ushort level) {
        if (level <= 8) {
            return 0;
        }

        return level * 10;
    }

    #region Fame
    // calcFameCompletionTicketCount (the same in the client's and the server's script): the completion tickets an alliance
    // quest takes, by its rank (S 0, A 1, B 2, C 3, D 4, none 5); the alliance is not read.
    public static int CalcFameCompletionTicketCount(int alliance, int rank) {
        int[] counts = [90, 60, 50, 20, 10, 0];
        return rank >= 0 && rank < counts.Length ? counts[rank] : 0;
    }

    // calcQuestRewardFamePoint, as the client's own script has it (the client shows this amount, FUN_140762c80): the
    // fame points a quest's complete reward gives when its useMainFamePoint is set, by the quest's rank (S 0 .. D 4,
    // none 5) and alliance; the grade is not read. The first five alliances get the larger table and a
    // useMainFamePoint of 2 or more doubles it; a later alliance gets the small table and ten times. The client's
    // script has no locale test (the upstream server script gives the larger table in KR and CN only).
    public static int CalcQuestRewardFamePoint(int rank, int grade, int useMainFamePoint, int alliance) {
        int[] points = [230, 200, 140, 100, 90, 0];
        int[] multipliers = [1, 10];
        if (alliance <= 5) {
            points = [2000, 1500, 1000, 800, 700, 0];
            multipliers = [1, 2];
        }
        return points[Math.Clamp(rank, 0, points.Length - 1)] * multipliers[Math.Clamp(useMainFamePoint, 1, multipliers.Length) - 1];
    }
    #endregion

    #region KillCount
    public static float CalcKillCountBonusExpRate(int killCount) {
        return killCount switch {
            > 100 => 0.15f,
            > 50 => 0.1f,
            > 40 => 0.07f,
            > 30 => 0.04f,
            > 20 => 0.01f,
            _ => 0f,
        };
    }

    public static int CalcKillCountGrade(int killCount) {
        return killCount switch {
            > 50 => 2,
            > 20 => 1,
            _ => 0,
        };
    }

    public static string CalcKillCountMsg(int killCount) {
        return killCount switch {
            30 => "s_killcount_msg1",
            40 => "s_killcount_msg2",
            50 => "s_killcount_msg3",
            100 => $"s_killcount_msg{Random.Shared.Next(4, 6)}",
            _ => string.Empty,
        };
    }

    // The client's calcKillCount: a kill 5 s or more after the last one starts a new count at 1; a sooner kill adds 1
    // when the target is level 50 or above or within 5 levels of the player, and leaves the count as it is otherwise.
    public static int CalcKillCount(int myLevel, int targetLevel, int timeElapsed, int killCount) {
        if (timeElapsed >= 5000) return 1;

        if (targetLevel >= 50 || Math.Abs(myLevel - targetLevel) <= 5) {
            return killCount + 1;
        }

        return killCount;
    }
    #endregion

    #region BlackMarket
    public static float CalcBlackMarketCostRate(int p1 = 0) {
        if (Locale == LocaleNa) {
            return 0.1f;
        } else {
            return 0.05f;
        }
    }

    public static int CalcBlackMarketBuyFeeCost(int cost, int currency, int p3 = 0, int p4 = 0) {
        return (int) Math.Floor(CalcBlackMarketCostRate(currency) * cost);
    }

    public static long CalcBlackMarketRegisterDepositMax() {
        return BlackmarketRegisterDepositMax;
    }

    public static int CalcBlackMarketRegisterDepositPercent() {
        return BlackmarketRegisterDepositPercent;
    }

    public static long CalcBlackMarketRegisterDeposit(long price) {
        return Math.Min((long) Math.Ceiling(price * BlackmarketRegisterDepositRate), BlackmarketRegisterDepositMax);
    }
    #endregion

    #region ItemMerge
    // The meso price of removing an item's merge option: by rarity, twice as much for item type 22.
    public static int CalcItemMergeRevertMeso(int rarity, int itemType) {
        int price;
        if (rarity < RankElite) {
            price = 0;
        } else if (rarity == RankElite) {
            price = 100000;
        } else if (rarity == RankExcellent) {
            price = 250000;
        } else {
            price = 500000;
        }

        if (itemType == 22) {
            price *= 2;
        }
        return price;
    }
    #endregion

    #region ItemBreak
    private const int ItemBreakLuckyBagCouponId = 40100027;
    private const int ItemBreakFreeLuckyBagCouponId = 40100032;
    private const string SkinCrystalItemTag = "SkinCrystal";

    // calcMeratSkinItemBreakReward: what dismantling an outfit gives when it has no break table entry. Skin
    // types 1, 2 and 5 give lucky bag coupons, 3 and 4 the free ones, more for a higher grade; type 99 gives a
    // count of 0. Any other type, and the NA branch, give nothing. levelLimit and price are not used.
    public static (string, int, int?, int?, string) CalcMeratSkinItemBreakReward(ushort levelLimit, int grade, long price, int skinType) {
        if (Locale == LocaleNa) {
            return (string.Empty, 0, null, null, string.Empty);
        }

        int amount = 1;
        switch (skinType) {
            case 1:
                if (grade == 3) {
                    amount *= 10;
                } else if (grade >= 4) {
                    amount *= 20;
                }
                return ("account", 1, ItemBreakLuckyBagCouponId, amount, SkinCrystalItemTag);
            case 2:
                if (grade == 1) {
                    amount *= 4;
                } else if (grade == 2) {
                    amount *= 5;
                } else if (grade == 3) {
                    amount *= 10;
                } else if (grade >= 4) {
                    amount *= 20;
                }
                return ("account", 1, ItemBreakLuckyBagCouponId, amount, SkinCrystalItemTag);
            case 3:
            case 4:
            case 5:
                if (grade >= 1) {
                    amount *= Math.Min(grade, 4);
                }
                return (skinType == 5 ? "account" : "character", 1,
                    skinType == 5 ? ItemBreakLuckyBagCouponId : ItemBreakFreeLuckyBagCouponId, amount, SkinCrystalItemTag);
            case 99:
                return ("account", 1, ItemBreakLuckyBagCouponId, 0, SkinCrystalItemTag);
            default:
                return (string.Empty, 0, null, null, string.Empty);
        }
    }

    // calcMeratSkinItemBreakRefundPercent: the share of an outfit's socket unlock cost that dismantling it returns
    // for each opened socket; none in the NA and JP branches.
    public static float CalcMeratSkinItemBreakRefundPercent() {
        if (Locale == LocaleNa || Locale == LocaleJp) {
            return 0;
        }
        return 75;
    }
    #endregion

    #region Gathering
    // The client's calcGatheringObjectgSuccessProp (CMasteryTable::LimitState, masterytable.cpp:620), in doubles as the
    // script runs. At another player's home (someoneElsesHome 0) both limits are scaled by 0.2 without rounding. Below the
    // high-rate count the rate is 100; at the recipe's max count it is 0; between, it falls on a parabola whose width
    // comes from the normal-rate limit with the script's 0.35 factor.
    public static float CalcGatheringObjectSuccessRate(int currentCount, int highPropLimitCount, int normalPropLimit, int someoneElsesHome = 1) {
        double high = highPropLimitCount;
        double normal = normalPropLimit;
        if (someoneElsesHome == 0) {
            normal *= 0.2;
            high *= 0.2;
        }

        double result;
        if (currentCount < high) {
            result = 100;
        } else if (currentCount == GatheringObjectMaxCount(high, normal)) {
            result = 0;
        } else {
            double width = (normal / 0.9 * 0.35 - 0.5) / 1.406;
            double half = (3 * width * 2 - width) / 2;
            double over = currentCount - high;
            result = (1 - 1 / (half * half) / 0.7111 * over * over) * 100;
        }

        return result < 0 ? 0 : (float) result;
    }

    // The client's calcGatheringObjectgMaxCount.
    public static int CalcGatheringObjectMaxCount(int highPropLimitCount, int normalPropLimitCount) {
        return (int) GatheringObjectMaxCount(highPropLimitCount, normalPropLimitCount);
    }

    private static double GatheringObjectMaxCount(double high, double normal) {
        return Math.Floor((normal * 10 / 3 - 5) * 1.7780938833570414 / 10 / Math.Sqrt(1 / 0.7111) + high) + 1;
    }
    #endregion

    [SuppressMessage("ReSharper", "CompareOfFloatsByEqualityOperator")]
    public static int CalcSendMailFee(int meso, int normalItemCount, int rateItemCount, int eliteItemCount, int excellentItemCount, int legendaryItemCount, int epicItemCount) {
        float normalFee = normalItemCount * 0.055f;
        float rareFee = rateItemCount * 0.07f;
        float eliteFee = eliteItemCount * 0.09f;
        float excellentFee = excellentItemCount * 0.115f;
        float legendaryFee = legendaryItemCount * 0.145f;
        float epicFee = epicItemCount * 0.18f;

        int feeFloored = (int) Math.Floor(normalFee);
        if (normalFee != feeFloored) {
            normalFee = feeFloored + 1;
        }

        feeFloored = (int) Math.Floor(rareFee);
        if (rareFee != feeFloored) {
            rareFee = feeFloored + 1;
        }

        feeFloored = (int) Math.Floor(eliteFee);
        if (eliteFee != feeFloored) {
            eliteFee = feeFloored + 1;
        }

        feeFloored = (int) Math.Floor(excellentFee);
        if (excellentFee != feeFloored) {
            excellentFee = feeFloored + 1;
        }

        feeFloored = (int) Math.Floor(legendaryFee);
        if (legendaryFee != feeFloored) {
            legendaryFee = feeFloored + 1;
        }

        feeFloored = (int) Math.Floor(epicFee);
        if (epicFee != feeFloored) {
            epicFee = feeFloored + 1;
        }

        feeFloored = (int) normalFee + (int) rareFee + (int) eliteFee + (int) excellentFee + (int) legendaryFee + (int) epicFee;

        float mesoFee;
        if (meso == 0) {
            mesoFee = 0;
        } else if (meso <= 100) {
            mesoFee = 5;
        } else {
            mesoFee = meso * 0.05f;
        }

        return feeFloored + (int) mesoFee;
    }

    public static float CalcNpcSpawnWeight(int mainTagCount, int subTagCount, int rareDegree, int difficultyDiff) {
        float mainWeight;
        float subWeight;
        float difficultyWeight;

        if (mainTagCount == 0) {
            mainWeight = 1;
        } else {
            mainWeight = (mainTagCount + 1) * 20;
        }

        if (subTagCount == 0) {
            subWeight = 1;
        } else {
            subWeight = (subTagCount + 1) * 1;
        }

        float rareWeight = rareDegree * 0.01f;

        if (difficultyDiff > 10) {
            difficultyWeight = 1;
        } else {
            difficultyWeight = 12 - difficultyDiff;
        }

        float weight = mainWeight + subWeight + rareWeight + difficultyWeight;
        return weight;
    }

    // calcTaxiCharge: the fare for a taxi ride over this many maps at this level. The script works in doubles; with
    // floats the fare can be 1 off (level 45 over 25 maps is 10971, floats gave 10972).
    public static int CalcTaxiCharge(int distance, int level) {
        double fee;
        if (level <= 24) {
            fee = 0.35307 * Math.Pow(level, 2) + -1.4401 * level + 34.075;
        } else {
            fee = 0.23451 * Math.Pow(level - 24, 2) + 24.221 * (level - 24) + 265.66;
        }

        return (int) Math.Floor(fee * distance / 2 + 0.5);
    }

    public static int CalcAirTaxiCharge(int level) {
        return 30000 + Math.Max(level - 10, 0) * 500;
    }

    public static int CalcRevivalMerat(int level, int dailyCount) {
        if (Locale == LocaleCn) {
            return 90 + 30 * dailyCount;
        } else if (Locale == LocaleJp) {
            if (dailyCount < 1) {
                return 20;
            } else if (dailyCount < 2) {
                return 30;
            } else if (dailyCount < 3) {
                return 40;
            } else {
                return 60;
            }
        } else {
            return 20 + 10 * dailyCount;
        }
    }

    // calcManufactureCompleteByMerat: the merets to finish a maid craft at once, from the seconds left (the dialog
    // passes ready time - server time, so a ready craft gives 0 or less). Lua numbers are doubles.
    public static long CalcManufactureCompleteByMerat(long seconds) {
        double t = seconds;
        double result;
        if (t < 120) {
            result = t / 120;
        } else if (t < 3600) {
            result = 1 + (t - 119) / 120 * 0.85;
        } else if (t < 7200) {
            result = 26 + (t - 3599) / 120 * 0.72;
        } else if (t < 10800) {
            result = 48 + (t - 7199) / 120 * 0.61;
        } else if (t < 28800) {
            result = 67 + (t - 10799) / 120 * 0.52;
        } else {
            result = 145 + (t - 28799) / 120 * 0.44;
        }
        if (Locale == LocaleCn) {
            result = 6 * result;
        }
        return (long) Math.Ceiling(result);
    }

    public static int GetMesoRevivalDailyMaxCount() {
        return 3;
    }

    public static int CalcRevivalMeso(int level, int dailyRemain, int dailyCount) {
        if (Locale == LocaleCn) {
            if (dailyCount == 1) {
                return 10000;
            } else {
                int[] local1 = [
                    11000,
                    22000,
                    33000,
                ];
                int[] local2 = [
                    55000,
                    70000,
                    100000,
                ];

                int local3 = Math.Min(GetMesoRevivalDailyMaxCount() - dailyRemain + 1, local1.Length);

                float local4 = local1[local3 - 1];
                float local5 = (local2[local3 - 1] - local1[local3 - 1]) / 40f;
                int local6 = Math.Max(level - 10, 0);

                local4 += local5 * local6;

                return (int) Math.Round(local4 / 1000) * 1000; // Rounding to nearest 1000
            }
        } else if (dailyCount == 1) {
            return 10000;
        } else {
            return 10000 + Math.Max(level - 10, 0) * 1000;
        }
    }

    public static (string, int, string, int, string, int) CalcGetItemRemakeIngredientNew(int itemType, int changeCount, int grade, int itemLevel) {
        string crystalTag = GetItemRemakeUseCrystalTag(itemType);
        float slotScore = GetSlotScore(itemType);
        int slotType = GetSlotType(itemType);

        int local2 = 1;
        if (slotType == SlotAcc) {
            local2 = 5;
        } else {
            local2 = 1;
        }

        int itemLevelCalcLimit;
        if (Locale == LocaleKr) {
            itemLevelCalcLimit = Math.Min(56, itemLevel);
        } else {
            itemLevelCalcLimit = Math.Min(50, itemLevel);
        }

        local2 = (int) Math.Floor(local2 * (1 + (itemLevelCalcLimit - 50) / 10.0f));
        changeCount = Math.Min(changeCount, 14);

        float local1 = 1.24f;
        if (changeCount < 10) {
            local1 = 1.25f;
        } else {
            local1 = 1.24f;
        }

        int amount = 1;
        int amount2 = 1;
        int amount3 = 1;

        if (grade <= 4) {
            amount2 = (int) Math.Floor(200 * Math.Pow(local1, changeCount)) * (int) slotScore;
        } else if (grade == 5) {
            amount2 = 400 * (changeCount + 1) * (int) slotScore;
        } else if (grade >= 6) {
            amount2 = 600 * (changeCount + 1) * (int) slotScore;
        }

        int local = 11 + changeCount + Math.Max(0, itemLevelCalcLimit - 50);
        amount3 = local * (int) slotScore;
        amount = local2 * (int) slotScore;

        if (grade >= 5) {
            if (Locale == LocaleKr && itemLevel < 60) {
                amount3 *= 3;
            } else {
                amount3 *= 15;
                amount *= 5;
            }

            amount = Math.Max(1, amount);
            amount2 = Math.Max(1, amount2);
            amount3 = Math.Max(1, amount3);

            return (crystalTag, amount, ItemRemakeCrystalChipTag, amount2, ItemRemakeMetaCellTag, amount3);
        }

        // Default return when l_44_2 < 5
        return (crystalTag, amount, ItemRemakeCrystalChipTag, amount2, ItemRemakeMetaCellTag, amount3);
    }

    public static (string, int, string, int, string, int) CalcGetPetRemakeIngredient(int timesChanged, int grade, int p3) {
        timesChanged = Math.Min(timesChanged, 14);

        int crystalCount = 5;
        int crystalChipCount = (int) (100 * Math.Pow(1.15, timesChanged) + 1e-5); // the client adds 1e-5 before the cast
        int metaCellCount = 100 + 20 * timesChanged;

        if (RankExcellent <= grade) {
            crystalCount = 2 * crystalCount;
            crystalChipCount = 3 * crystalChipCount;
            metaCellCount = 5 * metaCellCount;
        }

        return (ItemRemakeRedcrystalTag, crystalCount, ItemRemakeCrystalChipTag, crystalChipCount, ItemRemakeMetaCellTag, metaCellCount);
    }

    // calc_petExp: the exp a pet needs for its next level, scaled by the pet item's level limit
    // (x1 for 50, x1.5 for 60, x2.25 for 70, any other limit x1). The client casts the result to an integer.
    public static long CalcPetExp(long exp, int levelLimit) {
        return (long) (exp * PetLevelMultiplier(levelLimit));
    }

    // calc_petEnchantExp: the exp one pet fusion fodder gives, as (base, bonus). The arguments are the target
    // pet's rarity (not read), the fodder's rarity, pet level and pet exp, the player's fusion count before this
    // fodder, the daily bonus limit and the fodder item's level limit. While count + 1 stays within the limit
    // the fodder gives its full rarity exp and a 3000 bonus; after that the rarity part falls linearly to 20%
    // at a count of 60. A fodder rarity outside 1-5 reads nil in the script's tables; here it counts as 0.
    public static (long Base, long Bonus) CalcPetEnchantExp(int targetRarity, int fodderRarity, int fodderLevel, long fodderExp,
            int count, int limit, int levelLimit) {
        double multiplier = PetLevelMultiplier(levelLimit);
        double rarityExp = PetEnchantRarityExp(fodderRarity) * multiplier;
        double levelExp = PetEnchantLevelExp(fodderRarity) * multiplier;

        int next = count + 1;
        double factor;
        double bonus = 0;
        if (next <= limit) {
            factor = 1;
            bonus = 3000 * multiplier;
        } else if (next < 60) {
            factor = 1 - (next - limit) * 0.8 / (60 - limit);
        } else {
            factor = 0.2;
        }

        double total = Math.Floor(factor * rarityExp) + Math.Floor(0.8 * (levelExp * Math.Max(0, fodderLevel - 1) + fodderExp));
        return ((long) total, (long) bonus);
    }

    // calc_petEnchantCost: the meso one pet fusion fodder costs, by the target pet's rarity and the fodder's
    // pet level: the base cost plus a per-level step. The fodder rarity and pet exp are passed but not read.
    public static int CalcPetEnchantCost(int targetRarity, int fodderRarity, int fodderLevel, long fodderExp) {
        int step = targetRarity switch {
            1 => 1000,
            2 => 10000,
            3 => 50000,
            4 => 150000,
            5 => 250000,
            _ => 0,
        };
        return CalcPetEnchantBaseCost(targetRarity) + Math.Max(0, fodderLevel - 1) * step;
    }

    // calc_petEnchantBookCost: the meso one pet exp book costs, the base cost of the target pet's rarity.
    public static int CalcPetEnchantBookCost(int targetRarity) {
        return CalcPetEnchantBaseCost(targetRarity);
    }

    // calc_petEnchantBaseCost: 3000/5000/10000/30000/50000 for rarity 1-5; any other rarity reads nil (0).
    public static int CalcPetEnchantBaseCost(int rarity) {
        return rarity switch {
            1 => 3000,
            2 => 5000,
            3 => 10000,
            4 => 30000,
            5 => 50000,
            _ => 0,
        };
    }

    private static double PetLevelMultiplier(int levelLimit) => levelLimit switch {
        60 => 1.5,
        70 => 2.25,
        _ => 1,
    };

    private static int PetEnchantRarityExp(int rarity) => rarity switch {
        1 => 1500,
        2 => 3000,
        3 => 12000,
        4 => 24000,
        5 => 48000,
        _ => 0,
    };

    private static int PetEnchantLevelExp(int rarity) => rarity switch {
        1 => 22000,
        2 => 55000,
        3 => 90000,
        4 => 90000,
        5 => 90000,
        _ => 0,
    };

    // calc_petEvolutionCost: the meso one evolution fodder of this rarity costs. The script's table has four
    // entries; any other rarity reads nil, which the client takes as 0.
    public static long CalcPetEvolutionCost(int rarity) {
        return rarity switch {
            1 => 20000,
            2 => 50000,
            3 => 200000,
            4 => 500000,
            _ => 0,
        };
    }

    // calc_petEvolutionResultLevel: the pet level after an evolution to this rarity. The level limit is passed
    // but not read; ranks 1-5 give 1, any other rank reads nil (0).
    public static int CalcPetEvolutionResultLevel(int rarity, int levelLimit) {
        return rarity is >= 1 and <= 5 ? 1 : 0;
    }

    public static int CalcItemSocketMaxCount(int itemType, int grade, int levelLimit, int skinType) {
        if (skinType > 0) {
            return 0;
        }

        if (itemType != 19 && itemType != 20 && itemType != 12) {
            return 0;
        }

        if (levelLimit < 50) {
            return 0;
        }

        if (grade < 3) {
            return 0;
        }

        return grade == 3 ? 1 : 3;
    }

    #region StaticRate
    public static (float, float) StaticRateAbp(int baseValue, int deviation, int itemType, int jobCode, int optionLevelFactor, int grade, int levelLimit, int p7 = 0) {
        float abpMin = MathF.Round(0.0016f * optionLevelFactor + 0.0624f, 3);
        float abpMax = abpMin + 0.013f;

        float staticAbpValueMinFinal;
        float staticAbpValueMaxFinal;

        if (baseValue == 0) {
            staticAbpValueMinFinal = abpMin;
            staticAbpValueMaxFinal = abpMax;
        } else {
            staticAbpValueMinFinal = MathF.Round((abpMax) * (baseValue / 100f), 3);
            staticAbpValueMaxFinal = MathF.Round((abpMax) * (baseValue / 100f), 3);
        }

        return (staticAbpValueMinFinal, staticAbpValueMaxFinal);
    }
    #endregion

    #region StaticValue
    public static (float, float) StaticValueAddwap(int baseValue, int deviation, int itemType, int jobCode, int optionLevelFactor, int grade, int levelLimit, int p7 = 0) {
        // ReSharper disable once NotAccessedVariable
        float wapGrade = 0;
        // ReSharper disable once NotAccessedVariable
        float wapEpic = 0;

        if (levelLimit < 60) {
            if (Locale != LocaleKr) {
                if (Locale == LocaleCn) {
                    wapGrade = GetWeaponGradeAddWap(grade);
                    wapEpic = GetWeaponGradeAddWap(4);
                } else {
                    wapGrade = GetWeaponGradeAddWap50Na(grade);
                    wapEpic = GetWeaponGradeAddWap50Na(4);
                }
            } else {
                wapGrade = GetWeaponGradeAddWap(grade);
                wapEpic = GetWeaponGradeAddWap(4);
            }
        } else if (levelLimit < 70) {
            if (Locale == LocaleKr) {
                wapGrade = GetWeaponGradeAddWap60Kr(grade);
                wapEpic = GetWeaponGradeAddWap60Kr(4);
            } else if (Locale == LocaleCn) {
                wapGrade = GetWeaponGradeAddWap60Cn(grade);
                wapEpic = GetWeaponGradeAddWap60Cn(4);
            } else {
                wapGrade = GetWeaponGradeAddWap60Na(grade);
                wapEpic = GetWeaponGradeAddWap60Na(4);
            }
        } else if (levelLimit < 80) {
            if (Locale == LocaleKr) {
                wapGrade = GetWeaponGradeAddWap70Kr(grade);
                wapEpic = GetWeaponGradeAddWap70Kr(4);
            } else if (Locale == LocaleCn) {
                wapGrade = GetWeaponGradeAddWap70Cn(grade);
                wapEpic = GetWeaponGradeAddWap70Cn(4);
            } else {
                wapGrade = GetWeaponGradeAddWap70Na(grade);
                wapEpic = GetWeaponGradeAddWap70Na(4);
            }
        } else if (levelLimit < 90) {
            if (Locale == LocaleKr) {
                wapGrade = GetWeaponGradeAddWap80Kr(grade);
                wapEpic = GetWeaponGradeAddWap80Kr(4);
            } else if (Locale == LocaleCn) {
                wapGrade = GetWeaponGradeAddWap80Cn(grade);
                wapEpic = GetWeaponGradeAddWap80Cn(4);
            } else {
                wapGrade = GetWeaponGradeAddWap80Na(grade);
                wapEpic = GetWeaponGradeAddWap80Na(4);
            }
        } else {
            if (Locale == LocaleKr) {
                wapGrade = GetWeaponGradeAddWap90Kr(grade);
                wapEpic = GetWeaponGradeAddWap90Kr(4);
            } else if (Locale == LocaleCn) {
                wapGrade = GetWeaponGradeAddWap90Cn(grade);
                wapEpic = GetWeaponGradeAddWap90Cn(4);
            } else {
                wapGrade = GetWeaponGradeAddWap90Na(grade);
                wapEpic = GetWeaponGradeAddWap90Na(4);
            }
        }

        float local1 = 0;

        if (optionLevelFactor == 1) {
            local1 = 5;
        } else {
            float temp = 5;

            for (int i = 2; i <= optionLevelFactor; i++) {
                float local2 = 0;

                if (i > 49) {
                    local2 = temp * UpgradeFactor;
                } else {
                    local2 = Math.Max(i / 30f * 20f - 0.8f, 0);
                }

                temp += local2;
                local1 = temp;
            }
        }

        float local3;
        if (itemType == 54) {
            local3 = MathF.Round((local1) * GetWeaponSlotCoefficient((int) ItemSlotType.Staff) / GetWeaponAttackSpeedCoefficient((int) ItemSlotType.Staff), 1);
        } else {
            local3 = MathF.Round((local1) * GetWeaponSlotCoefficient(itemType) / GetWeaponAttackSpeedCoefficient(itemType), 1);
        }

        float wapMaxGrade = Math.Max(local3 * GetStaticWapmaxCoefficient(grade) * (1 + GetWeaponSlotDeviation(itemType, 1)), 2);
        float wapMaxEpic = Math.Max(local3 * GetStaticWapmaxCoefficient(4) * (1 + GetWeaponSlotDeviation(itemType, 1)), 2);

        float resultRounded = 0;

        if (levelLimit > 49 && grade > 3) {
            resultRounded = MathF.Round(wapMaxGrade + wapMaxEpic * (grade - 4), 0);
        }

        float result = Math.Max(resultRounded, 0);
        return (result, result);
    }

    public static (float, float) StaticValueWapMax(int baseValue, int deviation, int itemType, int jobCode, int optionLevelFactor, int grade, int levelLimit, int p7 = 0) {
        float local1 = 0;

        if (optionLevelFactor == 1) {
            local1 = 5;
        } else {
            float local2 = 5;

            for (int i = 2; i <= optionLevelFactor; i++) {
                float local3 = 0;

                if (Locale == LocaleKr) {
                    if (levelLimit < 60) {
                        local3 = Math.Max(i / 30f * 20f - 0.8f, 0);
                    } else if (i > 49) {
                        local3 = local2 * UpgradeFactor;
                    } else {
                        local3 = Math.Max(i / 30f * 20f - 0.8f, 0);
                    }
                } else if (i > 49) {
                    local3 = local2 * UpgradeFactor;
                } else {
                    local3 = Math.Max(i / 30f * 20f - 0.8f, 0);
                }

                local2 += local3;
                local1 = local2;
            }
        }


        float addWap;
        if (Locale == LocaleKr) {
            if (levelLimit < 60) {
                addWap = 0;
            } else {
                addWap = StaticValueAddwap(baseValue, deviation, itemType, jobCode, optionLevelFactor, grade, levelLimit, p7).Item1;
            }
        } else {
            addWap = StaticValueAddwap(baseValue, deviation, itemType, jobCode, optionLevelFactor, grade, levelLimit, p7).Item1;
        }


        float slotCoefficient;
        if (itemType == 54) {
            slotCoefficient = MathF.Round((local1) * GetWeaponSlotCoefficient((int) ItemSlotType.Staff) / GetWeaponAttackSpeedCoefficient((int) ItemSlotType.Staff), 1);
        } else {
            slotCoefficient = MathF.Round((local1) * GetWeaponSlotCoefficient(itemType) / GetWeaponAttackSpeedCoefficient(itemType), 1);
        }

        float staticWapmaxValueMax = Math.Max(slotCoefficient * GetStaticWapmaxCoefficient(grade) * (1 + GetWeaponSlotDeviation(itemType, 1)), 2) + addWap;
        float staticWapmaxValueMin = Math.Max(staticWapmaxValueMax * 0.78f, 1);

        float staticWapmaxValueMinFinal;
        float staticWapmaxValueMaxFinal;

        if (baseValue == 0) {
            staticWapmaxValueMinFinal = staticWapmaxValueMin;
            staticWapmaxValueMaxFinal = staticWapmaxValueMax;
        } else {
            staticWapmaxValueMinFinal = MathF.Round(staticWapmaxValueMax * (baseValue / 100f), 0);
            staticWapmaxValueMaxFinal = MathF.Round(staticWapmaxValueMax * (baseValue / 100f), 0);
        }

        return (staticWapmaxValueMinFinal, staticWapmaxValueMaxFinal);
    }
    public static (float, float) StaticValueHp(int baseValue, int deviation, int itemType, int jobCode, float optionLevelFactor, int grade, int levelLimit, int p7 = 0) {
        float local1;
        float local2;
        float resultMin;
        float resultMax;

        if (itemType == 22) {
            float temp = -0.00007f * (float) Math.Pow(optionLevelFactor, 3)
                         + 0.0162f * (float) Math.Pow(optionLevelFactor, 2)
                         + 0.1656f * optionLevelFactor
                         - 0.5098f;
            float maxed = Math.Max(temp, 1f) * 1.8f;
            local1 = (float) Math.Round(maxed, 0);
        } else {
            float temp = -0.00007f * (float) Math.Pow(optionLevelFactor, 3)
                         + 0.0162f * (float) Math.Pow(optionLevelFactor, 2)
                         + 0.1656f * optionLevelFactor
                         - 0.5098f;
            float maxed = Math.Max(temp, 1f);
            local1 = (float) Math.Round(maxed, 0);
        }

        if (local1 < 7) {
            local2 = local1 + 4;
        } else if (local1 is > 6 and < 9) {
            local2 = local1 + 5;
        } else if (local1 is > 8 and < 11) {
            local2 = local1 + 6;
        } else if (local1 is > 10 and < 14) {
            local2 = local1 + 7;
        } else if (local1 is > 13 and < 16) {
            local2 = local1 + 8;
        } else if (local1 is > 15 and < 20) {
            local2 = local1 + 9;
        } else if (local1 is > 19 and < 22) {
            local2 = local1 + 10;
        } else if (local1 is > 21 and < 25) {
            local2 = local1 + 11;
        } else if (local1 is > 24 and < 28) {
            local2 = local1 + 12;
        } else if (local1 is > 27 and < 32) {
            local2 = local1 + 13;
        } else if (local1 is > 31 and < 37) {
            local2 = local1 + 14;
        } else if (local1 is > 36 and < 42) {
            local2 = local1 + 15;
        } else if (local1 is > 41 and < 46) {
            local2 = local1 + 16;
        } else if (local1 is > 45 and < 53) {
            local2 = local1 + 17;
        } else if (local1 is > 52 and < 63) {
            local2 = local1 + 18;
        } else if (local1 is > 62 and < 75) {
            local2 = local1 + 19;
        } else if (local1 > 74) {
            local2 = local1 + 20;
        } else {
            local2 = local1;
        }

        if (baseValue == 0) {
            resultMin = local1;
            resultMax = local2;
        } else {
            float val = (float) Math.Round(local2 * (baseValue / 100f), 0);
            resultMin = val;
            resultMax = val;
        }

        return (resultMin, resultMax);
    }

    public static float StaticValueAddNdd(int baseValue, int deviation, int itemType, int jobCode, int optionLevelFactor, int grade, int levelLimit, int p7 = 0) {
        float nddGrade = 0;
        float nddEpic = 0;

        if (levelLimit < 60) {
            if (Locale != LocaleKr) {
                if (Locale == LocaleCn) {
                    nddGrade = GetWeaponGradeAddNdd(grade);
                    nddEpic = GetWeaponGradeAddNdd(4);
                } else {
                    nddGrade = GetWeaponGradeAddNdd50Na(grade);
                    nddEpic = GetWeaponGradeAddNdd50Na(4);
                }
            }
        } else if (levelLimit < 70) {
            if (Locale == LocaleKr) {
                nddGrade = GetWeaponGradeAddNdd60Kr(grade);
                nddEpic = GetWeaponGradeAddNdd60Kr(4);
            } else if (Locale == LocaleCn) {
                nddGrade = GetWeaponGradeAddNdd60(grade);
                nddEpic = GetWeaponGradeAddNdd60(4);
            } else {
                nddGrade = GetWeaponGradeAddNdd60Na(grade);
                nddEpic = GetWeaponGradeAddNdd60Na(4);
            }
        } else if (levelLimit < 80) {
            if (Locale == LocaleKr) {
                nddGrade = GetWeaponGradeAddNdd70Kr(grade);
                nddEpic = GetWeaponGradeAddNdd70Kr(4);
            } else if (Locale == LocaleCn) {
                nddGrade = GetWeaponGradeAddNdd70(grade);
                nddEpic = GetWeaponGradeAddNdd70(4);
            } else {
                nddGrade = GetWeaponGradeAddNdd70Na(grade);
                nddEpic = GetWeaponGradeAddNdd70Na(4);
            }
        } else if (levelLimit < 90) {
            if (Locale == LocaleKr) {
                nddGrade = GetWeaponGradeAddNdd80Kr(grade);
                nddEpic = GetWeaponGradeAddNdd80Kr(4);
            } else if (Locale == LocaleCn) {
                nddGrade = GetWeaponGradeAddNdd80(grade);
                nddEpic = GetWeaponGradeAddNdd80(4);
            } else {
                nddGrade = GetWeaponGradeAddNdd80Na(grade);
                nddEpic = GetWeaponGradeAddNdd80Na(4);
            }
        } else {
            if (Locale == LocaleKr) {
                nddGrade = GetWeaponGradeAddNdd90Kr(grade);
                nddEpic = GetWeaponGradeAddNdd90Kr(4);
            } else if (Locale == LocaleCn) {
                nddGrade = GetWeaponGradeAddNdd90(grade);
                nddEpic = GetWeaponGradeAddNdd90(4);
            } else {
                nddGrade = GetWeaponGradeAddNdd90Na(grade);
                nddEpic = GetWeaponGradeAddNdd90Na(4);
            }
        }

        float local1 = 0;
        float nddGrade2 = 0;
        float nddEpic2 = 0;

        if (optionLevelFactor == 1) {
            local1 = 9;
        } else {
            float local = 9;

            for (int i = 2; i <= optionLevelFactor; i++) {
                float armorLevelBaseCurrentLevelPlus;
                if (i > 49) {
                    armorLevelBaseCurrentLevelPlus = local * UpgradeFactor;
                } else {
                    armorLevelBaseCurrentLevelPlus = MathF.Max(1 + (i / 10f) * 4, 0);
                }
                local += armorLevelBaseCurrentLevelPlus;
                local1 = local;
            }
        }

        if (itemType == 21) { // belt
            nddGrade2 = MathF.Max(MathF.Round((local1) * GetArmorConstantSlotCoefficient(itemType) *
                                              GetArmorConstantJobCoefficient((int) JobCode.None) *
                                              GetStaticArmorGradeCoefficient(grade), 0), 4) * nddGrade;

            nddEpic2 = MathF.Max(MathF.Round((local1) * GetArmorConstantSlotCoefficient(itemType) *
                                             GetArmorConstantJobCoefficient((int) JobCode.None) *
                                             GetStaticArmorGradeCoefficient(4), 0), 4) * nddEpic;
        } else if (itemType is 12 or 18 or 19 or 20) {
            // Accessories: Earring, Cape, Necklace, Ring
            nddGrade2 = MathF.Max(MathF.Round((local1) * GetArmorConstantSlotCoefficient(itemType) *
                                              GetStaticAccGradeCoefficient(grade), 0), 0) * nddGrade;

            nddEpic2 = MathF.Max(MathF.Round((local1) * GetArmorConstantSlotCoefficient(itemType) *
                                             GetStaticAccGradeCoefficient(4), 0), 0) * nddEpic;
        } else {
            nddGrade2 = MathF.Max(MathF.Round((local1) * GetArmorConstantSlotCoefficient(itemType) *
                                              GetArmorConstantJobCoefficient(jobCode) *
                                              GetStaticArmorGradeCoefficient(grade), 0), 4) * nddGrade;

            nddEpic2 = MathF.Max(MathF.Round((local1) * GetArmorConstantSlotCoefficient(itemType) *
                                             GetArmorConstantJobCoefficient(jobCode) *
                                             GetStaticArmorGradeCoefficient(4), 0), 4) * nddEpic;
        }

        float resultRounded = 0;

        if (levelLimit > 49 && grade > 3) {
            if (baseValue == 0) {
                resultRounded = MathF.Round(nddGrade2 + nddEpic2 * (grade - 4), 0);
            } else {
                resultRounded = MathF.Round((nddGrade2 + nddEpic2 * (grade - 4)) * (baseValue / 100f), 0);
            }
        }

        return MathF.Max(resultRounded, 0);
    }

    public static (float, float) StaticValueNdd(int baseValue, int deviation, int itemType, int jobCode, int optionLevelFactor, int grade, int levelLimit, int p7 = 0) {
        float minNdd = 0;
        float maxNdd = 0;
        float l2010 = 0;
        float l2011;
        float armorLevelBaseCurrentLevelPlus = 0;

        if (optionLevelFactor == 1) {
            l2010 = 9;
        } else {
            float l2011Local = 9;

            for (int i = 2; i <= optionLevelFactor; i += 1) {
                if (Locale == LocaleKr) {
                    if (levelLimit < 60) {
                        armorLevelBaseCurrentLevelPlus = MathF.Max(1 + (i / 10f) * 4, 0);
                    } else if (i > 49) {
                        armorLevelBaseCurrentLevelPlus = l2011Local * UpgradeFactor;
                    } else {
                        armorLevelBaseCurrentLevelPlus = MathF.Max(1 + (i / 10f) * 4, 0);
                    }
                } else if (i > 49) {
                    armorLevelBaseCurrentLevelPlus = l2011Local * UpgradeFactor;
                } else {
                    armorLevelBaseCurrentLevelPlus = MathF.Max(1 + (i / 10f) * 4, 0);
                }
                l2011Local += armorLevelBaseCurrentLevelPlus;
            }
            l2010 = l2011Local;
        }

        // l_20_11
        if (Locale == LocaleKr) {
            if (levelLimit < 60) {
                l2011 = 0;
            } else {
                l2011 = StaticValueAddNdd(baseValue, deviation, itemType, jobCode, optionLevelFactor, grade, levelLimit, p7);
            }
        } else {
            l2011 = StaticValueAddNdd(baseValue, deviation, itemType, jobCode, optionLevelFactor, grade, levelLimit, p7);
        }

        if (itemType == 21) {
            maxNdd = MathF.Max(MathF.Round(
                (l2010) * GetArmorConstantSlotCoefficient(itemType) *
                GetArmorConstantJobCoefficient((int) JobCode.None) *
                GetStaticArmorGradeCoefficient(grade), 0), 4);
        } else if (itemType is 12 or 18 or 19 or 20) {
            maxNdd = MathF.Max(MathF.Round(
                (l2010) * GetArmorConstantSlotCoefficient(itemType) *
                GetStaticAccGradeCoefficient(grade), 0), 0);
        } else {
            maxNdd = MathF.Max(MathF.Round(
                (l2010) * GetArmorConstantSlotCoefficient(itemType) *
                GetArmorConstantJobCoefficient(jobCode) *
                GetStaticArmorGradeCoefficient(grade), 0), 4);
        }

        if (maxNdd < 466) {
            minNdd = MathF.Round(maxNdd * MathF.Max(0.0598f * MathF.Log(maxNdd) + 0.432f, 0.5f), 0);
        } else {
            minNdd = MathF.Max(MathF.Round(maxNdd * 0.8f, 0), 1);
        }

        float staticNddValueMinFinal, staticNddValueMaxFinal;
        if (baseValue == 0) {
            staticNddValueMinFinal = minNdd + l2011;
            staticNddValueMaxFinal = maxNdd + l2011;
        } else {
            staticNddValueMinFinal = MathF.Round(maxNdd * (baseValue / 100f), 0) + l2011;
            staticNddValueMaxFinal = MathF.Round(maxNdd * (baseValue / 100f), 0) + l2011;
        }

        return (staticNddValueMinFinal, staticNddValueMaxFinal);
    }

    public static (float, float) StaticValuePap(int baseValue, int deviation, int itemType, int jobCode, int optionLevelFactor, int grade, int levelLimit, int p7 = 0) {
        float papMin = 0;
        float papMax = 0;

        if (itemType is > 29 and < 50) {
            float temp = -3.8e-006f * MathF.Pow(optionLevelFactor, 3);
            temp += 0.0009f * MathF.Pow(optionLevelFactor, 2);
            temp += 0.0294f * optionLevelFactor;

            papMin = MathF.Round(temp, 0);
            papMax = papMin + 3;
        } else if (itemType is > 49 and < 60) {
            float temp = -3.8e-006f * MathF.Pow(optionLevelFactor, 3);
            temp += 0.0009f * MathF.Pow(optionLevelFactor, 2);
            temp += 0.0294f * optionLevelFactor;
            temp *= 1.8f;

            papMin = MathF.Round(temp, 0);
            papMax = papMin + 3;
        }

        float staticPapValueMinFinal;
        float staticPapValueMaxFinal;

        if (baseValue == 0) {
            staticPapValueMinFinal = papMin;
            staticPapValueMaxFinal = papMax;
        } else {
            staticPapValueMinFinal = MathF.Round(papMax * (baseValue / 100f), 0);
            staticPapValueMaxFinal = MathF.Round(papMax * (baseValue / 100f), 0);
        }

        return (staticPapValueMinFinal, staticPapValueMaxFinal);
    }

    public static (float, float) StaticValueMap(int baseValue, int deviation, int itemType, int jobCode, int optionLevelFactor, int grade, int levelLimit, int p7 = 0) {
        float mapMin = 0;
        float mapMax = 0;

        if (itemType is > 29 and < 50) {
            float temp = -3.8e-006f * MathF.Pow(optionLevelFactor, 3);
            temp += 0.0009f * MathF.Pow(optionLevelFactor, 2);
            temp += 0.0294f * optionLevelFactor;

            mapMin = MathF.Round(temp, 0);
            mapMax = mapMin + 3;
        } else if (itemType is > 49 and < 60) {
            float temp = -3.8e-006f * MathF.Pow(optionLevelFactor, 3);
            temp += 0.0009f * MathF.Pow(optionLevelFactor, 2);
            temp += 0.0294f * optionLevelFactor;
            temp *= 1.8f;

            mapMin = MathF.Round(temp, 0);
            mapMax = mapMin + 3;
        } else {
            mapMin = 0;
            mapMax = 0;
        }

        float staticMapValueMinFinal;
        float staticMapValueMaxFinal;

        if (baseValue == 0) {
            staticMapValueMinFinal = mapMin;
            staticMapValueMaxFinal = mapMax;
        } else {
            staticMapValueMinFinal = MathF.Round(mapMax * (baseValue / 100f), 0);
            staticMapValueMaxFinal = MathF.Round(mapMax * (baseValue / 100f), 0);
        }

        return (staticMapValueMinFinal, staticMapValueMaxFinal);
    }

    public static (float, float) StaticValuePar(int baseValue, int deviation, int itemType, int jobCode, int optionLevelFactor, int grade, int levelLimit, int p7 = 0) {

        float temp = 1e-005f * MathF.Pow(optionLevelFactor, 3);
        temp -= 0.003f * MathF.Pow(optionLevelFactor, 2);
        temp += 0.367f * optionLevelFactor;
        temp += 4.8841f;

        float parMin = MathF.Round(temp, 0);

        float parMax;
        if (parMin < 5) {
            parMax = parMin + 3;
            // ReSharper disable once CompareOfFloatsByEqualityOperator
        } else if (parMin == 5) {
            parMax = parMin + 4;
        } else if (parMin > 5) {
            parMax = parMin + 5;
        } else {
            parMax = parMin;
        }

        float staticParValueMinFinal;
        float staticParValueMaxFinal;

        if (baseValue == 0) {
            staticParValueMinFinal = parMin;
            staticParValueMaxFinal = parMax;
        } else {
            staticParValueMinFinal = MathF.Round(parMax * (baseValue / 100f), 0);
            staticParValueMaxFinal = MathF.Round(parMax * (baseValue / 100f), 0);
        }

        return (staticParValueMinFinal, staticParValueMaxFinal);
    }

    public static (float, float) StaticValueMar(int baseValue, int deviation, int itemType, int jobCode, int optionLevelFactor, int grade, int levelLimit, int p7 = 0) {
        float marMin;

        if (itemType == 40) {
            float temp = 1e-005f * MathF.Pow(optionLevelFactor, 3);
            temp -= 0.003f * MathF.Pow(optionLevelFactor, 2);
            temp += 0.367f * optionLevelFactor;
            temp += 4.8841f;

            marMin = MathF.Round(temp, 0);
        } else {
            float temp = 1e-005f * MathF.Pow(optionLevelFactor, 3);
            temp -= 0.003f * MathF.Pow(optionLevelFactor, 2);
            temp += 0.367f * optionLevelFactor;
            temp += 4.8841f;
            temp *= GetStaticGradeCoefficient(grade);

            marMin = MathF.Round(temp, 0);
        }

        float marMax;
        if (marMin < 5) {
            marMax = marMin + 3;
            // ReSharper disable once CompareOfFloatsByEqualityOperator
        } else if (marMin == 5) {
            marMax = marMin + 4;
        } else if (marMin > 5) {
            marMax = marMin + 5;
        } else {
            marMax = marMin;
        }

        float staticMarValueMinFinal;
        float staticMarValueMaxFinal;

        if (baseValue == 0) {
            staticMarValueMinFinal = marMin;
            staticMarValueMaxFinal = marMax;
        } else {
            staticMarValueMinFinal = MathF.Round(marMax * (baseValue / 100f), 0);
            staticMarValueMaxFinal = MathF.Round(marMax * (baseValue / 100f), 0);
        }

        return (staticMarValueMinFinal, staticMarValueMaxFinal);
    }
    #endregion

    #region ConstantValue
    public static (float, float) ConstantValueAddwap(int baseValue, int deviation, int itemType, int jobCode, int optionLevelFactor, int grade, ushort levelLimit, ushort p7 = 0) {
        float gradeWap;
        float epicGradeWap;

        if (levelLimit < 60) {
            if (Locale != LocaleKr) {
                if (Locale == LocaleCn) {
                    gradeWap = GetWeaponGradeAddWap(grade);
                    epicGradeWap = GetWeaponGradeAddWap(4);
                } else {
                    gradeWap = GetWeaponGradeAddWap50Na(grade);
                    epicGradeWap = GetWeaponGradeAddWap50Na(4);
                }
            } else {
                gradeWap = GetWeaponGradeAddWap50Na(grade);
                epicGradeWap = GetWeaponGradeAddWap50Na(4);
            }
        } else if (levelLimit < 70) {
            if (Locale == LocaleKr) {
                gradeWap = GetWeaponGradeAddWap60Kr(grade);
                epicGradeWap = GetWeaponGradeAddWap60Kr(4);
            } else if (Locale == LocaleCn) {
                gradeWap = GetWeaponGradeAddWap60Cn(grade);
                epicGradeWap = GetWeaponGradeAddWap60Cn(4);
            } else {
                gradeWap = GetWeaponGradeAddWap60Na(grade);
                epicGradeWap = GetWeaponGradeAddWap60Na(4);
            }
        } else if (levelLimit < 80) {
            if (Locale == LocaleKr) {
                gradeWap = GetWeaponGradeAddWap70Kr(grade);
                epicGradeWap = GetWeaponGradeAddWap70Kr(4);
            } else if (Locale == LocaleCn) {
                gradeWap = GetWeaponGradeAddWap70Cn(grade);
                epicGradeWap = GetWeaponGradeAddWap70Cn(4);
            } else {
                gradeWap = GetWeaponGradeAddWap70Na(grade);
                epicGradeWap = GetWeaponGradeAddWap70Na(4);
            }
        } else if (levelLimit < 90) {
            if (Locale == LocaleKr) {
                gradeWap = GetWeaponGradeAddWap80Kr(grade);
                epicGradeWap = GetWeaponGradeAddWap80Kr(4);
            } else if (Locale == LocaleCn) {
                gradeWap = GetWeaponGradeAddWap80Cn(grade);
                epicGradeWap = GetWeaponGradeAddWap80Cn(4);
            } else {
                gradeWap = GetWeaponGradeAddWap80Na(grade);
                epicGradeWap = GetWeaponGradeAddWap80Na(4);
            }
        } else {
            if (Locale == LocaleKr) {
                gradeWap = GetWeaponGradeAddWap90Kr(grade);
                epicGradeWap = GetWeaponGradeAddWap90Kr(4);
            } else if (Locale == LocaleCn) {
                gradeWap = GetWeaponGradeAddWap90Cn(grade);
                epicGradeWap = GetWeaponGradeAddWap90Cn(4);
            } else {
                gradeWap = GetWeaponGradeAddWap90Na(grade);
                epicGradeWap = GetWeaponGradeAddWap90Na(4);
            }
        }

        float local10 = 0;

        if (optionLevelFactor == 1) {
            local10 = 5;
        } else {
            float local = 5;

            for (int i = 2; i <= optionLevelFactor; i++) {
                float local19;
                if (i > 49) {
                    local19 = local * UpgradeFactor;
                } else {
                    local19 = Math.Max(i / 30.0f * 20.0f - 0.8f, 0);
                }
                local += local19;
                local10 = local;
            }
        }

        float local11 = MathF.Round(local10 * GetWeaponSlotCoefficient(itemType) / GetWeaponAttackSpeedCoefficient(itemType), 1);
        float local12 = MathF.Round(local11 * GetWeaponGradeCoefficient(deviation, grade) * (1 - GetWeaponSlotDeviation(itemType, deviation)), 0);
        float local13 = MathF.Round(local11 * GetWeaponGradeCoefficient(deviation, grade) * (1 + GetWeaponSlotDeviation(itemType, deviation)), 0);
        float local14 = (local12 + local13) / 2 * gradeWap;

        float local15 = MathF.Round(local11 * GetWeaponGradeCoefficient(deviation, 4) * (1 - GetWeaponSlotDeviation(itemType, deviation)), 0);
        float local16 = MathF.Round(local11 * GetWeaponGradeCoefficient(deviation, 4) * (1 + GetWeaponSlotDeviation(itemType, deviation)), 0);
        float local17 = (local15 + local16) / 2 * epicGradeWap;

        float roundedResult = 0;

        if (levelLimit > 49 && grade > 3) {
            roundedResult = MathF.Round(local14 + local17 * (grade - 4), 0);
        }

        float result = Math.Max(roundedResult, 0);
        return (result, result);
    }
    public static (float, float) ConstantValueStr(int baseValue, int deviation, int itemType, int jobCode, int optionLevelFactor, int grade, ushort levelLimit, ushort p7 = 0) {
        float result = 0f;

        if (levelLimit < 51) {
            if (jobCode is 10 or 20 or 90) {
                if (itemType == 22) {

                    result = MathF.Round(1.8f * (levelLimit - 20) / 3f, 0);
                } else {

                    result = MathF.Round((levelLimit - 20) / 3f, 0);
                }
            } else if (jobCode == 0) {
                if (itemType == 22) {

                    result = MathF.Round(1.8f * (levelLimit - 20) / 6f, 0);
                } else {

                    result = MathF.Round((levelLimit - 20) / 6f, 0);
                }
            } else {
                result = 0f;
            }
        } else if (jobCode is 10 or 20 or 90) {
            if (itemType == 22) {

                result = MathF.Round(1.8f * (2 * levelLimit - 90) / 2f, 0);
            } else {

                result = MathF.Round(2 * levelLimit - 90, 0);
            }
        } else if (jobCode == 0) {
            if (itemType == 22) {
                result = MathF.Round(1.8f * (2 * levelLimit - 90) / 6f, 0);
            } else {

                result = MathF.Round((2 * levelLimit - 90) / 3f, 0);
            }
        }

        return (result, result);
    }

    public static (float, float) ConstantValueInt(int baseValue, int deviation, int itemType, int jobCode, int optionLevelFactor, int grade, ushort levelLimit, ushort p7) {
        float result = 0f;

        if (levelLimit < 51) {
            if (jobCode == 30 || jobCode == 40 || jobCode == 110 || jobCode == 100 && p7 == 1) {
                if (itemType == 22) {
                    result = MathF.Round(1.8f * (levelLimit - 20) / 3f, 0);
                } else {
                    result = MathF.Round((levelLimit - 20) / 3f, 0);
                }
            } else if (jobCode == 0) {
                if (itemType == 22) {
                    result = MathF.Round(1.8f * (levelLimit - 20) / 6f, 0);
                } else {
                    result = MathF.Round((levelLimit - 20) / 6f, 0);
                }
            } else {
                result = 0;
            }
        } else if (jobCode == 30 || jobCode == 40 || jobCode == 110 || jobCode == 100 && p7 == 1) {
            if (itemType == 22) {
                result = MathF.Round(1.8f * (2 * levelLimit - 90) / 2f, 0);
            } else {
                result = MathF.Round(2 * levelLimit - 90, 0);
            }
        } else if (jobCode == 0) {
            if (itemType == 22) {
                result = MathF.Round(1.8f * (2 * levelLimit - 90) / 6f, 0);
            } else {
                result = MathF.Round((2 * levelLimit - 90) / 3f, 0);
            }
        }

        return (result, result);
    }

    public static (float, float) ConstantValueDex(int baseValue, int deviation, int itemType, int jobCode, int optionLevelFactor, int grade, ushort levelLimit, ushort p7 = 0) {
        float result = 0f;

        if (levelLimit < 51) {
            if (jobCode is 50 or 60 or 100) {
                if (itemType == 22) {
                    result = MathF.Round(1.8f * (levelLimit - 20) / 3f, 0);
                } else {
                    result = MathF.Round((levelLimit - 20) / 3f, 0);
                }
            } else if (jobCode == 0) {
                if (itemType == 22) {
                    result = MathF.Round(1.8f * (levelLimit - 20) / 6f, 0);
                } else {
                    result = MathF.Round((levelLimit - 20) / 6f, 0);
                }
            } else {
                result = 0;
            }
        } else if (jobCode is 50 or 60 or 100) {
            if (itemType == 22) {
                result = MathF.Round(1.8f * (2 * levelLimit - 90) / 2f, 0);
            } else {
                result = MathF.Round(2 * levelLimit - 90, 0);
            }
        } else if (jobCode == 0) {
            if (itemType == 22) {
                result = MathF.Round(1.8f * (2 * levelLimit - 90) / 6f, 0);
            } else {
                result = MathF.Round((2 * levelLimit - 90) / 3f, 0);
            }
        }

        return (result, result);
    }

    public static (float, float) ConstantValueLuk(int baseValue, int deviation, int itemType, int jobCode, int optionLevelFactor, int grade, ushort levelLimit, ushort p7 = 0) {
        float result = 0f;

        if (levelLimit < 51) {
            if (jobCode is 70 or 80) {
                if (itemType == 22) {
                    result = MathF.Round(1.8f * (levelLimit - 20) / 3f, 0);
                } else {
                    result = MathF.Round((levelLimit - 20) / 3f, 0);
                }
            } else if (jobCode == 0) {
                if (itemType == 22) {
                    result = MathF.Round(1.8f * (levelLimit - 20) / 6f, 0);
                } else {
                    result = MathF.Round((levelLimit - 20) / 6f, 0);
                }
            } else {
                result = 0;
            }
        } else if (jobCode is 70 or 80) {
            if (itemType == 22) {
                result = MathF.Round(1.8f * (2 * levelLimit - 90) / 2f, 0);
            } else {
                result = MathF.Round(2 * levelLimit - 90, 0);
            }
        } else if (jobCode == 0) {
            if (itemType == 22) {
                result = MathF.Round(1.8f * (2 * levelLimit - 90) / 6f, 0);
            } else {
                result = MathF.Round((2 * levelLimit - 90) / 3f, 0);
            }
        }

        return (result, result);
    }

    public static (float, float) ConstantValueHp(int baseValue, int deviation, int itemType, int jobCode, int optionLevelFactor, int grade, ushort levelLimit, ushort p7 = 0) {
        float result = 0f;

        if (itemType is 12 or 18 or 19 or 20 or 21) {
            if (optionLevelFactor > 50) {
                float local1;
                float local2;
                float local3;
                if (levelLimit < 60) {
                    if (Locale == LocaleKr) {
                        result = MathF.Round((optionLevelFactor - 50) / 2f * 13f, 0);
                    } else if (Locale == LocaleCn) {
                        if (grade >= 5 && optionLevelFactor >= 53) {
                            local1 = 600;
                            local2 = 0.6f;
                            local3 = 0.06f;
                            result = MathF.Round(local1 * local2 * MathF.Pow(1 + local3, optionLevelFactor - 53), 0);
                        } else {
                            result = MathF.Round((optionLevelFactor - 50) / 2f * 13f, 0);
                        }
                    } else if (grade == 4) {
                        local1 = 360;
                        local2 = 0.6f;
                        local3 = 0.06f;
                        result = MathF.Round(local1 * local2 * MathF.Pow(1 + local3, optionLevelFactor - 50), 0);
                    } else if (grade == 5) {
                        local1 = 600;
                        local2 = 0.6f;
                        local3 = 0.06f;
                        result = MathF.Round(local1 * local2 * MathF.Pow(1 + local3, optionLevelFactor - 50), 0);
                    } else if (grade >= 6) {
                        local1 = 757.44f;
                        local2 = 0.6f;
                        local3 = 0.06f;
                        result = MathF.Round(local1 * local2 * MathF.Pow(1 + local3, optionLevelFactor - 50), 0);
                    } else {
                        result = MathF.Round((optionLevelFactor - 50) / 2f * 13f, 0);
                    }
                } else {
                    float local4;
                    if (grade == 4) {
                        local1 = 360;
                        local2 = 0.6f;
                        local3 = 0.06f;
                        local4 = optionLevelFactor / 10f - 6f;
                        local4 *= 6f;
                        local4 = 53 + local4;
                        local4 = optionLevelFactor - local4;
                        result = MathF.Round(local1 * local2 * MathF.Pow(1 + local3, local4), 0);
                    } else if (grade == 5) {
                        local1 = 600;
                        local2 = 0.6f;
                        local3 = 0.06f;
                        local4 = optionLevelFactor / 10f - 6f;
                        local4 *= 6f;
                        local4 = 53 + local4;
                        local4 = optionLevelFactor - local4;
                        result = MathF.Round(local1 * local2 * MathF.Pow(1 + local3, local4), 0);
                    } else if (grade >= 6) {
                        local1 = 757.44f;
                        local2 = 0.6f;
                        local3 = 0.06f;
                        local4 = optionLevelFactor / 10f - 6f;
                        local4 *= 6f;
                        local4 = 53 + local4;
                        local4 = optionLevelFactor - local4;
                        result = MathF.Round(local1 * local2 * MathF.Pow(1 + local3, local4), 0);
                    } else {
                        result = MathF.Round((optionLevelFactor - 50) / 2f * 13f, 0);
                    }
                }
            } else {
                result = 0;
            }
        } else if (itemType is > 30 and < 40) {
            if (optionLevelFactor > 5) {
                result = MathF.Round(1.2884f * optionLevelFactor - 6.56f * GetWeaponGradeHpCorrection(grade) * GetWeaponHpSlotCorrection(itemType), 0);
                // ReSharper disable once ConditionIsAlwaysTrueOrFalse
            } else if (optionLevelFactor < 6) {
                result = 1;
            } else {
                result = 0;
            }
        } else if (itemType is > 49 and < 60) {
            if (optionLevelFactor > 5) {
                result = MathF.Round(1.2884f * optionLevelFactor - 6.56f * GetWeaponGradeHpCorrection(grade) * GetWeaponHpSlotCorrection(itemType) * 2, 0);
                // ReSharper disable once ConditionIsAlwaysTrueOrFalse
            } else if (optionLevelFactor < 6) {
                result = 2;
            } else {
                result = 0;
            }
        }

        return (result, result);
    }

    public static (float, float) ConstantValueCap(int baseValue, int deviation, int itemType, int jobCode, int optionLevelFactor, int grade, ushort levelLimit, ushort p7 = 0) {
        float result = 0;

        if (itemType is > 29 and < 40) {
            result = GetWeaponCapSlotGradeValue(1, grade);
        } else if (itemType is > 39 and < 60) {
            result = GetWeaponCapSlotGradeValue(2, grade);
        } else {
            result = 0;
        }

        return (result, result);
    }

    public static (float, float) ConstantValueAddndd(int baseValue, int deviation, int itemType, int jobCode, int optionLevelFactor, int grade, ushort levelLimit, ushort p7 = 0) {
        float addNddGrade;
        float addNddEpicGrade;

        if (levelLimit < 60) {
            // constant_value_addndd: KR and CN use the base table, other regions the 50 NA table.
            if (Locale == LocaleKr || Locale == LocaleCn) {
                addNddGrade = GetWeaponGradeAddNdd(grade);
                addNddEpicGrade = GetWeaponGradeAddNdd(4);
            } else {
                addNddGrade = GetWeaponGradeAddNdd50Na(grade);
                addNddEpicGrade = GetWeaponGradeAddNdd50Na(4);
            }
        } else if (levelLimit < 70) {
            if (Locale == LocaleKr) {
                addNddGrade = GetWeaponGradeAddNdd60Kr(grade);
                addNddEpicGrade = GetWeaponGradeAddNdd60Kr(4);
            } else if (Locale == LocaleCn) {
                addNddGrade = GetWeaponGradeAddNdd60(grade);
                addNddEpicGrade = GetWeaponGradeAddNdd60(4);
            } else {
                addNddGrade = GetWeaponGradeAddNdd60Na(grade);
                addNddEpicGrade = GetWeaponGradeAddNdd60Na(4);
            }
        } else if (levelLimit < 80) {
            if (Locale == LocaleKr) {
                addNddGrade = GetWeaponGradeAddNdd70Kr(grade);
                addNddEpicGrade = GetWeaponGradeAddNdd70Kr(4);
            } else if (Locale == LocaleCn) {
                addNddGrade = GetWeaponGradeAddNdd70(grade);
                addNddEpicGrade = GetWeaponGradeAddNdd70(4);
            } else {
                addNddGrade = GetWeaponGradeAddNdd70Na(grade);
                addNddEpicGrade = GetWeaponGradeAddNdd70Na(4);
            }
        } else if (levelLimit < 90) {
            if (Locale == LocaleKr) {
                addNddGrade = GetWeaponGradeAddNdd80Kr(grade);
                addNddEpicGrade = GetWeaponGradeAddNdd80Kr(4);
            } else if (Locale == LocaleCn) {
                addNddGrade = GetWeaponGradeAddNdd80(grade);
                addNddEpicGrade = GetWeaponGradeAddNdd80(4);
            } else {
                addNddGrade = GetWeaponGradeAddNdd80Na(grade);
                addNddEpicGrade = GetWeaponGradeAddNdd80Na(4);
            }
        } else {
            if (Locale == LocaleKr) {
                addNddGrade = GetWeaponGradeAddNdd90Kr(grade);
                addNddEpicGrade = GetWeaponGradeAddNdd90Kr(4);
            } else if (Locale == LocaleCn) {
                addNddGrade = GetWeaponGradeAddNdd90(grade);
                addNddEpicGrade = GetWeaponGradeAddNdd90(4);
            } else {
                addNddGrade = GetWeaponGradeAddNdd90Na(grade);
                addNddEpicGrade = GetWeaponGradeAddNdd90Na(4);
            }
        }

        float local1 = 0;
        float local2 = 0;
        float local3 = 0;

        if (optionLevelFactor == 1) {
            local1 = 9;
        } else {
            float local4 = 9;

            for (int i = 2; i <= optionLevelFactor; i++) {
                float armorLevelBaseCurrentLevelPlus;

                if (i > 49) {
                    armorLevelBaseCurrentLevelPlus = local4 * UpgradeFactor;
                } else {
                    armorLevelBaseCurrentLevelPlus = MathF.Max(1 + (i / 10f) * 4, 0);
                }

                local4 += armorLevelBaseCurrentLevelPlus;
                local1 = local4;
            }
        }

        if (itemType == 21) { // belt
            local2 = MathF.Round((local1) * GetArmorConstantSlotCoefficient(itemType) * GetArmorConstantJobCoefficient(0) * GetArmorConstantGradeCoefficient(deviation, grade), 0) * addNddGrade;
            local3 = MathF.Round((local1) * GetArmorConstantSlotCoefficient(itemType) * GetArmorConstantJobCoefficient(0) * GetArmorConstantGradeCoefficient(deviation, 4), 0) * addNddEpicGrade;
        } else {
            local2 = MathF.Round((local1) * GetArmorConstantSlotCoefficient(itemType) * GetArmorConstantJobCoefficient(jobCode) * GetArmorConstantGradeCoefficient(deviation, grade), 0) * addNddGrade;
            local3 = MathF.Round((local1) * GetArmorConstantSlotCoefficient(itemType) * GetArmorConstantJobCoefficient(jobCode) * GetArmorConstantGradeCoefficient(deviation, 4), 0) * addNddEpicGrade;
        }

        float roundedResult = 0;

        if (levelLimit > 49 && grade > 3) {
            roundedResult = MathF.Round(local2 + local3 * (grade - 4), 0);
        }

        float result = MathF.Max(roundedResult, 0);
        return (result, result);
    }

    public static (float, float) ConstantValueNdd(int baseValue, int deviation, int itemType, int jobCode, int optionLevelFactor, int grade, ushort levelLimit, ushort p7 = 0) {
        float result = 0;
        float local2 = 0;

        if (optionLevelFactor == 1) {
            local2 = 9;
        } else {
            float local3 = 9;

            for (int i = 2; i <= optionLevelFactor; i++) {
                float local4;

                if (Locale == LocaleKr) {
                    if (levelLimit < 60) {
                        local4 = MathF.Max(1 + (i / 10f) * 4, 0);
                    } else if (i > 49) {
                        local4 = local3 * UpgradeFactor;
                    } else {
                        local4 = MathF.Max(1 + (i / 10f) * 4, 0);
                    }
                } else if (i > 49) {
                    local4 = local3 * UpgradeFactor;
                } else {
                    local4 = MathF.Max(1 + (i / 10f) * 4, 0);
                }

                local3 += local4;
                local2 = local3;
            }
        }

        float addNdd = 0;

        if (Locale == LocaleKr) {
            if (levelLimit < 60) {
                addNdd = 0;
            } else {
                addNdd = ConstantValueAddndd(baseValue, deviation, itemType, jobCode, optionLevelFactor, grade, levelLimit, p7).Item1;
            }
        } else {
            addNdd = ConstantValueAddndd(baseValue, deviation, itemType, jobCode, optionLevelFactor, grade, levelLimit, p7).Item1;
        }

        if (itemType == 21) {
            result = MathF.Round((local2) * GetArmorConstantSlotCoefficient(itemType) * GetArmorConstantJobCoefficient(0) * GetArmorConstantGradeCoefficient(deviation, grade), 0) + addNdd;
        } else if (levelLimit >= 70 && baseValue > 0) {
            result = MathF.Round((local2) * GetArmorConstantSlotCoefficient(itemType) * GetArmorConstantJobCoefficient(jobCode) * GetArmorConstantGradeCoefficient(deviation, grade) * (baseValue / 100f), 0) + MathF.Round(addNdd * (baseValue / 100f), 0);
        } else {
            result = MathF.Round((local2) * GetArmorConstantSlotCoefficient(itemType) * GetArmorConstantJobCoefficient(jobCode) * GetArmorConstantGradeCoefficient(deviation, grade), 0) + addNdd;
        }

        return (result, result);
    }

    public static (float, float) ConstantValueMar(int baseValue, int deviation, int itemType, int jobCode, int optionLevelFactor, int grade, ushort levelLimit, ushort p7 = 0) {
        float result = 0f;

        if (itemType == (int) ItemSlotType.Earring) {
            result = MathF.Round(MathF.Max(0, (float) (((optionLevelFactor - 12) * 1 + 4.5) / 1.5 * GetAccConstantGradeCoefficient(grade))), 0);
        } else if (itemType == (int) ItemSlotType.Ring) {
            result = MathF.Round(MathF.Max(0, (float) (((optionLevelFactor - 12) * 1.5 + 4) / 1.5 * GetAccConstantGradeCoefficient(grade))), 0);
        } else if (itemType is (int) ItemSlotType.Spellbook or (int) ItemSlotType.Shield) {
            if (grade == (int) ItemGrade.Normal) {
                result = 2;
            } else if (grade == (int) ItemGrade.Rare) {
                result = 4;
            } else if (grade == (int) ItemGrade.Elite) {
                result = 7;
            } else if (grade == (int) ItemGrade.Excellent) {
                result = 9;
            } else if (grade >= (int) ItemGrade.Legendary) {
                result = 12;
            } else {
                result = 0;
            }
        }

        return (result, result);
    }

    public static (float, float) ConstantValuePar(int baseValue, int deviation, int itemType, int jobCode, int optionLevelFactor, int grade, ushort levelLimit, ushort p7 = 0) {
        float result = 0f;

        if (itemType == (int) ItemSlotType.Cape) {
            result = MathF.Round(MathF.Max(0, (float) (((optionLevelFactor - 12) * 1) / 1.5 * GetAccConstantGradeCoefficient(grade) + 3)), 0);
        } else if (itemType == (int) ItemSlotType.Necklace) {
            result = MathF.Round(MathF.Max(0, (float) (((optionLevelFactor - 12) * 1.5) / 1.5 * GetAccConstantGradeCoefficient(grade) + 3)), 0);
        } else if (itemType == (int) ItemSlotType.Spellbook) {
            if (grade == (int) ItemGrade.Normal) {
                result = 2;
            } else if (grade == (int) ItemGrade.Rare) {
                result = 4;
            } else if (grade == (int) ItemGrade.Elite) {
                result = 7;
            } else if (grade == (int) ItemGrade.Excellent) {
                result = 9;
            } else if (grade >= (int) ItemGrade.Legendary) {
                result = 12;
            } else {
                result = 0;
            }
        }

        return (result, result);
    }

    public static (float, float) ConstantValueMap(int baseValue, int deviation, int itemType, int jobCode, int optionLevelFactor, int grade, ushort levelLimit, ushort p7 = 0) {
        float result = 0f;

        if (optionLevelFactor < 58) {
            result = MathF.Round(MathF.Round(0.5502f * optionLevelFactor + 0.1806f, 0) * GetWeaponGradeDoombookMapCorrection(grade), 0);
            // ReSharper disable once ConditionIsAlwaysTrueOrFalse
        } else if (optionLevelFactor > 57) {
            if (optionLevelFactor % 2 == 1) {
                int adjustedLevel = optionLevelFactor - 1;
                result = MathF.Round((0.2751f * adjustedLevel + 16.136f) * GetWeaponGradeDoombookMapCorrection(grade), 0);
            } else {
                result = MathF.Round((0.2751f * optionLevelFactor + 16.136f) * GetWeaponGradeDoombookMapCorrection(grade), 0);
            }
        }

        return (result, result);
    }

    public static (float, float) ConstantValueWapMin(int baseValue, int deviation, int itemType, int jobCode, int optionLevelFactor, int grade, ushort levelLimit, ushort p7 = 0) {
        float levelBase = 0f;

        if (optionLevelFactor == 1) {
            levelBase = 5;
        } else {
            float local = 5;

            for (int i = 2; i <= optionLevelFactor; i++) {
                float increment;

                if (Locale == LocaleKr) {
                    if (levelLimit < 60) {
                        increment = MathF.Max(i / 30f * 20 - 0.8f, 0);
                    } else if (i > 49) {
                        increment = local * UpgradeFactor;
                    } else {
                        increment = MathF.Max(i / 30f * 20 - 0.8f, 0);
                    }
                } else if (i > 49) {
                    increment = local * UpgradeFactor;
                } else {
                    increment = MathF.Max(i / 30f * 20 - 0.8f, 0);
                }

                local += increment;
                levelBase = local;
            }
        }

        float roundedBase = MathF.Round(levelBase * GetWeaponSlotCoefficient(itemType) / GetWeaponAttackSpeedCoefficient(itemType), 1);

        float addWap;
        if (Locale == LocaleKr) {
            if (levelLimit < 60) {
                addWap = 0;
            } else {
                addWap = ConstantValueAddwap(baseValue, deviation, itemType, jobCode, optionLevelFactor, grade, levelLimit, p7).Item1;
            }
        } else {
            addWap = ConstantValueAddwap(baseValue, deviation, itemType, jobCode, optionLevelFactor, grade, levelLimit, p7).Item1;
        }

        float result;
        if (levelLimit < 60) {
            result = MathF.Round(roundedBase * GetWeaponGradeCoefficient(deviation, grade) * (1 - GetWeaponSlotDeviation(itemType, deviation)), 0) + addWap;
        } else if (levelLimit >= 70 && baseValue > 0) {
            result = MathF.Round(roundedBase * GetWeaponGradeCoefficient(deviation, grade) * (1 - GetWeaponSlotDeviation(itemType, deviation)) * (baseValue / 100f), 0) +
                     MathF.Round(addWap * (1 - GetWeaponSlotDeviation(itemType, deviation)) * (baseValue / 100f), 0);
        } else {
            result = MathF.Round(roundedBase * GetWeaponGradeCoefficient(deviation, grade) * (1 - GetWeaponSlotDeviation(itemType, deviation)), 0) +
                     MathF.Round(addWap * (1 - GetWeaponSlotDeviation(itemType, deviation)), 0);
        }

        return (result, result);
    }

    public static (float, float) ConstantValueWapMax(int baseValue, int deviation, int itemType, int jobCode, int optionLevelFactor, int grade, ushort levelLimit, ushort p7 = 0) {
        float levelBase = 0f;

        if (optionLevelFactor == 1) {
            levelBase = 5;
        } else {
            float local = 5;

            for (int i = 2; i <= optionLevelFactor; i++) {
                float increment;

                if (Locale == LocaleKr) {
                    if (levelLimit < 60) {
                        increment = MathF.Max(i / 30f * 20 - 0.8f, 0);
                    } else if (i > 49) {
                        increment = local * UpgradeFactor;
                    } else {
                        increment = MathF.Max(i / 30f * 20 - 0.8f, 0);
                    }
                } else if (i > 49) {
                    increment = local * UpgradeFactor;
                } else {
                    increment = MathF.Max(i / 30f * 20 - 0.8f, 0);
                }

                local += increment;
                levelBase = local;
            }
        }

        float roundedBase = MathF.Round(levelBase * GetWeaponSlotCoefficient(itemType) / GetWeaponAttackSpeedCoefficient(itemType), 1);

        float addWap;
        if (Locale == LocaleKr) {
            if (levelLimit < 60) {
                addWap = 0;
            } else {
                addWap = ConstantValueAddwap(baseValue, deviation, itemType, jobCode, optionLevelFactor, grade, levelLimit, p7).Item1;
            }
        } else {
            addWap = ConstantValueAddwap(baseValue, deviation, itemType, jobCode, optionLevelFactor, grade, levelLimit, p7).Item1;
        }

        float result;
        if (levelLimit < 60) {
            result = MathF.Round(roundedBase * GetWeaponGradeCoefficient(deviation, grade) * (1 + GetWeaponSlotDeviation(itemType, deviation)), 0) + addWap;
        } else if (levelLimit >= 70 && baseValue > 0) {
            result = MathF.Round(roundedBase * GetWeaponGradeCoefficient(deviation, grade) * (1 + GetWeaponSlotDeviation(itemType, deviation)) * (baseValue / 100f), 0) +
                     MathF.Round(addWap * (1 + GetWeaponSlotDeviation(itemType, deviation)) * (baseValue / 100f), 0);
        } else {
            result = MathF.Round(roundedBase * GetWeaponGradeCoefficient(deviation, grade) * (1 + GetWeaponSlotDeviation(itemType, deviation)), 0) +
                     MathF.Round(addWap * (1 + GetWeaponSlotDeviation(itemType, deviation)), 0);
        }

        return (result, result);
    }
    #endregion

    #region Helpers
    private enum ItemSlotType {
        // Accessories
        Earring = 12,
        Hat = 13,
        Clothes = 14,
        Pants = 15,
        Gloves = 16,
        Shoes = 17,
        Cape = 18,
        Necklace = 19,
        Ring = 20,
        Belt = 21,
        Overall = 22,

        Bludgeon = 30,
        Dagger = 31,
        Longsword = 32,
        Scepter = 33,
        ThrowingStar = 34,

        Spellbook = 40,
        Shield = 41,

        Largesword = 50,
        Bow = 51,
        Staff = 52,
        HeavyGun = 53,
        Runeblade = 54,
        Knuckle = 55,
        Orb = 56,
    }

    private enum ItemGrade {
        Normal = 1,
        Rare = 2,
        Elite = 3,
        Excellent = 4,
        Legendary = 5,
        Artifact = 6,
    }

    private static float GetWeaponSlotCoefficient(int itemType) {
        return itemType switch {
            (int) ItemSlotType.Bludgeon => 1.0f,
            (int) ItemSlotType.Dagger => 1.03f,
            (int) ItemSlotType.Longsword => 0.95f,
            (int) ItemSlotType.Scepter => 0.92f,
            (int) ItemSlotType.ThrowingStar => 0.95f,
            (int) ItemSlotType.Largesword => 1.1f,
            (int) ItemSlotType.Bow => 1.03f,
            (int) ItemSlotType.Staff => 1.2f,
            (int) ItemSlotType.HeavyGun => 1.05f,
            (int) ItemSlotType.Runeblade => 1.045f,
            (int) ItemSlotType.Knuckle => 1.13f,
            (int) ItemSlotType.Orb => 0.96f,
            _ => 1.0f,
        };
    }

    private static float GetWeaponAttackSpeedCoefficient(int itemType) {
        return itemType switch {
            (int) ItemSlotType.Bludgeon => 0.95f,
            (int) ItemSlotType.Dagger => 1.05f,
            (int) ItemSlotType.Longsword => 1.0f,
            (int) ItemSlotType.Scepter => 1.1f,
            (int) ItemSlotType.ThrowingStar => 1.0f,
            (int) ItemSlotType.Largesword => 0.95f,
            (int) ItemSlotType.Bow => 1.05f,
            (int) ItemSlotType.Staff => 1.0f,
            (int) ItemSlotType.HeavyGun => 0.9f,
            (int) ItemSlotType.Runeblade => 0.975f,
            (int) ItemSlotType.Knuckle => 1.05f,
            (int) ItemSlotType.Orb => 0.95f,
            _ => 1.0f,
        };
    }

    private static float GetWeaponGradeCoefficient(int deviation, int grade) {
        if (deviation is 1 or 2) {
            return grade switch {
                (int) ItemGrade.Normal => 0.9f,
                (int) ItemGrade.Rare => 0.98f,
                (int) ItemGrade.Elite => 1.06f,
                (int) ItemGrade.Excellent => 1.14f,
                (int) ItemGrade.Legendary => 1.21f,
                (int) ItemGrade.Artifact => 1.4f,
                _ => 1.0f,
            };
        }
        return 1.0f;
    }

    private static float GetWeaponSlotDeviation(int itemType, int deviation) {
        return deviation switch {
            1 => itemType switch {
                (int) ItemSlotType.Bludgeon => 0.05f,
                (int) ItemSlotType.Dagger => 0.15f,
                (int) ItemSlotType.Longsword => 0.02f,
                (int) ItemSlotType.Scepter => 0.05f,
                (int) ItemSlotType.ThrowingStar => 0.15f,
                (int) ItemSlotType.Largesword => 0.1f,
                (int) ItemSlotType.Bow => 0.05f,
                (int) ItemSlotType.Staff => 0.05f,
                (int) ItemSlotType.HeavyGun => 0.1f,
                (int) ItemSlotType.Runeblade => 0.1f,
                (int) ItemSlotType.Knuckle => 0.05f,
                (int) ItemSlotType.Orb => 0.1f,
                _ => 0.0f,
            },
            2 => itemType switch {
                (int) ItemSlotType.Bludgeon => 0.1f,
                (int) ItemSlotType.Dagger => 0.3f,
                (int) ItemSlotType.Longsword => 0.04f,
                (int) ItemSlotType.Scepter => 0.1f,
                (int) ItemSlotType.ThrowingStar => 0.3f,
                (int) ItemSlotType.Largesword => 0.2f,
                (int) ItemSlotType.Bow => 0.1f,
                (int) ItemSlotType.Staff => 0.1f,
                (int) ItemSlotType.HeavyGun => 0.2f,
                (int) ItemSlotType.Runeblade => 0.2f,
                (int) ItemSlotType.Knuckle => 0.1f,
                (int) ItemSlotType.Orb => 0.2f,
                _ => 0.0f,
            },
            _ => 0.0f,
        };
    }

    private static float GetWeaponGradeAddWap(int grade) {
        return grade switch {
            (int) ItemGrade.Excellent => 0.9828f,
            (int) ItemGrade.Legendary => 2.0589f,
            (int) ItemGrade.Artifact => 2.0589f,
            _ => 0.0f,
        };
    }

    private static float GetWeaponGradeAddWap50Na(int grade) {
        return grade switch {
            (int) ItemGrade.Excellent => 0.9828f,
            (int) ItemGrade.Legendary => 1.4215f,
            (int) ItemGrade.Artifact => 1.7975f,
            _ => 0.0f,
        };
    }

    private static float GetWeaponGradeAddWap60Cn(int grade) {
        return grade switch {
            (int) ItemGrade.Excellent => 1.531f,
            (int) ItemGrade.Legendary => 1.8285f,
            (int) ItemGrade.Artifact => 2.118f,
            _ => 0.0f,
        };
    }

    private static float GetWeaponGradeAddWap60Kr(int grade) {
        return grade switch {
            (int) ItemGrade.Excellent => 0.6486f,
            (int) ItemGrade.Legendary => 1.123f,
            (int) ItemGrade.Artifact => 1.5408f,
            _ => 0.0f,
        };
    }

    private static float GetWeaponGradeAddWap60Na(int grade) {
        return grade switch {
            (int) ItemGrade.Excellent => 2.0159f,
            (int) ItemGrade.Legendary => 2.2145f,
            (int) ItemGrade.Artifact => 2.4356f,
            _ => 0.0f,
        };
    }

    private static float GetWeaponGradeAddWap70Cn(int grade) {
        return grade switch {
            (int) ItemGrade.Excellent => 2.4979f,
            (int) ItemGrade.Legendary => 2.5491f,
            (int) ItemGrade.Artifact => 2.6866f,
            _ => 0.0f,
        };
    }

    private static float GetWeaponGradeAddWap70Kr(int grade) {
        return grade switch {
            (int) ItemGrade.Excellent => 1.2422f,
            (int) ItemGrade.Legendary => 1.6133f,
            (int) ItemGrade.Artifact => 1.9477f,
            _ => 0.0f,
        };
    }

    private static float GetWeaponGradeAddWap70Na(int grade) {
        return grade switch {
            (int) ItemGrade.Excellent => 3.1675f,
            (int) ItemGrade.Legendary => 3.0833f,
            (int) ItemGrade.Artifact => 3.1261f,
            _ => 0.0f,
        };
    }

    private static float GetWeaponGradeAddWap80Cn(int grade) {
        return grade switch {
            (int) ItemGrade.Excellent => 3.8106f,
            (int) ItemGrade.Legendary => 3.5679f,
            (int) ItemGrade.Artifact => 3.5112f,
            _ => 0.0f,
        };
    }

    private static float GetWeaponGradeAddWap80Kr(int grade) {
        return grade switch {
            (int) ItemGrade.Excellent => 2.0836f,
            (int) ItemGrade.Legendary => 2.2663f,
            (int) ItemGrade.Artifact => 2.4763f,
            _ => 0.0f,
        };
    }

    private static float GetWeaponGradeAddWap80Na(int grade) {
        return grade switch {
            (int) ItemGrade.Excellent => 4.76f,
            (int) ItemGrade.Legendary => 4.2835f,
            (int) ItemGrade.Artifact => 4.0804f,
            _ => 0.0f,
        };
    }

    private static float GetWeaponGradeAddWap90Cn(int grade) {
        return grade switch {
            (int) ItemGrade.Excellent => 5.6481f,
            (int) ItemGrade.Legendary => 4.9527f,
            (int) ItemGrade.Artifact => 4.6126f,
            _ => 0.0f,
        };
    }

    private static float GetWeaponGradeAddWap90Kr(int grade) {
        return grade switch {
            (int) ItemGrade.Excellent => 3.2613f,
            (int) ItemGrade.Legendary => 3.1538f,
            (int) ItemGrade.Artifact => 3.1822f,
            _ => 0.0f,
        };
    }

    private static float GetWeaponGradeAddWap90Na(int grade) {
        return grade switch {
            (int) ItemGrade.Excellent => 6.9602f,
            (int) ItemGrade.Legendary => 5.9416f,
            (int) ItemGrade.Artifact => 5.399f,
            _ => 0.0f,
        };
    }

    private static float GetWeaponHpSlotCorrection(int itemType) {
        return itemType switch {
            (int) ItemSlotType.Bludgeon => 0.1f,
            (int) ItemSlotType.Dagger => 0.3f,
            (int) ItemSlotType.Longsword => 0.04f,
            (int) ItemSlotType.Scepter => 0.1f,
            (int) ItemSlotType.ThrowingStar => 0.3f,
            (int) ItemSlotType.Largesword => 0.2f,
            (int) ItemSlotType.Bow => 0.1f,
            (int) ItemSlotType.Staff => 0.1f,
            (int) ItemSlotType.HeavyGun => 0.2f,
            (int) ItemSlotType.Runeblade => 0.1f,
            (int) ItemSlotType.Knuckle => 0.1f,
            (int) ItemSlotType.Orb => 0.2f,
            _ => throw new ArgumentOutOfRangeException(nameof(itemType), "Invalid item type"),
        };
    }

    private static float GetWeaponGradeHpCorrection(int grade) {
        return grade switch {
            (int) ItemGrade.Normal => 0.3f,
            (int) ItemGrade.Rare => 0.4f,
            (int) ItemGrade.Elite => 0.5f,
            (int) ItemGrade.Excellent => 0.6f,
            (int) ItemGrade.Legendary => 0.7f,
            (int) ItemGrade.Artifact => 0.8f,
            _ => throw new ArgumentOutOfRangeException(nameof(grade), "Invalid grade value"),
        };
    }

    private static int GetWeaponCapSlotGradeValue(int deviation, int grade) {
        return deviation switch {
            1 => grade switch {
                (int) ItemGrade.Normal => 6,
                (int) ItemGrade.Rare => 7,
                (int) ItemGrade.Elite => 8,
                (int) ItemGrade.Excellent => 9,
                (int) ItemGrade.Legendary => 10,
                (int) ItemGrade.Artifact => 10,
                _ => throw new ArgumentOutOfRangeException(nameof(grade), "Invalid grade value"),
            },
            2 => grade switch {
                (int) ItemGrade.Normal => 12,
                (int) ItemGrade.Rare => 14,
                (int) ItemGrade.Elite => 16,
                (int) ItemGrade.Excellent => 18,
                (int) ItemGrade.Legendary => 20,
                (int) ItemGrade.Artifact => 20,
                _ => throw new ArgumentOutOfRangeException(nameof(grade), "Invalid grade value"),
            },
            _ => 0,
        };
    }

    private static float GetArmorConstantSlotCoefficient(int slotType) {
        return slotType switch {
            (int) ItemSlotType.Overall => 0.62f, // ISC_ONEPIECE
            (int) ItemSlotType.Clothes => 0.32f, // ISC_CLOTH
            (int) ItemSlotType.Pants => 0.3f, // ISC_PANTS
            (int) ItemSlotType.Hat => 0.21f, // ISC_CAP
            (int) ItemSlotType.Gloves => 0.06f, // ISC_GLOVES
            (int) ItemSlotType.Shoes => 0.06f, // ISC_SHOES
            (int) ItemSlotType.Earring => 0.05f, // ISC_EARRING
            (int) ItemSlotType.Cape => 0.05f, // ISC_MENTLE
            (int) ItemSlotType.Necklace => 0.05f, // ISC_PANDANT
            (int) ItemSlotType.Ring => 0.05f, // ISC_RI
            (int) ItemSlotType.Belt => 0.05f, // ISC_BELT
            (int) ItemSlotType.Shield => 0.15f, // ISC_SHIELD
            _ => throw new ArgumentOutOfRangeException(nameof(slotType), "Invalid slot type"),
        };
    }

    private static float GetArmorConstantJobCoefficient(int jobCode) {
        return jobCode switch {
            (int) JobCode.None => 0.8f,
            (int) JobCode.Newbie => 0.9f,
            (int) JobCode.Knight => 1.1f,
            (int) JobCode.Berserker => 1.0f,
            (int) JobCode.Wizard => 0.9f,
            (int) JobCode.Priest => 0.88f,
            (int) JobCode.Archer => 0.93f,
            (int) JobCode.HeavyGunner => 0.95f,
            (int) JobCode.Thief => 0.95f,
            (int) JobCode.Assassin => 0.9f,
            (int) JobCode.RuneBlader => 0.97f,
            (int) JobCode.Striker => 1.0f,
            (int) JobCode.SoulBinder => 0.9f,
            _ => throw new ArgumentOutOfRangeException(nameof(jobCode), "Invalid job code"),
        };
    }

    private static float GetArmorConstantGradeCoefficient(int deviation, int grade) {
        return deviation switch {
            1 => grade switch {
                (int) ItemGrade.Normal => 0.9f,
                (int) ItemGrade.Rare => 0.98f,
                (int) ItemGrade.Elite => 1.06f,
                (int) ItemGrade.Excellent => 1.14f,
                (int) ItemGrade.Legendary => 1.21f,
                (int) ItemGrade.Artifact => 1.4f,
                _ => throw new ArgumentOutOfRangeException(nameof(grade), "Invalid grade value"),
            },
            2 => grade switch {
                (int) ItemGrade.Normal => 1.0f,
                (int) ItemGrade.Rare => 1.1f,
                (int) ItemGrade.Elite => 1.2f,
                (int) ItemGrade.Excellent => 1.3f,
                (int) ItemGrade.Legendary => 1.45f,
                (int) ItemGrade.Artifact => 1.6f,
                _ => throw new ArgumentOutOfRangeException(nameof(grade), "Invalid grade value"),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(deviation), "Invalid deviation value"),
        };
    }

    private static float GetWeaponGradeAddNdd(int grade) {
        return grade switch {
            (int) ItemGrade.Normal => 0f,
            (int) ItemGrade.Rare => 0f,
            (int) ItemGrade.Elite => 0f,
            (int) ItemGrade.Excellent => 0.9846f,
            (int) ItemGrade.Legendary => 2.061f,
            (int) ItemGrade.Artifact => 2.061f,
            _ => throw new ArgumentOutOfRangeException(nameof(grade), "Invalid grade value"),
        };
    }

    private static float GetWeaponGradeAddNdd50Na(int grade) {
        return grade switch {
            (int) ItemGrade.Normal => 0f,
            (int) ItemGrade.Rare => 0f,
            (int) ItemGrade.Elite => 0f,
            (int) ItemGrade.Excellent => 0.9846f,
            (int) ItemGrade.Legendary => 1.426f,
            (int) ItemGrade.Artifact => 1.795f,
            _ => throw new ArgumentOutOfRangeException(nameof(grade), "Invalid grade value"),
        };
    }

    private static float GetWeaponGradeAddNdd60(int grade) {
        return grade switch {
            (int) ItemGrade.Normal => 0f,
            (int) ItemGrade.Rare => 0f,
            (int) ItemGrade.Elite => 0f,
            (int) ItemGrade.Excellent => 1.531f,
            (int) ItemGrade.Legendary => 1.833f,
            (int) ItemGrade.Artifact => 2.132f,
            _ => throw new ArgumentOutOfRangeException(nameof(grade), "Invalid grade value"),
        };
    }

    private static float GetWeaponGradeAddNdd60Kr(int grade) {
        return grade switch {
            (int) ItemGrade.Normal => 0f,
            (int) ItemGrade.Rare => 0f,
            (int) ItemGrade.Elite => 0f,
            (int) ItemGrade.Excellent => 0.6486f,
            (int) ItemGrade.Legendary => 1.123f,
            (int) ItemGrade.Artifact => 1.5408f,
            _ => throw new ArgumentOutOfRangeException(nameof(grade), "Invalid grade value"),
        };
    }

    private static float GetWeaponGradeAddNdd60Na(int grade) {
        return grade switch {
            (int) ItemGrade.Normal => 0f,
            (int) ItemGrade.Rare => 0f,
            (int) ItemGrade.Elite => 0f,
            (int) ItemGrade.Excellent => 2.016f,
            (int) ItemGrade.Legendary => 2.22f,
            (int) ItemGrade.Artifact => 2.442f,
            _ => throw new ArgumentOutOfRangeException(nameof(grade), "Invalid grade value"),
        };
    }

    private static float GetWeaponGradeAddNdd70(int grade) {
        return grade switch {
            (int) ItemGrade.Normal => 0f,
            (int) ItemGrade.Rare => 0f,
            (int) ItemGrade.Elite => 0f,
            (int) ItemGrade.Excellent => 3.248f,
            (int) ItemGrade.Legendary => 3.105f,
            (int) ItemGrade.Artifact => 3.127f,
            _ => throw new ArgumentOutOfRangeException(nameof(grade), "Invalid grade value"),
        };
    }

    private static float GetWeaponGradeAddNdd70Kr(int grade) {
        return grade switch {
            (int) ItemGrade.Normal => 0f,
            (int) ItemGrade.Rare => 0f,
            (int) ItemGrade.Elite => 0f,
            (int) ItemGrade.Excellent => 1.2445f,
            (int) ItemGrade.Legendary => 1.6125f,
            (int) ItemGrade.Artifact => 1.947f,
            _ => throw new ArgumentOutOfRangeException(nameof(grade), "Invalid grade value"),
        };
    }

    private static float GetWeaponGradeAddNdd70Na(int grade) {
        return grade switch {
            (int) ItemGrade.Normal => 0f,
            (int) ItemGrade.Rare => 0f,
            (int) ItemGrade.Elite => 0f,
            (int) ItemGrade.Excellent => 3.174f,
            (int) ItemGrade.Legendary => 3.084f,
            (int) ItemGrade.Artifact => 3.129f,
            _ => throw new ArgumentOutOfRangeException(nameof(grade), "Invalid grade value"),
        };
    }

    private static float GetWeaponGradeAddNdd80(int grade) {
        return grade switch {
            (int) ItemGrade.Normal => 0f,
            (int) ItemGrade.Rare => 0f,
            (int) ItemGrade.Elite => 0f,
            (int) ItemGrade.Excellent => 4.841f,
            (int) ItemGrade.Legendary => 4.344f,
            (int) ItemGrade.Artifact => 4.13f,
            _ => throw new ArgumentOutOfRangeException(nameof(grade), "Invalid grade value"),
        };
    }

    private static float GetWeaponGradeAddNdd80Kr(int grade) {
        return grade switch {
            (int) ItemGrade.Normal => 0f,
            (int) ItemGrade.Rare => 0f,
            (int) ItemGrade.Elite => 0f,
            (int) ItemGrade.Excellent => 2.085f,
            (int) ItemGrade.Legendary => 2.2669f,
            (int) ItemGrade.Artifact => 2.4771f,
            _ => throw new ArgumentOutOfRangeException(nameof(grade), "Invalid grade value"),
        };
    }

    private static float GetWeaponGradeAddNdd80Na(int grade) {
        return grade switch {
            (int) ItemGrade.Normal => 0f,
            (int) ItemGrade.Rare => 0f,
            (int) ItemGrade.Elite => 0f,
            (int) ItemGrade.Excellent => 4.7672f,
            (int) ItemGrade.Legendary => 4.2895f,
            (int) ItemGrade.Artifact => 4.0858f,
            _ => throw new ArgumentOutOfRangeException(nameof(grade), "Invalid grade value"),
        };
    }

    private static float GetWeaponGradeAddNdd90(int grade) {
        return grade switch {
            (int) ItemGrade.Normal => 0f,
            (int) ItemGrade.Rare => 0f,
            (int) ItemGrade.Elite => 0f,
            (int) ItemGrade.Excellent => 7.0691f,
            (int) ItemGrade.Legendary => 6.0235f,
            (int) ItemGrade.Artifact => 5.4645f,
            _ => throw new ArgumentOutOfRangeException(nameof(grade), "Invalid grade value"),
        };
    }

    private static float GetWeaponGradeAddNdd90Kr(int grade) {
        return grade switch {
            (int) ItemGrade.Normal => 0f,
            (int) ItemGrade.Rare => 0f,
            (int) ItemGrade.Elite => 0f,
            (int) ItemGrade.Excellent => 3.262f,
            (int) ItemGrade.Legendary => 3.1535f,
            (int) ItemGrade.Artifact => 3.182f,
            _ => throw new ArgumentOutOfRangeException(nameof(grade), "Invalid grade value"),
        };
    }

    private static float GetWeaponGradeAddNdd90Na(int grade) {
        return grade switch {
            (int) ItemGrade.Normal => 0f,
            (int) ItemGrade.Rare => 0f,
            (int) ItemGrade.Elite => 0f,
            (int) ItemGrade.Excellent => 6.9675f,
            (int) ItemGrade.Legendary => 5.947f,
            (int) ItemGrade.Artifact => 5.4043f,
            _ => throw new ArgumentOutOfRangeException(nameof(grade), "Invalid grade value"),
        };
    }

    private static float GetAccConstantGradeCoefficient(int grade) {
        return grade switch {
            (int) ItemGrade.Normal => 1.0f,
            (int) ItemGrade.Rare => 1.1f,
            (int) ItemGrade.Elite => 1.2f,
            (int) ItemGrade.Excellent => 1.3f,
            (int) ItemGrade.Legendary => 1.4f,
            (int) ItemGrade.Artifact => 1.4f,
            _ => throw new ArgumentOutOfRangeException(nameof(grade), "Invalid grade value"),

        };
    }

    private static float GetShieldConstantGradeCoefficient(int grade) {
        return grade switch {
            (int) ItemGrade.Normal => 0.1f,
            (int) ItemGrade.Rare => 0.2f,
            (int) ItemGrade.Elite => 0.3f,
            (int) ItemGrade.Excellent => 0.4f,
            (int) ItemGrade.Legendary => 0.5f,
            (int) ItemGrade.Artifact => 0.5f,
            _ => throw new ArgumentOutOfRangeException(nameof(grade), "Invalid grade value"),

        };
    }

    private static float GetWeaponGradeDoombookMapCorrection(int grade) {
        return grade switch {
            (int) ItemGrade.Normal => 0.925f,
            (int) ItemGrade.Rare => 1.0175f,
            (int) ItemGrade.Elite => 1.11f,
            (int) ItemGrade.Excellent => 1.2025f,
            (int) ItemGrade.Legendary => 1.2825f,
            (int) ItemGrade.Artifact => 1.2825f,
            _ => throw new ArgumentOutOfRangeException(nameof(grade), "Invalid grade value"),
        };
    }

    private static float GetStaticArmorGradeCoefficient(int grade) {
        return grade switch {
            (int) ItemGrade.Normal => 0.1f,
            (int) ItemGrade.Rare => 0.12f,
            (int) ItemGrade.Elite => 0.14f,
            (int) ItemGrade.Excellent => 0.16f,
            (int) ItemGrade.Legendary => 0.18f,
            (int) ItemGrade.Artifact => 0.2f,
            _ => throw new ArgumentOutOfRangeException(nameof(grade), "Invalid grade value"),
        };
    }

    private static float GetStaticAccGradeCoefficient(int grade) {
        return grade switch {
            (int) ItemGrade.Normal => 0.4f,
            (int) ItemGrade.Rare => 0.55f,
            (int) ItemGrade.Elite => 0.7f,
            (int) ItemGrade.Excellent => 0.85f,
            (int) ItemGrade.Legendary => 1.0f,
            (int) ItemGrade.Artifact => 1.0f,
            _ => throw new ArgumentOutOfRangeException(nameof(grade), "Invalid grade value"),
        };
    }

    private static float GetStaticGradeCoefficient(int grade) {
        return grade switch {
            (int) ItemGrade.Normal => 0.4f,
            (int) ItemGrade.Rare => 0.55f,
            (int) ItemGrade.Elite => 0.7f,
            (int) ItemGrade.Excellent => 0.85f,
            (int) ItemGrade.Legendary => 1.0f,
            (int) ItemGrade.Artifact => 1.0f,
            _ => throw new ArgumentOutOfRangeException(nameof(grade), "Invalid grade value"),
        };
    }

    private static float GetStaticWapmaxCoefficient(int grade) {
        return grade switch {
            (int) ItemGrade.Normal => 0.1f,
            (int) ItemGrade.Rare => 0.12f,
            (int) ItemGrade.Elite => 0.14f,
            (int) ItemGrade.Excellent => 0.16f,
            (int) ItemGrade.Legendary => 0.18f,
            (int) ItemGrade.Artifact => 0.2f,
            _ => throw new ArgumentOutOfRangeException(nameof(grade), "Invalid grade value"),

        };
    }

    private static bool IsItemRemakeArmor(int itemType) {
        return itemType is 22 or 14 or 15 or 13 or 16 or 17;
    }

    private static bool IsItemRemakeWeapon(int itemType) {
        return itemType is 30 or 31 or 32 or 33 or 34 or 40 or 41 or 50 or 51 or 52 or 53 or 54 or 55 or 56;
    }

    private static bool IsItemRemakeAccessory(int itemType) {
        return itemType is 19 or 20 or 21 or 18 or 12;
    }

    private static string GetItemRemakeUseCrystalTag(int itemType) {
        if (IsItemRemakeWeapon(itemType)) {
            return ItemRemakeRedcrystalTag;
        }

        if (IsItemRemakeArmor(itemType)) {
            return ItemRemakeBluecrystalTag;
        }

        if (IsItemRemakeAccessory(itemType)) {
            return ItemRemakeGreencrystalTag;
        }

        return string.Empty;
    }

    private static float GetSlotScore(int itemType) {
        return itemType is 22 or 30 or 32 or 33 or 50 or 51 or 52 or 53 or 54 or 55 or 56 ? 2 : 1;
    }

    private static int GetSlotType(int itemType) {
        return itemType switch {
            30 or 31 or 32 or 33 or 34 or 50 or 51 or 52 or 53 or 54 or 55 or 56 => SlotWeapon,
            19 or 20 or 21 or 18 or 12 => SlotAcc,
            _ => SlotArmor,
        };
    }

    private static float GetCriticalMajorValue(int jobCode) {
        return jobCode switch {
            (int) JobCode.Newbie => 1.63625f,
            (int) JobCode.Knight => 3.78f,
            (int) JobCode.Berserker => 4.305f,
            (int) JobCode.Wizard => 3.40375f,
            (int) JobCode.Priest => 7.34125f,
            (int) JobCode.Archer => 6.4575f,
            (int) JobCode.HeavyGunner => 2.03875f,
            (int) JobCode.Thief => 0.60375f,
            (int) JobCode.Assassin => 0.55125f,
            (int) JobCode.RuneBlader => 3.78f,
            (int) JobCode.Striker => 2.03875f,
            (int) JobCode.SoulBinder => 3.40375f,
            (int) JobCode.None or (int) JobCode.GameMaster => 0.7f, // "GM"
            _ => throw new ArgumentOutOfRangeException(nameof(jobCode), "Invalid job code"),
        };
    }
    #endregion

    #region ItemLevel
    private static float GetItemLevelRankCoefficient(int itemType) {
        return itemType switch {
            // All these items have coefficient 1
            (int) ItemSlotType.Bludgeon => 1.0f,
            (int) ItemSlotType.Dagger => 1.0f,
            (int) ItemSlotType.Longsword => 1.0f,
            (int) ItemSlotType.Scepter => 1.0f,
            (int) ItemSlotType.ThrowingStar => 1.0f,
            (int) ItemSlotType.Spellbook => 1.0f,
            (int) ItemSlotType.Shield => 1.0f,
            (int) ItemSlotType.Largesword => 1.0f,
            (int) ItemSlotType.Bow => 1.0f,
            (int) ItemSlotType.Staff => 1.0f,
            (int) ItemSlotType.HeavyGun => 1.0f,
            (int) ItemSlotType.Runeblade => 1.0f,
            (int) ItemSlotType.Knuckle => 1.0f,
            (int) ItemSlotType.Orb => 1.0f,
            (int) ItemSlotType.Hat => 1.0f,
            (int) ItemSlotType.Clothes => 1.0f,
            (int) ItemSlotType.Pants => 1.0f,
            (int) ItemSlotType.Gloves => 1.0f,
            (int) ItemSlotType.Shoes => 1.0f,
            (int) ItemSlotType.Overall => 1.0f,
            (int) ItemSlotType.Belt => 1.0f,

            // These items have coefficient 0
            (int) ItemSlotType.Earring => 0f,
            (int) ItemSlotType.Cape => 0f,
            (int) ItemSlotType.Necklace => 0f,
            (int) ItemSlotType.Ring => 0f,

            _ => throw new ArgumentOutOfRangeException(nameof(itemType), "Invalid item type"),
        };
    }

    private static float GetItemLevelCoefficient(int itemType) {
        return itemType switch {
            // Weapons - 1.0
            (int) ItemSlotType.Bludgeon => 1.0f,
            (int) ItemSlotType.Dagger => 1.0f,
            (int) ItemSlotType.Longsword => 1.0f,
            (int) ItemSlotType.Scepter => 1.0f,
            (int) ItemSlotType.ThrowingStar => 1.0f,
            (int) ItemSlotType.Spellbook => 1.0f,
            (int) ItemSlotType.Shield => 1.0f,
            (int) ItemSlotType.Largesword => 1.0f,
            (int) ItemSlotType.Bow => 1.0f,
            (int) ItemSlotType.Staff => 1.0f,
            (int) ItemSlotType.HeavyGun => 1.0f,
            (int) ItemSlotType.Runeblade => 1.0f,
            (int) ItemSlotType.Knuckle => 1.0f,
            (int) ItemSlotType.Orb => 1.0f,

            // Armor with various coefficients
            (int) ItemSlotType.Hat => 0.21f,
            (int) ItemSlotType.Clothes => 0.32f,
            (int) ItemSlotType.Pants => 0.3f,
            (int) ItemSlotType.Gloves => 0.06f,
            (int) ItemSlotType.Shoes => 0.06f,
            (int) ItemSlotType.Overall => 0.62f,

            // Accessories - 0.2
            (int) ItemSlotType.Earring => 0.2f,
            (int) ItemSlotType.Cape => 0.2f,
            (int) ItemSlotType.Necklace => 0.2f,
            (int) ItemSlotType.Ring => 0.2f,
            (int) ItemSlotType.Belt => 0.2f,

            _ => throw new ArgumentOutOfRangeException(nameof(itemType), "Invalid item type"),
        };
    }

    private static float GetAdditionalItemLevelCoefficient(int enchantLevel) {
        return enchantLevel switch {
            0 => 0f,
            1 => 0.02f,
            2 => 0.04f,
            3 => 0.06f,
            4 => 0.08f,
            5 => 0.1f,
            6 => 0.14f,
            7 => 0.18f,
            8 => 0.23f,
            9 => 0.29f,
            10 => 0.44f,
            11 => 0.74f,
            12 => 1.05f,
            13 => 1.36f,
            14 => 1.68f,
            15 => 2f,
            _ => throw new ArgumentOutOfRangeException(nameof(enchantLevel), "Invalid enchant level"),
        };
    }

    private static float GetAdditionalItemLevelCoefficientR5(int enchantLevel) {
        return enchantLevel switch {
            0 => 0f,
            1 => 0.02f,
            2 => 0.04f,
            3 => 0.06f,
            4 => 0.08f,
            5 => 0.1f,
            6 => 0.14f,
            7 => 0.18f,
            8 => 0.23f,
            9 => 0.29f,
            10 => 0.35f,
            11 => 0.41f,
            12 => 0.47f,
            13 => 0.53f,
            14 => 0.59f,
            15 => 0.65f,
            _ => throw new ArgumentOutOfRangeException(nameof(enchantLevel), "Invalid enchant level"),
        };
    }

    private static float GetAdditionalItemLevelCoefficientNa(int enchantLevel) {
        return enchantLevel switch {
            0 => 0f,
            1 => 0.02f,
            2 => 0.04f,
            3 => 0.07f,
            4 => 0.1f,
            5 => 0.14f,
            6 => 0.19f,
            7 => 0.25f,
            8 => 0.32f,
            9 => 0.4f,
            10 => 0.5f,
            11 => 0.64f,
            12 => 0.84f,
            13 => 1.12f,
            14 => 1.5f,
            15 => 2f,
            _ => throw new ArgumentOutOfRangeException(nameof(enchantLevel), "Invalid enchant level"),
        };
    }

    private static float GetAdditionalItemLevelCoefficientL70(int enchantLevel) {
        return enchantLevel switch {
            0 => 0f,
            1 => 0.02f,
            2 => 0.04f,
            3 => 0.07f,
            4 => 0.1f,
            5 => 0.14f,
            6 => 0.19f,
            7 => 0.25f,
            8 => 0.32f,
            9 => 0.4f,
            10 => 0.5f,
            11 => 0.525f,
            12 => 0.655f,
            13 => 0.707f,
            14 => 0.772f,
            15 => 0.88f,
            _ => throw new ArgumentOutOfRangeException(nameof(enchantLevel), "Invalid enchant level"),
        };
    }

    private static float GetRankScoreFactor(int rank) {
        return rank switch {
            0 => 1f,
            (int) ItemGrade.Normal => 1f,
            (int) ItemGrade.Rare => 1f,
            (int) ItemGrade.Elite => 1f,
            (int) ItemGrade.Excellent => 0.558f,
            (int) ItemGrade.Legendary => 1.2f,
            (int) ItemGrade.Artifact => 1.9f,
            _ => throw new ArgumentOutOfRangeException(nameof(rank), "Invalid rank value"),
        };
    }

    private static float GetLevelScoreFactorKr(int level) {
        return level switch {
            67 => 4.41f,
            70 => 5.2f,
            80 => 19.537f,
            90 => 61.35f,
            _ => 0f,
        };
    }

    private static float GetLevelScoreFactorCn(int level) {
        return level switch {
            67 => 4.41f,
            70 => 5.2f,
            80 => 19.537f,
            90 => 61.35f,
            _ => 0f,
        };
    }

    private static float GetLevelScoreFactorNa(int level) {
        return level switch {
            57 => 2.899f,
            60 => 3.4442f,
            67 => 12.538f,
            70 => 14.15f,
            80 => 45.91f,
            90 => 140.13f,
            _ => 0f,
        };
    }
    #endregion
}
