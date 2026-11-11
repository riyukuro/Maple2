using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Model;

// A placed home mannequin and the items on it. The items are rows of group FittingDoll owned by the cube uid, so
// they stay with the cube while it is placed; Lock guards Items.
public class FieldFittingDoll {
    public readonly int ObjectId;
    public readonly PlotCube Cube;
    public readonly Plot Plot;
    public readonly FunctionCubeMetadata.FittingDollData Data;
    public readonly Dictionary<EquipSlot, Item> Items = new();
    public readonly object Lock = new();

    public long Uid => Cube.Id;
    public Vector3 Position => Cube.Position;
    public Vector3 Rotation => new(0, 0, Cube.Rotation);

    public FieldFittingDoll(int objectId, PlotCube cube, Plot plot, FunctionCubeMetadata.FittingDollData data) {
        ObjectId = objectId;
        Cube = cube;
        Plot = plot;
        Data = data;
    }

    public List<(EquipSlot Slot, IReadOnlyCollection<EquipSlot> ItemSlots, Item Item)> Worn() =>
        Items.Select(entry => (entry.Key, (IReadOnlyCollection<EquipSlot>) entry.Value.Metadata.SlotNames, entry.Value)).ToList();
}
