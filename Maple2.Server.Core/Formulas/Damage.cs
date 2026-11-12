namespace Maple2.Server.Core.Formulas;

public static class Damage {
    public static double CalculateResistance(double targetPiercingResistance, double casterPiercingMultiplier) {
        return (1500.0 - Math.Max(0, targetPiercingResistance - 1500 * casterPiercingMultiplier)) / 1500;
    }

    /// <summary>
    /// Checks hit rate for evading
    /// </summary>
    public static bool Evade(float targetAccuracyResistance, long targetEvasionTotal, long targetDexterityTotal, long casterAccuracyTotal, float casterEvasionResistance, long casterLuckTotal, bool superArmorBreak) {
        if (superArmorBreak) {
            return false;
        }

        double casterAccuracy = casterAccuracyTotal * (1 - targetAccuracyResistance);
        double targetEvasion = targetEvasionTotal * (1 - casterEvasionResistance);

        return Random.Shared.NextDouble() > HitRate(casterAccuracy, targetEvasion, casterLuckTotal);
    }

    /// <summary>
    /// The chance that an attack is not evaded. The caster's luck adds up to 0.05 to it. The luck term used to be
    /// subtracted and, like a term of the target's dexterity, was divided as a whole number, so both were always 0;
    /// the dexterity term is left out (unmeasured), which changes nothing. Fitted to KMS2 player hits on npcs: at
    /// accuracy 82 and luck 18 to 26 the misses at evasion 82, 86, 87, 91, 97 and 103 are 0.7 %, 2.6 %, 3.1 %, 5.2 %, 11.5 % and 13.0 %,
    /// where this gives 1.1 %, 3.7 %, 3.9 %, 6.7 %, 10.1 % and 13.3 % and the rate without the luck gives 2.4 %,
    /// 5.0 %, 5.6 %, 8.0 %, 11.4 % and 14.6 %.
    /// </summary>
    public static double HitRate(double casterAccuracy, double targetEvasion, long casterLuck) {
        double luck = casterLuck > 0 ? Math.Min(0.05, casterLuck / (casterLuck + 50.0) * 0.05) : 0;
        return (casterAccuracy - 10) / (targetEvasion + (0.8 * casterAccuracy)) * 2 + luck;
    }

    public static bool TriggerCompulsionEvent(float totalCompulsionRate) {
        if (totalCompulsionRate >= 1 || (totalCompulsionRate > 0 && Random.Shared.NextDouble() <= totalCompulsionRate)) {
            return true;
        }

        return false;
    }
}
