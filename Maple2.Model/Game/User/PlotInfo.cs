using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Model.Game;

public class PlotInfo {
    public readonly UgcMapGroup Metadata;

    public long Id { get; set; }
    public long OwnerId { get; set; }
    private string? name;
    public string Name {
        get => name ?? string.Empty;
        set {
            if (!string.IsNullOrWhiteSpace(value)) {
                name = value;
            }
        }
    }
    public int MapId { get; set; }
    public int Number { get; init; }
    public int ApartmentNumber { get; init; }

    public long ExpiryTime { get; set; }

    public PlotMode PlotMode { get; set; }

    public bool IsPlanner => PlotMode is PlotMode.DecorPlanner or PlotMode.BlueprintPlanner or PlotMode.ModelHome;


    public PlotInfo(UgcMapGroup metadata) {
        Metadata = metadata;
    }

    public PlotState State {
        get {
            if (ExpiryTime > 0) {
                return OwnerId != 0 ? PlotState.Taken : PlotState.Pending;
            }
            if (ExpiryTime == 0) {
                return PlotState.Open;
            }

            return PlotState.Open; // return state=2?
        }
    }
}

public class Plot(UgcMapGroup metadata) : PlotInfo(metadata) {
    public readonly Dictionary<Vector3B, PlotCube> Cubes = new();
    // Held while a cube is placed, removed, rotated or replaced: the owner and its build helpers edit the
    // same plot from their own sessions.
    public readonly object EditLock = new();
    // Rows of cubes this instance took off the plot but could not delete (a place refused after its row was written,
    // a remove): the instance's next save deletes them. Read and changed under EditLock.
    public readonly HashSet<long> StrayRows = [];

    public void SetPlannerMode(PlotMode mode) {
        PlotMode = mode;
    }

    public override string ToString() {
        return $"Plot: {Number}, Owner: {OwnerId}, Name: {Name}, MapId: {MapId}, Expiry: {ExpiryTime}, Mode: {PlotMode}, State: {State}";
    }
}
