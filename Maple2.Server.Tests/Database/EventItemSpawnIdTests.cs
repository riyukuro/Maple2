using System.Collections.Generic;
using System.Numerics;
using Maple2.Database.Storage;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Database;

// 02000360_bf and 63000033_cs carry two item spawn points with id 14 (EventSpawnPointItem14 and 15).
public class EventItemSpawnIdTests {
    private static EventSpawnPointItem Point(int id, float x) => new(id, new Vector3(x, 0, 0), Vector3.Zero, 0f, 0, 0, 0, true);

    [TestCase(false)]
    [TestCase(true)]
    public void ASharedIdKeepsTheFirstName(bool reversed) {
        var spawns = new Dictionary<int, EventSpawnPointItem>();
        var names = new Dictionary<int, string>();
        var rows = new List<(string, EventSpawnPointItem)> {
            ("EventSpawnPointItem14", Point(14, 1)),
            ("EventSpawnPointItem15", Point(14, 2)),
            ("EventSpawnPointItem16", Point(16, 3)),
        };
        if (reversed) {
            rows.Reverse();
        }
        foreach ((string name, EventSpawnPointItem point) in rows) {
            MapEntityStorage.AddEventItemSpawn(spawns, names, name, point);
        }
        Assert.Multiple(() => {
            Assert.That(spawns.Keys, Is.EquivalentTo(new[] { 14, 16 }));
            Assert.That(spawns[14].Position.X, Is.EqualTo(1));
            Assert.That(spawns.ContainsKey(15), Is.False);
        });
    }
}
