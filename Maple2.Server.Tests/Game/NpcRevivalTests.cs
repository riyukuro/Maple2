using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// Dead.Revival: a dead npc's revival npcs stand where it died, each removed after Dead.LifeTime.
public class NpcRevivalTests : TriggerTestBase {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int MobId = 21001015;
    private const int RevivalId = 21001016;
    private const int FriendId = 29000037;

    [TestCase(0, 0, 0)]
    [TestCase(0, 5, 0)]
    [TestCase(21001016, 5, 5)]
    [TestCase(29000037, 1, 1)]
    [TestCase(29000037, 0, 1)]
    public void Count(int revival, int count, int expected) {
        Assert.That(NpcRevivalRules.Count(new NpcMetadataDead(5, revival, count, 0, 0)), Is.EqualTo(expected));
    }

    [TestCase(0f, 0.0)]
    [TestCase(10f, 10.0)]
    [TestCase(99999f, 99999.0)]
    public void LifeTime(float lifeTime, double seconds) {
        Assert.That(NpcRevivalRules.LifeTime(new NpcMetadataDead(5, 1, 1, lifeTime, 0)), Is.EqualTo(TimeSpan.FromSeconds(seconds)));
    }

    private FieldNpc SpawnMob(NpcMetadataDead dead, params NpcMetadata[] others) {
        Start();
        typeof(FieldManager).GetField("<Navigation>k__BackingField", Any)!.SetValue(Field, TriggerHallGameTests.FlatNavigation());
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(test.Session.Player, Field);
        typeof(FieldManager).GetField("<Mobs>k__BackingField", Any)!.SetValue(Field, new ConcurrentDictionary<int, FieldNpc>());
        typeof(FieldManager).GetField("fieldBreakables", Any)!.SetValue(Field, new ConcurrentDictionary<string, FieldBreakable>());
        typeof(FieldManager).GetField("<FieldInstance>k__BackingField", Any)!.SetValue(Field,
            new InstanceFieldMetadata(0, InstanceType.none, 0, false, 0, true, 0, 0, 0, 0));
        test.Register(TestGameSession.Metadata(30000071), TestGameSession.Metadata(50300014), TestGameSession.Metadata(30000084));
        test.SetServerTable(new NpcStatFactorTable(new System.Collections.Generic.Dictionary<int,
            System.Collections.Generic.IReadOnlyDictionary<int, System.Collections.Generic.IReadOnlyDictionary<int, NpcStatFactorMetadata>>>()));
        NpcMetadata mob = TriggerNpcSpawnTests.Npc(MobId, 0) with { Dead = dead };
        typeof(FieldManager).GetField("<NpcMetadata>k__BackingField", Any)!.SetValue(Field,
            TriggerNpcSpawnTests.NpcTable(others.Prepend(mob).ToArray()));
        return Field.SpawnNpc(mob, new Vector3(100, 200, 0), new Vector3(0, 0, 90))!;
    }

    [Test]
    public void SleepyShroom_FiveRevivalMobsNearTheDeadOne_KeptWithoutLifeTime() {
        FieldNpc shroom = SpawnMob(new NpcMetadataDead(5, RevivalId, 5, 0, 0), TriggerNpcSpawnTests.Npc(RevivalId, 0));
        int queued = Field.Scheduler.Count;

        shroom.SpawnRevival();

        FieldNpc[] revivals = Field.Mobs.Values.Where(npc => npc.Value.Metadata.Id == RevivalId).ToArray();
        Assert.Multiple(() => {
            Assert.That(revivals, Has.Length.EqualTo(5));
            Assert.That(revivals.All(npc => Vector2.Distance(new Vector2(npc.Position.X, npc.Position.Y), new Vector2(100, 200))
                <= NpcRevivalRules.ScatterRadius + 1), Is.True);
            Assert.That(Sent(SendOp.FieldAddNpc), Has.Count.EqualTo(5));
            // No lifeTime: no removal is queued.
            Assert.That(Field.Scheduler.Count, Is.EqualTo(queued));
        });
    }

    [Test]
    public void Mano_OneFriendlyNpcAtTheDeadOne_RemovedAfterLifeTime() {
        FieldNpc mano = SpawnMob(new NpcMetadataDead(3, FriendId, 1, 30, 0), TriggerNpcSpawnTests.Npc(FriendId, 1));
        int queued = Field.Scheduler.Count;

        mano.SpawnRevival();

        FieldNpc friend = Field.Npcs.Values.Single(npc => npc.Value.Metadata.Id == FriendId);
        Assert.Multiple(() => {
            Assert.That(friend.Position, Is.EqualTo(new Vector3(100, 200, 0)));
            Assert.That(friend.Rotation, Is.EqualTo(mano.Rotation));
            Assert.That(Field.Scheduler.Count, Is.EqualTo(queued + 1));
        });
    }

    [Test]
    public void Corpse_LeavesTheRevivalsAsItIsRemoved() {
        FieldNpc shroom = SpawnMob(new NpcMetadataDead(5, RevivalId, 5, 0, 0), TriggerNpcSpawnTests.Npc(RevivalId, 0));
        Field.Scheduler.Start();

        shroom.RemoveCorpse(TimeSpan.Zero);
        Field.Scheduler.InvokeAll();
        Field.Scheduler.InvokeAll();

        Assert.That(Field.Mobs.Values.Select(npc => npc.Value.Metadata.Id), Is.EqualTo(Enumerable.Repeat(RevivalId, 5)));
    }

    [Test]
    public void CorpseRemovedByATrigger_NoRevivals() {
        FieldNpc shroom = SpawnMob(new NpcMetadataDead(5, RevivalId, 5, 0, 0), TriggerNpcSpawnTests.Npc(RevivalId, 0));
        Field.Scheduler.Start();

        Field.RemoveNpc(shroom.ObjectId);
        shroom.RemoveCorpse(TimeSpan.Zero);
        Field.Scheduler.InvokeAll();
        Field.Scheduler.InvokeAll();

        Assert.That(Field.Mobs, Is.Empty);
    }

    [Test]
    public void NoRevival_SpawnsNothing() {
        FieldNpc mob = SpawnMob(new NpcMetadataDead(5, 0, 5, 0, 0));
        mob.SpawnRevival();
        Assert.That(Field.Mobs.Values.Select(npc => npc.Value.Metadata.Id), Is.EqualTo(new[] { MobId }));
    }
}
