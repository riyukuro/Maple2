using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using System.Xml;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
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
}
