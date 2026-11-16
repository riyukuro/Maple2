using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using System.Xml;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// Trigger 0x04, the GM "Next Trigger" key: the trigger moves on as if the first condition of its state that
// names a transition had passed (FieldTrigger.RequestNext, TriggerState.ForceNext).
public class NextTriggerTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // a: a condition without a transition (its action must not run), then two that never pass on their own.
    // b: sets "b" on entry, no transition at all.
    private const string Script = "<ms2>"
        + "<state name=\"a\">"
        + "<condition name=\"user_value\" key=\"never\" value=\"1\"><action name=\"set_user_value\" key=\"skipped\" value=\"1\" /></condition>"
        + "<condition name=\"user_value\" key=\"never\" value=\"2\"><action name=\"set_user_value\" key=\"first\" value=\"1\" /><transition state=\"b\" /></condition>"
        + "<condition name=\"user_value\" key=\"never\" value=\"3\"><action name=\"set_user_value\" key=\"second\" value=\"1\" /><transition state=\"a\" /></condition>"
        + "</state>"
        + "<state name=\"b\"><onEnter><action name=\"set_user_value\" key=\"b\" value=\"1\" /></onEnter>"
        + "<condition name=\"user_value\" key=\"never\" value=\"4\"><action name=\"set_user_value\" key=\"inB\" value=\"1\" /></condition>"
        + "</state>"
        + "</ms2>";

    private const int TriggerId = 7;

    private static (FieldManager Field, FieldTrigger Trigger) Setup(TestGameSession test) {
        FieldManager field = test.Session.Field!;
        var document = new XmlDocument();
        document.LoadXml(Script);
        var cache = new TriggerCache(null!);
        cache.AddReplace(("xblock", "next"), TriggerCache.ParseTrigger("xblock", "next", document));
        typeof(FieldManager).GetField("<TriggerCache>k__BackingField", Any)!.SetValue(field, cache);
        // The harness field is built without its constructor.
        typeof(FieldManager).GetField("fieldTriggers", Any)!.SetValue(field, new ConcurrentDictionary<string, FieldTrigger>());
        typeof(FieldManager).GetField(nameof(FieldManager.UserValues), Any)!.SetValue(field, new TriggerUserValues());
        MapMetadata map = field.Metadata;
        typeof(MapMetadata).GetField("<XBlock>k__BackingField", Any)!.SetValue(map, "xblock");

        FieldTrigger? trigger = field.AddTrigger(new TriggerModel(TriggerId, "next", Vector3.Zero, Vector3.Zero));
        Assert.That(trigger, Is.Not.Null);
        return (field, trigger!);
    }

    private static long tick;

    private static void Step(FieldTrigger trigger) {
        tick += 100_000;
        trigger.Update(tick);
    }

    [Test]
    public void NextRunsFirstTransitionConditionAndMovesOn() {
        var test = new TestGameSession();
        test.Start();
        (FieldManager field, FieldTrigger trigger) = Setup(test);
        int Value(string key) => field.UserValues.Get(TriggerId, key);

        Step(trigger); // enters a
        Step(trigger); // a: nothing passes
        Assert.That(Value("first") + Value("second") + Value("skipped") + Value("b"), Is.EqualTo(0));

        trigger.RequestNext();
        Step(trigger); // a -> b through the second condition, the first with a transition
        Assert.That(Value("first"), Is.EqualTo(1));
        Assert.That(Value("skipped"), Is.EqualTo(0));
        Assert.That(Value("second"), Is.EqualTo(0));
        Step(trigger); // enters b
        Assert.That(Value("b"), Is.EqualTo(1));

        // b has no transition: the request is used up and b stays; its condition's action does not run.
        field.UserValues.Set(TriggerId, "b", 0);
        trigger.RequestNext();
        Step(trigger);
        Step(trigger);
        Assert.That(Value("inB"), Is.EqualTo(0));
        Assert.That(Value("b"), Is.EqualTo(0));
    }

    [Test]
    public void WithoutRequestNothingMoves() {
        var test = new TestGameSession();
        test.Start();
        (FieldManager field, FieldTrigger trigger) = Setup(test);
        for (int i = 0; i < 5; i++) {
            Step(trigger);
        }
        Assert.That(field.UserValues.Get(TriggerId, "first"), Is.EqualTo(0));
        Assert.That(field.UserValues.Get(TriggerId, "b"), Is.EqualTo(0));
    }

    private static void Send(TestGameSession test, byte command) =>
        new TriggerHandler().Handle(test.Session, TestGameSession.Packet(writer => writer.WriteByte(command)));

    // Trigger 0x04 as the client sends it (a bare mode byte): from a GM, every trigger of the field moves on.
    [Test]
    public void NextTriggerKey_FromAGm_MovesTheTriggerOn() {
        var test = new TestGameSession();
        test.Start();
        (FieldManager field, FieldTrigger trigger) = Setup(test);
        test.Session.Player.AdminPermissions = AdminPermissions.GameMaster;
        Step(trigger);

        Send(test, 4);
        Step(trigger);

        Assert.That(field.UserValues.Get(TriggerId, "first"), Is.EqualTo(1));
    }

    // The key is bound for every player; from anyone else it is dropped and the trigger stays.
    [Test]
    public void NextTriggerKey_FromAPlayer_IsDropped() {
        var test = new TestGameSession();
        test.Start();
        (FieldManager field, FieldTrigger trigger) = Setup(test);
        Step(trigger);

        Send(test, 4);
        Step(trigger);
        Step(trigger);

        Assert.That(field.UserValues.Get(TriggerId, "first"), Is.EqualTo(0));
        Assert.That(field.UserValues.Get(TriggerId, "b"), Is.EqualTo(0));
    }

    // Trigger 0x08, the cutscene skip (a bare mode byte): the trigger goes to the state its set_skip named.
    [Test]
    public void SkipCutscene_GoesToTheSkipState() {
        var test = new TestGameSession();
        test.Start();
        (FieldManager field, FieldTrigger trigger) = Setup(test);
        Step(trigger);
        trigger.Context.SetSkip("b");

        Send(test, 8);
        Step(trigger);
        Step(trigger);

        Assert.That(field.UserValues.Get(TriggerId, "b"), Is.EqualTo(1));
        Assert.That(field.UserValues.Get(TriggerId, "first"), Is.EqualTo(0));
    }

    // Without a skip state the skip does nothing.
    [Test]
    public void SkipCutscene_WithoutASkipState_ChangesNothing() {
        var test = new TestGameSession();
        test.Start();
        (FieldManager field, FieldTrigger trigger) = Setup(test);
        Step(trigger);

        Send(test, 8);
        Step(trigger);
        Step(trigger);

        Assert.That(field.UserValues.Get(TriggerId, "b"), Is.EqualTo(0));
    }
}
