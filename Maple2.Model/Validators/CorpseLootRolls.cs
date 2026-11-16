namespace Maple2.Model.Validators;

// The rolls of an npc corpse's drop box (globalDeadDropBoxId, a corpse with <corpse hitAble="1">). The client puts no
// limit on hits on a corpse: it reads hitAble only to play Dead_Damg_A/B on a hit (StateDeadNPC, statedead.cpp) and
// keeps no count, timer or flag, and the corpse box is server data. The corpse lasts the npc's <dead time>. Chosen
// here: anyone who hits the corpse rolls the box at most once each IntervalMs, so a multi-hit skill does not roll
// it on every target hit (Ureus, box 1011: about 3 meso piles a roll, up to 20 rolls a player in its 20 s).
public sealed class CorpseLootRolls {
    // One roll a player each second. The original server's limit is unknown; 1 s is the corpse's own broadcast
    // period (FieldNpc.Update).
    public const long IntervalMs = 1000;

    private readonly Dictionary<long, long> lastRolls = new();

    // Whether the player's hit at now rolls the box: its last roll on this corpse is IntervalMs back or more, or none.
    public bool TryRoll(long characterId, long now) {
        lock (lastRolls) {
            if (lastRolls.TryGetValue(characterId, out long last) && now - last < IntervalMs) {
                return false;
            }
            lastRolls[characterId] = now;
            return true;
        }
    }
}
