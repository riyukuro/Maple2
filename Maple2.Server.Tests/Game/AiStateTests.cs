using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Caching;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.ActorStateComponent;
using static Maple2.Server.Game.Model.ActorStateComponent.TaskState;
using static Maple2.Model.Metadata.AiMetadata;

namespace Maple2.Server.Tests.Game;

// The npc AI (AiState) run on a field npc whose AI file is built from KR AI rows, as the ingest maps them
// (AiMapper); the npc fights the test player.
public class AiStateTests : TriggerTestBase {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const string AiPath = "AI_StateTest.xml";
    private const int NpcId = 21000001;

    private FieldNpc npc = null!;

    private FieldNpc Spawn(Entry[] battle, Condition[]? reserved = null, Vector3? position = null) {
        Start();
        typeof(FieldManager).GetField("<Navigation>k__BackingField", Any)!.SetValue(Field, TriggerHallGameTests.FlatNavigation());
        typeof(Actor<Maple2.Model.Game.Player>).GetField("<Field>k__BackingField", Any)!.SetValue(test.Session.Player, Field);
        typeof(FieldManager).GetField("<Mobs>k__BackingField", Any)!.SetValue(Field, new ConcurrentDictionary<int, FieldNpc>());
        typeof(FieldManager).GetField("fieldBreakables", Any)!.SetValue(Field, new ConcurrentDictionary<string, FieldBreakable>());
        typeof(FieldManager).GetField("<FieldInstance>k__BackingField", Any)!.SetValue(Field,
            new InstanceFieldMetadata(0, InstanceType.none, 0, false, 0, true, 0, 0, 0, 0));
        test.Register(TestGameSession.Metadata(30000071), TestGameSession.Metadata(50300014), TestGameSession.Metadata(30000084));
        test.SetServerTable(new NpcStatFactorTable(new Dictionary<int, IReadOnlyDictionary<int, IReadOnlyDictionary<int, NpcStatFactorMetadata>>>()));
        typeof(FieldManager).GetField("<NpcMetadata>k__BackingField", Any)!.SetValue(Field, NpcTable(Npc(NpcId)));
        typeof(FieldManager).GetField("<AiMetadata>k__BackingField", Any)!.SetValue(Field,
            AiTable(new AiMetadata(AiPath, reserved ?? [], battle, [], [])));
        SetTick(1_000_000);

        Assert.That(Field.NpcMetadata.TryGet(NpcId, out NpcMetadata? metadata), Is.True);
        npc = Field.SpawnNpc(metadata!, position ?? new Vector3(1000, 0, 0), Vector3.Zero)!;
        test.Session.Player.Position = Vector3.Zero;
        npc.BattleState.ForceTarget(test.Session.Player);
        test.TakeSent();
        return npc;
    }

    private void SetTick(long tick) => typeof(FieldManager).GetField("<FieldTick>k__BackingField", Any)!.SetValue(Field, tick);

    private NpcTask? CurrentTask => (NpcTask?) typeof(AiState).GetField("currentTask", Any)!.GetValue(npc.AiState);

    private static NpcMetadata Npc(int id) {
        var basic = new NpcMetadataBasic(0, 0, 0, 0, 0, 0, 0, 1, 1, false, 0, 0, 0, 0, [], [], 0);
        var stat = new NpcMetadataStat(new Dictionary<BasicAttribute, long> { [BasicAttribute.Health] = 100 }, [], [], [], []);
        return new NpcMetadata(id, "npc", AiPath, new NpcMetadataModel("test_model", 1, 1), stat, basic,
            new NpcMetadataDistance(0, 0, 0, 0, 0, 0, 0), new NpcMetadataSkill([], 0),
            new NpcMetadataProperty([], new NpcMetadataCapsule(10, 100), null),
            new NpcMetadataDropInfo(0, 0, [], [], [], [], []), new NpcMetadataAction(0, 100, 100, [], 0, ""),
            new NpcMetadataDead(0, 0, 0, 0, 0), null, new NpcMetadataLookAtTarget("", false, false));
    }

    private static NpcMetadataStorage NpcTable(params NpcMetadata[] npcs) {
        var storage = (NpcMetadataStorage) RuntimeHelpers.GetUninitializedObject(typeof(NpcMetadataStorage));
        var cache = new LRUCache<int, NpcMetadata>(100, 5);
        foreach (NpcMetadata metadata in npcs) {
            cache.AddReplace(metadata.Id, metadata);
        }
        typeof(MetadataStorage<int, NpcMetadata>).GetField("Cache", Any)!.SetValue(storage, cache);
        var animations = new LRUCache<string, AnimationMetadata>(10, 1);
        animations.AddReplace("test_model", new AnimationMetadata("test_model", new Dictionary<string, AnimationSequenceMetadata>()));
        typeof(NpcMetadataStorage).GetField("AniCache", Any)!.SetValue(storage, animations);
        typeof(NpcMetadataStorage).GetField("tagLookup", Any)!.SetValue(storage, new Dictionary<string, HashSet<int>>());
        return storage;
    }

    private static AiMetadataStorage AiTable(params AiMetadata[] ais) {
        var storage = (AiMetadataStorage) RuntimeHelpers.GetUninitializedObject(typeof(AiMetadataStorage));
        var cache = new LRUCache<string, AiMetadata>(100, 5);
        foreach (AiMetadata ai in ais) {
            cache.AddReplace(ai.Name, ai);
        }
        typeof(MetadataStorage<string, AiMetadata>).GetField("Cache", Any)!.SetValue(storage, cache);
        return storage;
    }

    private static TraceNode Trace(int limit, int till = 0, int skillIdx = 0, long cooltime = 0) =>
        new("trace", [], limit, skillIdx, "", 0, till, 0, cooltime, false);

    private static StandbyNode Standby(int limit, int faceTarget = 1, bool keepBattle = false) =>
        new("standby", [], limit, 0, "", Vector3.Zero, faceTarget, 0, 0, keepBattle);

    private static SetValueNode SetValue(string key, int value, bool isModify = false, bool keepBattle = false) =>
        new("SetValue", [], key, value, 0, 0, isModify, keepBattle);

    // The harness runs the tree: a standby (the most used node, 28127 rows) takes the npc's AI task.
    [Test]
    public void Standby_IsTheNpcsTask() {
        Spawn([Standby(1000)]);
        npc.AiState.Update(Field.FieldTick);

        Assert.That(test.Errors, Is.Empty);
        Assert.That(CurrentTask, Is.InstanceOf<MovementState.NpcStandbyTask>());
    }

    // KR AI_SkyFortress_Mason.xml: <node name="trace" till="430" limit="410" cooltime="15000" /> under a select
    // (BossDungeon/Balrog/AI_Balrog_Chaos.xml comments its till 430 traces: trace up to distance 430). The npc walks at
    // its target until it is 430 away, not onto it.
    [Test]
    public void Trace_TillIsTheStopDistance() {
        Spawn([Trace(410, till: 430, cooltime: 15000)]);
        npc.AiState.Update(Field.FieldTick);

        Assert.That(test.Errors, Is.Empty);
        Assert.That(CurrentTask, Is.InstanceOf<MovementState.NpcMoveTargetDistanceTask>());
        Assert.That(((MovementState.NpcMoveTargetDistanceTask) CurrentTask!).Distance, Is.EqualTo(430));
    }
}
