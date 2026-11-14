using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Serilog;

namespace Maple2.Server.Game.Manager.Field;

// The world champion npc of a field whose map is a season's field (WorldChampionBase.xml fieldID). Each second the
// field reports to the World server the damage its players dealt to the npc since the last report, and shows what it
// is answered: the step's npc (spawned at the season's spawn point) with the health all channels left it. The npc
// does not die in the field; a step cleared by any channel swaps it for the next step's npc (without a death, so no
// channel drops loot or exp for it), and a cleared or ended season removes it. The rewards are mailed after the end.
// Runs on the field thread; only the World call runs in the background, one at a time.
public sealed class WorldChampionSync {
    public interface IHost {
        // Spawns the npc at the region spawn: its object id and health, or null.
        (int ObjectId, long MaxHealth)? Spawn(int npcId, int spawnPointId);
        void Remove(int objectId);
        void SetHealth(int objectId, long health);
    }

    // The World server's answer (season 0: none on), or null when the call failed.
    public delegate (int Season, int Step, long StepDamage)? Report(int season, int step, long maxHealth, IReadOnlyCollection<WorldChampionDamage> damages);

    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    // The damage dealt to the npc of a step, not reported yet.
    private sealed record Batch(int Season, int Step, long MaxHealth) {
        public readonly Dictionary<long, WorldChampionDamage> Damages = [];
    }

    private readonly ILogger logger = Log.Logger.ForContext<WorldChampionSync>();
    private readonly Func<WorldChampionTable> table;
    private readonly Func<DateTime> now;
    private readonly IHost host;
    private readonly Report report;
    private readonly Action<Action> background;
    private readonly Action<Action> post;
    private readonly List<Batch> pending = [];
    private bool inFlight;
    private long maxHealth;
    private (int Season, int Step) spawnFailed;

    public int Season { get; private set; }
    public int Step { get; private set; }
    // The npc shown, 0 for none.
    public int ObjectId { get; private set; }

    // background runs the World call off the field thread, post runs its answer back on it.
    public WorldChampionSync(Func<WorldChampionTable> table, Func<DateTime> now, IHost host, Report report, Action<Action> background, Action<Action> post) {
        this.table = table;
        this.now = now;
        this.host = host;
        this.report = report;
        this.background = background;
        this.post = post;
    }

    // Whether a field of the map shows a world champion: the WorldChampionMushroom feature is on and a season of the
    // table is fought on the map.
    public static bool Runs(WorldChampionTable table, FeatureTable features, int mapId) {
        return WorldChampionRules.On(features) && table.Entries.Values.Any(season => season.MapId == mapId);
    }

    // A player hit the npc.
    public void Damaged(int objectId, long accountId, long characterId, long damage) {
        if (objectId == 0 || objectId != ObjectId || damage <= 0 || characterId == 0) {
            return;
        }
        Batch? batch = pending.Count > 0 ? pending[^1] : null;
        if (batch == null || batch.Season != Season || batch.Step != Step) {
            batch = new Batch(Season, Step, maxHealth);
            pending.Add(batch);
        }
        long dealt = batch.Damages.TryGetValue(characterId, out WorldChampionDamage? before) ? before.Damage : 0;
        batch.Damages[characterId] = new WorldChampionDamage(accountId, characterId, dealt + damage);
    }

    // Each Interval: reports the oldest damage not reported (or none, to read the state).
    public void Tick() {
        if (inFlight) {
            return;
        }
        // No season on and nothing shown or to report: no call.
        if (ObjectId == 0 && pending.Count == 0 && WorldChampionRules.Current(table(), now()) == null) {
            Season = 0;
            Step = 0;
            return;
        }
        Batch? batch = null;
        if (pending.Count > 0) {
            batch = pending[0];
            pending.RemoveAt(0);
        }
        int season = batch?.Season ?? Season;
        int step = batch?.Step ?? Step;
        long health = batch?.MaxHealth ?? maxHealth;
        List<WorldChampionDamage> damages = batch?.Damages.Values.ToList() ?? [];
        inFlight = true;
        background(() => {
            (int, int, long)? answer = null;
            try {
                answer = report(season, step, health, damages);
            } catch (Exception ex) {
                logger.Warning(ex, "World champion report failed");
            }
            post(() => {
                inFlight = false;
                Answer(batch, answer);
            });
        });
    }

    private void Answer(Batch? sent, (int Season, int Step, long StepDamage)? answer) {
        if (answer == null) {
            // Reported again at the next tick.
            if (sent != null) {
                pending.Insert(0, sent);
            }
            return;
        }
        (int season, int step, long stepDamage) = answer.Value;
        WorldChampionTable.Season? row = season != 0 && table().Entries.TryGetValue(season, out WorldChampionTable.Season? found) ? found : null;
        WorldChampionTable.Step? stepRow = row == null ? null : WorldChampionRules.StepOf(row, step);
        if (row == null || stepRow == null) {
            Despawn();
            Season = season;
            Step = step;
            return;
        }
        if (season != Season || step != Step || ObjectId == 0) {
            Despawn();
            Season = season;
            Step = step;
            if (spawnFailed == (season, step)) {
                return;
            }
            (int ObjectId, long MaxHealth)? spawned = host.Spawn(stepRow.NpcId, row.SpawnPointId);
            if (spawned == null) {
                logger.Warning("World champion season {Season} step {Step}: npc {NpcId} not spawned at spawn point {SpawnPointId}",
                    season, step, stepRow.NpcId, row.SpawnPointId);
                spawnFailed = (season, step);
                return;
            }
            (ObjectId, maxHealth) = spawned.Value;
        }
        long unreported = pending
            .Where(batch => batch.Season == Season && batch.Step == Step)
            .Sum(batch => batch.Damages.Values.Sum(damage => damage.Damage));
        host.SetHealth(ObjectId, WorldChampionRules.Health(maxHealth, stepDamage + unreported));
    }

    // Removes the npc shown (not a death); its damage not reported yet is still reported.
    private void Despawn() {
        if (ObjectId != 0) {
            host.Remove(ObjectId);
            ObjectId = 0;
        }
    }
}
