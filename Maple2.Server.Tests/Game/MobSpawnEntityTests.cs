using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Tools;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game;

// A map's random mob spawn (FieldMobSpawn): the whole group at the first update, each dead mob coming back on its own
// timer (death + deadTime + cooldown) and never without a cooldown.
public class MobSpawnEntityTests : TriggerTestBase {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int MobId = 21001015;
    private const int SpawnId = 11;

    private WeightedSet<NpcMetadata> mobs = null!;

    private void StartField() {
        Start();
        typeof(FieldManager).GetField("<Navigation>k__BackingField", Any)!.SetValue(Field, TriggerHallGameTests.FlatNavigation());
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(test.Session.Player, Field);
        typeof(FieldManager).GetField("<Mobs>k__BackingField", Any)!.SetValue(Field, new ConcurrentDictionary<int, FieldNpc>());
        typeof(FieldManager).GetField("<Pets>k__BackingField", Any)!.SetValue(Field, new ConcurrentDictionary<int, FieldPet>());
        typeof(FieldManager).GetField("fieldBreakables", Any)!.SetValue(Field, new ConcurrentDictionary<string, FieldBreakable>());
        typeof(FieldManager).GetField("<FieldInstance>k__BackingField", Any)!.SetValue(Field,
            new InstanceFieldMetadata(0, InstanceType.none, 0, false, 0, true, 0, 0, 0, 0));
        // No map row: the spawn stands on its own position.
        typeof(FieldManager).GetField("<MapMetadata>k__BackingField", Any)!.SetValue(Field, new MapMetadataStorage(
            new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options)));
        test.Register(TestGameSession.Metadata(30000071), TestGameSession.Metadata(50300014), TestGameSession.Metadata(30000084));
        test.SetServerTable(new NpcStatFactorTable(new Dictionary<int,
            IReadOnlyDictionary<int, IReadOnlyDictionary<int, NpcStatFactorMetadata>>>()));
        NpcMetadata mob = TriggerNpcSpawnTests.Npc(MobId, 0);
        typeof(FieldManager).GetField("<NpcMetadata>k__BackingField", Any)!.SetValue(Field, TriggerNpcSpawnTests.NpcTable(mob));
        mobs = new WeightedSet<NpcMetadata>();
        mobs.Add(mob, 1);
        test.TakeSent();
    }

    private FieldMobSpawn Spawn(int population, int cooldown) {
        StartField();
        var metadata = new MapMetadataSpawn(SpawnId, 0, 0, population, cooldown, [], 0, 0, new Dictionary<int, int>());
        var spawn = new FieldMobSpawn(Field, 2000, metadata, mobs, new WeightedSet<ItemMetadata>());
        spawn.Position = new Vector3(100, 200, 0);
        return spawn;
    }

    private List<FieldNpc> Alive() => Field.Mobs.Values.Where(npc => npc.SpawnPointId == SpawnId).ToList();

    [Test]
    public void FirstUpdate_FillsThePopulation_AndAnnouncesEachMob() {
        FieldMobSpawn spawn = Spawn(3, 30);

        spawn.Update(1000);

        Assert.Multiple(() => {
            Assert.That(Alive(), Has.Count.EqualTo(3));
            Assert.That(Sent(SendOp.FieldAddNpc), Has.Count.EqualTo(3));
        });
        spawn.Update(2000);
        Assert.That(Alive(), Has.Count.EqualTo(3), "a full group spawns no more");
    }

    [Test]
    public void ADeadMob_ComesBackOnItsOwnTimer() {
        FieldMobSpawn spawn = Spawn(2, 30);
        spawn.Update(1000);
        FieldNpc dead = Alive()[0];
        Field.Mobs.TryRemove(dead.ObjectId, out _);

        long died = Environment.TickCount64;
        spawn.Despawn(dead.ObjectId, 2);
        spawn.Update(died + 31_000);
        Assert.That(Alive(), Has.Count.EqualTo(1), "deadTime + cooldown (32 s) not over");

        spawn.Update(died + 32_100);
        Assert.That(Alive(), Has.Count.EqualTo(2));
    }

    [Test]
    public void NoCooldown_NeverRespawns() {
        FieldMobSpawn spawn = Spawn(1, 0);
        spawn.Update(1000);
        FieldNpc dead = Alive().Single();
        Field.Mobs.TryRemove(dead.ObjectId, out _);

        spawn.Despawn(dead.ObjectId, 0);
        spawn.Update(Environment.TickCount64 + 10_000_000);

        Assert.That(Alive(), Is.Empty);
    }

    [Test]
    public void Despawn_OfAnObjectNotFromThisSpawn_StartsNoTimer() {
        FieldMobSpawn spawn = Spawn(1, 30);
        spawn.Update(1000);

        spawn.Despawn(987654, 0);
        spawn.Update(Environment.TickCount64 + 10_000_000);

        Assert.That(Alive(), Has.Count.EqualTo(1));
    }
}
