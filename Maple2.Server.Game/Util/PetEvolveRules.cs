namespace Maple2.Server.Game.Util;

// Evolution points of a pet item. The client keeps them as an unsigned value and compares them with the
// points the next evolution needs (table/petEvolution.xml by item level and rarity). The server does not
// load that table, so the upper bound is passed in only where it is known.
public static class PetEvolveRules {
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
