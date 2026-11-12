using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

/// <summary>
/// When a player's next regen tick is due, per regenerating stat (health, spirit, stamina).
/// A tick is due once its interval (at least <see cref="Constant.MinStatIntervalTick"/>) has passed since the last
/// tick. The ticks stay on the interval grid: a tick seen late by the field update does not move the next one, so
/// the period is the interval itself and not the interval plus one field update. After a pause of two intervals or
/// more (the stat was full) the grid starts again at the tick that is taken.
/// A hit restarts the wait: the next tick comes a wait tick plus one interval after the hit. For health
/// (RecoveryHPWaitTick 1000) that is +10 every 3.0 s, the first tick 4.0 s after any hit, and none while the player is
/// hit at least every 4 s. A stamina use has no wait tick: the first refill comes one interval after the use.
/// Casting does not restart the health wait, and a spirit cost holds nothing.
/// </summary>
public sealed class RegenClock {
    private readonly Dictionary<BasicAttribute, long> lastTick = new();

    /// <summary>The stat was hit or used at <paramref name="tick"/>: hold its regen for <paramref name="waitTick"/> on top of one interval.</summary>
    public void Hit(BasicAttribute attribute, long tick, int waitTick) {
        lastTick[attribute] = tick + waitTick;
    }

    /// <summary>Whether a regen tick is due at <paramref name="tick"/>; if so, the tick is taken and the next one is due one interval after this one was due.</summary>
    public bool TryTick(BasicAttribute attribute, long tick, long interval) {
        lastTick.TryGetValue(attribute, out long last);
        long period = Math.Max(interval, Constant.MinStatIntervalTick);
        if (tick - last < period) {
            return false;
        }
        long due = last + period;
        lastTick[attribute] = tick - due < period ? due : tick;
        return true;
    }
}
