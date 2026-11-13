namespace Maple2.Model.Game.Pvp;

// One Maple Survival game (the field game MapleSurvival of the survival maps' scripts): the participants, who is still
// alive, the order they fell in and their kills. Not thread safe.
//
// From the scripts: the game starts with CreateFieldGame MapleSurvival, the script's round runs while
// IsPlayingMapleSurvival holds. Chosen here (not settled by the client or the data): the participants are the players
// in the field when the game is created; a participant's death or departure ends its game; the kill goes to the
// participant that hit it last within KillCreditMs; the game ends when one participant (the winner) or none is left;
// a participant's rank is the number of squads still alive when its squad's last member fell, plus one (a solo
// player is a squad of one); the last squad standing wins (rank 1), and squad members are no enemies.
public sealed class SurvivalGameState {
    public const long KillCreditMs = 10_000;

    public sealed class Entry {
        public required long CharacterId { get; init; }
        // The squad it plays in (its first member's id; a solo player is its own).
        public long Unit { get; init; }
        public bool Alive { get; set; } = true;
        public int Kills { get; set; }
        public int Rank { get; set; }
    }

    private readonly Dictionary<long, Entry> entries = [];
    private readonly Dictionary<long, (long Attacker, long Tick)> lastHitBy = [];
    private readonly List<long> fallen = [];

    public bool Ended { get; private set; }
    public long Winner { get; private set; }
    public IReadOnlyList<long> Fallen => fallen;
    public IReadOnlyCollection<Entry> Entries => entries.Values;
    public int Alive => entries.Values.Count(entry => entry.Alive);

    // unitOf: a participant's squad (Maple Survival squads); without it every participant is its own.
    public SurvivalGameState(IEnumerable<long> participants, Func<long, long>? unitOf = null) {
        foreach (long characterId in participants.Distinct()) {
            entries[characterId] = new Entry { CharacterId = characterId, Unit = unitOf?.Invoke(characterId) ?? characterId };
        }
        if (AliveUnits <= 1) {
            End();
        }
    }

    // The squads (units) with a member still alive.
    public int AliveUnits => entries.Values.Where(entry => entry.Alive).Select(entry => entry.Unit).Distinct().Count();

    // The participants of a unit.
    public IEnumerable<Entry> UnitOf(long characterId) {
        return entries.TryGetValue(characterId, out Entry? entry)
            ? entries.Values.Where(other => other.Unit == entry.Unit)
            : [];
    }

    public Entry? Get(long characterId) => entries.GetValueOrDefault(characterId);
    public bool IsAlive(long characterId) => entries.TryGetValue(characterId, out Entry? entry) && entry.Alive && !Ended;

    // Living participants of different squads fight each other while the game runs.
    public bool Hostile(long caster, long target) => caster != target && IsAlive(caster) && IsAlive(target)
                                                     && entries[caster].Unit != entries[target].Unit;

    public void Hit(long attacker, long victim, long tick) {
        if (Hostile(attacker, victim)) {
            lastHitBy[victim] = (attacker, tick);
        }
    }

    // A participant fell (died or left): its rank, and the killer when there is one. False when it was not playing.
    public bool Fall(long victim, long tick, out long killer) {
        killer = 0;
        if (!IsAlive(victim)) {
            return false;
        }
        Entry dead = entries[victim];
        dead.Alive = false;
        // A squad's rank is set when its last member falls: the squads still alive, plus one; all its members share it.
        if (UnitOf(victim).All(member => !member.Alive)) {
            int rank = AliveUnits + 1;
            foreach (Entry member in UnitOf(victim)) {
                member.Rank = rank;
            }
        }
        fallen.Add(victim);
        if (lastHitBy.Remove(victim, out (long Attacker, long Tick) hit) && tick - hit.Tick <= KillCreditMs && entries.TryGetValue(hit.Attacker, out Entry? winner)) {
            killer = hit.Attacker;
            winner.Kills++;
        }
        if (AliveUnits <= 1) {
            End();
        }
        return true;
    }

    // The game ends with one squad (or nobody) left: its members, fallen or not, rank 1; Winner is a living member.
    private void End() {
        Ended = true;
        Entry? last = entries.Values.FirstOrDefault(entry => entry.Alive);
        if (last != null) {
            foreach (Entry member in UnitOf(last.CharacterId)) {
                member.Rank = 1;
            }
            Winner = last.CharacterId;
        }
    }
}

// The storm of the survival maps' SurvivalContents widget (03_storm.xml: StormData, then EnterStep 1 to 8, each
// until TimeOver). Not settled by the client or the data (no table holds the storm): chosen here, the safe zone is a
// circle at the field's center whose radius goes down in StepCount equal steps from StartRadius to MinRadius, each step
// lasting StepMs and announced ReadyMs before it shrinks.
public sealed class SurvivalStorm {
    public const int StepCount = 8;
    public const long StepMs = 60_000;
    public const long ReadyMs = 10_000;

    public float StartRadius { get; }
    public float MinRadius { get; }
    public int Step { get; private set; }
    public long StepEnd { get; private set; }

    public SurvivalStorm(float startRadius, float minRadius) {
        StartRadius = startRadius;
        MinRadius = Math.Min(minRadius, startRadius);
    }

    // The safe zone's radius after a step (0: before any).
    public float Radius(int step) {
        int done = Math.Clamp(step, 0, StepCount);
        return StartRadius - (StartRadius - MinRadius) * done / StepCount;
    }

    public float SafeRadius => Radius(Step);

    public void Enter(int step, long tick) {
        Step = Math.Clamp(step, 0, StepCount);
        StepEnd = tick + StepMs;
    }

    public bool TimeOver(long tick) => Step > 0 && tick >= StepEnd;

    public static bool Outside(float dx, float dy, float radius) => dx * dx + dy * dy > radius * radius;
}
