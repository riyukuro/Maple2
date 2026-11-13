using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.ActorStateComponent;
using Maple2.Server.Game.Model.Widget;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Trigger;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Server.Game.Util;
using Maple2.Tools.Scheduler;
using Maple2.Tools.VectorMath;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Maple2.Server.Tests.Game;

// A trigger script's actions and conditions run on a TestGameSession field whose map holds the given trigger objects.
// Each node is read by the trigger mapping, as from the ingested Xml (snake_case attributes).
public abstract class TriggerTestBase {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    protected TestGameSession test = null!;
    protected TriggerContext context = null!;
    protected FieldManager Field => test.Session.Field!;

    // A field with the trigger objects (meshes, effects, boxes, ...) and a script context on it; the start packets are taken.
    protected void Start(params Ms2Trigger[] triggers) {
        test = new TestGameSession();
        test.Start();
        SetTriggers(triggers);
        context = NewContext();
        test.TakeSent();
    }

    protected void SetTriggers(params Ms2Trigger[] triggers) {
        var entities = (MapEntityMetadata) RuntimeHelpers.GetUninitializedObject(typeof(MapEntityMetadata));
        typeof(MapEntityMetadata).GetField("<Trigger>k__BackingField", Any)!.SetValue(entities, new TriggerStorage(triggers.ToList()));
        typeof(FieldManager).GetField("<Entities>k__BackingField", Any)!.SetValue(Field, entities);
        typeof(FieldManager).GetField(nameof(FieldManager.TriggerObjects), Any)!.SetValue(Field, new TriggerCollection(entities));
        // The test field skips the FieldManager ctor, so its timer table is made here.
        typeof(FieldManager).GetField(nameof(FieldManager.Timers), Any)!.SetValue(Field, new Dictionary<string, TickTimer>());
    }

    // The script's context as FieldTrigger makes it; its queue of delayed steps is not started (Events.Count shows them).
    protected TriggerContext NewContext() {
        var owner = (FieldTrigger) RuntimeHelpers.GetUninitializedObject(typeof(FieldTrigger));
        GC.SuppressFinalize(owner);
        typeof(FieldEntity<TriggerModel>).GetField("<Field>k__BackingField", Any)!.SetValue(owner, Field);
        typeof(FieldEntity<TriggerModel>).GetField("<Value>k__BackingField", Any)!.SetValue(owner, new TriggerModel(1, "main", Vector3.Zero, Vector3.Zero));
        // The script has one state, "end", for the skip actions to name.
        typeof(FieldTrigger).GetField("trigger", Any)!.SetValue(owner, new Trigger([new Trigger.State("end", null, null, null)]));
        var created = (TriggerContext) RuntimeHelpers.GetUninitializedObject(typeof(TriggerContext));
        ILogger logger = Log.Logger.ForContext<TriggerContext>();
        typeof(TriggerContext).GetField("owner", Any)!.SetValue(created, owner);
        typeof(TriggerContext).GetField("logger", Any)!.SetValue(created, logger);
        typeof(TriggerContext).GetField("lastDebugKey", Any)!.SetValue(created, "");
        typeof(TriggerContext).GetField("currentRandom", Any)!.SetValue(created, float.MaxValue);
        typeof(TriggerContext).GetField(nameof(TriggerContext.Events), Any)!.SetValue(created, new EventQueue(logger));
        created.StartTick = Environment.TickCount64;
        return created;
    }

    private static XmlAttributeCollection Node(string xml) {
        var document = new XmlDocument();
        document.LoadXml(xml);
        return document.DocumentElement!.Attributes;
    }

    // <action name="set_mesh" trigger_ids="1101" visible="1" />
    protected void Action(string xml) {
        XmlAttributeCollection attributes = Node(xml);
        TriggerFunctionMapping.ActionMap[attributes["name"]!.Value](attributes).Execute(context);
    }

    protected bool Condition(string xml) {
        XmlAttributeCollection attributes = Node(xml);
        return TriggerFunctionMapping.ConditionMap[attributes["name"]!.Value](attributes).Evaluate(context);
    }

    // The sent packets of this op, each read from after its op code.
    protected List<ByteReader> Sent(SendOp op) {
        return test.TakeSent().Where(packet => TestGameSession.Op(packet) == op).Select(packet => new ByteReader(packet, 2)).ToList();
    }

    // The Trigger Update packets (command 3): the object id and visibility, then the reader for the class's own fields.
    protected List<(int Id, bool Visible, ByteReader Body)> Updates() {
        var updates = new List<(int, bool, ByteReader)>();
        foreach (ByteReader reader in Sent(SendOp.Trigger)) {
            if (reader.ReadByte() != 3) {
                continue;
            }
            updates.Add((reader.ReadInt(), reader.ReadBool(), reader));
        }
        return updates;
    }

    protected void SetRandom(float value) {
        typeof(TriggerContext).GetField("currentRandom", Any)!.SetValue(context, value);
    }
}

public class TriggerFieldObjectTests : TriggerTestBase {
    private static Ms2TriggerMesh Mesh(int id, bool visible = false) => new(1.5f, id, visible, false);

    // set_mesh (45261 KR uses): each named mesh that changes is shown at once, its fade sent with it; a mesh already
    // in that state and an id the map lacks send nothing.
    [Test]
    public void SetMesh_ShowsTheChangedMeshes() {
        Start(Mesh(1101), Mesh(1102), Mesh(1103, visible: true));

        Action("""<action name="set_mesh" trigger_ids="1101-1103,1199" visible="1" fade="3" />""");

        Assert.That(Field.TriggerObjects.Meshes.Values.Select(mesh => mesh.Visible), Is.All.True);
        List<(int Id, bool Visible, ByteReader Body)> updates = Updates();
        Assert.That(updates.Select(update => (update.Id, update.Visible)), Is.EqualTo(new[] { (1101, true), (1102, true) }));
        ByteReader rest = updates[0].Body;
        Assert.That((rest.ReadBool(), rest.ReadInt(), rest.ReadUnicodeString(), rest.ReadFloat()), Is.EqualTo((false, 3, "", 1.5f)));
        Assert.That(context.Events.Count, Is.Zero);
    }

    // With an interval the meshes change one by one through the script's queue: nothing is sent yet.
    [Test]
    public void SetMesh_IntervalQueuesEachMesh() {
        Start(Mesh(1101, visible: true), Mesh(1102, visible: true));

        Action("""<action name="set_mesh" trigger_ids="1101,1102" visible="0" start_delay="100" interval="200" />""");

        Assert.That(context.Events.Count, Is.EqualTo(2));
        Assert.That(Updates(), Is.Empty);
        Assert.That(Field.TriggerObjects.Meshes[1101].Visible, Is.True);
    }

    // set_effect (12766): the first effect shows at once, the next ones after the interval.
    [Test]
    public void SetEffect_FirstAtOnceThenStaggered() {
        Start(new Ms2TriggerEffect(601, false), new Ms2TriggerEffect(602, false), new Ms2TriggerEffect(603, true));

        Action("""<action name="set_effect" trigger_ids="601,602" visible="1" interval="500" />""");

        Assert.That(Updates().Select(update => (update.Id, update.Visible)), Is.EqualTo(new[] { (601, true) }));
        Assert.That(context.Events.Count, Is.EqualTo(1));

        Action("""<action name="set_effect" trigger_ids="603" visible="1" />""");
        Assert.That(Updates().Select(update => (update.Id, update.Visible)), Is.EqualTo(new[] { (603, true) }));
        Action("""<action name="set_effect" trigger_ids="603" visible="0" />""");
        Assert.That(Updates().Select(update => (update.Id, update.Visible)), Is.EqualTo(new[] { (603, false) }));
        Assert.That(Field.TriggerObjects.Effects[603].Visible, Is.False);
    }

    // set_actor (6514): visibility and the sequence the actor plays, sent with it.
    [Test]
    public void SetActor_VisibleWithSequence() {
        Start(new Ms2TriggerActor("Idle_A", 201, false));

        Action("""<action name="set_actor" trigger_id="201" visible="1" initial_sequence="Attack_01_A" />""");

        TriggerObjectActor actor = Field.TriggerObjects.Actors[201];
        Assert.That((actor.Visible, actor.SequenceName), Is.EqualTo((true, "Attack_01_A")));
        (int id, bool visible, ByteReader rest) = Updates().Single();
        Assert.That((id, visible, rest.ReadUnicodeString()), Is.EqualTo((201, true, "Attack_01_A")));
    }

    // select_camera (2517): the camera turns on; a camera the map lacks sends nothing.
    [Test]
    public void SelectCamera_TurnsTheCameraOn() {
        Start(new Ms2TriggerCamera(301, false));

        Action("""<action name="select_camera" trigger_id="301" enable="1" />""");
        Action("""<action name="select_camera" trigger_id="399" enable="1" />""");

        Assert.That(Field.TriggerObjects.Cameras[301].Visible, Is.True);
        Assert.That(Updates().Select(update => (update.Id, update.Visible)), Is.EqualTo(new[] { (301, true) }));
    }

    // select_camera_path (1815): the path ids as a count byte and ints, then the return-view flag (command 5).
    [Test]
    public void SelectCameraPath_SendsThePath() {
        Start();

        Action("""<action name="select_camera_path" path_ids="301,302,303" return_view="1" />""");

        ByteReader reader = Sent(SendOp.Trigger).Single();
        Assert.That((reader.ReadByte(), reader.ReadByte(), reader.ReadInt(), reader.ReadInt(), reader.ReadInt(), reader.ReadBool()),
            Is.EqualTo(((byte) 5, (byte) 3, 301, 302, 303, true)));
        Assert.That(reader.Available, Is.Zero);
    }

    // set_agent (2120): each agent the map has is set; the update carries its id only.
    [Test]
    public void SetAgent_UpdatesEachAgent() {
        Start(new Ms2TriggerAgent(401, true), new Ms2TriggerAgent(402, true));

        Action("""<action name="set_agent" trigger_ids="401,402,499" visible="0" />""");

        Assert.That(Field.TriggerObjects.Agents.Values.Select(agent => agent.Visible), Is.All.False);
        List<ByteReader> sent = Sent(SendOp.Trigger);
        Assert.That(sent.Select(reader => (reader.ReadByte(), reader.ReadInt(), reader.Available)),
            Is.EqualTo(new[] { ((byte) 3, 401, 0), ((byte) 3, 402, 0) }));
    }

    // set_ladder (2092): visibility, the climb animation and its delay.
    [Test]
    public void SetLadder_VisibleAnimated() {
        Start(new Ms2TriggerLadder(501, false), new Ms2TriggerLadder(502, false));

        Action("""<action name="set_ladder" trigger_ids="599,502" visible="1" enable="1" fade="7" />""");

        TriggerObjectLadder ladder = Field.TriggerObjects.Ladders[502];
        Assert.That((ladder.Visible, ladder.Animate, ladder.Delay), Is.EqualTo((true, true, 7)));
        Assert.That(Field.TriggerObjects.Ladders[501].Visible, Is.False);
        (int id, bool visible, ByteReader rest) = Updates().Single();
        Assert.That((id, visible, rest.ReadBool(), rest.ReadInt()), Is.EqualTo((502, true, true, 7)));
    }

    // set_cube (52): the named cubes that change take the visibility; one already in that state is not sent.
    [Test]
    public void SetCube_ChangedCubes() {
        Start(new Ms2TriggerCube(601, false), new Ms2TriggerCube(602, true));
        Field.TriggerObjects.Cubes[602].Visible = true;

        Action("""<action name="set_cube" trigger_ids="601,602,699" is_visible="1" />""");

        Assert.That(Field.TriggerObjects.Cubes[601].Visible, Is.True);
        (int id, bool visible, ByteReader _) = Updates().Single();
        Assert.That((id, visible), Is.EqualTo((601, true)));
    }

    // set_cube: random_count shows that many of the named cubes.
    [Test]
    public void SetCube_RandomCount() {
        Start(new Ms2TriggerCube(601, false), new Ms2TriggerCube(602, false), new Ms2TriggerCube(603, false));

        Action("""<action name="set_cube" trigger_ids="601,602,603" is_visible="1" random_count="2" />""");

        Assert.That(Field.TriggerObjects.Cubes.Values.Count(cube => cube.Visible), Is.EqualTo(2));
        Assert.That(Updates(), Has.Count.EqualTo(2));
    }

    // set_rope (60): visibility, the animation and its delay, as set_ladder.
    [Test]
    public void SetRope_VisibleAnimated() {
        Start(new Ms2TriggerRope(701, false));

        Action("""<action name="set_rope" trigger_id="701" visible="1" enable="1" fade="4" />""");
        Action("""<action name="set_rope" trigger_id="799" visible="1" />""");

        TriggerObjectRope rope = Field.TriggerObjects.Ropes[701];
        Assert.That((rope.Visible, rope.Animate, rope.Delay), Is.EqualTo((true, true, 4)));
        (int id, bool visible, ByteReader rest) = Updates().Single();
        Assert.That((id, visible, rest.ReadBool(), rest.ReadInt()), Is.EqualTo((701, true, true, 4)));
    }

    // set_sound (939): the sound object turns on.
    [Test]
    public void SetSound_TurnsTheSoundOn() {
        Start(new Ms2TriggerSound(701, false));

        Action("""<action name="set_sound" trigger_id="701" enable="1" />""");

        Assert.That(Field.TriggerObjects.Sounds[701].Visible, Is.True);
        Assert.That(Updates().Select(update => (update.Id, update.Visible)), Is.EqualTo(new[] { (701, true) }));
    }

    // set_mesh_animation (288): each named mesh that changes is sent once; one already in that state is not.
    [Test]
    public void SetMeshAnimation_ChangedMeshes() {
        Start(Mesh(1101), Mesh(1102, visible: true));

        Action("""<action name="set_mesh_animation" trigger_ids="1101,1102" visible="1" />""");

        Assert.That(Field.TriggerObjects.Meshes[1101].Visible, Is.True);
        Assert.That(Updates().Select(update => (update.Id, update.Visible)), Is.EqualTo(new[] { (1101, true) }));
    }

    // set_random_mesh (361): the named meshes change (in a random order).
    [Test]
    public void SetRandomMesh_ChangesTheMeshes() {
        Start(Mesh(1101), Mesh(1102));

        Action("""<action name="set_random_mesh" trigger_ids="1101,1102" visible="1" />""");

        Assert.That(Updates().Select(update => update.Id).Order(), Is.EqualTo(new[] { 1101, 1102 }));
        Assert.That(Field.TriggerObjects.Meshes.Values.Select(mesh => mesh.Visible), Is.All.True);
    }
}

public class TriggerTimerTests : TriggerTestBase {
    // set_timer (5570): the field keeps the timer; a shown one sends its dialog (start tick, duration in ms, auto
    // remove, offset, type).
    [Test]
    public void SetTimer_KeepsAndShowsTheTimer() {
        Start();

        Action("""<action name="set_timer" timer_id="1" seconds="30" auto_remove="1" display="1" v_offset="80" />""");

        TickTimer timer = Field.Timers["1"];
        Assert.That((timer.Duration, timer.AutoRemove, timer.Display, timer.VerticalOffset), Is.EqualTo((30000, true, true, 80)));
        ByteReader reader = Sent(SendOp.Trigger).Single();
        Assert.That(reader.ReadByte(), Is.EqualTo(15));
        reader.ReadByte();
        Assert.That((reader.ReadInt(), reader.ReadInt(), reader.ReadBool(), reader.ReadInt(), reader.ReadUnicodeString()),
            Is.EqualTo((timer.StartTick, 30000, true, 80, "")));
    }

    // A timer that is not shown sends nothing.
    [Test]
    public void SetTimer_Hidden() {
        Start();

        Action("""<action name="set_timer" timer_id="2" seconds="5" />""");

        Assert.That(Field.Timers["2"].Duration, Is.EqualTo(5000));
        Assert.That(Sent(SendOp.Trigger), Is.Empty);
    }

    // time_expired (6590): false while the timer runs, true once past it, and true for a timer never set.
    [Test]
    public void TimeExpired_ByTheTimer() {
        Start();
        Action("""<action name="set_timer" timer_id="1" seconds="30" />""");
        Field.Timers["2"] = new TickTimer(-1);

        Assert.That(Condition("""<condition name="time_expired" timer_id="1" />"""), Is.False);
        Assert.That(Condition("""<condition name="time_expired" timer_id="2" />"""), Is.True);
        Assert.That(Condition("""<condition name="time_expired" timer_id="9" />"""), Is.True);
    }

    // reset_timer (1629): the timer starts again from now and a shown one sends its dialog again.
    [Test]
    public void ResetTimer_StartsAgain() {
        Start();
        Action("""<action name="set_timer" timer_id="1" seconds="30" display="1" />""");
        TickTimer timer = Field.Timers["1"];
        typeof(TickTimer).GetProperty(nameof(TickTimer.StartTick))!.SetValue(timer, timer.StartTick - 60000);
        test.TakeSent();

        Action("""<action name="reset_timer" timer_id="1" />""");

        Assert.That(Condition("""<condition name="time_expired" timer_id="1" />"""), Is.False);
        Assert.That(Sent(SendOp.Trigger).Single().ReadByte(), Is.EqualTo(15));
    }
}

public class TriggerScriptConditionTests : TriggerTestBase {
    // wait_tick (24703): true once the state has run longer than the wait.
    [Test]
    public void WaitTick_AfterTheWait() {
        Start();
        context.StartTick = Environment.TickCount64 - 5000;

        Assert.That(Condition("""<condition name="wait_tick" wait_tick="1000" />"""), Is.True);
        Assert.That(Condition("""<condition name="wait_tick" wait_tick="60000" />"""), Is.False);
    }

    // random_condition (5821): the state's weights split one roll. Four rows of 25 (as in KR trigger 02000066_bf
    // alon.xml): a roll of 60 falls in the third share. 99 and 1: a roll of 99.5 takes the 1.
    [TestCase(60f, new[] { 25f, 25f, 25f, 25f }, 2)]
    [TestCase(10f, new[] { 25f, 25f, 25f, 25f }, 0)]
    [TestCase(99.5f, new[] { 99f, 1f }, 1)]
    [TestCase(50f, new[] { 99f, 1f }, 0)]
    [TestCase(50f, new[] { 50f, 50f }, 1)]
    public void RandomCondition_WeightsSplitTheRoll(float roll, float[] weights, int passing) {
        Start();
        SetRandom(roll);

        int first = Array.FindIndex(weights, weight => Condition($"""<condition name="random_condition" weight="{weight}" />"""));

        Assert.That(first, Is.EqualTo(passing));
    }

    // A pass in which no weight took the roll leaves it; the next pass rolls again.
    [Test]
    public void RandomCondition_NewPassRollsAgain() {
        Start();
        SetRandom(80f);
        Assert.That(Condition("""<condition name="random_condition" weight="5" />"""), Is.False);

        context.NewConditionPass();
        SetRandom(3f);
        Assert.That(Condition("""<condition name="random_condition" weight="5" />"""), Is.True);
    }
}

public class TriggerInteractTests : TriggerTestBase {
    private static InteractObjectMetadata Row(int id) {
        return new InteractObjectMetadata(id, InteractType.Mesh, Collection: 0, ReactCount: 0, TargetPortalId: 0, GuildPosterId: 0, WeaponItemId: 0,
            new InteractObjectMetadataItem(0, 0, 0, 0, 0), new InteractObjectMetadataTime(0, 0, 0),
            new InteractObjectMetadataDrop(0, [], [], 0, 0), new InteractObjectMetadataEffect([], [], 0, 0), []);
    }

    private FieldInteract Add(int id, string entityId) {
        return test.AddInteract(Row(id), new InteractMeshObject(entityId, new Ms2InteractMesh(id, Vector3.Zero, Vector3.Zero)));
    }

    // set_interact_object (9124) and object_interacted (6766): the named objects take the state, the others keep theirs;
    // the condition holds once any named object is in the state.
    [Test]
    public void SetInteractObject_ThenInteracted() {
        Start();
        FieldInteract first = Add(10001, "aa");
        FieldInteract second = Add(10002, "bb");
        FieldInteract other = Add(10003, "cc");
        test.TakeSent();

        Assert.That(Condition("""<condition name="object_interacted" interact_ids="10001,10002" state="0" />"""), Is.False);

        Action("""<action name="set_interact_object" trigger_ids="10001,10002" state="0" />""");

        Assert.That((first.State, second.State, other.State), Is.EqualTo((InteractState.Normal, InteractState.Normal, InteractState.Reactable)));
        Assert.That(Sent(SendOp.InteractObject), Has.Count.EqualTo(2));
        Assert.That(Condition("""<condition name="object_interacted" interact_ids="10001,10002" state="0" />"""), Is.True);
        Assert.That(Condition("""<condition name="object_interacted" interact_ids="10003" state="0" />"""), Is.False);
    }
}

public class TriggerCinematicTests : TriggerTestBase {
    // The Cinematic SetSkip packet (command 4): the label type (2 skip state, 1 skip scene, 0 none) and its string.
    private (byte Type, string Text) Skip() {
        ByteReader reader = Sent(SendOp.Cinematic).Single();
        Assert.That(reader.ReadByte(), Is.EqualTo(4));
        return (reader.ReadByte(), reader.ReadString());
    }

    // set_skip (2258 KR uses): a state to skip to shows the skip key; the empty state (1002 uses) takes it away, as
    // the client turns the skip off on type 0 (UICutSceneCinematic::FUN_14190a4c0).
    [Test]
    public void SetSkip_StateThenNone() {
        Start();

        Action("""<action name="set_skip" state="end" />""");
        Assert.That(Skip(), Is.EqualTo(((byte) 2, "")));
        Assert.That(context.TryGetSkip(out TriggerState? state), Is.True);

        Action("""<action name="set_skip" state="" />""");
        Assert.That(Skip(), Is.EqualTo(((byte) 0, "")));
        Assert.That(context.TryGetSkip(out state), Is.False);
    }

    // set_scene_skip (736): the scene label carries the action; the empty state takes the key away.
    [Test]
    public void SetSceneSkip_StateThenNone() {
        Start();

        Action("""<action name="set_scene_skip" state="end" action="exit" />""");
        Assert.That(Skip(), Is.EqualTo(((byte) 1, "exit")));
        Assert.That(context.TryGetSkip(out TriggerState? state), Is.True);

        Action("""<action name="set_scene_skip" />""");
        Assert.That(Skip(), Is.EqualTo(((byte) 0, "")));
        Assert.That(context.TryGetSkip(out state), Is.False);
    }

    // set_cinematic_ui: 1 puts the client in the cinematic key mode, 0 takes it out (command 1, a bool;
    // OnPacketKeyMode), 2 closes the script and direction (command 2), 3 the letterbox view (command 3: an int id 3, then the
    // script; OnPacketStartCinematic).
    [Test]
    public void SetCinematicUi_ByType() {
        Start();

        Action("""<action name="set_cinematic_ui" type="1" />""");
        Action("""<action name="set_cinematic_ui" type="0" />""");
        Action("""<action name="set_cinematic_ui" type="2" />""");
        Action("""<action name="set_cinematic_ui" type="3" script="abc" />""");

        List<ByteReader> sent = Sent(SendOp.Cinematic);
        Assert.That(sent, Has.Count.EqualTo(4));
        Assert.That((sent[0].ReadByte(), sent[0].ReadBool()), Is.EqualTo(((byte) 1, true)));
        Assert.That((sent[1].ReadByte(), sent[1].ReadBool()), Is.EqualTo(((byte) 1, false)));
        Assert.That(sent[2].ReadByte(), Is.EqualTo(2));
        Assert.That((sent[3].ReadByte(), sent[3].ReadInt(), sent[3].ReadUnicodeString()), Is.EqualTo(((byte) 3, 3, "abc")));
    }

    // set_onetime_effect: the effect id and switch, the path only when switched on.
    [Test]
    public void SetOnetimeEffect_PathWhenOn() {
        Start();

        Action("""<action name="set_onetime_effect" id="5" enable="1" path="BG/Common/Sound/Eff_Sound.xml" />""");
        Action("""<action name="set_onetime_effect" id="5" enable="0" path="BG/Common/Sound/Eff_Sound.xml" />""");

        List<ByteReader> sent = Sent(SendOp.OneTimeEffect);
        Assert.That(sent, Has.Count.EqualTo(2));
        Assert.That((sent[0].ReadInt(), sent[0].ReadBool(), sent[0].ReadUnicodeString()), Is.EqualTo((5, true, "BG/Common/Sound/Eff_Sound.xml")));
        Assert.That((sent[1].ReadInt(), sent[1].ReadBool(), sent[1].Available), Is.EqualTo((5, false, 0)));
    }

    // add_cinematic_talk: the client reads npc id, illustration (byte string), text, duration and the align byte
    // (UICutSceneCinematic::FUN_141909880).
    [Test]
    public void AddCinematicTalk_Talk() {
        Start();

        Action("""<action name="add_cinematic_talk" npc_id="11000064" illust_id="Lennon_normal" msg="hi" duration="3000" align="right" />""");

        ByteReader reader = Sent(SendOp.Cinematic).Single();
        Assert.That(reader.ReadByte(), Is.EqualTo(6));
        Assert.That((reader.ReadInt(), reader.ReadString(), reader.ReadUnicodeString(), reader.ReadInt()), Is.EqualTo((11000064, "Lennon_normal", "hi", 3000)));
        reader.ReadByte();
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}

public class TriggerFieldStateTests : TriggerTestBase {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // Box 1 holds the player at the origin, box 2 is far off.
    private static readonly Ms2TriggerBox Near = new(Vector3.Zero, new Vector3(300, 300, 300), 1, true);
    private static readonly Ms2TriggerBox Far = new(new Vector3(5000, 0, 0), new Vector3(300, 300, 300), 2, true);

    private void StartWithBoxes(params Ms2Trigger[] triggers) {
        Start([Near, Far, .. triggers]);
        test.Session.Player.Position = Vector3.Zero;
        test.Session.Player.TagId = 1;
        foreach (string name in new[] { "fieldPortals", "fieldBreakables", "fieldLiftables", "fieldSkills" }) {
            FieldInfo info = typeof(FieldManager).GetField(name, Any)!;
            info.SetValue(Field, Activator.CreateInstance(info.FieldType));
        }
        typeof(FieldManager).GetField("triggerBreakable", Any)!.SetValue(Field, new ConcurrentDictionary<int, FieldBreakable>());
    }

    // check_same_user_tag (128): the box holds a player and all of them carry one user tag; an empty box is false.
    [Test]
    public void CheckSameUserTag_PlayersInTheBox() {
        StartWithBoxes();

        Assert.That(Condition("""<condition name="check_same_user_tag" box_id="1" />"""), Is.True);
        Assert.That(Condition("""<condition name="check_same_user_tag" box_id="2" />"""), Is.False);
        Assert.That(Condition("""<condition name="check_same_user_tag" box_id="2" negate="1" />"""), Is.True);
    }

    // play_system_sound_in_box (4808): the players in the boxes hear it, and with no box everyone does.
    [Test]
    public void PlaySystemSoundInBox_PlayersInTheBoxes() {
        StartWithBoxes();

        Action("""<action name="play_system_sound_in_box" sound="System_Far_01" box_ids="2" />""");
        Assert.That(Sent(SendOp.PlaySystemSound), Is.Empty);

        Action("""<action name="play_system_sound_in_box" sound="System_Near_01" box_ids="2,1" />""");
        Assert.That(Sent(SendOp.PlaySystemSound).Single().ReadUnicodeString(), Is.EqualTo("System_Near_01"));

        Action("""<action name="play_system_sound_in_box" sound="System_All_01" />""");
        Assert.That(Sent(SendOp.PlaySystemSound).Single().ReadUnicodeString(), Is.EqualTo("System_All_01"));
    }

    // count_users (4025): the players in the box against the count (by default at least that many), a missing box
    // only passes negated, and a tag counts only the players with it.
    [Test]
    public void CountUsers_InTheBox() {
        StartWithBoxes();

        Assert.That(Condition("""<condition name="count_users" box_id="1" min_users="1" />"""), Is.True);
        Assert.That(Condition("""<condition name="count_users" box_id="1" min_users="2" />"""), Is.False);
        Assert.That(Condition("""<condition name="count_users" box_id="2" min_users="1" />"""), Is.False);
        Assert.That(Condition("""<condition name="count_users" box_id="2" min_users="1" negate="1" />"""), Is.True);
        Assert.That(Condition("""<condition name="count_users" box_id="9" min_users="0" />"""), Is.False);
        Assert.That(Condition("""<condition name="count_users" box_id="1" min_users="1" user_tag_id="2" />"""), Is.False);
        Assert.That(Condition("""<condition name="count_users" box_id="1" min_users="1" user_tag_id="1" />"""), Is.True);
    }

    // quest_user_detected (1844): a player in the boxes with the quest in a listed state (3 = completed).
    [Test]
    public void QuestUserDetected_CompletedInTheBox() {
        StartWithBoxes();
        test.SetQuest(100, QuestState.Completed);

        Assert.That(Condition("""<condition name="quest_user_detected" box_ids="1" quest_ids="100" quest_states="3" />"""), Is.True);
        Assert.That(Condition("""<condition name="quest_user_detected" box_ids="2" quest_ids="100" quest_states="3" />"""), Is.False);
        Assert.That(Condition("""<condition name="quest_user_detected" box_ids="1" quest_ids="101" quest_states="3" />"""), Is.False);
        Assert.That(Condition("""<condition name="quest_user_detected" box_ids="1" quest_ids="100" quest_states="3" negate="1" />"""), Is.False);
    }

    // detect_liftable_object (4257): a placed liftable of the item inside one of the boxes.
    [Test]
    public void DetectLiftableObject_ItemInTheBox() {
        StartWithBoxes();
        Field.AddLiftable("aa", new Liftable(30000611, 1, 0, 0, 0, "", "", "", "", false, new Vector3(10, 0, 0), Vector3.Zero));

        Assert.That(Condition("""<condition name="detect_liftable_object" box_ids="1" item_id="30000611" />"""), Is.True);
        Assert.That(Condition("""<condition name="detect_liftable_object" box_ids="2" item_id="30000611" />"""), Is.False);
        Assert.That(Condition("""<condition name="detect_liftable_object" box_ids="1" item_id="30000612" />"""), Is.False);
        Assert.That(Condition("""<condition name="detect_liftable_object" box_ids="1" item_id="30000611" negate="1" />"""), Is.False);
    }

    // set_portal (2292): the portal takes the switches and the field is told (FieldPortal Update: id, visible,
    // enabled, minimap).
    [Test]
    public void SetPortal_UpdatesThePortal() {
        StartWithBoxes();
        FieldPortal portal = Field.SpawnPortal(new Portal(3, 2000001, 1, PortalType.Field, PortalActionType.Touch, Vector3.Zero, Vector3.Zero,
            new Vector3(100, 100, 100), 0, 0, false, false, false));
        test.TakeSent();

        Action("""<action name="set_portal" portal_id="3" visible="1" enable="1" minimap_visible="0" />""");

        Assert.That((portal.Visible, portal.Enabled, portal.MinimapVisible), Is.EqualTo((true, true, false)));
        ByteReader reader = Sent(SendOp.FieldPortal).Single();
        Assert.That((reader.ReadByte(), reader.ReadInt(), reader.ReadBool(), reader.ReadBool(), reader.ReadBool()), Is.EqualTo(((byte) 2, 3, true, true, false)));
    }

    // set_breakable (2606): the breakables named by trigger id show or hide, sent in one batch.
    [Test]
    public void SetBreakable_ShowsByTriggerId() {
        StartWithBoxes();
        FieldBreakable breakable = Field.AddBreakable("bb", new BreakableActor(false, 7001, 1000, 1000, 0, Vector3.Zero, Vector3.Zero))!;
        test.TakeSent();

        Action("""<action name="set_breakable" trigger_ids="7001" enable="1" />""");

        Assert.That(breakable.State, Is.EqualTo(BreakableState.Show));
        // One Update (1) with the new state; the client replays a state's animation on every packet it gets.
        ByteReader shown = Sent(SendOp.Breakable).Single();
        Assert.That((shown.ReadByte(), shown.ReadString(), shown.ReadByte()), Is.EqualTo(((byte) 1, "bb", (byte) BreakableState.Show)));

        // Already shown: nothing is sent.
        Action("""<action name="set_breakable" trigger_ids="7001" enable="1" />""");
        Assert.That(Sent(SendOp.Breakable), Is.Empty);

        Action("""<action name="set_breakable" trigger_ids="7001" enable="0" />""");
        Assert.That(breakable.State, Is.EqualTo(BreakableState.Hide));
        Assert.That(Sent(SendOp.Breakable).Single().ReadByte(), Is.EqualTo(1));
    }

    // set_visible_breakable_object (1164): the named breakables take the flag, sent in one batch (0) of each with
    // its state and the flag.
    [Test]
    public void SetVisibleBreakableObject_Batch() {
        StartWithBoxes();
        FieldBreakable breakable = Field.AddBreakable("bb", new BreakableActor(false, 7001, 1000, 1000, 0, Vector3.Zero, Vector3.Zero))!;
        test.TakeSent();

        Action("""<action name="set_visible_breakable_object" trigger_ids="7001" visible="1" />""");

        Assert.That(breakable.Visible, Is.True);
        ByteReader batch = Sent(SendOp.Breakable).Single();
        Assert.That((batch.ReadByte(), batch.ReadInt(), batch.ReadString(), batch.ReadByte(), batch.ReadBool()),
            Is.EqualTo(((byte) 0, 1, "bb", (byte) breakable.State, true)));
    }

    // set_skill (36171): an enabled trigger skill is placed as a region skill of the field; disabled, it is taken
    // off again.
    [Test]
    public void SetSkill_AddsThenRemoves() {
        StartWithBoxes(new Ms2TriggerSkill(70000001, 1, new Vector3(10, 0, 0), Vector3.Zero, 801, false, 1));
        var skills = new SkillMetadataStorage(new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));
        object cache = typeof(SkillMetadataStorage).BaseType!.GetField("Cache", Any)!.GetValue(skills)!;
        var property = (SkillMetadataProperty) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataProperty));
        cache.GetType().GetMethod("AddReplace")!.Invoke(cache, [(70000001, (short) 1), new SkillMetadata(70000001, 1, null, property, null!, null!)]);
        typeof(FieldManager).GetField("<SkillMetadata>k__BackingField", Any)!.SetValue(Field, skills);
        var actor = (FieldActor) RuntimeHelpers.GetUninitializedObject(typeof(FieldActor));
        GC.SuppressFinalize(actor);
        typeof(FieldManager).GetField(nameof(FieldManager.FieldActor), Any)!.SetValue(Field, actor);

        Action("""<action name="set_skill" trigger_ids="801" enable="1" />""");
        ByteReader added = Sent(SendOp.RegionSkill).Single();
        Assert.That(added.ReadByte(), Is.EqualTo(0));
        int objectId = added.ReadInt();

        Action("""<action name="set_skill" trigger_ids="801" enable="0" />""");
        ByteReader removed = Sent(SendOp.RegionSkill).Single();
        Assert.That((removed.ReadByte(), removed.ReadInt()), Is.EqualTo(((byte) 1, objectId)));
    }
    // The effect is known to the field's skill data, and the field's own actor (the caster of trigger buffs) is a
    // real one.
    private void WithEffect(int id) {
        var skills = new SkillMetadataStorage(new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));
        object cache = typeof(SkillMetadataStorage).GetField("EffectCache", Any)!.GetValue(skills)!;
        // No begin condition: the effect is on once held.
        var always = new BeginCondition(0, 0, Gender.All, [], 1, 0, 0, 0, false, false, false, true, false, true, [],
            new Dictionary<BasicAttribute, long>(), [], [], [], [], null, null, null, null);
        cache.GetType().GetMethod("AddReplace")!.Invoke(cache, [(id, (short) 1), DisabledBuffTests.Effect(id) with {
            Condition = always,
            Reflect = new AdditionalEffectMetadataReflect(0, 0, 0, 0, 0, 0, new Dictionary<BasicAttribute, long>(), new Dictionary<BasicAttribute, float>()),
            Update = new AdditionalEffectMetadataUpdate(null, [], [], [], []),
            Consume = new AdditionalEffectMetadataConsume(0, 0),
        }]);
        typeof(FieldManager).GetField("<SkillMetadata>k__BackingField", Any)!.SetValue(Field, skills);
        typeof(FieldManager).GetField(nameof(FieldManager.FieldActor), Any)!.SetValue(Field, new FieldActor(Field, 99, Field.Metadata, null!));
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(test.Session.Player, Field);
    }

    // add_buff (977), remove_buff (173), check_any_user_additional_effect (86): the players in the boxes get the
    // effect, cast by the field; a far box gives none; remove_buff takes it off again.
    [Test]
    public void AddBuff_ThenCheckAndRemove() {
        StartWithBoxes();
        WithEffect(70000107);

        Action("""<action name="add_buff" box_ids="2" skill_id="70000107" level="1" ignore_player="0" />""");
        Assert.That(test.Session.Player.Buffs.HasBuff(70000107), Is.False);
        Assert.That(Condition("""<condition name="check_any_user_additional_effect" box_id="1" additional_effect_id="70000107" level="1" />"""), Is.False);

        Action("""<action name="add_buff" box_ids="2,1" skill_id="70000107" level="1" ignore_player="0" />""");
        Assert.That(test.Session.Player.Buffs.HasBuff(70000107), Is.True);
        Assert.That(Condition("""<condition name="check_any_user_additional_effect" box_id="1" additional_effect_id="70000107" level="1" />"""), Is.True);
        Assert.That(Condition("""<condition name="check_any_user_additional_effect" box_id="1" additional_effect_id="70000107" level="1" negate="1" />"""), Is.False);
        Assert.That(Condition("""<condition name="check_any_user_additional_effect" box_id="2" additional_effect_id="70000107" level="1" />"""), Is.False);

        // Removed from the far box: the player keeps it.
        Action("""<action name="remove_buff" box_id="2" skill_id="70000107" />""");
        Assert.That(test.Session.Player.Buffs.HasBuff(70000107), Is.True);
        Action("""<action name="remove_buff" box_id="1" skill_id="70000107" />""");
        Assert.That(test.Session.Player.Buffs.HasBuff(70000107), Is.False);
    }

    // remove_buff is_player (17 KR uses: 02020101, 02020111, 02020144, 02020145, 02020301, 52100301): each takes off an
    // effect the players hold, so the player in the box loses it too.
    [Test]
    public void RemoveBuff_IsPlayerTakesItFromThePlayers() {
        StartWithBoxes();
        WithEffect(70000107);
        Action("""<action name="add_buff" box_ids="1" skill_id="70000107" level="1" ignore_player="0" />""");
        Assert.That(test.Session.Player.Buffs.HasBuff(70000107), Is.True);

        Action("""<action name="remove_buff" box_id="2" skill_id="70000107" is_player="1" />""");
        Assert.That(test.Session.Player.Buffs.HasBuff(70000107), Is.True);
        Action("""<action name="remove_buff" box_id="1" skill_id="70000107" is_player="1" />""");
        Assert.That(test.Session.Player.Buffs.HasBuff(70000107), Is.False);
    }

    // remove_buff names the field as caster, which matters only for a casterIndividualEffect buff; none of the 44
    // buffs KR removes sets it (deathflower 62100021-62100023 come from an npc's random_additional_effect, mount
    // 73000004 from the player's own interact), so a buff another actor cast comes off too.
    [Test]
    public void RemoveBuff_CastByAnotherActor() {
        StartWithBoxes();
        WithEffect(62100021);
        test.Session.Player.Buffs.AddBuff(test.Session.Player, test.Session.Player, 62100021, 1, Field.FieldTick);
        Assert.That(test.Session.Player.Buffs.HasBuff(62100021), Is.True);

        Action("""<action name="remove_buff" box_id="1" skill_id="62100021" is_player="1" />""");
        Assert.That(test.Session.Player.Buffs.HasBuff(62100021), Is.False);
    }

    // add_buff: ignore_player gives the effect to the monsters in the boxes, not the players; the skill-set flag
    // gives nothing.
    [Test]
    public void AddBuff_IgnorePlayerOrSkillSet() {
        StartWithBoxes();
        WithEffect(70000107);
        typeof(FieldManager).GetField("<Mobs>k__BackingField", Any)!.SetValue(Field, new ConcurrentDictionary<int, FieldNpc>());

        Action("""<action name="add_buff" box_ids="1" skill_id="70000107" level="1" ignore_player="1" />""");
        Assert.That(test.Session.Player.Buffs.HasBuff(70000107), Is.False);
        Action("""<action name="add_buff" box_ids="1" skill_id="70000107" level="1" is_skill_set="1" />""");
        Assert.That(test.Session.Player.Buffs.HasBuff(70000107), Is.False);
    }

    // A map the client never played: the move is refused with a notice and the field-enter error.
    private void AddUnplayedMap(int id) {
        test.AddMap(TestGameSession.Map(id, notInPlayedClient: true) with {
            Limit = (MapMetadataLimit) RuntimeHelpers.GetUninitializedObject(typeof(MapMetadataLimit)),
        });
    }

    // move_user (974): the players in the box move to the map; the far box moves nobody. The field's own map with a
    // portal it lacks moves nobody either.
    [Test]
    public void MoveUser_PlayersInTheBox() {
        StartWithBoxes();
        AddUnplayedMap(99000001);

        Action("""<action name="move_user" map_id="99000001" portal_id="1" box_id="2" />""");
        Assert.That(test.TakeSentOps(), Is.Empty);

        Action("""<action name="move_user" map_id="99000001" portal_id="1" box_id="1" />""");
        Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.Notice, SendOp.RequestFieldEnter }));

        Action($"""<action name="move_user" map_id="{Field.MapId}" portal_id="9" box_id="1" />""");
        Assert.That(test.TakeSentOps(), Is.Empty);
    }

    // move_random_user (51): the field's own map goes to the portal there (a missing portal: no move, no field
    // change); another map is a field change of the players picked from the box.
    [Test]
    public void MoveRandomUser_OwnMapStaysInTheField() {
        StartWithBoxes();
        AddUnplayedMap(99000001);

        Action($"""<action name="move_random_user" map_id="{Field.MapId}" portal_id="9" box_id="1" count="1" />""");
        Assert.That(test.TakeSentOps(), Is.Empty);

        Action("""<action name="move_random_user" map_id="99000001" portal_id="1" box_id="2" count="1" />""");
        Assert.That(test.TakeSentOps(), Is.Empty);
        Action("""<action name="move_random_user" map_id="99000001" portal_id="1" box_id="1" count="5" />""");
        Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.Notice, SendOp.RequestFieldEnter }));
    }

    // play_system_sound_by_user_tag (46): the players of the tag hear it.
    [Test]
    public void PlaySystemSoundByUserTag_TaggedPlayers() {
        StartWithBoxes();

        Action("""<action name="play_system_sound_by_user_tag" user_tag_id="2" sound_key="System_Tag_01" />""");
        Assert.That(Sent(SendOp.PlaySystemSound), Is.Empty);
        Action("""<action name="play_system_sound_by_user_tag" user_tag_id="1" sound_key="System_Tag_01" />""");
        Assert.That(Sent(SendOp.PlaySystemSound).Single().ReadUnicodeString(), Is.EqualTo("System_Tag_01"));
    }

    // show_event_result (42): a notice is a MassiveEvent Banner (2) of type Text (6) with the text and duration, for
    // the players in the box (or outside it with is_outside); the other types send nothing.
    [Test]
    public void ShowEventResult_NoticeInTheBox() {
        StartWithBoxes();

        Action("""<action name="show_event_result" type="notice" text="$t$" duration="3000" trigger_box_id="1" />""");
        ByteReader banner = Sent(SendOp.MassiveEvent).Single();
        Assert.That((banner.ReadByte(), banner.ReadByte(), banner.ReadUnicodeString(), banner.ReadInt()), Is.EqualTo(((byte) 2, (byte) 6, "$t$", 3000)));

        Action("""<action name="show_event_result" type="notice" text="$t$" duration="3000" trigger_box_id="1" is_outside="1" />""");
        Assert.That(Sent(SendOp.MassiveEvent), Is.Empty);
        Action("""<action name="show_event_result" type="notice" text="$t$" duration="3000" trigger_box_id="2" />""");
        Assert.That(Sent(SendOp.MassiveEvent), Is.Empty);
        Action("""<action name="show_event_result" type="notice" text="$t$" duration="3000" user_tag_id="2" />""");
        Assert.That(Sent(SendOp.MassiveEvent), Is.Empty);
        Action("""<action name="show_event_result" type="winner" text="$t$" duration="3000" />""");
        Assert.That(Sent(SendOp.MassiveEvent), Is.Empty);
    }
}

public class TriggerNpcTests : TriggerTestBase {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private ConcurrentDictionary<int, FieldNpc> mobs = null!;

    [SetUp]
    public void SetUpNpcs() {
        Start();
        mobs = new ConcurrentDictionary<int, FieldNpc>();
        typeof(FieldManager).GetField("<Mobs>k__BackingField", Any)!.SetValue(Field, mobs);
        // The test field skips the FieldManager ctor: the npc effect table a removal clears is made here.
        typeof(FieldManager).GetField(nameof(FieldManager.NpcEffects), Any)!.SetValue(Field,
            Activator.CreateInstance(typeof(FieldManager).GetField(nameof(FieldManager.NpcEffects), Any)!.FieldType));
        test.TakeSent();
    }

    // A monster of the spawn point; only what the trigger code reads is set.
    private FieldNpc Mob(int objectId, int spawnPointId, bool dead = false, AnimationMetadata? animation = null) {
        var npc = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        GC.SuppressFinalize(npc);
        typeof(Actor<Npc>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(npc, objectId);
        typeof(Actor<Npc>).GetField("<IsDead>k__BackingField", Any)!.SetValue(npc, dead);
        FieldInfo summons = typeof(FieldNpc).GetField("summons", Any)!;
        summons.SetValue(npc, Activator.CreateInstance(summons.FieldType));
        typeof(Actor<Npc>).GetField("<Field>k__BackingField", Any)!.SetValue(npc, Field);
        var metadata = (NpcMetadata) RuntimeHelpers.GetUninitializedObject(typeof(NpcMetadata));
        typeof(NpcMetadata).GetField("<Property>k__BackingField", Any)!.SetValue(metadata,
            new NpcMetadataProperty([], new NpcMetadataCapsule(10, 100), null));
        typeof(Actor<Npc>).GetField("<Value>k__BackingField", Any)!.SetValue(npc, new Npc(metadata, animation));
        typeof(FieldNpc).GetField(nameof(FieldNpc.AiExtraData), Any)!.SetValue(npc, new ConcurrentDictionary<string, int>());
        typeof(FieldNpc).GetField(nameof(FieldNpc.BattleState), Any)!.SetValue(npc, new BattleState(npc));
        typeof(Actor<Npc>).GetField("<Transform>k__BackingField", Any)!.SetValue(npc, new Transform());
        typeof(Actor<Npc>).GetField("Logger", Any)!.SetValue(npc, Log.Logger);
        npc.SpawnPointId = spawnPointId;
        mobs[objectId] = npc;
        return npc;
    }

    // monster_dead (4177): true once every monster of the spawn points is dead (none left counts as dead).
    [Test]
    public void MonsterDead_AllOfTheSpawnPoints() {
        Mob(9001, 5);
        Mob(9002, 6, dead: true);

        Assert.That(Condition("""<condition name="monster_dead" spawn_ids="6" />"""), Is.True);
        Assert.That(Condition("""<condition name="monster_dead" spawn_ids="5,6" />"""), Is.False);
        Assert.That(Condition("""<condition name="monster_dead" spawn_ids="7" />"""), Is.True);
        Assert.That(Condition("""<condition name="monster_dead" spawn_ids="5" negate="1" />"""), Is.True);
    }

    // destroy_monster (4237): the monsters of the listed spawn points leave the field (-1: all of them).
    [Test]
    public void DestroyMonster_BySpawnPoint() {
        Mob(9001, 5);
        Mob(9002, 6);
        Mob(9003, 7);

        Action("""<action name="destroy_monster" spawn_ids="5,6" />""");
        Field.Scheduler.InvokeAll();
        Assert.That(mobs.Keys, Is.EqualTo(new[] { 9003 }));
        Assert.That(Sent(SendOp.FieldRemoveNpc).Select(r => r.ReadInt()).Order(), Is.EqualTo(new[] { 9001, 9002 }));

        Mob(9004, 8);
        Action("""<action name="destroy_monster" spawn_ids="-1" />""");
        Field.Scheduler.InvokeAll();
        Assert.That(mobs, Is.Empty);
    }

    // move_npc (3710): the npcs of the spawn point take the map's patrol of that name as a script path.
    [Test]
    public void MoveNpc_TakesTheNamedPatrol() {
        var patrol = new MS2PatrolData("uuid", "MS2PatrolData_101", false, 1, false, []);
        typeof(MapEntityMetadata).GetField("<Patrols>k__BackingField", Any)!.SetValue(Field.Entities, new List<MS2PatrolData> { patrol });
        FieldNpc moved = Mob(9001, 5);
        FieldNpc other = Mob(9002, 6);

        Action("""<action name="move_npc" spawn_id="5" patrol_name="MS2PatrolData_102" />""");
        Assert.That(moved.Patrol, Is.Null);

        Action("""<action name="move_npc" spawn_id="5" patrol_name="MS2PatrolData_101" />""");
        Assert.That((moved.Patrol, moved.OnScriptPath, other.Patrol), Is.EqualTo((patrol, true, (MS2PatrolData?) null)));
    }

    // npc_to_patrol_in_box (96): each npc of npc_id in the box takes the path (spawn_id names where it came from and
    // picks nothing); another npc id, an npc out of the box, a path the map lacks and box 0 move nothing.
    [Test]
    public void NpcToPatrolInBox_TheNpcIdInTheBox() {
        SetTriggers(new Ms2TriggerBox(Vector3.Zero, new Vector3(300, 300, 300), 1, true),
            new Ms2TriggerBox(new Vector3(5000, 0, 0), new Vector3(300, 300, 300), 2, true));
        var patrol = new MS2PatrolData("uuid", "MS2PatrolData_101", false, 1, false, []);
        typeof(MapEntityMetadata).GetField("<Patrols>k__BackingField", Any)!.SetValue(Field.Entities, new List<MS2PatrolData> { patrol });
        context = NewContext();
        FieldNpc boat = Mob(9001, 5);
        FieldNpc other = Mob(9002, 5);
        FieldNpc far = Mob(9003, 5);
        far.Position = new Vector3(5000, 0, 0);
        foreach ((FieldNpc npc, int id) in new[] { (boat, 21000001), (other, 21000002), (far, 21000001) }) {
            typeof(NpcMetadata).GetField("<Id>k__BackingField", Any)!.SetValue(npc.Value.Metadata, id);
        }

        Action("""<action name="npc_to_patrol_in_box" box_id="0" npc_id="21000001" spawn_id="interactObject" patrol_name="MS2PatrolData_101" />""");
        Action("""<action name="npc_to_patrol_in_box" box_id="1" npc_id="21000001" spawn_id="interactObject" patrol_name="MS2PatrolData_102" />""");
        Assert.That(boat.Patrol, Is.Null);

        Action("""<action name="npc_to_patrol_in_box" box_id="1" npc_id="21000001" spawn_id="interactObject" patrol_name="MS2PatrolData_101" />""");
        Assert.That((boat.Patrol, boat.OnScriptPath, other.Patrol, far.Patrol), Is.EqualTo((patrol, true, (MS2PatrolData?) null, (MS2PatrolData?) null)));
    }

    // spawn_monster (8884): with a delay each spawn point's spawn waits on the script's queue (the spawn itself
    // needs the field's navmesh, which the test field has none of).
    [Test]
    public void SpawnMonster_DelayedSpawnsQueue() {
        Action("""<action name="spawn_monster" spawn_ids="101,102" delay="1000" />""");
        Assert.That(context.Events.Count, Is.EqualTo(2));
    }

    // set_dialogue (3571): type 1 is a balloon over the spawn point's npc, type 2 a cinematic talk of the npc id;
    // time is in seconds.
    [Test]
    public void SetDialogue_BalloonOrTalk() {
        Mob(9001, 5);

        Action("""<action name="set_dialogue" type="1" spawn_id="5" script="abc" time="3" />""");
        ByteReader balloon = Sent(SendOp.Cinematic).Single();
        Assert.That((balloon.ReadByte(), balloon.ReadByte(), balloon.ReadInt(), balloon.ReadUnicodeString(), balloon.ReadInt()),
            Is.EqualTo(((byte) 8, (byte) 0, 9001, "abc", 3000)));

        Action("""<action name="set_dialogue" type="1" spawn_id="6" script="abc" time="3" />""");
        Assert.That(Sent(SendOp.Cinematic), Is.Empty);

        Action("""<action name="set_dialogue" type="2" spawn_id="11000001" script="def" time="2" />""");
        ByteReader talk = Sent(SendOp.Cinematic).Single();
        Assert.That((talk.ReadByte(), talk.ReadInt(), talk.ReadString(), talk.ReadUnicodeString(), talk.ReadInt()),
            Is.EqualTo(((byte) 6, 11000001, "11000001", "def", 2000)));
    }

    // add_balloon_talk (584): a balloon of the Trigger table over the spawn point's npc, with the duration and delay in
    // ms; spawn id 0 (94 of the KR uses) puts it over each player's own character. A spawn point without an npc sends
    // nothing.
    [Test]
    public void AddBalloonTalk_OverTheNpcOrThePlayer() {
        Mob(9001, 5);

        Action("""<action name="add_balloon_talk" spawn_id="5" msg="abc" duration="3000" delay_tick="500" />""");
        ByteReader balloon = Sent(SendOp.Cinematic).Single();
        Assert.That((balloon.ReadByte(), balloon.ReadByte(), balloon.ReadInt(), balloon.ReadUnicodeString(), balloon.ReadInt(), balloon.ReadInt()),
            Is.EqualTo(((byte) 8, (byte) 0, 9001, "abc", 3000, 500)));

        Action("""<action name="add_balloon_talk" spawn_id="6" msg="abc" duration="3000" />""");
        Assert.That(Sent(SendOp.Cinematic), Is.Empty);

        Action("""<action name="add_balloon_talk" spawn_id="0" msg="def" duration="2000" />""");
        ByteReader own = Sent(SendOp.Cinematic).Single();
        Assert.That((own.ReadByte(), own.ReadByte(), own.ReadInt(), own.ReadUnicodeString(), own.ReadInt(), own.ReadInt()),
            Is.EqualTo(((byte) 8, (byte) 0, test.Session.Player.ObjectId, "def", 2000, 0)));
    }

    // npc_detected (1413): an npc of the spawn points stands in the box; box 0 is the whole field and spawn id 0
    // any npc. An npc out of the box is not seen.
    [Test]
    public void NpcDetected_InTheBox() {
        SetTriggers(new Ms2TriggerBox(Vector3.Zero, new Vector3(300, 300, 300), 1, true),
            new Ms2TriggerBox(new Vector3(5000, 0, 0), new Vector3(300, 300, 300), 2, true));
        Mob(9001, 5);
        Mob(9002, 6).Position = new Vector3(5000, 0, 0);

        Assert.That(Condition("""<condition name="npc_detected" box_id="1" spawn_ids="5" />"""), Is.True);
        Assert.That(Condition("""<condition name="npc_detected" box_id="1" spawn_ids="6" />"""), Is.False);
        Assert.That(Condition("""<condition name="npc_detected" box_id="2" spawn_ids="0" />"""), Is.True);
        Assert.That(Condition("""<condition name="npc_detected" box_id="0" spawn_ids="6" />"""), Is.True);
        Assert.That(Condition("""<condition name="npc_detected" box_id="1" spawn_ids="6" negate="1" />"""), Is.True);
    }

    // add_effect_nif (1353), remove_effect_nif: the spawn point's npcs get the nif (kept for a later entrant) and
    // the Trigger AddEffect (17), then lose it with RemoveEffect (18).
    [Test]
    public void EffectNif_AddsThenRemoves() {
        Mob(9001, 5);
        Mob(9002, 6);

        Action("""<action name="add_effect_nif" spawn_id="5" nif_path="Eff/a.nif" is_outline="1" scale="1.5" rotate_z="90" />""");
        Assert.That(Field.NpcEffects.Keys, Is.EqualTo(new[] { 9001 }));
        ByteReader added = Sent(SendOp.Trigger).Single();
        Assert.That((added.ReadByte(), added.ReadInt(), added.ReadString(), added.ReadBool(), added.ReadFloat(), added.ReadFloat()),
            Is.EqualTo(((byte) 17, 9001, "Eff/a.nif", true, 1.5f, 90f)));

        Action("""<action name="remove_effect_nif" spawn_id="5" />""");
        Assert.That(Field.NpcEffects, Is.Empty);
        ByteReader removed = Sent(SendOp.Trigger).Single();
        Assert.That((removed.ReadByte(), removed.ReadInt()), Is.EqualTo(((byte) 18, 9001)));
    }

    // set_ai_extra_data (166): the value is set on the npcs in the box (0: all), or added to the old one with
    // is_modify.
    [Test]
    public void SetAiExtraData_SetsOrAdds() {
        FieldNpc npc = Mob(9001, 5);

        Action("""<action name="set_ai_extra_data" key="Phase" value="2" />""");
        Action("""<action name="set_ai_extra_data" key="Phase" value="3" is_modify="1" />""");
        Action("""<action name="set_ai_extra_data" key="Other" value="4" is_modify="1" />""");
        Assert.That((npc.AiExtraData["Phase"], npc.AiExtraData["Other"]), Is.EqualTo((5, 4)));
    }

    // monster_in_combat (151): a monster of the spawn points is in battle.
    [Test]
    public void MonsterInCombat_BySpawnPoint() {
        Mob(9001, 5).BattleState.KeepBattle = true;
        Mob(9002, 6);

        Assert.That(Condition("""<condition name="monster_in_combat" spawn_ids="5" />"""), Is.True);
        Assert.That(Condition("""<condition name="monster_in_combat" spawn_ids="6" />"""), Is.False);
        Assert.That(Condition("""<condition name="monster_in_combat" spawn_ids="6" negate="1" />"""), Is.True);
    }

    // remove_balloon_talk (134): Cinematic RemoveBalloonTalk (9) for the spawn point's npcs; spawn 0 also clears
    // the players' own balloons.
    [Test]
    public void RemoveBalloonTalk_BySpawnPoint() {
        Mob(9001, 5);
        Mob(9002, 6);

        Action("""<action name="remove_balloon_talk" spawn_id="5" />""");
        ByteReader removed = Sent(SendOp.Cinematic).Single();
        Assert.That((removed.ReadByte(), removed.ReadInt()), Is.EqualTo(((byte) 9, 9001)));

        Action("""<action name="remove_balloon_talk" spawn_id="0" />""");
        List<int> ids = Sent(SendOp.Cinematic).Select(reader => {
            reader.ReadByte();
            return reader.ReadInt();
        }).ToList();
        Assert.That(ids, Is.EquivalentTo(new[] { 9001, 9002, test.Session.Player.ObjectId }));
    }

    // set_npc_duel_hp_bar (123): Trigger DuelHpBar (21) opens on the spawn point's npc with the time and hp step;
    // the close is the flag alone (CTriggerService::OnNpcDuelHpBar), sent when the npc is already gone.
    [Test]
    public void SetNpcDuelHpBar_OpenThenCloseWithoutTheNpc() {
        // The npc lookup also reads the field's pets, whose table the skipped ctor did not make.
        typeof(FieldManager).GetField("<Pets>k__BackingField", Any)!.SetValue(Field, new ConcurrentDictionary<int, FieldPet>());
        Mob(9001, 5);

        Action("""<action name="set_npc_duel_hp_bar" is_open="1" spawn_id="5" duration_tick="300000" npc_hp_step="100" />""");
        ByteReader opened = Sent(SendOp.Trigger).Single();
        Assert.That((opened.ReadByte(), opened.ReadBool(), opened.ReadInt(), opened.ReadInt(), opened.ReadInt()),
            Is.EqualTo(((byte) 21, true, 9001, 300000, 100)));

        Action("""<action name="set_npc_duel_hp_bar" is_open="1" spawn_id="6" duration_tick="300000" npc_hp_step="100" />""");
        Assert.That(Sent(SendOp.Trigger), Is.Empty);

        mobs.Clear();
        Action("""<action name="set_npc_duel_hp_bar" is_open="0" spawn_id="5" />""");
        ByteReader closed = Sent(SendOp.Trigger).Single();
        Assert.That((closed.ReadByte(), closed.ReadBool(), closed.Available), Is.EqualTo(((byte) 21, false, 0)));
    }

    // set_npc_rotation (49): every npc of the spawn point turns to the angle (z, degrees); others keep theirs.
    [Test]
    public void SetNpcRotation_TheSpawnPoint() {
        typeof(FieldManager).GetField("<Npcs>k__BackingField", Any)!.SetValue(Field, new ConcurrentDictionary<int, FieldNpc>());
        FieldNpc turned = Mob(9001, 5);
        FieldNpc other = Mob(9002, 6);
        Action("""<action name="set_npc_rotation" spawn_id="5" rotation="90" />""");
        Assert.That(turned.Rotation.Z, Is.EqualTo(90).Within(1e-3));
        Assert.That(other.Rotation.Z, Is.EqualTo(0).Within(1e-3));
    }

    // The npc lookups by spawn point (EnumerateNpcs) also read the field's npc table, which the skipped ctor did not make.
    private void WithNpcTable() {
        typeof(FieldManager).GetField("<Npcs>k__BackingField", Any)!.SetValue(Field, new ConcurrentDictionary<int, FieldNpc>());
    }

    // The npc's hp: the stats hold only the health stat, at current of total.
    private static void Health(FieldNpc npc, long current, long total) {
        var stats = (StatsManager) RuntimeHelpers.GetUninitializedObject(typeof(StatsManager));
        typeof(StatsManager).GetField("<Values>k__BackingField", Any)!.SetValue(stats, new Stats(new Dictionary<BasicAttribute, long>(), 0));
        stats.Values[BasicAttribute.Health].Total = total;
        stats.Values[BasicAttribute.Health].Current = current;
        typeof(Actor<Npc>).GetField("<Stats>k__BackingField", Any)!.SetValue(npc, stats);
    }

    // npc_hp (99): the hp of the spawn point's npc in percent of its max (is_relative, every shipped use) or in
    // points, against the value; no npc of the spawn point fails, and a negative hp counts as 0.
    [Test]
    public void NpcHp_PercentOrPoints() {
        WithNpcTable();
        Health(Mob(9001, 5), 250, 1000);

        Assert.That(Condition("""<condition name="npc_hp" spawn_id="5" is_relative="1" value="25" compare="lowerEqual" />"""), Is.True);
        Assert.That(Condition("""<condition name="npc_hp" spawn_id="5" is_relative="1" value="24" compare="lowerEqual" />"""), Is.False);
        Assert.That(Condition("""<condition name="npc_hp" spawn_id="5" is_relative="1" value="25" compare="lower" />"""), Is.False);
        Assert.That(Condition("""<condition name="npc_hp" spawn_id="5" is_relative="1" value="20" compare="higher" />"""), Is.True);
        Assert.That(Condition("""<condition name="npc_hp" spawn_id="5" is_relative="1" value="25" compare="higherEqual" />"""), Is.True);
        Assert.That(Condition("""<condition name="npc_hp" spawn_id="5" is_relative="0" value="250" compare="lowerEqual" />"""), Is.True);
        Assert.That(Condition("""<condition name="npc_hp" spawn_id="5" is_relative="0" value="249" compare="lowerEqual" />"""), Is.False);
        Assert.That(Condition("""<condition name="npc_hp" spawn_id="6" is_relative="1" value="100" compare="lowerEqual" />"""), Is.False);

        Health(Mob(9002, 7), -40, 1000);
        Assert.That(Condition("""<condition name="npc_hp" spawn_id="7" is_relative="0" value="0" compare="higherEqual" />"""), Is.True);
        Assert.That(Condition("""<condition name="npc_hp" spawn_id="7" is_relative="0" value="0" compare="lower" />"""), Is.False);
    }

    // A monster that can take an emote task: its model has the sequences, and the task queue is its own.
    private FieldNpc Emoting(int objectId, int spawnPointId, params string[] sequences) {
        var animation = new AnimationMetadata("model", sequences.Select((name, i) => new AnimationSequenceMetadata(name, (short) (i + 1), 1f, []))
            .ToDictionary(sequence => sequence.Name));
        FieldNpc npc = Mob(objectId, spawnPointId, animation: animation);
        object movement = RuntimeHelpers.GetUninitializedObject(typeof(MovementState));
        typeof(MovementState).GetField("actor", Any)!.SetValue(movement, npc);
        typeof(FieldNpc).GetField(nameof(FieldNpc.MovementState), Any)!.SetValue(npc, movement);
        typeof(FieldNpc).GetField(nameof(FieldNpc.TaskState), Any)!.SetValue(npc, new TaskState(npc));
        return npc;
    }

    private static MovementState.NpcEmoteTask? EmoteTask(FieldNpc npc) {
        return typeof(FieldNpc).GetField("idleTask", Any)!.GetValue(npc) as MovementState.NpcEmoteTask;
    }

    // set_npc_emotion_loop (605): the spawn point's npc plays the sequence for the duration (ms); a sequence its
    // model lacks plays nothing.
    [Test]
    public void SetNpcEmotionLoop_PlaysForTheDuration() {
        WithNpcTable();
        FieldNpc npc = Emoting(9001, 5, "Talk_A");
        FieldNpc other = Emoting(9002, 6, "Talk_A");

        Action("""<action name="set_npc_emotion_loop" spawn_id="5" sequence_name="Bore_A" duration="4000" />""");
        Assert.That(EmoteTask(npc), Is.Null);

        Action("""<action name="set_npc_emotion_loop" spawn_id="5" sequence_name="Talk_A" duration="4000" />""");
        MovementState.NpcEmoteTask task = EmoteTask(npc)!;
        Assert.That((task.Sequence, typeof(MovementState.NpcEmoteTask).GetProperty("Duration", Any)!.GetValue(task)), Is.EqualTo(("Talk_A", 4000f)));
        Assert.That(EmoteTask(other), Is.Null);
    }

    // set_npc_emotion_sequence (700): the spawn point's npc plays the names of the list once each, in order; a name its
    // model lacks is left out.
    [Test]
    public void SetNpcEmotionSequence_PlaysTheList() {
        WithNpcTable();
        FieldNpc npc = Emoting(9001, 5, "Bore_A", "ChatUp_A");

        Action("""<action name="set_npc_emotion_sequence" spawn_id="5" sequence_name="Bore_A,Dance_A,ChatUp_A" duration_tick="2400" />""");
        MovementState.NpcEmoteTask task = EmoteTask(npc)!;
        Assert.That(task.Sequence, Is.EqualTo("Bore_A"));
        Assert.That((task.TryNext(out string next), next), Is.EqualTo((true, "ChatUp_A")));
        Assert.That(task.TryNext(out _), Is.False);

        Action("""<action name="set_npc_emotion_sequence" spawn_id="6" sequence_name="Bore_A" duration_tick="2400" />""");
        Assert.That(EmoteTask(npc), Is.SameAs(task));
    }

    // change_monster (222): the npcs of the first spawn point leave the field and the second one spawns; with no such
    // spawn point on the map (the test map has none) only the warning is left.
    [Test]
    public void ChangeMonster_RemovesThenSpawns() {
        WithNpcTable();
        var entities = Field.Entities;
        typeof(MapEntityMetadata).GetField("<NpcSpawns>k__BackingField", Any)!.SetValue(entities, new List<SpawnPointNPC>());
        typeof(MapEntityMetadata).GetField("<EventNpcSpawns>k__BackingField", Any)!.SetValue(entities, new Dictionary<int, EventSpawnPointNPC>());
        var warnings = new LogSink();
        typeof(TriggerContext).GetField("logger", Any)!.SetValue(context, new LoggerConfiguration().WriteTo.Sink(warnings).CreateLogger());
        Mob(9001, 5);
        Mob(9002, 5);
        Mob(9003, 6);

        Action("""<action name="change_monster" from_spawn_id="5" to_spawn_id="7" />""");
        Field.Scheduler.InvokeAll();
        Assert.That(mobs.Keys, Is.EqualTo(new[] { 9003 }));
        Assert.That(Sent(SendOp.FieldRemoveNpc).Select(r => r.ReadInt()).Order(), Is.EqualTo(new[] { 9001, 9002 }));
        Assert.That(warnings.Events.Where(e => e.Level == LogEventLevel.Warning).Select(e => e.Properties["SpawnId"].ToString()),
            Is.EqualTo(new[] { "7" }));
    }

    private sealed class LogSink : ILogEventSink {
        public readonly List<LogEvent> Events = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    // check_npc_additional_effect (51): a monster of the spawn point holds the effect at the level or above.
    [Test]
    public void CheckNpcAdditionalEffect_HeldBySpawnPoint() {
        typeof(FieldManager).GetField("<Npcs>k__BackingField", Any)!.SetValue(Field, new ConcurrentDictionary<int, FieldNpc>());
        typeof(FieldManager).GetField("<Pets>k__BackingField", Any)!.SetValue(Field, new ConcurrentDictionary<int, FieldPet>());
        FieldNpc mob = Mob(9001, 5);
        typeof(Actor<Npc>).GetField("<Buffs>k__BackingField", Any)!.SetValue(mob, new BuffManager(mob));
        var held = (ConcurrentDictionary<int, List<Buff>>) typeof(BuffManager).GetField("buffs", Any)!.GetValue(mob.Buffs)!;
        held[70000107] = [DisabledBuffTests.Held(DisabledBuffTests.Effect(70000107), true)];

        Assert.That(Condition("""<condition name="check_npc_additional_effect" spawn_id="5" additional_effect_id="70000107" level="1" />"""), Is.True);
        Assert.That(Condition("""<condition name="check_npc_additional_effect" spawn_id="5" additional_effect_id="70000107" level="2" />"""), Is.False);
        Assert.That(Condition("""<condition name="check_npc_additional_effect" spawn_id="6" additional_effect_id="70000107" level="1" />"""), Is.False);
        Assert.That(Condition("""<condition name="check_npc_additional_effect" spawn_id="6" additional_effect_id="70000107" level="1" negate="1" />"""), Is.True);
    }
}

public class TriggerItemTests : TriggerTestBase {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int Coin = 30000001;
    private const int Potion = 30000002;
    private const int BoxId = 900002;
    // KR 52000066 spawn point 300: box 310000175 holds one quest-only row, 30000023 for quest 60100070.
    private const int QuestBoxId = 310000175;
    private const int QuestItem = 30000023;
    private const int QuestId = 60100070;
    // One group of drop count 1 that mixes the quest-only row with a plain one, each of weight 50.
    private const int MixedBoxId = 900003;

    // A field whose map has item spawn point 9001 (box BoxId, 30 s), 9002 (no box, no lifetime), 9004 (QuestBoxId) and
    // 9005 (MixedBoxId).
    [SetUp]
    public void SetUpItems() {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(Coin), TestGameSession.Metadata(Potion), TestGameSession.Metadata(QuestItem));
        IndividualDropItemTable.Entry.DropCount[] always = [new(1, 100)];
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>> {
            [BoxId] = TestGameSession.DropBox([(Coin, 3)]),
            [QuestBoxId] = new Dictionary<int, IndividualDropItemTable.Entry> {
                [1] = new(1, 0, always, 0, false, false, [
                    new([QuestItem, 0], false, 0, 0, 0, new IndividualDropItemTable.Range<int>(1, 1), [], 0, 0, false, false, false, false, [], true),
                ]),
            },
            [MixedBoxId] = new Dictionary<int, IndividualDropItemTable.Entry> {
                [1] = new(1, 0, always, 0, false, false, [
                    new([QuestItem, 0], false, 0, 0, 50, new IndividualDropItemTable.Range<int>(1, 1), [], 0, 0, false, false, false, false, [], true),
                    new([Coin, 0], false, 0, 0, 50, new IndividualDropItemTable.Range<int>(1, 1), [], 0, 0, false, false, false, false, [], false),
                ]),
            },
        }));
        test.Start();
        SetTriggers();
        typeof(MapEntityMetadata).GetField("<EventItemSpawns>k__BackingField", Any)!.SetValue(Field.Entities, new Dictionary<int, EventSpawnPointItem> {
            [9001] = new(9001, new Vector3(100, 200, 300), Vector3.Zero, 30, BoxId, 0, 0, true),
            [9002] = new(9002, new Vector3(-100, 0, 0), Vector3.Zero, 0, 0, 0, 0, true),
            [9004] = new(9004, new Vector3(6800, -1125, 4950), Vector3.Zero, 0, QuestBoxId, 0, 0, true),
            [9005] = new(9005, Vector3.Zero, Vector3.Zero, 0, MixedBoxId, 0, 0, true),
        });
        context = NewContext();
        test.TakeSent();
    }

    private List<(int ItemId, int Amount, long Receiver, Vector3 Position, short Kind, bool Fixed)> Drops() {
        return Sent(SendOp.FieldAddItem).Select(reader => {
            reader.ReadInt();
            int itemId = reader.ReadInt();
            int amount = reader.ReadInt();
            long receiver = reader.ReadLong();
            var position = new Vector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat());
            reader.ReadInt();
            reader.ReadByte();
            reader.ReadInt();
            return (itemId, amount, receiver, position, reader.ReadShort(), reader.ReadBool());
        }).ToList();
    }

    // create_item (298): each spawn point drops its box's items, and the item_id item too, at its own place for anyone
    // (receiver 0), fixed, as a trigger drop (1062) that lasts the spawn point's lifetime. A spawn point the map lacks
    // drops nothing.
    [Test]
    public void CreateItem_DropsAtTheSpawnPoints() {
        Action("""<action name="create_item" spawn_ids="9001,9002,9003" />""");
        Assert.That(Drops(), Is.EqualTo(new[] { (Coin, 3, 0L, new Vector3(100, 200, 300), ItemDropKind.Trigger, true) }));

        Action("""<action name="create_item" spawn_ids="9002" trigger_id="0" item_id="30000002" />""");
        Assert.That(Drops(), Is.EqualTo(new[] { (Potion, 1, 0L, new Vector3(-100, 0, 0), ItemDropKind.Trigger, true) }));

        Action("""<action name="create_item" spawn_ids="9003" item_id="30000002" />""");
        Assert.That(Drops(), Is.Empty);
    }

    // 60100070's one condition: hold 1 of 30000023 (item_exist, no map).
    private Quest StartQuest(TestGameSession? session = null) {
        return (session ?? test).StartQuest(TestGameSession.QuestData(QuestId) with {
            Conditions = [new ConditionMetadata(ConditionType.item_exist, 1, new ConditionMetadata.Parameters(Integers: [QuestItem]), null)],
        });
    }

    // A spawn point box's quest-only row drops for the player whose started quest needs the item, as that player's
    // item (52000066 AgentEscape03: create_item spawn_ids="300" arg5="6000"). While that copy lies on the field the
    // trigger running again drops no other.
    [Test]
    public void CreateItem_QuestRowForThePlayerWhoNeedsIt() {
        StartQuest();
        long characterId = test.Session.CharacterId;

        Action("""<action name="create_item" spawn_ids="9004" arg5="6000" />""");
        Assert.That(Drops(), Is.EqualTo(new[] { (QuestItem, 1, characterId, new Vector3(6800, -1125, 4950), ItemDropKind.Trigger, true) }));

        Action("""<action name="create_item" spawn_ids="9004" arg5="6000" />""");
        Assert.That(Drops(), Is.Empty);
    }

    // No drop without the quest, with the quest's item already held (its count met), or with the quest done.
    [Test]
    public void CreateItem_QuestRowNotNeeded() {
        Action("""<action name="create_item" spawn_ids="9004" />""");
        Assert.That(Drops(), Is.Empty);

        Quest quest = StartQuest();
        quest.Conditions.Values.Single().Counter = 1;
        Action("""<action name="create_item" spawn_ids="9004" />""");
        Assert.That(Drops(), Is.Empty);

        quest.Conditions.Values.Single().Counter = 0;
        quest.State = QuestState.Completed;
        Action("""<action name="create_item" spawn_ids="9004" />""");
        Assert.That(Drops(), Is.Empty);
    }

    // spawn_item_range rolls its spawn points' quest-only rows the same way.
    [Test]
    public void SpawnItemRange_QuestRow() {
        StartQuest();
        Action("""<action name="spawn_item_range" range_ids="9004" random_pick_count="1" />""");
        Assert.That(Drops().Select(drop => (drop.ItemId, drop.Receiver)), Is.EqualTo(new[] { (QuestItem, test.Session.CharacterId) }));
    }

    // A second player in the same field.
    private TestGameSession SecondPlayer() {
        var other = new TestGameSession(test.Options);
        other.Start();
        typeof(Actor<Player>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(other.Session.Player, 2);
        Field.Players[2] = other.Session.Player;
        other.Session.Field = Field;
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(other.Session.Player, Field);
        return other;
    }

    // A group mixing quest-only and plain rows rolls its drop count once over all of them, as the client's box roll
    // does (ItemDropTable::DropIndividualGroup calls PickDropCount once per group): a count of 1 gives one row, the
    // plain one for anyone or the quest one for the player who needs it, never both.
    [Test]
    public void MixedGroup_OneDropCountForBothKinds() {
        StartQuest();
        TestGameSession other = SecondPlayer();
        var seen = new HashSet<int>();
        for (int i = 0; i < 200; i++) {
            ItemDropManager.SpawnBoxDrops box = Field.ItemDrop.GetSpawnBoxItems(MixedBoxId, [test.Session, other.Session]);
            Assert.That(box.Shared.Count + box.Owned.Count, Is.EqualTo(1));
            Assert.That(box.Owned.Select(drop => (drop.Session, drop.Item.Id)), Is.SubsetOf(new[] { (test.Session, QuestItem) }));
            seen.UnionWith(box.Shared.Concat(box.Owned.Select(drop => drop.Item)).Select(item => item.Id));
        }
        Assert.That(seen, Is.EquivalentTo(new[] { QuestItem, Coin }));
    }

    // Two players, one with the quest: each create_item drops one item, the coin for anyone or the quest item for that
    // player only, and the quest item once while it lies on the field.
    [Test]
    public void CreateItem_MixedGroupWithTwoPlayers() {
        StartQuest();
        TestGameSession other = SecondPlayer();
        var drops = new List<(int ItemId, long Receiver)>();
        for (int i = 0; i < 100; i++) {
            Action("""<action name="create_item" spawn_ids="9005" />""");
            List<(int ItemId, long Receiver)> run = Drops().Select(drop => (drop.ItemId, drop.Receiver)).ToList();
            Assert.That(run, Has.Count.LessThanOrEqualTo(1));
            drops.AddRange(run);
        }
        Assert.That(drops.Where(drop => drop.ItemId == QuestItem), Is.EqualTo(new[] { (QuestItem, test.Session.CharacterId) }));
        Assert.That(drops.Where(drop => drop.ItemId == Coin).Select(drop => drop.Receiver).Distinct(), Is.EqualTo(new[] { 0L }));
        Assert.That(drops.Select(drop => drop.Receiver), Has.None.EqualTo(other.Session.CharacterId));
    }

    // With both players on the quest, a pick of the quest row gives each of them their own copy.
    [Test]
    public void CreateItem_MixedGroupQuestRowForEachPlayerWhoNeedsIt() {
        StartQuest();
        TestGameSession other = SecondPlayer();
        StartQuest(other);
        for (int i = 0; i < 100; i++) {
            Action("""<action name="create_item" spawn_ids="9005" />""");
            List<(int ItemId, long Receiver)> run = Drops().Select(drop => (drop.ItemId, drop.Receiver)).ToList();
            if (run.Any(drop => drop.ItemId == QuestItem)) {
                Assert.That(run, Is.EquivalentTo(new[] { (QuestItem, test.Session.CharacterId), (QuestItem, other.Session.CharacterId) }));
                return;
            }
            Assert.That(run, Is.EqualTo(new[] { (Coin, 0L) }));
        }
        Assert.Fail("the quest row was never picked");
    }
}

public class TriggerAchievementTests : TriggerTestBase {
    private const int Trophy = 90000001;
    private const int OtherTrophy = 90000002;

    // Two trophies of the trigger condition, one per code; box 1 holds the player at the origin, box 2 is far off.
    [SetUp]
    public void SetUpTrophies() {
        test = new TestGameSession();
        test.AddAchievement(CodeTrophy(Trophy, "oxquiz_correct"), CodeTrophy(OtherTrophy, "ddstop_pass"));
        test.Start();
        SetTriggers(new Ms2TriggerBox(Vector3.Zero, new Vector3(300, 300, 300), 1, true),
            new Ms2TriggerBox(new Vector3(5000, 0, 0), new Vector3(300, 300, 300), 2, true));
        test.Session.Player.Position = Vector3.Zero;
        context = NewContext();
        test.TakeSent();
    }

    private static AchievementMetadata CodeTrophy(int id, string code) {
        var grade = new AchievementMetadataGrade(1, new ConditionMetadata(ConditionType.trigger,
            10, new ConditionMetadata.Parameters(Strings: [code]), null), null);
        return new AchievementMetadata(id, $"{id}", false, AchievementCategory.Life, [], new Dictionary<int, AchievementMetadataGrade> { [1] = grade }, false);
    }

    // set_achievement (518): each player in the box (trigger_id; 0 is the whole field) counts the trigger condition of
    // the achieve code; an empty type is the trigger condition (6 KR uses leave it out). A player out of the box
    // counts nothing.
    [Test]
    public void SetAchievement_PlayersInTheBoxCountTheCode() {
        Action("""<action name="set_achievement" trigger_id="1" type="trigger" achieve="oxquiz_correct" />""");
        Assert.That((test.TrophyCounter(Trophy), test.TrophyCounter(OtherTrophy)), Is.EqualTo(((long?) 1, (long?) null)));

        Action("""<action name="set_achievement" trigger_id="2" type="trigger" achieve="oxquiz_correct" />""");
        Assert.That(test.TrophyCounter(Trophy), Is.EqualTo(1));

        Action("""<action name="set_achievement" trigger_id="0" achieve="oxquiz_correct" />""");
        Assert.That(test.TrophyCounter(Trophy), Is.EqualTo(2));

        Action("""<action name="set_achievement" trigger_id="1" type="trigger" achieve="ddstop_pass" />""");
        Assert.That(test.TrophyCounter(OtherTrophy), Is.EqualTo(1));
    }
}

public class TriggerFieldGameTests : TriggerTestBase {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // A field whose map has no hideAndSeek.xml row, with the trigger user values a new game sets.
    [SetUp]
    public void SetUpField() {
        test = new TestGameSession();
        test.SetServerTable(new HideAndSeekTable(new Dictionary<int, HideAndSeekTable.Field>()));
        test.Start();
        SetTriggers();
        typeof(FieldManager).GetField(nameof(FieldManager.UserValues))!.SetValue(Field, new TriggerUserValues());
        context = NewContext();
        test.TakeSent();
    }

    private FieldSpawnGroup? SpawnGroup(int groupId) =>
        ((ConcurrentDictionary<int, FieldSpawnGroup>) typeof(FieldManager).GetField("fieldSpawnGroups", Any)!.GetValue(Field)!).GetValueOrDefault(groupId);

    private static bool Active(FieldSpawnGroup group) => (bool) typeof(FieldSpawnGroup).GetField("active", Any)!.GetValue(group)!;

    // start_combine_spawn (356): each listed group of the field's own map turns on (is_start 1) or off; a group of
    // another map whose spawn point the field lacks, and a group of no map, are not made. The groups here have no
    // count, so nothing spawns.
    [Test]
    public void StartCombineSpawn_TogglesTheFieldsGroups() {
        test.SetServerTable(new CombineSpawnTable(
            new Dictionary<int, Dictionary<int, SpawnGroupMetadata>> {
                [Field.MapId] = new() { [10] = new SpawnGroupMetadata(10, CombineSpawnGroupType.npc, 0, 0, Field.MapId) },
                [82000012] = new() { [30] = new SpawnGroupMetadata(30, CombineSpawnGroupType.npc, 0, 0, 82000012) },
            },
            new Dictionary<int, Dictionary<int, SpawnNpcMetadata>> { [30] = new() { [1] = new SpawnNpcMetadata(1, 30, 100, 900) } },
            new Dictionary<int, Dictionary<int, SpawnInteractObjectMetadata>>()));
        typeof(MapEntityMetadata).GetField("<NpcSpawns>k__BackingField", Any)!.SetValue(Field.Entities, new List<SpawnPointNPC>());
        typeof(MapEntityMetadata).GetField("<RegionSpawns>k__BackingField", Any)!.SetValue(Field.Entities, new Dictionary<int, Ms2RegionSpawn>());
        typeof(FieldManager).GetField("fieldSpawnGroups", Any)!.SetValue(Field, new ConcurrentDictionary<int, FieldSpawnGroup>());

        Action("""<action name="start_combine_spawn" group_id="10,30,99" is_start="1" />""");
        Assert.That((SpawnGroup(10) is { } group && Active(group), SpawnGroup(30), SpawnGroup(99)), Is.EqualTo((true, (FieldSpawnGroup?) null, (FieldSpawnGroup?) null)));

        Action("""<action name="start_combine_spawn" group_id="10" is_start="0" />""");
        Assert.That(Active(SpawnGroup(10)!), Is.False);
    }

    private string? GameConstant(string key) {
        var state = (HideAndSeekState) typeof(HideAndSeekField).GetField("state", Any)!.GetValue(Field.HideAndSeek)!;
        return state.Constant(key);
    }

    // field_game_constant (308): a constant of the field's Hide and Seek game; one of another region (locale CN) is
    // skipped, one without a locale or of the server's own is kept. Before a game is made the action does nothing.
    [Test]
    public void FieldGameConstant_SetsTheGamesConstant() {
        Action("""<action name="field_game_constant" key="RequireUserCount" value="4" feature="MassiveHideAndSeek" />""");
        Assert.That(Field.HideAndSeek, Is.Null);

        Field.StartHideAndSeek(1);
        Action("""<action name="field_game_constant" key="RequireUserCount" value="4" feature="MassiveHideAndSeek" />""");
        Action("""<action name="field_game_constant" key="EnterableSeconds" value="90" locale="CN" />""");
        Action($"""<action name="field_game_constant" key="DivideTeamsSeconds" value="7" locale="{Maple2.Tools.Locale.Read()}" />""");
        Assert.That((GameConstant("RequireUserCount"), GameConstant("EnterableSeconds"), GameConstant("DivideTeamsSeconds")),
            Is.EqualTo(("4", (string?) null, "7")));
    }
}

public class TriggerInterfaceTests : TriggerTestBase {
    // show_guide_summary (1392), hide_guide_summary (680): Trigger Ui (9) ShowSummary (2) with the entity, text and
    // duration, HideSummary (3) with the entity (the client reads one int).
    [Test]
    public void GuideSummary_ShowThenHide() {
        Start();

        Action("""<action name="show_guide_summary" entity_id="20002" text_id="20002" duration="5000" />""");
        ByteReader shown = Sent(SendOp.Trigger).Single();
        Assert.That((shown.ReadByte(), shown.ReadByte(), shown.ReadInt(), shown.ReadInt(), shown.ReadInt()),
            Is.EqualTo(((byte) 9, (byte) 2, 20002, 20002, 5000)));

        Action("""<action name="hide_guide_summary" entity_id="20002" />""");
        ByteReader hidden = Sent(SendOp.Trigger).Single();
        Assert.That((hidden.ReadByte(), hidden.ReadByte(), hidden.ReadInt(), hidden.Available), Is.EqualTo(((byte) 9, (byte) 3, 20002, 0)));
    }

    // remove_cinematic_talk (1144): Cinematic RemoveTalk (7) alone.
    [Test]
    public void RemoveCinematicTalk_Sends7() {
        Start();

        Action("""<action name="remove_cinematic_talk" />""");
        ByteReader removed = Sent(SendOp.Cinematic).Single();
        Assert.That((removed.ReadByte(), removed.Available), Is.EqualTo(((byte) 7, 0)));
    }

    // reset_camera (495): CameraInterpolation with the time.
    [Test]
    public void ResetCamera_SendsTheTime() {
        Start();

        Action("""<action name="reset_camera" interpolation_time="1.5" />""");
        Assert.That(Sent(SendOp.CameraInterpolation).Single().ReadFloat(), Is.EqualTo(1.5f));
    }

    // check_user (195): a player is on the field; negate: none is.
    [Test]
    public void CheckUser_PlayerOnTheField() {
        Start();

        Assert.That(Condition("""<condition name="check_user" />"""), Is.True);
        Assert.That(Condition("""<condition name="check_user" negate="1" />"""), Is.False);
    }

    // dungeon_variable (278): the field's variable equals the value; an unset one reads 0.
    [Test]
    public void DungeonVariable_Equals() {
        Start();
        // The test field skips the FieldManager ctor: its variable table is made here.
        typeof(FieldManager).GetField(nameof(FieldManager.DungeonVariables))!.SetValue(Field, new ConcurrentDictionary<int, int>());
        Field.DungeonVariables[3] = 1;

        Assert.That(Condition("""<condition name="dungeon_variable" var_id="3" value="1" />"""), Is.True);
        Assert.That(Condition("""<condition name="dungeon_variable" var_id="3" value="0" />"""), Is.False);
        Assert.That(Condition("""<condition name="dungeon_variable" var_id="4" value="0" />"""), Is.True);
    }

    // create_widget (129), widget_value (237), widget_action (331): the field gets the widget of the type;
    // its value is checked by name, and its action (SceneMovie Clear) clears the values. No widget: negate.
    [Test]
    public void Widget_CreateCheckAction() {
        Start();
        typeof(FieldManager).GetField(nameof(FieldManager.Widgets))!.SetValue(Field, new Dictionary<string, Widget>());
        Assert.That(Condition("""<condition name="widget_value" type="SceneMovie" widget_name="IsStop" condition="1" negate="1" />"""), Is.True);

        Action("""<action name="create_widget" type="SceneMovie" />""");
        Assert.That(Field.Widgets["SceneMovie"], Is.TypeOf<SceneMovieWidget>());
        Field.Widgets["SceneMovie"].Conditions["IsStop"] = 1;
        Assert.That(Condition("""<condition name="widget_value" type="SceneMovie" widget_name="IsStop" condition="1" />"""), Is.True);

        Action("""<action name="widget_action" type="SceneMovie" func="Clear" />""");
        Assert.That(Condition("""<condition name="widget_value" type="SceneMovie" widget_name="IsStop" condition="1" />"""), Is.False);
        Assert.That(Condition("""<condition name="widget_value" type="SceneMovie" widget_name="IsStop" condition="1" negate="1" />"""), Is.True);
    }

    // enable_spawn_point_pc (485): the player spawn point of the id is turned on or off.
    [Test]
    public void EnableSpawnPointPc_TogglesTheSpawn() {
        Start();
        var spawns = new ConcurrentDictionary<int, FieldPlayerSpawnPoint>();
        typeof(FieldManager).GetField("fieldPlayerSpawnPoints", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Field, spawns);
        spawns[1] = new FieldPlayerSpawnPoint(Field, 9001, new SpawnPointPC(1, Vector3.Zero, Vector3.Zero, true, false), 1);

        Action("""<action name="enable_spawn_point_pc" spawn_id="1" is_enable="1" />""");
        Assert.That(spawns[1].Enable, Is.True);
        Action("""<action name="enable_spawn_point_pc" spawn_id="1" is_enable="0" />""");
        Assert.That(spawns[1].Enable, Is.False);
    }

    // set_pc_emotion_loop (169): Trigger Ui (9) EmotionLoop (8) as the client reads it: byte loop flag, int
    // duration, unicode sequence name.
    [Test]
    public void SetPcEmotionLoop_Packet() {
        Start();

        Action("""<action name="set_pc_emotion_loop" sequence_name="Sit_Down_A" duration="18000" loop="1" />""");
        ByteReader sent = Sent(SendOp.Trigger).Single();
        Assert.That((sent.ReadByte(), sent.ReadByte(), sent.ReadBool(), sent.ReadInt(), sent.ReadUnicodeString()),
            Is.EqualTo(((byte) 9, (byte) 8, true, 18000, "Sit_Down_A")));
    }

    // check_dungeon_lobby_user_count (180): a field outside a dungeon has no lobby to fill: false, negate true.
    [Test]
    public void CheckDungeonLobbyUserCount_NotADungeon() {
        Start();

        Assert.That(Condition("""<condition name="check_dungeon_lobby_user_count" />"""), Is.False);
        Assert.That(Condition("""<condition name="check_dungeon_lobby_user_count" negate="1" />"""), Is.True);
    }

    // The test field skips the FieldManager ctor: its field property table is made here.
    private void StartWithProperties() {
        Start();
        typeof(FieldManager).GetField("fieldProperties", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Field,
            new ConcurrentDictionary<FieldProperty, IFieldProperty>());
    }

    // FieldProperty Add (1) or Remove (2) of the type.
    private (byte Command, byte Type) PropertySent() {
        ByteReader reader = Sent(SendOp.FieldProperty).Single();
        return (reader.ReadByte(), reader.ReadByte());
    }

    // visible_my_pc (139), lock_my_pc (136): hiding adds the HidePlayer (3) property and showing removes it; locking
    // adds LockPlayer (4) and unlocking removes it.
    [Test]
    public void VisibleAndLockMyPc_FieldProperties() {
        StartWithProperties();

        Action("""<action name="visible_my_pc" is_visible="0" />""");
        Assert.That(PropertySent(), Is.EqualTo(((byte) 1, (byte) 3)));
        Action("""<action name="visible_my_pc" is_visible="1" />""");
        Assert.That(PropertySent(), Is.EqualTo(((byte) 2, (byte) 3)));

        Action("""<action name="lock_my_pc" is_lock="1" />""");
        Assert.That(PropertySent(), Is.EqualTo(((byte) 1, (byte) 4)));
        Action("""<action name="lock_my_pc" is_lock="0" />""");
        Assert.That(PropertySent(), Is.EqualTo(((byte) 2, (byte) 4)));
    }

    // set_gravity (29): the Gravity (1) property is added with the value.
    [Test]
    public void SetGravity_AddsTheProperty() {
        StartWithProperties();

        Action("""<action name="set_gravity" gravity="-0.5" />""");
        ByteReader reader = Sent(SendOp.FieldProperty).Single();
        Assert.That((reader.ReadByte(), reader.ReadByte(), reader.ReadFloat()), Is.EqualTo(((byte) 1, (byte) 1, -0.5f)));
    }

    // set_ambient_light (82): the AmbientLight (8) property is added.
    [Test]
    public void SetAmbientLight_AddsTheProperty() {
        StartWithProperties();

        Action("""<action name="set_ambient_light" primary="128,64,32" />""");
        Assert.That(PropertySent(), Is.EqualTo(((byte) 1, (byte) 8)));
    }

    // show_count_ui (129): MassiveEvent Countdown (1) as UIMassiveEvent reads it: text, stage, count, sound type.
    [Test]
    public void ShowCountUi_Packet() {
        Start();

        Action("""<action name="show_count_ui" text="$abc$" stage="2" count="5" sound_type="1" />""");
        ByteReader sent = Sent(SendOp.MassiveEvent).Single();
        Assert.That((sent.ReadByte(), sent.ReadUnicodeString(), sent.ReadInt(), sent.ReadInt(), sent.ReadInt()),
            Is.EqualTo(((byte) 1, "$abc$", 2, 5, 1)));
    }

    // face_emotion (122): Trigger Ui (9) FaceEmotion (9) with the actor and the emotion; spawn 0 is the player.
    // A spawn point with no npc sends nothing.
    [Test]
    public void FaceEmotion_PlayerOrNpc() {
        Start();

        Action("""<action name="face_emotion" spawn_id="0" emotion_name="Trigger_Panic" />""");
        ByteReader sent = Sent(SendOp.Trigger).Single();
        Assert.That((sent.ReadByte(), sent.ReadByte(), sent.ReadInt(), sent.ReadString()),
            Is.EqualTo(((byte) 9, (byte) 9, test.Session.Player.ObjectId, "Trigger_Panic")));

        Action("""<action name="face_emotion" spawn_id="5" emotion_name="Trigger_Panic" />""");
        Assert.That(Sent(SendOp.Trigger), Is.Empty);
    }

    // show_caption (113): Cinematic Caption (10) as UICutSceneCinematic::OnPacketSetCaption reads it: type, title,
    // text, align name, duration, offset x and y, scale.
    [Test]
    public void ShowCaption_Packet() {
        Start();

        Action("""<action name="show_caption" type="NameCaption" title="$t$" desc="$d$" align="centerLeft" offset_rate_x="0.05" offset_rate_y="0.15" duration="3000" scale="2" />""");
        ByteReader sent = Sent(SendOp.Cinematic).Single();
        Assert.That((sent.ReadByte(), sent.ReadUnicodeString(), sent.ReadUnicodeString(), sent.ReadUnicodeString(), sent.ReadUnicodeString()),
            Is.EqualTo(((byte) 10, "NameCaption", "$t$", "$d$", "centerLeft")));
        Assert.That((sent.ReadInt(), sent.ReadFloat(), sent.ReadFloat(), sent.ReadFloat()), Is.EqualTo((3000, 0.05f, 0.15f, 2f)));
    }

    // is_dungeon_room (95): the field is a dungeon room; the test field is not.
    [Test]
    public void IsDungeonRoom_NotADungeon() {
        Start();

        Assert.That(Condition("""<condition name="is_dungeon_room" />"""), Is.False);
        Assert.That(Condition("""<condition name="is_dungeon_room" negate="1" />"""), Is.True);
    }

    // set_directional_light (69): the DirectionalLight (9) property is added.
    [Test]
    public void SetDirectionalLight_AddsTheProperty() {
        StartWithProperties();

        Action("""<action name="set_directional_light" diffuse_color="255,200,100" specular_color="20,20,20" />""");
        Assert.That(PropertySent(), Is.EqualTo(((byte) 1, (byte) 9)));
    }

    // set_local_camera (113): LocalCamera with the camera id, the switch and no object.
    [Test]
    public void SetLocalCamera_Packet() {
        Start();

        Action("""<action name="set_local_camera" camera_id="301" enable="1" />""");
        ByteReader sent = Sent(SendOp.LocalCamera).Single();
        Assert.That((sent.ReadInt(), sent.ReadBool(), sent.ReadInt()), Is.EqualTo((301, true, 0)));
    }

    // play_scene_movie (100): Trigger Ui (9) StartMovie (4) with the file and the movie id.
    [Test]
    public void PlaySceneMovie_Packet() {
        Start();

        Action("""<action name="play_scene_movie" file_name="common\JobIntro_Thief.usm" movie_id="1" skip_type="needAll" />""");
        ByteReader sent = Sent(SendOp.Trigger).Single();
        Assert.That((sent.ReadByte(), sent.ReadByte(), sent.ReadString(), sent.ReadInt()), Is.EqualTo(((byte) 9, (byte) 4, "common\\JobIntro_Thief.usm", 1)));
    }

    // set_pc_emotion_sequence (91): Trigger Ui (9) EmotionSequence (7) with the count and each sequence name.
    [Test]
    public void SetPcEmotionSequence_Packet() {
        Start();

        Action("""<action name="set_pc_emotion_sequence" sequence_names="Bore_A,Idle_A" />""");
        ByteReader sent = Sent(SendOp.Trigger).Single();
        Assert.That((sent.ReadByte(), sent.ReadByte(), sent.ReadInt(), sent.ReadUnicodeString(), sent.ReadUnicodeString()),
            Is.EqualTo(((byte) 9, (byte) 7, 2, "Bore_A", "Idle_A")));
    }

    // set_time_scale (37): TimeScale with the switch, the start and end scale, the duration and the interpolator.
    [Test]
    public void SetTimeScale_Packet() {
        Start();

        Action("""<action name="set_time_scale" enable="1" start_scale="1" end_scale="0.1" duration="3" interpolator="2" />""");
        ByteReader sent = Sent(SendOp.TimeScale).Single();
        Assert.That((sent.ReadBool(), sent.ReadFloat(), sent.ReadFloat(), sent.ReadFloat(), sent.ReadByte()), Is.EqualTo((true, 1f, 0.1f, 3f, (byte) 2)));
    }

    // day_of_week (37): today's day (1 = Sunday) is in the list; negate: it is not.
    [Test]
    public void DayOfWeek_Today() {
        Start();
        int today = (int) DateTime.UtcNow.DayOfWeek + 1;
        int other = today % 7 + 1;

        Assert.That(Condition($"""<condition name="day_of_week" day_of_weeks="{other},{today}" />"""), Is.True);
        Assert.That(Condition($"""<condition name="day_of_week" day_of_weeks="{other}" />"""), Is.False);
        Assert.That(Condition($"""<condition name="day_of_week" day_of_weeks="{other}" negate="1" />"""), Is.True);
    }

    // wait_seconds_user_value (34): the state has run the seconds the script's user value names; an unset value is 0.
    [Test]
    public void WaitSecondsUserValue_ByTheValue() {
        Start();
        // The test field skips the FieldManager ctor: its user value table is made here.
        typeof(FieldManager).GetField(nameof(FieldManager.UserValues))!.SetValue(Field, new TriggerUserValues());
        Field.UserValues.Set(1, "WaitTime", 60);

        Assert.That(Condition("""<condition name="wait_seconds_user_value" key="WaitTime" />"""), Is.False);
        context.StartTick = Environment.TickCount64 - 61_000;
        Assert.That(Condition("""<condition name="wait_seconds_user_value" key="WaitTime" />"""), Is.True);
        context.StartTick = Environment.TickCount64 - 1;
        Assert.That(Condition("""<condition name="wait_seconds_user_value" key="Unset" />"""), Is.True);
    }

    // dungeon_timeout (35): a field without a room timer has not timed out.
    [Test]
    public void DungeonTimeout_NoTimer() {
        Start();

        Assert.That(Condition("""<condition name="dungeon_timeout" />"""), Is.False);
    }

    // dungeon_timeout: true once the room timer's time ran out; not before, and not after dungeon_set_end_time.
    [Test]
    public void DungeonTimeout_AfterTheTime() {
        Start();
        Field.SetRoomTimer(RoomTimerType.Clock, 60_000, false);
        Field.RoomTimer!.Clock.Start(100_000);
        FieldInfo tick = typeof(FieldManager).GetField("<FieldTick>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;

        tick.SetValue(Field, 160_000L);
        Assert.That(Condition("""<condition name="dungeon_timeout" />"""), Is.False);
        tick.SetValue(Field, 160_001L);
        Assert.That(Condition("""<condition name="dungeon_timeout" />"""), Is.True);
        Field.RoomTimer.End();
        Assert.That(Condition("""<condition name="dungeon_timeout" />"""), Is.False);
    }
}

// The dungeon rows on a room's lobby field (TestGameSession.EnterDungeon): the room record keeps the result, the
// variables and the start tick; the player's user record keeps the missions. Made-up room 20003001.
public class TriggerDungeonTests : TriggerTestBase {
    private const int Room = 20003001;
    private DungeonRoomRecord record = null!;

    private void StartRoom(int roundId = 0) {
        test = new TestGameSession();
        DungeonRoomMetadata room = TestGameSession.DungeonRoom(Room, 2000023) with { RoundId = roundId };
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> { [Room] = room }));
        test.Start();
        // The player's run is not in the room's results, so the clear grants no rewards (the test has no reward tables).
        record = test.EnterDungeon(room);
        // The test field skips the field initializers, so its result logger is set here.
        typeof(DungeonFieldManager).GetField("resultLogger", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(Field, Log.Logger.ForContext<DungeonFieldManager>());
        SetTriggers();
        context = NewContext();
        test.TakeSent();
    }

    private static string Hex(ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    // dungeon_clear (154 KR uses): the room is cleared and the give-up vote closed (Stage 1); a clear after the
    // result is ignored. Outside a dungeon nothing happens.
    [Test]
    public void DungeonClear_ClearsTheRoomOnce() {
        StartRoom();
        Action("""<action name="dungeon_clear" />""");
        Assert.That(record.State, Is.EqualTo(DungeonState.Clear));
        Assert.That(test.TakeSent().Select(Convert.ToHexString), Does.Contain(Hex(DungeonMissionPacket.Stage(1))));
        Action("""<action name="dungeon_clear" />""");
        Assert.That(test.TakeSent(), Is.Empty);

        Start();
        Action("""<action name="dungeon_clear" />""");
        Assert.That(test.TakeSent(), Is.Empty);
    }

    // ui_type None clears the room as well (the Colosseum's lost round after the first).
    [Test]
    public void DungeonClear_NoneStillClears() {
        StartRoom();
        Action("""<action name="dungeon_clear" ui_type="None" />""");
        Assert.That(record.State, Is.EqualTo(DungeonState.Clear));
    }

    // dungeon_state: the room's result by name; outside a room only the empty name holds.
    [Test]
    public void DungeonState_ReadsTheRoomResult() {
        StartRoom();
        Assert.That(Condition("""<condition name="dungeon_state" check_state="Fail" />"""), Is.False);
        record.State = DungeonState.Fail;
        Assert.That(Condition("""<condition name="dungeon_state" check_state="Fail" />"""), Is.True);
        Assert.That(Condition("""<condition name="dungeon_state" check_state="" />"""), Is.False);

        Start();
        Assert.That(Condition("""<condition name="dungeon_state" check_state="" />"""), Is.True);
        Assert.That(Condition("""<condition name="dungeon_state" check_state="Fail" />"""), Is.False);
    }

    // dungeon_play_time counts from the room's start tick; outside a room it is false.
    [Test]
    public void DungeonPlayTime_FromTheRoomStart() {
        StartRoom();
        typeof(FieldManager).GetField("<FieldTick>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(Field, 1_000_000L);
        record.StartTick = 699_000;
        Assert.That(Condition("""<condition name="dungeon_play_time" play_seconds="300" />"""), Is.True);
        Assert.That(Condition("""<condition name="dungeon_play_time" play_seconds="400" />"""), Is.False);
        Assert.That(Condition("""<condition name="dungeon_play_time" play_seconds="400" operator="Less" />"""), Is.True);

        Start();
        Assert.That(Condition("""<condition name="dungeon_play_time" play_seconds="0" />"""), Is.False);
    }

    // dungeon_mission_complete: a mission the player holds gets its full score and is sent; another id is skipped.
    [Test]
    public void DungeonMissionComplete_TheHeldMission() {
        StartRoom();
        var mission = new DungeonMission(new DungeonMissionMetadata(3, DungeonMissionType.Trigger, [], 0, 500, 0, false));
        test.Session.Dungeon.UserRecord!.Missions[3] = mission;
        Action("""<action name="dungeon_mission_complete" mission_id="9" />""");
        Assert.That(test.TakeSent(), Is.Empty);
        Action("""<action name="dungeon_mission_complete" mission_id="3" />""");
        Assert.That(mission.Score, Is.EqualTo(500));
        Assert.That(test.TakeSent().Select(Convert.ToHexString).ToArray(), Is.EqualTo(new[] { Hex(DungeonMissionPacket.Update(mission)) }));
    }

    // set_dungeon_variable writes the room record's store, which dungeon_variable reads (an unset id reads 0).
    [Test]
    public void SetDungeonVariable_TheRoomStore() {
        StartRoom();
        Assert.That(Condition("""<condition name="dungeon_variable" var_id="2" value="0" />"""), Is.True);
        Action("""<action name="set_dungeon_variable" var_id="2" value="1" />""");
        Assert.That(record.Variables[2], Is.EqualTo(1));
        Assert.That(Condition("""<condition name="dungeon_variable" var_id="2" value="1" />"""), Is.True);
        Assert.That(Condition("""<condition name="dungeon_variable" var_id="2" value="0" />"""), Is.False);
    }

    // dungeon_fail (33): the room's result is Fail and the players get the result; a room with a result keeps it
    // (a second fail, or a fail after a clear, sends nothing). Outside a dungeon nothing happens.
    [Test]
    public void DungeonFail_FailsTheRoomOnce() {
        StartRoom();
        Action("""<action name="dungeon_fail" />""");
        Assert.That(record.State, Is.EqualTo(DungeonState.Fail));
        Assert.That(test.TakeSent(), Is.Not.Empty);
        Action("""<action name="dungeon_fail" />""");
        Assert.That(test.TakeSent(), Is.Empty);

        StartRoom();
        Action("""<action name="dungeon_clear" />""");
        test.TakeSent();
        Action("""<action name="dungeon_fail" />""");
        Assert.That(record.State, Is.EqualTo(DungeonState.Clear));
        Assert.That(test.TakeSent(), Is.Empty);

        Start();
        Action("""<action name="dungeon_fail" />""");
        Assert.That(test.TakeSent(), Is.Empty);
    }

    // dungeon_enable_give_up (28): the room's give-up switch (DungeonRewardHandler's give-up asks it), and the
    // players' give-up button (DungeonMission SetAbandon).
    [Test]
    public void DungeonEnableGiveUp_TheRoomSwitch() {
        StartRoom();
        var room = (DungeonFieldManager) Field;
        Action("""<action name="dungeon_enable_give_up" is_enable="1" />""");
        Assert.That(room.GiveUpEnabled, Is.True);
        Assert.That(test.TakeSent().Select(Convert.ToHexString).ToArray(), Is.EqualTo(new[] { Hex(DungeonMissionPacket.SetAbandon(true)) }));

        Action("""<action name="dungeon_enable_give_up" is_enable="0" />""");
        Assert.That(room.GiveUpEnabled, Is.False);
        Assert.That(test.TakeSent().Select(Convert.ToHexString).ToArray(), Is.EqualTo(new[] { Hex(DungeonMissionPacket.SetAbandon(false)) }));
    }

    // dungeon_clear_round (27): the players' user record keeps the round reached (0 is the Colosseum's lost round).
    [Test]
    public void DungeonClearRound_TheUserRecord() {
        StartRoom();
        Action("""<action name="dungeon_clear_round" round="3" />""");
        Assert.That(test.Session.Dungeon.UserRecord!.Round, Is.EqualTo(3));
        Action("""<action name="dungeon_clear_round" round="0" />""");
        Assert.That(test.Session.Dungeon.UserRecord!.Round, Is.EqualTo(0));
    }

    // dungeon_round (23): round N of the room's round data (entry N-1) asks for no more gear score than the player
    // has; a round past the data, or a room without round data, does not pass.
    [Test]
    public void DungeonRound_ByTheGearScore() {
        test = new TestGameSession();
        test.SetTable(new DungeonRoundDataTable(new Dictionary<int, DungeonRoundMetadata[]> { [7] = [new(0, 100), new(0, 500)] }));
        StartRoomOn(test, 7);
        test.Session.Stats.Values.GearScore = 300;

        Assert.That(Condition("""<condition name="dungeon_round" round="1" />"""), Is.True);
        Assert.That(Condition("""<condition name="dungeon_round" round="2" />"""), Is.False);
        Assert.That(Condition("""<condition name="dungeon_round" round="3" />"""), Is.False);
        test.Session.Stats.Values.GearScore = 500;
        Assert.That(Condition("""<condition name="dungeon_round" round="2" />"""), Is.True);

        test = new TestGameSession();
        test.SetTable(new DungeonRoundDataTable(new Dictionary<int, DungeonRoundMetadata[]>()));
        StartRoomOn(test, 7);
        Assert.That(Condition("""<condition name="dungeon_round" round="1" />"""), Is.False);
    }

    private void StartRoomOn(TestGameSession session, int roundId) {
        DungeonRoomMetadata room = TestGameSession.DungeonRoom(Room, 2000023) with { RoundId = roundId };
        session.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> { [Room] = room }));
        session.Start();
        record = session.EnterDungeon(room);
        SetTriggers();
        context = NewContext();
        session.TakeSent();
    }

    // dungeon_set_end_time (28), dungeon_close_timer (19), dungeon_stop_timer: the room timer's time function ends
    // (no timeout after it), its window closes (Cancel), and its time stops with the window closed (Stop).
    [Test]
    public void DungeonTimerRows_TheRoomTimer() {
        StartRoom();
        Field.SetRoomTimer(RoomTimerType.Clock, 60_000, false);
        test.TakeSent();

        Action("""<action name="dungeon_set_end_time" />""");
        Assert.That((Field.RoomTimer!.Clock.Ended, Field.RoomTimer.Closed, Field.RoomTimer.Clock.Stopped), Is.EqualTo((true, false, false)));
        Assert.That(test.TakeSent(), Is.Empty);

        Action("""<action name="dungeon_close_timer" />""");
        Assert.That((Field.RoomTimer.Closed, Field.RoomTimer.Clock.Stopped), Is.EqualTo((true, false)));
        Assert.That(test.TakeSent().Select(Convert.ToHexString).ToArray(), Is.EqualTo(new[] { Hex(RoomTimerPacket.Cancel()) }));

        Action("""<action name="dungeon_stop_timer" />""");
        Assert.That(Field.RoomTimer.Clock.Stopped, Is.True);
        Assert.That(test.TakeSent().Select(Convert.ToHexString).ToArray(), Is.EqualTo(new[] { Hex(RoomTimerPacket.Stop()) }));
    }
}

public class TriggerRoundUiTests : TriggerTestBase {
    // show_round_ui: MassiveEvent mode 7 carries the round, the final-round flag and the duration, in that order
    // (client UIMassiveEvent mode 7 reads int, bool, int).
    [TestCase("0", false)]
    [TestCase("1", true)]
    public void ShowRoundUi_SendsTheFinalRoundFlag(string isFinal, bool expected) {
        Start();

        Action($"""<action name="show_round_ui" round="3" duration="5000" is_final_round="{isFinal}" />""");

        ByteReader reader = Sent(SendOp.MassiveEvent).Single();
        Assert.That((reader.ReadByte(), reader.ReadInt(), reader.ReadBool(), reader.ReadInt()), Is.EqualTo(((byte) 7, 3, expected, 5000)));
        Assert.That(reader.Available, Is.Zero);
    }
}

// The wedding hall's and the mini games' rows on a plain test field.
public class TriggerRoomRowTests : TriggerTestBase {
    // wedding_hall_state (48): weddingComplete with success 1 passes once the vow is complete, with success 0 before;
    // another state, or a field without a ceremony, does not pass.
    [Test]
    public void WeddingHallState_TheVow() {
        Start();
        Assert.That(Condition("""<condition name="wedding_hall_state" hall_state="weddingComplete" success="0" />"""), Is.False);

        var ceremony = new WeddingCeremony(1, 100, 200, 100);
        Field.Wedding = ceremony;
        Assert.That(Condition("""<condition name="wedding_hall_state" hall_state="weddingComplete" success="0" />"""), Is.True);
        Assert.That(Condition("""<condition name="wedding_hall_state" hall_state="weddingComplete" success="1" />"""), Is.False);

        ceremony.TryComplete();
        Assert.That(Condition("""<condition name="wedding_hall_state" hall_state="weddingComplete" success="1" />"""), Is.True);
        Assert.That(Condition("""<condition name="wedding_hall_state" hall_state="weddingComplete" success="0" />"""), Is.False);
        Assert.That(Condition("""<condition name="wedding_hall_state" hall_state="weddingStart" success="1" />"""), Is.False);
    }

    // mini_game_give_exp (30): each player in the box with a game record gets the mini game exp at exp_rate, and the
    // record's exp reward counts it; a player without a record, or out of the box, gets none.
    [Test]
    public void MiniGameGiveExp_TheRecordedPlayersInTheBox() {
        Start(new Ms2TriggerBox(Vector3.Zero, new Vector3(300, 300, 300), 1, true),
            new Ms2TriggerBox(new Vector3(5000, 0, 0), new Vector3(300, 300, 300), 2, true));
        test.SetTable(new CommonExpTable(new Dictionary<ExpType, CommonExpTable.Entry> { [ExpType.miniGame] = new(1, 1f) }));
        test.SetTable(new ExpTable(new Dictionary<int, IReadOnlyDictionary<int, long>> {
            [1] = Enumerable.Range(1, 99).ToDictionary(level => level, level => level * 10L),
        }, Enumerable.Range(1, 99).ToDictionary(level => level, _ => 1_000_000L)));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        test.Session.Player.Value.Character.Level = 50;
        test.Session.Player.Position = Vector3.Zero;

        Action("""<action name="mini_game_give_exp" box_id="1" exp_rate="2" />""");
        Assert.That(test.Session.Player.Value.Character.Exp, Is.Zero);

        test.Session.MiniGameRecord = new MiniGameUserRecord(test.Session.CharacterId) { TotalRounds = 1, ShowResult = false, MinRound = 1 };
        Action("""<action name="mini_game_give_exp" box_id="2" exp_rate="2" />""");
        Assert.That(test.Session.Player.Value.Character.Exp, Is.Zero);

        Action("""<action name="mini_game_give_exp" box_id="1" exp_rate="2" />""");
        long gained = test.Session.Player.Value.Character.Exp;
        Assert.That(gained, Is.EqualTo(1000));
        Assert.That(test.Session.MiniGameRecord.Rewards[DungeonRewardType.Exp], Is.EqualTo(1000));
    }
}
