namespace Maple2.Server.Game.Util;

/// <summary>
/// When each id of a set_effect action changes: the first after start_delay, each next one interval ms later.
/// The 43 placed actions that give either use them as a sweep: 52000150 turns on four rows of five effects with
/// start_delay 0, 200, 400 and 600 and interval 200 (one diagonal wave), and 52000151 lights seven chains of
/// 5 to 12 effects one by one, 100 ms apart. The other 12,500 placed actions give 0 for both.
/// </summary>
public static class TriggerStagger {
    public static int Delay(int index, int startDelay, int interval) {
        long delay = Math.Max(0, startDelay) + (long) index * Math.Max(0, interval);
        return (int) Math.Min(delay, int.MaxValue);
    }
}
