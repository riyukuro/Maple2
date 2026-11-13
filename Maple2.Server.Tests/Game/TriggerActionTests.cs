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
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.ActorStateComponent;
using Maple2.Server.Game.Model.Widget;
using Maple2.Server.Game.Trigger;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Tools.Scheduler;
using Maple2.Tools.VectorMath;
using Microsoft.EntityFrameworkCore;
using Serilog;

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
    private TriggerContext NewContext() {
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
    private FieldNpc Mob(int objectId, int spawnPointId, bool dead = false) {
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
        typeof(Actor<Npc>).GetField("<Value>k__BackingField", Any)!.SetValue(npc, new Npc(metadata, null));
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
