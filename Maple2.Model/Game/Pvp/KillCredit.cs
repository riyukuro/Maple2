namespace Maple2.Model.Game.Pvp;

// The last hit each player took from another, for a one-on-one fight's kill credit (the rank duel and the Red Arena
// face-off): a death is a kill for the player that hit the victim last, within PvpZoneState.KillCreditMs, the same
// rule as the free-for-all and Treasure Island. A death without such a hit (a fall, a trap, its own skill) is no kill.
// Not thread safe; ticks in milliseconds.
public sealed class KillCredit {
    private readonly Dictionary<long, (long Attacker, long Tick)> lastHitBy = [];

    public void Hit(long attacker, long victim, long tick) {
        if (attacker != victim) {
            lastHitBy[victim] = (attacker, tick);
        }
    }

    // The killer of the victim dying at tick, or 0; the victim's hit is used up.
    public long Killer(long victim, long tick) {
        if (lastHitBy.Remove(victim, out (long Attacker, long Tick) hit) && tick - hit.Tick <= PvpZoneState.KillCreditMs) {
            return hit.Attacker;
        }
        return 0;
    }

    public void Clear() => lastHitBy.Clear();
}
