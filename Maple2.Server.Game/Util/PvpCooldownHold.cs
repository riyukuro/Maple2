namespace Maple2.Server.Game.Util;

// The town cooldowns a PvP-zone entry ends (SkillCastRules.PvpZoneClears), held for the way out: the zone's own
// rule, not a free reset. The first Pvp entry of a stay holds each cleared cooldown; a Pvp-to-Pvp move keeps that
// hold. The first entry of a map of another type ends the stay and puts back each held cooldown that has not run
// out, unless the one kept now has more uses out or, with as many, ends later (a use inside the zone). T is the
// kept cooldown record, endAt its end tick at a tick (a skill with recharge uses settles them first) and usesOut its
// recharge uses out after that (0 for a skill without them). The session's save on disconnect reads the hold from
// another thread: every access takes the lock and the readers get a copy.
public sealed class PvpCooldownHold<T> {
    private readonly object guard = new();
    private readonly Dictionary<int, T> held = [];
    private readonly Func<T, long, long> endAt;
    private readonly Func<T, int> usesOut;
    private bool inStay;

    public bool InStay {
        get {
            lock (guard) {
                return inStay;
            }
        }
    }

    public IReadOnlyDictionary<int, T> Held => Snapshot().Held;

    public PvpCooldownHold(Func<T, long, long> endAt, Func<T, int>? usesOut = null) {
        this.endAt = endAt;
        this.usesOut = usesOut ?? (_ => 0);
    }

    // The held records and whether the stay runs, read together.
    public (Dictionary<int, T> Held, bool InStay) Snapshot() {
        lock (guard) {
            return (new Dictionary<int, T>(held), inStay);
        }
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
        lock (guard) {
            if (inStay) {
                return;
            }
            inStay = true;
            foreach (int id in cleared) {
                if (cooldowns.TryGetValue(id, out T? cooldown)) {
                    held[id] = cooldown;
                }
            }
        }
    }

    // A non-Pvp-map entry: ends the stay and returns the ids of the held cooldowns put back into cooldowns.
    public List<int> Leave(IDictionary<int, T> cooldowns, long tick) {
        List<int> restored = [];
        lock (guard) {
            foreach ((int id, T cooldown) in held) {
                long end = endAt(cooldown, tick);
                if (end <= tick) {
                    continue;
                }
                if (cooldowns.TryGetValue(id, out T? current) && Keeps(current, cooldown, end, tick)) {
                    continue;
                }
                cooldowns[id] = cooldown;
                restored.Add(id);
            }
            held.Clear();
            inStay = false;
        }
        return restored;
    }

    // Whether the record kept now stays over the held one ending at end: more uses out, or as many and no earlier end.
    private bool Keeps(T current, T cooldown, long end, long tick) {
        long currentEnd = endAt(current, tick);
        int currentOut = usesOut(current);
        int heldOut = usesOut(cooldown);
        return currentOut != heldOut ? currentOut > heldOut : currentEnd >= end;
    }

    // The hold kept over a session move or a relog (the World's player config cache).
    public void Load(IEnumerable<T> cooldowns, Func<T, int> idOf, bool inStay) {
        lock (guard) {
            held.Clear();
            foreach (T cooldown in cooldowns) {
                held[idOf(cooldown)] = cooldown;
            }
            this.inStay = inStay;
        }
    }
}
