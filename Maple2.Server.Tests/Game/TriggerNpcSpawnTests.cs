using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Caching;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Tests.Game;

// The trigger rows that spawn real npcs (FieldManager.SpawnNpc), on a field with the flat test navmesh of
// TriggerHallGameTests and an npc table of made-up npcs.
public class TriggerNpcSpawnTests : TriggerTestBase {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int MobId = 21000001;
    private const int FriendId = 11000001;

    private void StartSpawns(params SpawnPointNPC[] spawns) {
        Start();
        test.Session.Player.Position = new Vector3(100, 200, 0);
        typeof(FieldManager).GetField("<Navigation>k__BackingField", Any)!.SetValue(Field, TriggerHallGameTests.FlatNavigation());
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(test.Session.Player, Field);
        typeof(FieldManager).GetField("<Mobs>k__BackingField", Any)!.SetValue(Field, new ConcurrentDictionary<int, FieldNpc>());
        typeof(FieldManager).GetField("<FieldInstance>k__BackingField", Any)!.SetValue(Field,
            new InstanceFieldMetadata(0, InstanceType.none, 0, false, 0, true, 0, 0, 0, 0));
        // A spawned npc's movement makes its debug markers (MovementState.InitDebugMarker) from these items.
        test.Register(TestGameSession.Metadata(30000071), TestGameSession.Metadata(50300014), TestGameSession.Metadata(30000084));
        test.SetServerTable(new NpcStatFactorTable(new Dictionary<int, IReadOnlyDictionary<int, IReadOnlyDictionary<int, NpcStatFactorMetadata>>>()));
        typeof(FieldManager).GetField("<NpcMetadata>k__BackingField", Any)!.SetValue(Field,
            NpcTable(Npc(MobId, 0), Npc(FriendId, 1), Npc(Constant.DummyNpcMale, 1), Npc(Constant.DummyNpcFemale, 1)));
        typeof(MapEntityMetadata).GetField("<NpcSpawns>k__BackingField", Any)!.SetValue(Field.Entities, spawns.ToList());
        typeof(MapEntityMetadata).GetField("<EventNpcSpawns>k__BackingField", Any)!.SetValue(Field.Entities, new Dictionary<int, EventSpawnPointNPC>());
        typeof(MapEntityMetadata).GetField("<Patrols>k__BackingField", Any)!.SetValue(Field.Entities, new List<MS2PatrolData>());
    }

    private static NpcMetadata Npc(int id, int friendly) {
        var basic = new NpcMetadataBasic(friendly, 0, 0, 0, 0, 0, 0, 1, 1, false, 0, 0, 0, 0, [], [], 0);
        var stat = new NpcMetadataStat(new Dictionary<BasicAttribute, long> { [BasicAttribute.Health] = 100 }, [], [], [], []);
        return new NpcMetadata(id, "npc", "", new NpcMetadataModel("test_model", 1, 1), stat, basic,
            new NpcMetadataDistance(0, 0, 0, 0, 0, 0, 0), new NpcMetadataSkill([], 0),
            new NpcMetadataProperty([], new NpcMetadataCapsule(10, 100), null),
            new NpcMetadataDropInfo(0, 0, [], [], [], [], []), new NpcMetadataAction(0, 100, 100, [], 0, ""),
            new NpcMetadataDead(0, 0, 0, 0, 0), null, new NpcMetadataLookAtTarget("", false, false));
    }

    // The npc table without a database: the npcs and the model's (empty) animation are in its caches.
    private static NpcMetadataStorage NpcTable(params NpcMetadata[] npcs) {
        var storage = (NpcMetadataStorage) RuntimeHelpers.GetUninitializedObject(typeof(NpcMetadataStorage));
        var cache = new LRUCache<int, NpcMetadata>(100, 5);
        foreach (NpcMetadata npc in npcs) {
            cache.AddReplace(npc.Id, npc);
        }
        typeof(MetadataStorage<int, NpcMetadata>).GetField("Cache", Any)!.SetValue(storage, cache);
        var animations = new LRUCache<string, AnimationMetadata>(10, 1);
        animations.AddReplace("test_model", new AnimationMetadata("test_model", new Dictionary<string, AnimationSequenceMetadata>()));
        typeof(NpcMetadataStorage).GetField("AniCache", Any)!.SetValue(storage, animations);
        typeof(NpcMetadataStorage).GetField("tagLookup", Any)!.SetValue(storage, new Dictionary<string, HashSet<int>>());
        return storage;
    }

    private static SpawnPointNPC Spawn(int id, Vector3 position, params SpawnPointNPCListEntry[] npcs) {
        return new SpawnPointNPC("", id, position, new Vector3(0, 0, 90), true, false, 0, npcs.ToList(), 0, null);
    }

    // spawn_npc_range (119): each spawn point of range_ids spawns its npcs at its position, each npc carries the
    // spawn id and the score; a mob goes to the mobs, a friendly npc to the npcs, both are sent to the field.
    [Test]
    public void SpawnNpcRange_SpawnsEachPoint() {
        StartSpawns(Spawn(5, new Vector3(300, 0, 0), new SpawnPointNPCListEntry(MobId, 2)),
            Spawn(6, new Vector3(-300, 0, 0), new SpawnPointNPCListEntry(FriendId, 1)),
            Spawn(7, new Vector3(0, 300, 0), new SpawnPointNPCListEntry(MobId, 1)));

        Action("""<action name="spawn_npc_range" range_ids="5,6" is_auto_targeting="0" score="300" />""");

        Assert.That(Field.Mobs.Values.Select(npc => (npc.Value.Metadata.Id, npc.SpawnPointId, npc.TriggerScore, npc.Position)),
            Is.EqualTo(new[] { (MobId, 5, 300, new Vector3(300, 0, 0)), (MobId, 5, 300, new Vector3(300, 0, 0)) }));
        Assert.That(Field.Npcs.Values.Select(npc => (npc.Value.Metadata.Id, npc.SpawnPointId, npc.TriggerScore, npc.Position)),
            Is.EqualTo(new[] { (FriendId, 6, 300, new Vector3(-300, 0, 0)) }));
        Assert.That(Sent(SendOp.FieldAddNpc), Has.Count.EqualTo(3));
    }

    // random_pick_count picks that many of the spawn points; an unknown spawn id spawns nothing.
    [Test]
    public void SpawnNpcRange_RandomPickCount() {
        StartSpawns(Spawn(5, new Vector3(300, 0, 0), new SpawnPointNPCListEntry(MobId, 1)),
            Spawn(6, new Vector3(-300, 0, 0), new SpawnPointNPCListEntry(MobId, 1)),
            Spawn(7, new Vector3(0, 300, 0), new SpawnPointNPCListEntry(MobId, 1)));

        Action("""<action name="spawn_npc_range" range_ids="5,6,7" random_pick_count="2" />""");
        Assert.That(Field.Mobs.Values.Select(npc => npc.SpawnPointId).Distinct().Count(), Is.EqualTo(2));
        Assert.That(Field.Mobs, Has.Count.EqualTo(2));

        Action("""<action name="spawn_npc_range" range_ids="9" />""");
        Assert.That(Field.Mobs, Has.Count.EqualTo(2));
    }

    // spawn_monster plays the event spawn point's spawn animation unless auto_target is 0: a missing auto_target
    // (457 KR rows) reads as the trigger definition's default True.
    [Test]
    public void SpawnMonster_SpawnAnimationUnlessTold() {
        StartSpawns();
        var spawn = new EventSpawnPointNPC("", 5, new Vector3(300, 0, 0), new Vector3(0, 0, 90), true, false, 0, 1,
            [new SpawnPointNPCListEntry(MobId, 1)], 0, 0, "Regen_A");
        typeof(MapEntityMetadata).GetField("<EventNpcSpawns>k__BackingField", Any)!.SetValue(Field.Entities,
            new Dictionary<int, EventSpawnPointNPC> { [5] = spawn });

        Action("""<action name="spawn_monster" spawn_ids="5" />""");
        Action("""<action name="spawn_monster" spawn_ids="5" auto_target="0" />""");
        Action("""<action name="spawn_monster" spawn_ids="5" auto_target="1" />""");
        Assert.That(Field.Mobs.Values.OrderBy(npc => npc.ObjectId).Select(npc => npc.SpawnAnimation),
            Is.EqualTo(new[] { "Regen_A", "", "Regen_A" }));
    }

    // move_user_path (366): each player gets a dummy npc of their gender at their position that walks the named
    // patrol, and is told to follow it (FollowNpc); a path the map lacks spawns nothing.
    [Test]
    public void MoveUserPath_PlayerFollowsADummyNpc() {
        StartSpawns();
        var patrol = new MS2PatrolData("uuid", "MS2PatrolData_101", false, 1, false,
            [new MS2WayPoint("w1", true, new Vector3(500, 0, 0), Vector3.Zero, "", "", 0, false)]);
        typeof(MapEntityMetadata).GetField("<Patrols>k__BackingField", Any)!.SetValue(Field.Entities, new List<MS2PatrolData> { patrol });

        Action("""<action name="move_user_path" patrol_name="MS2PatrolData_102" />""");
        Assert.That(Field.Npcs, Is.Empty);
        Assert.That(test.TakeSent(), Is.Empty);

        Action("""<action name="move_user_path" patrol_name="MS2PatrolData_101" />""");
        FieldNpc dummy = Field.Npcs.Values.Single();
        int dummyId = test.Session.Player.Value.Character.Gender is Gender.Female ? Constant.DummyNpcFemale : Constant.DummyNpcMale;
        Assert.That((dummy.Value.Metadata.Id, dummy.Position, dummy.Patrol), Is.EqualTo((dummyId, new Vector3(100, 200, 0), patrol)));
        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Where(packet => TestGameSession.Op(packet) == SendOp.FollowNpc).Select(packet => BitConverter.ToInt32(packet, 2)),
            Is.EqualTo(new[] { dummy.ObjectId }));
        Assert.That(sent.Count(packet => TestGameSession.Op(packet) == SendOp.FieldAddNpc), Is.EqualTo(1));
    }

    // wedding_user_to_patrol (24): the groom or the bride of the room's ceremony walks the path behind a dummy npc,
    // as move_user_path moves a player; the other role, or a room without a ceremony, moves no one.
    [Test]
    public void WeddingUserToPatrol_TheRolePlayer() {
        StartSpawns();
        var patrol = new MS2PatrolData("uuid", "MS2PatrolData_101", false, 1, false,
            [new MS2WayPoint("w1", true, new Vector3(500, 0, 0), Vector3.Zero, "", "", 0, false)]);
        typeof(MapEntityMetadata).GetField("<Patrols>k__BackingField", Any)!.SetValue(Field.Entities, new List<MS2PatrolData> { patrol });
        long groom = test.Session.CharacterId;

        Action("""<action name="wedding_user_to_patrol" patrol_name="MS2PatrolData_101" entry_type="Groom" patrol_index="1" />""");
        Field.Wedding = new WeddingCeremony(1, groom, groom + 1, groom);
        Action("""<action name="wedding_user_to_patrol" patrol_name="MS2PatrolData_101" entry_type="Bride" patrol_index="2" />""");
        Assert.That(Field.Npcs, Is.Empty);

        Action("""<action name="wedding_user_to_patrol" patrol_name="MS2PatrolData_101" entry_type="Groom" patrol_index="1" />""");
        FieldNpc dummy = Field.Npcs.Values.Single();
        Assert.That((dummy.Position, dummy.Patrol), Is.EqualTo((new Vector3(100, 200, 0), patrol)));
        Assert.That(Sent(SendOp.FollowNpc).Select(reader => reader.ReadInt()), Is.EqualTo(new[] { dummy.ObjectId }));
    }
}
