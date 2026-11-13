using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Trigger;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Tools.Scheduler;
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
