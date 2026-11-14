using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Serilog;

namespace Maple2.Server.World.Containers;

// The world champion season (WorldChampionBase.xml) all channels fight together: the step whose npc is out, the
// damage dealt to it, and each character's damage of the season. A field of the season's map reports its damage each
// second (Report) and shows the step and health it is answered. The state is under one lock; it is saved after a
// step is cleared and every SaveInterval while it changed, one save at a time, from a snapshot taken in the save's
// turn, so a later save never writes an older state.
public sealed class WorldChampionLookup : IDisposable {
    public delegate (int Step, long StepDamage, List<WorldChampionDamage> Damages)? Load(int season);
    public delegate bool Save(int season, int step, long stepDamage, IReadOnlyCollection<WorldChampionDamage> damages);

    private sealed class State {
        public int Step = 1;
        public long StepDamage;
        public readonly Dictionary<long, WorldChampionDamage> Damages = [];
        public readonly HashSet<long> Changed = [];
        // The last batch applied of each reporting field (its sender id): a batch sent again is not counted twice.
        // Not saved: a World server restart loses the damage not saved yet anyway.
        public readonly Dictionary<long, long> Sequences = [];
        public bool Dirty;
    }

    public static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(5);

    private readonly ILogger logger = Log.Logger.ForContext<WorldChampionLookup>();
    private readonly object mutex = new();
    private readonly object saveMutex = new();
    private readonly Dictionary<int, State> states = [];
    private readonly Func<WorldChampionTable> table;
    private readonly Func<DateTime> now;
    private readonly Load load;
    private readonly Save save;
    private readonly Timer? timer;

    public WorldChampionLookup(GameStorage gameStorage, ServerTableMetadataStorage serverTableMetadata)
        : this(() => serverTableMetadata.WorldChampionTable, () => DateTime.Now, LoadFrom(gameStorage), SaveTo(gameStorage), true) { }

    public WorldChampionLookup(Func<WorldChampionTable> table, Func<DateTime> now, Load load, Save save, bool saveTimer = false) {
        this.table = table;
        this.now = now;
        this.load = load;
        this.save = save;
        if (saveTimer) {
            timer = new Timer(_ => Flush(), null, SaveInterval, SaveInterval);
        }
    }

    public void Dispose() {
        timer?.Dispose();
        Flush();
    }

    // Adds a field's damage to the npc of damageStep (whose health is maxHealth) when the season is the one on, and
    // answers the season on (0: none), its step and the step's damage. One rule for late damage: a batch of the season
    // on counts for its characters (their reward damage) whatever the step, also once the last step is cleared; it
    // counts for the step only when damageStep is the step out. Damage of a season that is no longer on is dropped.
    // The damage counted is the hits dealt (overkill included, as the trophy counter); the step damage past the npc's
    // health is not carried to the next step. A batch whose sequence the sender already had applied (sent again after
    // a lost answer) is not counted again.
    public (int Season, int Step, long StepDamage) Report(int season, int damageStep, long maxHealth, IEnumerable<WorldChampionDamage> damages,
        long sender = 0, long sequence = 0) {
        WorldChampionTable.Season? current = WorldChampionRules.Current(table(), now());
        if (current == null) {
            return (0, 0, 0);
        }
        bool stepCleared = false;
        (int, int, long) result;
        lock (mutex) {
            State state = Get(current.Id);
            bool repeated = sender != 0 && sequence != 0 && state.Sequences.TryGetValue(sender, out long last) && sequence <= last;
            if (season == current.Id && !repeated) {
                if (sender != 0 && sequence != 0) {
                    state.Sequences[sender] = sequence;
                }
                long total = 0;
                foreach (WorldChampionDamage damage in damages) {
                    if (damage.Damage <= 0 || damage.CharacterId == 0) {
                        continue;
                    }
                    long dealt = state.Damages.TryGetValue(damage.CharacterId, out WorldChampionDamage? before) ? before.Damage : 0;
                    state.Damages[damage.CharacterId] = new WorldChampionDamage(damage.AccountId, damage.CharacterId, dealt + damage.Damage);
                    state.Changed.Add(damage.CharacterId);
                    state.Dirty = true;
                    total += damage.Damage;
                }
                int step = state.Step;
                (state.Step, state.StepDamage) = WorldChampionRules.Apply(current, state.Step, state.StepDamage, damageStep, total, maxHealth);
                if (state.Step != step) {
                    stepCleared = true;
                    logger.Information("World champion season {Season}: step {Step} cleared", current.Id, step);
                }
            }
            result = (current.Id, state.Step, state.StepDamage);
        }
        // With the save timer (the server's lookup), a cleared step is saved at once.
        if (stepCleared && timer != null) {
            Task.Run(Flush);
        }
        return result;
    }

    // The season's state, read from the database the first time.
    private State Get(int season) {
        if (states.TryGetValue(season, out State? state)) {
            return state;
        }
        state = new State();
        (int Step, long StepDamage, List<WorldChampionDamage> Damages)? saved = null;
        try {
            saved = load(season);
        } catch (Exception ex) {
            logger.Error(ex, "World champion season {Season}: load failed", season);
        }
        if (saved != null) {
            state.Step = Math.Max(1, saved.Value.Step);
            state.StepDamage = saved.Value.StepDamage;
            foreach (WorldChampionDamage damage in saved.Value.Damages) {
                state.Damages[damage.CharacterId] = damage;
            }
        }
        states[season] = state;
        return state;
    }

    // Saves the seasons that changed since their last save. A failed save is tried again at the next one.
    public void Flush() {
        lock (saveMutex) {
            var snapshots = new List<(int Season, int Step, long StepDamage, List<WorldChampionDamage> Damages)>();
            lock (mutex) {
                foreach ((int season, State state) in states) {
                    if (!state.Dirty) {
                        continue;
                    }
                    snapshots.Add((season, state.Step, state.StepDamage, state.Changed.Select(id => state.Damages[id]).ToList()));
                    state.Changed.Clear();
                    state.Dirty = false;
                }
            }
            foreach ((int season, int step, long stepDamage, List<WorldChampionDamage> damages) in snapshots) {
                bool saved;
                try {
                    saved = save(season, step, stepDamage, damages);
                } catch (Exception ex) {
                    logger.Error(ex, "World champion season {Season}: save failed", season);
                    saved = false;
                }
                if (!saved) {
                    lock (mutex) {
                        State state = states[season];
                        state.Dirty = true;
                        foreach (WorldChampionDamage damage in damages) {
                            state.Changed.Add(damage.CharacterId);
                        }
                    }
                }
            }
        }
    }

    // The state Report would answer, without damage (the season dialog, WorldChampion 8).
    public (int Season, int Step, long StepDamage) Get() => Report(0, 0, 0, []);

    // The character's damage of the season so far.
    public long DamageOf(int season, long characterId) {
        lock (mutex) {
            return states.TryGetValue(season, out State? state) && state.Damages.TryGetValue(characterId, out WorldChampionDamage? damage)
                ? damage.Damage : 0;
        }
    }

    private static Load LoadFrom(GameStorage gameStorage) {
        return season => {
            using GameStorage.Request db = gameStorage.Context();
            (int Step, long StepDamage)? row = db.GetWorldChampionSeason(season);
            List<WorldChampionDamage> damages = db.GetWorldChampionDamages(season);
            return row == null && damages.Count == 0 ? null : (row?.Step ?? 1, row?.StepDamage ?? 0, damages);
        };
    }

    private static Save SaveTo(GameStorage gameStorage) {
        return (season, step, stepDamage, damages) => {
            using GameStorage.Request db = gameStorage.Context();
            return db.SaveWorldChampion(season, step, stepDamage, damages);
        };
    }
}
