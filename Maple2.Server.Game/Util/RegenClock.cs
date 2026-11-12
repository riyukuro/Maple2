using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

/// <summary>
/// When a player's next regen tick is due, per regenerating stat (health, spirit, stamina).
/// A tick is due once its interval (at least <see cref="Constant.MinStatIntervalTick"/>) has passed since the last
/// tick. Taking damage restarts the wait: the next tick comes a wait tick (RecoveryHPWaitTick 1000 for health) plus
/// one interval after the hit. For health that is +10 every 3.0 s, the first tick wait + one interval after any hit,
/// and none while the player is hit at least every 4 s. Casting does not restart it.
/// </summary>
public sealed class RegenClock {
    private readonly Dictionary<BasicAttribute, long> lastTick = new();

    /// <summary>The stat took damage at <paramref name="tick"/>: hold its regen for <paramref name="waitTick"/> on top of one interval.</summary>
    public void Hit(BasicAttribute attribute, long tick, int waitTick) {
        lastTick[attribute] = tick + waitTick;
    }

    /// <summary>Whether a regen tick is due at <paramref name="tick"/>; if so, the tick is taken and the next one counts from it.</summary>
    public bool TryTick(BasicAttribute attribute, long tick, long interval) {
        lastTick.TryGetValue(attribute, out long last);
        if (tick - last <= Math.Max(interval, Constant.MinStatIntervalTick)) {
            return false;
        }
        lastTick[attribute] = tick;
        return true;
    }
}
