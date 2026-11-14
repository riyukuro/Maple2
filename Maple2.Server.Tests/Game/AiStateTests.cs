using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Caching;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
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
    private const float Sight = 3000;

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
            new NpcMetadataDistance(0, Sight, 300, 300, Sight, 300, 300), new NpcMetadataSkill([], 0),
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

    // Another player in the npc's field, x away from the npc.
    private FieldPlayer AddPlayer(int objectId, float x) {
        var other = new TestGameSession(test.Options);
        other.Start();
        typeof(Actor<Maple2.Model.Game.Player>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(other.Session.Player, objectId);
        typeof(Actor<Maple2.Model.Game.Player>).GetField("<Field>k__BackingField", Any)!.SetValue(other.Session.Player, Field);
        other.Session.Field = Field;
        other.Session.Player.Position = npc.Position + new Vector3(x, 0, 0);
        Field.Players[objectId] = other.Session.Player;
        return other.Session.Player;
    }

    private static TargetNode Target(NodeTargetType type, int from, int to) =>
        new("target", [], type, 0, 0, 0, 0, from, to, Vector3.Zero, NodeAiTarget.DefaultTarget, false, 0, 0, false);

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

    // KR AI_Witch2CerberosTrainer_PaterA.xml: <node name="teleport" pos="-2700, -1800, 2750" isKeepBattle="true"> (its
    // children then target a far player, 300 to 1300 away). The node holds the battle; one without the flag does not.
    [Test]
    public void Teleport_IsKeepBattle_HoldsTheBattle() {
        Spawn([new TeleportNode("teleport", [], new Vector3(-2700, -1800, 2750), 0, Vector3.Zero, 0, 0, 0, true)]);
        npc.AiState.Update(Field.FieldTick);

        Assert.That(test.Errors, Is.Empty);
        Assert.That(npc.Position, Is.EqualTo(new Vector3(-2700, -1800, 2750)));
        Assert.That(npc.BattleState.KeepBattle, Is.True);

        Spawn([new TeleportNode("teleport", [], new Vector3(-2700, -1800, 2750), 0, Vector3.Zero, 0, 0, 0, false)]);
        npc.AiState.Update(Field.FieldTick);
        Assert.That(npc.BattleState.KeepBattle, Is.False);
    }

    // KR BossDungeon/EyeOfLapenta/AI_YuperiaRbladerDark.xml: <condition name="extraData" isKeepBattle="true"
    // key="SpecialPattern" value="2"> in a conditions node, its branch setting SpecialPattern 3 (the standby before it
    // left out, as a standby holds the battle by itself). The passing condition holds the battle.
    [Test]
    public void ExtraDataCondition_IsKeepBattle_HoldsTheBattle() {
        var condition = new ExtraDataCondition("extraData", [SetValue("SpecialPattern", 3)], "SpecialPattern", 2, AiConditionOp.Equal, true);
        Spawn([new ConditionsNode("conditions", [], [condition], 0, 0, false)]);
        npc.AiExtraData["SpecialPattern"] = 2;
        for (int i = 0; i < 3; i++) {
            npc.AiState.Update(Field.FieldTick);
        }

        Assert.That(test.Errors, Is.Empty);
        Assert.That(npc.AiExtraData["SpecialPattern"], Is.EqualTo(3), "the branch ran");
        Assert.That(npc.BattleState.KeepBattle, Is.True);
    }

    private void Run(int updates = 1) {
        for (int i = 0; i < updates; i++) {
            SetTick(Field.FieldTick + 100);
            npc.AiState.Update(Field.FieldTick);
        }
    }

    private void SetHealthPercent(long percent) {
        Stat health = npc.Stats.Values[BasicAttribute.Health];
        health.Add(health.Total * percent / 100 - health.Current);
    }

    // KR AI_SkyFortress_Mason.xml: <node name="SetValue" key="BattleEnd" value="1" isModify="1" /> adds 1 at each run
    // (the comment: each standby adds 1 until the value reaches 30); <node name="SetValue" key="MoveGo" value="0" />
    // sets. A modify of a key not set yet sets it.
    [Test]
    public void SetValue_IsModifyAdds_ElseSets() {
        Spawn([SetValue("BattleEnd", 1, isModify: true)]);
        Run();
        Assert.That(npc.AiExtraData["BattleEnd"], Is.EqualTo(1));
        npc.AiExtraData["BattleEnd"] = 28;
        Run();
        Assert.That(npc.AiExtraData["BattleEnd"], Is.EqualTo(29));

        Spawn([SetValue("MoveGo", 0)]);
        npc.AiExtraData["MoveGo"] = 1;
        Run();
        Assert.That(npc.AiExtraData["MoveGo"], Is.EqualTo(0));
        Assert.That(test.Errors, Is.Empty);
    }

    // KR AI_SkyFortress_Mason.xml: a conditions node whose first condition is extraData BattleEnd greaterEqual 30 and
    // whose last is true. The first condition that passes runs, and only it.
    [Test]
    public void Conditions_TheFirstThatPassesRuns() {
        Entry[] Battle() => [new ConditionsNode("conditions", [], [
            new ExtraDataCondition("extraData", [SetValue("Ran", 1)], "BattleEnd", 30, AiConditionOp.GreaterEqual, false),
            new TrueCondition("true", [SetValue("Ran", 2)]),
        ], 0, 0, false)];

        Spawn(Battle());
        npc.AiExtraData["BattleEnd"] = 30;
        Run();
        Assert.That(npc.AiExtraData["Ran"], Is.EqualTo(1));

        Spawn(Battle());
        npc.AiExtraData["BattleEnd"] = 29;
        Run();
        Assert.That(npc.AiExtraData["Ran"], Is.EqualTo(2));
        Assert.That(test.Errors, Is.Empty);
    }

    // KR AI_Alex_AnchorLizard.xml: <node name="select" prob="9999,1000,0"> whose first child has cooltime="999999999"
    // (its comment: first time only) and whose last has weight 0 (KR AI_SkyFortress_Mason.xml: prob="99999,0"). The
    // first child runs once; with it on its cooltime the select falls back to the child of weight 0.
    [Test]
    public void Select_CooltimeOnce_ThenTheZeroWeightChild() {
        var once = new SetValueNode("SetValue", [], "Once", 1, 0, 999999999, true, false);
        Spawn([new SelectNode("select", [once, SetValue("Fallback", 1, isModify: true)], [9999, 0], false)]);
        Run();
        Assert.That(npc.AiExtraData["Once"], Is.EqualTo(1));
        Assert.That(npc.AiExtraData.ContainsKey("Fallback"), Is.False);

        Run(3);
        Assert.That(npc.AiExtraData["Once"], Is.EqualTo(1), "the first child is on its cooltime");
        Assert.That(npc.AiExtraData["Fallback"], Is.EqualTo(3));
        Assert.That(test.Errors, Is.Empty);
    }

    // KR AI_GigantikaHead.xml: <node name="select" prob="1,1,1,1,1,1"> of six summon spots, then <node name="RemoveMe" />.
    // One child of the select runs, once, and then the node after the select.
    [Test]
    public void Select_OneChildOnce_ThenTheNextSibling() {
        Spawn([
            new SelectNode("select", [SetValue("Summon", 1, isModify: true), SetValue("Summon", 1, isModify: true)], [1, 1], false),
            SetValue("After", 1),
        ]);
        Run();
        Assert.That(npc.AiExtraData["Summon"], Is.EqualTo(1));
        Assert.That(npc.AiExtraData.GetValueOrDefault("After"), Is.EqualTo(1));
        Assert.That(test.Errors, Is.Empty);
    }

    // KR AI_Alex_AnchorLizard.xml: <node name="target" type="near" from="10" to="500" /> after a skill. The npc turns
    // to the nearest player in 10 to 500 when the node runs, from a target it had 400 away.
    [Test]
    public void Target_Near_TakesTheNearestInItsWindow() {
        Spawn([Target(NodeTargetType.Near, 10, 500)], position: Vector3.Zero);
        FieldPlayer far = AddPlayer(2, 400);
        FieldPlayer near = AddPlayer(3, 100);
        AddPlayer(4, 5);
        npc.BattleState.ForceTarget(far);
        Run();
        Assert.That(npc.BattleState.Target, Is.SameAs(near));
        Assert.That(test.Errors, Is.Empty);
    }

    // KR AI_Alex_AnchorLizard.xml: a standby with cooltime="10000" (its comment: the periodic target switch) whose child is
    // <node name="target" type="rand" from="10" to="1500" />. Each run picks one of the players in the window at
    // random; with none in it the npc keeps its target.
    [Test]
    public void Target_Rand_PicksAtRandomInItsWindow() {
        Spawn([Target(NodeTargetType.Rand, 10, 1500)], position: Vector3.Zero);
        FieldPlayer first = AddPlayer(2, 400);
        FieldPlayer second = AddPlayer(3, 800);
        FieldPlayer outside = AddPlayer(4, 2000);
        var picked = new HashSet<IActor>();
        for (int i = 0; i < 60; i++) {
            npc.BattleState.ForceTarget(outside);
            Run();
            picked.Add(npc.BattleState.Target!);
        }
        Assert.That(picked, Is.EquivalentTo(new IActor[] { first, second }));

        Spawn([Target(NodeTargetType.Rand, 10, 300)], position: Vector3.Zero);
        FieldPlayer kept = AddPlayer(2, 400);
        npc.BattleState.ForceTarget(kept);
        Run();
        Assert.That(npc.BattleState.Target, Is.SameAs(kept));
        Assert.That(test.Errors, Is.Empty);
    }

    // KR AI_Alex_AnchorLizard.xml: <condition name="hpOver" value="40"> then <condition name="true"> (its comment: 40%
    // and below). At 40% health hpOver 40 still passes; at 39% the true branch runs.
    [Test]
    public void HpOver_PassesAtTheValueAndAbove() {
        Entry[] Battle() => [new ConditionsNode("conditions", [], [
            new HpOverCondition("hpOver", [SetValue("Ran", 1)], 40),
            new TrueCondition("true", [SetValue("Ran", 2)]),
        ], 0, 0, false)];

        Spawn(Battle());
        SetHealthPercent(40);
        Run();
        Assert.That(npc.AiExtraData["Ran"], Is.EqualTo(1));

        Spawn(Battle());
        SetHealthPercent(39);
        Run();
        Assert.That(npc.AiExtraData["Ran"], Is.EqualTo(2));
        Assert.That(test.Errors, Is.Empty);
    }

    // KR AI_SkyFortress_Mason.xml: <reserved><condition name="hpLess" value="100"><node name="SetValue" key="BattleEnd"
    // value="0" /></condition></reserved>. The reserved branch runs once when it passes (at full health, as hpLess 100
    // passes at 100%), before the battle tree; the battle's modify then counts from 0.
    [Test]
    public void Reserved_HpLess_RunsOnceBeforeTheBattle() {
        Spawn([SetValue("BattleEnd", 1, isModify: true)], [new HpLessCondition("hpLess", [SetValue("BattleEnd", 0)], 100)]);
        npc.AiExtraData["BattleEnd"] = 25;
        Run(3);

        Assert.That(npc.AiExtraData["BattleEnd"], Is.EqualTo(2), "reset to 0 once, then 1 added twice");
        Assert.That(test.Errors, Is.Empty);
    }
}
