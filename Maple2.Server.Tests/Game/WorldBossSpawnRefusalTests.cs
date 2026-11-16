using System;
using System.Reflection;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;

namespace Maple2.Server.Tests.Game;

// SpawnWorldBoss spawns nothing, and says so, when the event row is unusable: no npc or spawn point, or a unique
// boss already in the field. The ids are made up.
public class WorldBossSpawnRefusalTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int MapId = 2000001;

    private FieldManager field = null!;

    [SetUp]
    public void SetUp() {
        var test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(MapId));
        field = test.Session.Field!;
    }

    private static WorldBossMetadata Boss(int[] npcs, int[] spawns, bool unique = false) =>
        new(1, 100, DateTime.MinValue, DateTime.MaxValue, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, [MapId], spawns, npcs, 0, unique, false, 0f, false, false);

    [Test]
    public void WithoutNpcIds_NothingSpawns() {
        Assert.That(field.SpawnWorldBoss(Boss([], [1]), 1), Is.Null);
    }

    [Test]
    public void WithoutSpawnPointIds_NothingSpawns() {
        Assert.That(field.SpawnWorldBoss(Boss([23000001], []), 1), Is.Null);
    }

    [Test]
    public void AUniqueBossAlreadyThere_NothingSpawns() {
        typeof(FieldManager).GetField("worldBossObjectId", Any)!.SetValue(field, 77);
        Assert.That(field.SpawnWorldBoss(Boss([23000001], [1], unique: true), 1), Is.Null);
    }
}
