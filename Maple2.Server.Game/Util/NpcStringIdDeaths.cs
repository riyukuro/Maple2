namespace Maple2.Server.Game.Util;

/// <summary>
/// The npc_is_dead_by_string_id trigger condition. An npc xml's basic stringID names a boss across its variants
/// (23001001-23001006 all carry the same one; 02020051_bf spawns one of them at random). The condition holds once
/// a monster with that string id has died on the field and none with it is still alive. A dead monster leaves
/// Field.Mobs after its corpse time, so the deaths are kept here. A row ingested without a string id never
/// counts, so the condition stays false and the script's own fallback (a timer) ends the stage, as before.
/// </summary>
public sealed class NpcStringIdDeaths {
    private readonly HashSet<string> died = new(StringComparer.OrdinalIgnoreCase);
    private readonly object mutex = new();

    public void Died(string? stringId) {
        if (string.IsNullOrEmpty(stringId)) {
            return;
        }
        lock (mutex) {
            died.Add(stringId);
        }
    }

    /// <param name="mobs">The string id and dead flag of each monster on the field.</param>
    public bool IsDead(string stringId, IEnumerable<(string? StringId, bool IsDead)> mobs) {
        if (string.IsNullOrEmpty(stringId)) {
            return false;
        }
        lock (mutex) {
            if (!died.Contains(stringId)) {
                return false;
            }
        }
        return !mobs.Any(mob => !mob.IsDead && string.Equals(mob.StringId, stringId, StringComparison.OrdinalIgnoreCase));
    }
}
