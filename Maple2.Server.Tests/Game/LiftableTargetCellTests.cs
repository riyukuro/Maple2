using System.Numerics;
using Maple2.Database.Storage;
using Maple2.Model.Common;

namespace Maple2.Server.Tests.Game;

// A drop counts for a liftable target when the cell of the drop is the cell of the target box, both found as the
// client does (CubeMapInformationService::GetCellPosition: each axis (int) (p / 150)); the box has no other size.
public class LiftableTargetCellTests {
    [Test]
    public void ABoxInsideACell_IsThatCell() {
        Assert.That(MapEntityStorage.LiftableTargetCell(new Vector3(2700, 1650, 2850)), Is.EqualTo(new Vector3B(18, 11, 19)));
    }

    // 2775 is past half of cell 18: the client's cell is 18, and the cell before it, 17, is not the box.
    [Test]
    public void ABoxPastHalfACell_StaysInItsCell() {
        Vector3B cell = MapEntityStorage.LiftableTargetCell(new Vector3(2775, 1725, 2925));
        Assert.That(cell, Is.EqualTo(new Vector3B(18, 11, 19)));
        Assert.That(cell, Is.Not.EqualTo((Vector3B) new Vector3(2550, 1650, 2850)));
    }

    [Test]
    public void ANegativePosition_IsCutTowardZero() {
        Assert.That(MapEntityStorage.LiftableTargetCell(new Vector3(-100, -200, 0)), Is.EqualTo(new Vector3B(0, -1, 0)));
    }
}
