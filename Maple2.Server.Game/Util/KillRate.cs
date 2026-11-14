namespace Maple2.Server.Game.Util;

// The exp and meso rates of a kill (SpecialAttribute.Experience and Meso, as a fraction: 0.05 = +5%), also used for the
// shop sell, dungeon clear and item box meso rates. A rate below -100% gives nothing, never a negative amount.
public static class KillRate {
    public static long Apply(long amount, double rate) => (long) (amount * Math.Max(0d, 1d + rate));

    public static int Apply(int amount, double rate) => (int) Math.Min(int.MaxValue, Apply((long) amount, rate));
}
