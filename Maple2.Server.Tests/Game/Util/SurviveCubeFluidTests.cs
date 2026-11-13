using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game.Field;
using Maple2.Model.Metadata.FieldEntity;
using Maple2.Tools.VectorMath;

namespace Maple2.Server.Tests.Game.Util;

// survive_cube looks for the liquids of the player's cell and of the cell under the feet, on fields with a fluid only.
public class SurviveCubeFluidTests {
    private const float Block = FieldAccelerationStructure.BLOCK_SIZE;
    private static readonly FieldEntityId Id = new(0, 0, string.Empty);

    private static FieldAccelerationStructure Field(params (Vector3S Cell, FieldEntity Entity)[] cubes) {
        var grid = new Dictionary<Vector3S, List<FieldEntity>>();
        foreach ((Vector3S cell, FieldEntity entity) in cubes) {
            grid[cell] = [entity];
        }
        var structure = new FieldAccelerationStructure();
        structure.AddEntities(grid, new Vector3S(0, 0, 0), new Vector3S(4, 4, 4), [], 0);
        return structure;
    }

    private static BoundingBox3 CellBounds(Vector3S cell) {
        Vector3 at = Block * new Vector3(cell.X, cell.Y, cell.Z);
        return new BoundingBox3(at - new Vector3(Block / 2, Block / 2, 0), at + new Vector3(Block / 2, Block / 2, Block));
    }

    private static FieldFluidEntity Lava(Vector3S cell) =>
        new(Id, Block * new Vector3(cell.X, cell.Y, cell.Z), default, 1f, LiquidType.lava, CellBounds(cell), 0, false, true, MapAttribute.none);

    private static FieldBoxColliderEntity Grass(Vector3S cell) =>
        new(Id, Block * new Vector3(cell.X, cell.Y, cell.Z), default, 1f, CellBounds(cell), default, false, false, MapAttribute.grass);

    private static List<LiquidType> At(FieldAccelerationStructure structure, Vector3 position) {
        var liquids = new List<LiquidType>();
        structure.QueryFluidsAt(position, fluid => liquids.Add(fluid.LiquidType));
        return liquids.Distinct().ToList(); // a cube may come from the grid and the trimmed list both
    }

    [Test]
    public void TheCellAndTheCellUnderTheFeetAreLookedAt() {
        var lava = new Vector3S(2, 2, 1);
        FieldAccelerationStructure structure = Field((lava, Lava(lava)), (new Vector3S(0, 0, 0), Grass(new Vector3S(0, 0, 0))));
        Assert.Multiple(() => {
            Assert.That(structure.HasFluids, Is.True);
            Assert.That(At(structure, new Vector3(2 * Block, 2 * Block, Block + 10)), Is.EqualTo(new[] { LiquidType.lava })); // in it
            Assert.That(At(structure, new Vector3(2 * Block, 2 * Block, 2 * Block + 10)), Is.EqualTo(new[] { LiquidType.lava })); // on it
            Assert.That(At(structure, new Vector3(2 * Block, 2 * Block, 3 * Block + 10)), Is.Empty); // a cell above
            Assert.That(At(structure, new Vector3(3 * Block, 2 * Block, Block + 10)), Is.Empty); // beside it
        });
    }

    [Test]
    public void AFieldWithNoFluidHasNone() {
        var cell = new Vector3S(1, 1, 0);
        Assert.That(Field((cell, Grass(cell))).HasFluids, Is.False);
    }
}
