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

    [Test]
    public void NoTilesOrNoMobsPicksNothing() {
        Assert.That(SpawnPicker.Pick([], 3, new Random(1), _ => Spread), Is.Empty);
        Assert.That(SpawnPicker.Pick(Tiles(3), 0, new Random(1), _ => Spread), Is.Empty);
    }
}
