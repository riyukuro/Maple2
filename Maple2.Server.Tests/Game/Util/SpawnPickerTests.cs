using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class SpawnPickerTests {
    private static readonly Vector3 Spread = new(9999, 0, 0);

    private static List<Vector3> Tiles(int n) => Enumerable.Range(1, n).Select(i => new Vector3(i * 150, 0, 0)).ToList();

    [Test]
    public void EachMobTakesItsOwnTileWhileThereAreTiles() {
        List<Vector3> tiles = Tiles(6);
        for (int seed = 0; seed < 200; seed++) {
            List<Vector3> picked = SpawnPicker.Pick(tiles, 4, new Random(seed), _ => Spread);
            Assert.That(picked, Has.Count.EqualTo(4));
            Assert.That(picked.Distinct().Count(), Is.EqualTo(4), $"seed {seed}");
            Assert.That(picked, Is.SubsetOf(tiles));
        }
    }

    [Test]
    public void EveryTileCanBePicked() {
        List<Vector3> tiles = Tiles(5);
        var seen = new HashSet<Vector3>();
        for (int seed = 0; seed < 200; seed++) {
            seen.UnionWith(SpawnPicker.Pick(tiles, 2, new Random(seed), _ => Spread));
        }
        Assert.That(seen, Is.EquivalentTo(tiles));
    }

    [Test]
    public void MobsPastTheTilesAreSpreadAroundOne() {
        List<Vector3> tiles = Tiles(1);
        var spreadFrom = new List<Vector3>();
        List<Vector3> picked = SpawnPicker.Pick(tiles, 4, new Random(1), tile => {
            spreadFrom.Add(tile);
            return tile + new Vector3(0, spreadFrom.Count * 10, 0);
        });
        Assert.That(picked, Has.Count.EqualTo(4));
        Assert.That(picked[0], Is.EqualTo(tiles[0]));
        Assert.That(spreadFrom, Has.Count.EqualTo(3));
        Assert.That(spreadFrom, Is.All.EqualTo(tiles[0]));
        Assert.That(picked.Distinct().Count(), Is.EqualTo(4));
    }

    // A point on a mesh's floor: the point and its neighbours that have ground under them, none past a ledge
    // (ground a block lower, or only found far aside) or without ground.
    [Test]
    public void GroundSpots_AreTheWalkableNeighbours() {
        var point = new Vector3(1050, 2850, 2100);
        List<Vector3> spots = SpawnPicker.GroundSpots(point, 150, 250, candidate => {
            if (candidate.X > point.X) {
                return null; // no ground on that side
            }
            if (candidate.X < point.X && candidate.Y > point.Y) {
                return candidate with { Z = point.Z - 300 }; // a ledge
            }
            if (candidate.X < point.X && candidate.Y < point.Y) {
                return candidate + new Vector3(100, 0, 0); // the nearest ground is the next block's
            }
            return candidate with { Z = point.Z - 20 };
        });
        Assert.That(spots, Is.EquivalentTo(new[] {
            new Vector3(900, 2850, 2080), new Vector3(1050, 2700, 2080), new Vector3(1050, 2850, 2080), new Vector3(1050, 3000, 2080),
        }));
    }

    [Test]
    public void GroundSpots_KeepToTheRange() {
        Assert.That(SpawnPicker.GroundSpots(Vector3.Zero, 150, 250, candidate => candidate), Has.Count.EqualTo(9));
        Assert.That(SpawnPicker.GroundSpots(Vector3.Zero, 150, 200, candidate => candidate), Has.Count.EqualTo(5));
        Assert.That(SpawnPicker.GroundSpots(Vector3.Zero, 150, 250, _ => null), Is.Empty);
    }

    [Test]
    public void NoTilesOrNoMobsPicksNothing() {
        Assert.That(SpawnPicker.Pick([], 3, new Random(1), _ => Spread), Is.Empty);
        Assert.That(SpawnPicker.Pick(Tiles(3), 0, new Random(1), _ => Spread), Is.Empty);
    }
}
