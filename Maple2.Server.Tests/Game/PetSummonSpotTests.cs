using System.Numerics;
using Maple2.Server.Game.PacketHandlers;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// A pet is summoned on the floor under its owner (PetHandler.SummonSpot). The summon of a field entry came at the
// arrival spot, up to 324 above the floor after a channel change, and a pet sees no target below its own height
// (sightHeightDown 0 for every pet): it stood beside the mobs and never attacked, while a pet summoned by hand on the
// ground did.
public class PetSummonSpotTests {
    private static readonly Vector3 Arrival = new(-1200, 300, 6025);

    [Test]
    public void ArrivalAboveTheFloor_TakesTheFloorsHeight() {
        Assert.That(PetHandler.SummonSpot(Arrival, new Vector3(-1195, 302, 5851)), Is.EqualTo(new Vector3(-1200, 300, 5851)));
    }

    [Test]
    public void OwnerOnTheFloor_StaysWhereTheOwnerIs() {
        var ground = new Vector3(-1200, 300, 5851);
        Assert.That(PetHandler.SummonSpot(ground, ground), Is.EqualTo(ground));
    }

    // The nearest navmesh point of an owner off the mesh (cubes, a ledge) can lie aside or above: the owner's spot.
    [Test]
    public void FloorAsideOrAbove_TheOwnersSpot() {
        Assert.Multiple(() => {
            Assert.That(PetHandler.SummonSpot(Arrival, new Vector3(-1200, 300 + PetHandler.SummonFloorReach + 1, 5851)), Is.EqualTo(Arrival));
            Assert.That(PetHandler.SummonSpot(Arrival, Arrival with { Z = 6100 }), Is.EqualTo(Arrival));
        });
    }
}
