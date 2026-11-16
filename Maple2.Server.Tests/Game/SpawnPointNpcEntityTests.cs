using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Tests.Game;

// A map's NPC spawn point as the field runs it: spawn at field creation, regen checks on its own clock, the cap by
// the list's counts, and a point a combine group owns staying quiet.
public class SpawnPointNpcEntityTests : TriggerTestBase {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int MobA = 21001015;
    private const int MobB = 21001016;
    private const int PointId = 7;

    private void StartField(params NpcMetadata[] npcs) {
        Start();
        typeof(FieldManager).GetField("<Navigation>k__BackingField", Any)!.SetValue(Field, TriggerHallGameTests.FlatNavigation());
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(test.Session.Player, Field);
        typeof(FieldManager).GetField("<Mobs>k__BackingField", Any)!.SetValue(Field, new ConcurrentDictionary<int, FieldNpc>());
        typeof(FieldManager).GetField("<Pets>k__BackingField", Any)!.SetValue(Field, new ConcurrentDictionary<int, FieldPet>());
        typeof(FieldManager).GetField("fieldBreakables", Any)!.SetValue(Field, new ConcurrentDictionary<string, FieldBreakable>());
        typeof(FieldManager).GetField("<FieldInstance>k__BackingField", Any)!.SetValue(Field,
            new InstanceFieldMetadata(0, InstanceType.none, 0, false, 0, true, 0, 0, 0, 0));
        test.Register(TestGameSession.Metadata(30000071), TestGameSession.Metadata(50300014), TestGameSession.Metadata(30000084));
        test.SetServerTable(new NpcStatFactorTable(new Dictionary<int,
            IReadOnlyDictionary<int, IReadOnlyDictionary<int, NpcStatFactorMetadata>>>()));
        typeof(FieldManager).GetField("<NpcMetadata>k__BackingField", Any)!.SetValue(Field, TriggerNpcSpawnTests.NpcTable(npcs));
        test.TakeSent();
    }

    private static SpawnPointNPC Point(int regen, bool onCreate, int npcCount, params (int Id, int Count)[] list) {
        return new SpawnPointNPC("p", PointId, new Vector3(100, 200, 0), Vector3.Zero, true, onCreate, 0,
            list.Select(entry => new SpawnPointNPCListEntry(entry.Id, entry.Count)).ToList(), regen, null, npcCount);
    }

    private int Alive(int npcId) => Field.GetActorsBySpawnId(PointId).OfType<FieldNpc>().Count(npc => npc.Value.Id == npcId);

    [Test]
    public void SpawnOnCreate_FillsTheListAndTagsTheNpcs() {
        StartField(TriggerNpcSpawnTests.Npc(MobA, 0), TriggerNpcSpawnTests.Npc(MobB, 0));
        var point = new FieldSpawnPointNpc(Field, 1000, Point(10, true, 0, (MobA, 2), (MobB, 1)));

        point.SpawnOnCreate();

        Assert.Multiple(() => {
            Assert.That(Alive(MobA), Is.EqualTo(2));
            Assert.That(Alive(MobB), Is.EqualTo(1));
            Assert.That(Sent(SendOp.FieldAddNpc), Has.Count.EqualTo(3));
        });
    }

    [Test]
    public void SpawnOnCreate_NotSet_SpawnsNothing() {
        StartField(TriggerNpcSpawnTests.Npc(MobA, 0));
        var point = new FieldSpawnPointNpc(Field, 1000, Point(10, false, 0, (MobA, 1)));

        point.SpawnOnCreate();

        Assert.That(Alive(MobA), Is.EqualTo(0));
    }

    [Test]
    public void GroupSpawnedPoint_DoesNotFillOnCreateOrOnItsOwnClock() {
        StartField(TriggerNpcSpawnTests.Npc(MobA, 0));
        var point = new FieldSpawnPointNpc(Field, 1000, Point(10, true, 0, (MobA, 1)), groupSpawned: true);

        point.SpawnOnCreate();
        point.Update(1000);
        point.Update(100000);

        Assert.Multiple(() => {
            Assert.That(point.GroupSpawned, Is.True);
            Assert.That(Alive(MobA), Is.EqualTo(0));
        });
    }

    [Test]
    public void Update_RegenChecksOnTheRegenClock_AndRefillsAfterADeath() {
        StartField(TriggerNpcSpawnTests.Npc(MobA, 0));
        var point = new FieldSpawnPointNpc(Field, 1000, Point(10, false, 0, (MobA, 1)));

        point.Update(0);
        Assert.That(Alive(MobA), Is.EqualTo(1), "first check fills the point");
        FieldNpc npc = Field.GetActorsBySpawnId(PointId).OfType<FieldNpc>().Single();
        Field.Npcs.TryRemove(npc.ObjectId, out _);
        Field.Mobs.TryRemove(npc.ObjectId, out _);
        point.Update(9999);
        Assert.That(Alive(MobA), Is.EqualTo(0), "before the next check nothing comes");
        point.Update(10000);
        Assert.That(Alive(MobA), Is.EqualTo(1), "the next check refills it");
        point.Update(20000);
        Assert.That(Alive(MobA), Is.EqualTo(1), "a full point stays full");
    }

    [Test]
    public void Update_NoRegenTime_NeverSpawns() {
        StartField(TriggerNpcSpawnTests.Npc(MobA, 0));
        var point = new FieldSpawnPointNpc(Field, 1000, Point(0, true, 0, (MobA, 1)));

        point.Update(1000000);

        Assert.That(Alive(MobA), Is.EqualTo(0));
    }

    [Test]
    public void TriggerSpawn_NpcCountCapsTheList() {
        StartField(TriggerNpcSpawnTests.Npc(MobA, 0));
        var capped = new FieldSpawnPointNpc(Field, 1000, Point(10, false, 1, (MobA, 3)));

        capped.TriggerSpawn();
        capped.TriggerSpawn();
        Assert.That(Alive(MobA), Is.EqualTo(1));
    }
}
