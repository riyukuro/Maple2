using Maple2.Model.Enum;

namespace Maple2.Server.Core.Formulas;

// The attack and resistance a character has from its str, dex, int and luk. KMS2 derives the base value from the
// base stats and the total from the total stats, each rounded down. The per-job coefficients below reproduce
// every KMS2 value known for that job; a job marked "chosen here" has too few known values to settle its own.
public static class AttackStat {
    private const float PRIMARY = 19.0f / 30.0f;
    private const float SECONDARY = 1.0f / 6.0f;

    // floor(a x first + b x second), a and b in ten-thousandths.
    private static long Scaled(long a, long first, long b = 0, long second = 0) {
        return (a * first + b * second) / 10000;
    }

    public static long PhysicalAtk(JobCode jobCode, long str, long dex, long luk) {
        switch (jobCode) {
            case JobCode.Newbie:
            case JobCode.Knight:
            case JobCode.Berserker:
            case JobCode.RuneBlader:
                // 0.63 str + 0.175 dex, at least 1.
                return Math.Max(1, Scaled(6300, str, 1750, dex));
        }

        // Chosen here: Striker's known values fit these; the casters' and the other jobs' are not settled.
        (float strCoefficient, float dexCoefficient, float lukCoefficient) = jobCode switch {
            JobCode.Wizard => (0.5666f, SECONDARY, 0.0f),
            JobCode.Priest => (0.4721f, SECONDARY, 0.0f),
            JobCode.Archer => (SECONDARY, PRIMARY, 0.0f),
            JobCode.HeavyGunner => (0.0f, PRIMARY, SECONDARY),
            JobCode.Thief => (SECONDARY, 0.0f, PRIMARY),
            JobCode.Assassin => (0.0f, SECONDARY, PRIMARY),
            JobCode.Striker => (SECONDARY, PRIMARY, 0.0f),
            JobCode.SoulBinder => (0.5666f, SECONDARY, 0.0f),
            _ => (0.0f, 0.0f, 0.0f),
        };

        return (long) (strCoefficient * str + dexCoefficient * dex + lukCoefficient * luk);
    }

    public static long MagicalAtk(JobCode jobCode, long @int) {
        switch (jobCode) {
            case JobCode.Wizard:
            case JobCode.SoulBinder:
            case JobCode.RuneBlader:
                return Scaled(5670, @int);
            case JobCode.Priest:
                return Scaled(4725, @int);
            case JobCode.Newbie:
            case JobCode.Knight:
            case JobCode.Berserker:
            case JobCode.Striker:
                // 0.35 int, at least 1.
                return Math.Max(1, Scaled(3500, @int));
        }

        // Chosen here: Archer, HeavyGunner, Thief and Assassin keep the older value.
        float intCoefficient = jobCode switch {
            JobCode.Archer => PRIMARY,
            JobCode.HeavyGunner => PRIMARY,
            JobCode.Thief => PRIMARY,
            JobCode.Assassin => PRIMARY,
            _ => 0.0f,
        };

        return (long) (intCoefficient * @int);
    }

    public static long PhysicalRes(JobCode jobCode, long str, long dex) {
        return jobCode switch {
            // 0.11 str + 0.165 dex.
            JobCode.Newbie or JobCode.Knight or JobCode.Berserker or JobCode.RuneBlader => Scaled(1100, str, 1650, dex),
            // Chosen here: the str jobs' rule with str and dex swapped; it fits Striker's known values, which do not settle it alone.
            JobCode.Striker => Scaled(1100, dex, 1650, str),
            // Not settled for the other jobs: none, as before.
            _ => 0,
        };
    }

    public static long MagicalRes(JobCode jobCode, long @int) {
        return jobCode switch {
            JobCode.Wizard or JobCode.Priest or JobCode.SoulBinder => Scaled(1100, @int),
            JobCode.RuneBlader => Scaled(2200, @int),
            // Chosen here: 0.55; the known values allow 0.539 to 0.555.
            JobCode.Newbie or JobCode.Knight or JobCode.Berserker or JobCode.Striker => Scaled(5500, @int),
            // Not settled for the other jobs: none, as before.
            _ => 0,
        };
    }
}
