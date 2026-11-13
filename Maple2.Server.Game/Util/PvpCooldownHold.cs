namespace Maple2.Server.Game.Util;

// The town cooldowns a PvP-zone entry ends (SkillCastRules.PvpZoneClears), held for the way out: the zone's own
// rule, not a free reset. The first Pvp entry of a stay holds each cleared cooldown; a Pvp-to-Pvp move keeps that
// hold. The first entry of a map of another type ends the stay and puts back each held cooldown that has not run
// out, unless the one kept now ends later (a use inside the zone). T is the kept cooldown record and endAt its end
// tick at a tick (a skill with recharge uses settles them first).
public sealed class PvpCooldownHold<T> {
    private readonly Dictionary<int, T> held = [];
    private readonly Func<T, long, long> endAt;

    public bool InStay { get; private set; }
    public IReadOnlyDictionary<int, T> Held => held;

    public PvpCooldownHold(Func<T, long, long> endAt) {
        this.endAt = endAt;
    }

    // A map entry, before the clear of a Pvp one: holds on a Pvp map, else puts the hold back (the ids restored).
    public List<int> EnterMap(bool pvp, IDictionary<int, T> cooldowns, IEnumerable<int> cleared, long tick) {
        if (pvp) {
            Enter(cooldowns, cleared);
            return [];
        }
        return Leave(cooldowns, tick);
    }

    // A Pvp-map entry, before its clear: holds the cooldowns about to be cleared, once per stay.
    public void Enter(IDictionary<int, T> cooldowns, IEnumerable<int> cleared) {
        if (InStay) {
            return;
        }
        InStay = true;
        foreach (int id in cleared) {
            if (cooldowns.TryGetValue(id, out T? cooldown)) {
                held[id] = cooldown;
            }
        }
    }

    // A non-Pvp-map entry: ends the stay and returns the ids of the held cooldowns put back into cooldowns.
    public List<int> Leave(IDictionary<int, T> cooldowns, long tick) {
        List<int> restored = [];
        foreach ((int id, T cooldown) in held) {
            long end = endAt(cooldown, tick);
            if (end <= tick) {
                continue;
            }
            if (cooldowns.TryGetValue(id, out T? current) && endAt(current, tick) >= end) {
                continue;
            }
            cooldowns[id] = cooldown;
            restored.Add(id);
        }
        held.Clear();
        InStay = false;
        return restored;
    }

    // The hold kept over a session move or a relog (the World's player config cache).
    public void Load(IEnumerable<T> cooldowns, Func<T, int> idOf, bool inStay) {
        held.Clear();
        foreach (T cooldown in cooldowns) {
            held[idOf(cooldown)] = cooldown;
        }
        InStay = inStay;
    }
}
