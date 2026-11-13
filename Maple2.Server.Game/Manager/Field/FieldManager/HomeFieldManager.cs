using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Manager.Field;

public class HomeFieldManager : FieldManager {
    public long OwnerId => Home.AccountId;
    public Home Home { get; init; }

    public HomeSurvey? HomeSurvey { get; private set; }

    private readonly int roomId;
    // The cube rows of each plot (by number) as this field loaded them or last saved them (CommitPlot).
    // Made on first use: a field built without its constructor (the tests) has none.
    private Dictionary<int, HashSet<long>>? savedCubes;

    public HomeFieldManager(Home home, MapMetadata mapMetadata, UgcMapMetadata ugcMetadata, MapEntityMetadata entities, NpcMetadataStorage npcMetadata, int roomId)
        : base(mapMetadata, ugcMetadata, entities, npcMetadata) {
        this.roomId = roomId;
        Home = home;
    }

    public override void Init() {
        base.Init();
        LoadPlots();
    }

    // The home's plots and cubes as the database has them.
    internal void LoadPlots() {
        using GameStorage.Request db = GameStorage.Context();
        foreach (Plot plot in db.LoadPlotsForMap(MapId, OwnerId)) {
            // If roomId is -1, it's a planner plot and should start empty.
            if (roomId == -1) {
                plot.Cubes.Clear();
            }
            Plots[plot.Number] = plot;
            Saved(plot);

            plot.Cubes.Values
                .Where(p => p.Interact?.PortalSettings is not null)
                .ToList()
                .ForEach(cubePortal => SpawnCubePortal(cubePortal));

            plot.Cubes.Values
                .Where(plotCube => plotCube.Interact != null)
                .ToList()
                .ForEach(plotCube => AddFieldFunctionInteract(plotCube, plot));

            foreach (PlotCube plotCube in plot.Cubes.Values) {
                AddMaid(plotCube, plot);
            }
        }
    }

    // Called at the load, before anyone can edit the plot, and by a save under lock(Plots) and the plot's edit lock.
    internal void Saved(Plot plot) {
        (savedCubes ??= new Dictionary<int, HashSet<long>>())[plot.Number] = plot.Cubes.Values.Select(cube => cube.Id).Where(id => id < Constant.FurnishingBaseId).ToHashSet();
    }

    // The cubes of this field are not the home's any more: the database has a cube row this field neither holds nor
    // loaded or saved, or has lost one it holds, or a held cube's row has another rotation. Something other than this
    // field wrote the home's cubes since: a model house (InitNewHome) or an interior package applied elsewhere, or a
    // save or a rotation from another instance. A field's own place, remove and rotation write their rows at once
    // (HousingManager.CreateCube, DeleteCube, TryRotateCube), so its own edits leave it current. Entering such a
    // field showed the old cubes, and the owner's leave saved them over the home's (SaveCubes deletes every row the
    // list does not hold).
    public bool Outdated(GameStorage.Request db) {
        List<Plot> plots;
        lock (Plots) {
            plots = Plots.Values.ToList();
        }
        foreach (Plot plot in plots) {
            if (plot.IsPlanner || plot.Id == 0) {
                continue;
            }
            // Under the plot's edit lock: a place or a remove writes its row and changes the cube list in it, so the
            // rows and the held cubes are read at one point (and the list is not changed while it is read).
            lock (plot.EditLock) {
                if (Outdated(db, plot, out _)) {
                    return true;
                }
            }
        }
        return false;
    }

    // The same for one plot, called under its edit lock; rows gets the plot's cube row ids as read.
    internal bool Outdated(GameStorage.Request db, Plot plot, out HashSet<long> rows) {
        List<PlotCube> cubes = plot.Cubes.Values.Where(cube => cube.Id < Constant.FurnishingBaseId).ToList();
        HashSet<long> held = cubes.Select(cube => cube.Id).ToHashSet();
        Dictionary<long, float> rotations = db.GetCubeRotations(plot.Id);
        rows = rotations.Keys.ToHashSet();
        HashSet<long> saved = savedCubes?.GetValueOrDefault(plot.Number) ?? [];
        if (Outdated(saved, held, rows)) {
            return true;
        }
        // Another instance turned a cube: same rows, another rotation.
        return cubes.Any(cube => rotations.TryGetValue(cube.Id, out float rotation) && Math.Abs(rotation - cube.Rotation) > 0.01f);
    }

    public static bool Outdated(IReadOnlySet<long> saved, IReadOnlySet<long> held, IReadOnlySet<long> rows) {
        return rows.Any(id => !saved.Contains(id) && !held.Contains(id)) || held.Any(id => !rows.Contains(id));
    }

    public void SetHomeSurvey(HomeSurvey survey) {
        HomeSurvey = survey;
    }

    public void RemoveHomeSurvey() {
        HomeSurvey = null;
    }
}
