using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Trigger;
using Maple2.Server.Game.Trigger.Helpers;

namespace Maple2.Server.Tests.Game;

// set_state (a list of the script's states) and use_state (run the next one's on-enter actions).
public class TriggerStateListTests : TriggerTestBase {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private sealed class Record(List<string> log, string name) : IAction {
        public void Execute(TriggerContext context) => log.Add(name);
    }

    // A script of states a, b and c, each of which logs its name when entered.
    private List<string> StartScript() {
        Start();
        var log = new List<string>();
        var states = new[] { "a", "b", "c" }.Select(name => new Trigger.State(name, null,
            new Trigger.State.OnEnter(new LinkedList<IAction>([new Record(log, name)]), null), null)).ToList();
        FieldTrigger owner = (FieldTrigger) typeof(TriggerContext).GetField("owner", Any)!.GetValue(context)!;
        typeof(FieldTrigger).GetField("trigger", Any)!.SetValue(owner, new Trigger(states));
        typeof(FieldTrigger).GetField(nameof(FieldTrigger.Context), Any)!.SetValue(owner, context);
        // The test field skips the FieldManager ctor, so its state lists are made here.
        typeof(FieldManager).GetField(nameof(FieldManager.States), Any)!.SetValue(Field, new Dictionary<int, List<TriggerState>>());
        return log;
    }

    // The list keeps set_state's order, not the script's: with randomize="1" that order is the shuffle, and
    // 61000001_me_001 and 61000005_me shuffle with set_state, then take the states in order with use_state
    // randomize="0". A used-up list, a name the script lacks, or an unset id runs nothing.
    [Test]
    public void UseState_TakesTheStatesInSetStateOrder() {
        List<string> log = StartScript();

        Action("""<action name="set_state" id="1" states="c,x,a" randomize="0" />""");
        for (int i = 0; i < 3; i++) {
            Action("""<action name="use_state" id="1" randomize="0" />""");
        }
        Action("""<action name="use_state" id="2" randomize="0" />""");

        Assert.That(log, Is.EqualTo(new[] { "c", "a" }));
    }

    // randomize="1" on set_state: over many lists, each state comes first sometimes.
    [Test]
    public void SetState_RandomizeShufflesTheOrder() {
        List<string> log = StartScript();

        var first = new HashSet<string>();
        for (int i = 0; i < 60; i++) {
            log.Clear();
            Action("""<action name="set_state" id="1" states="a,b,c" randomize="1" />""");
            Action("""<action name="use_state" id="1" randomize="0" />""");
            first.Add(log.Single());
        }

        Assert.That(first, Is.EquivalentTo(new[] { "a", "b", "c" }));
    }
}
