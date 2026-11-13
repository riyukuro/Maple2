namespace Maple2.Server.Game.Util;

// The points of a Shadow Expedition boss gauge (shadow_expedition_open_boss_gauge): the monsters killed since the
// open, out of the field's monsters at the open, scaled to the gauge's most points. The scripts say no more than
// that the gauge counts the field's monsters (02000401_bf waits 1.3 s after the open so that no kill falls before
// it), and no table or npc field gives a monster its own points. The count is the monsters alive at the open plus
// those spawned before the first kill (the scripts open the gauge and spawn the wave in one step); after that it
// is fixed, as the siege fields' spawn points refill every 10 s and a share of the live ones would never fill.
// Every monster killed while the gauge is open adds one, refilled ones too (a player's summon or a friendly npc
// does not); a counted monster removed without dying (destroy_monster) leaves the count. The points never go down
// and stop at the most.
public sealed class ShadowGauge {
    private readonly object gaugeLock = new();
    private readonly HashSet<int> counted;
    // The monsters a kill counts for: those at the open and every one spawned since.
    private readonly HashSet<int> monsters;
    private int total;
    private int killed;
    private int points;

    public int MaxPoints { get; }

    public int Points {
        get {
            lock (gaugeLock) {
                return points;
            }
        }
    }

    public ShadowGauge(IEnumerable<int> aliveMobs, int maxPoints) {
        counted = [..aliveMobs];
        total = counted.Count;
        monsters = [..counted];
        MaxPoints = Math.Max(maxPoints, 0);
    }

    // The monsters the open counts: the field's live ones, without a player's summon (the spawn path leaves those
    // out too, so a summon killed by the monsters never counts as a kill).
    public static IEnumerable<int> AtOpen(IEnumerable<(int ObjectId, bool Dead, bool PlayerSummon)> mobs) =>
        mobs.Where(mob => !mob.Dead && !mob.PlayerSummon).Select(mob => mob.ObjectId);

    // A monster spawned while the gauge is open: its kill adds one; it joins the count until the first kill.
    public void Count(int objectId) {
        lock (gaugeLock) {
            if (monsters.Add(objectId) && killed == 0 && counted.Add(objectId)) {
                total++;
            }
        }
    }

    // The new points when a monster's death raised them; null otherwise.
    public int? Killed(int objectId) {
        lock (gaugeLock) {
            if (!monsters.Remove(objectId)) {
                return null;
            }
            counted.Remove(objectId);
            killed++;
            return Raise();
        }
    }

    // A counted monster gone without dying: the points of the smaller count, when that raised them.
    public int? Removed(int objectId) {
        lock (gaugeLock) {
            monsters.Remove(objectId);
            if (!counted.Remove(objectId)) {
                return null;
            }
            total--;
            return Raise();
        }
    }

    private int? Raise() {
        if (killed == 0) {
            return null;
        }
        int share = killed >= total ? MaxPoints : (int) ((long) killed * MaxPoints / total);
        if (share <= points) {
            return null;
        }
        points = share;
        return points;
    }
}
