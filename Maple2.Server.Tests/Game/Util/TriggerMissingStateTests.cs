using System.Collections.Generic;
using Maple2.Server.Game.Trigger;
using Maple2.Server.Game.Trigger.Helpers;

namespace Maple2.Server.Tests.Game.Util;

// A transition to a state the script does not have: the actions run once and the state stops, instead of
// running them again every tick.
public class TriggerMissingStateTests {
    private sealed class CountAction : IAction {
        public int Count;
        public void Execute(TriggerContext context) => Count++;
    }

    private sealed class AlwaysCondition : ICondition {
        public string Name => "true";
        public string? NextState { get; set; }
        public LinkedList<IAction> Actions { get; set; } = new();
        public bool Evaluate(TriggerContext context) => true;
    }

    [Test]
    public void MissingTarget_RunsTheActionsOnceAndStops() {
        var action = new CountAction();
        var condition = new AlwaysCondition { NextState = "Missing" };
        condition.Actions.AddLast(action);
        var trigger = new Trigger([new Trigger.State("Wait", new LinkedList<ICondition>([condition]), null, null)]);
        var state = new TriggerState(trigger, null!);

        for (int tick = 0; tick < 5; tick++) {
            Assert.That(state.OnTick(), Is.Null);
        }

        Assert.That(action.Count, Is.EqualTo(1));
        Assert.That(state.Stopped, Is.True);
    }

    [Test]
    public void KnownTarget_Moves() {
        var condition = new AlwaysCondition { NextState = "End" };
        var trigger = new Trigger([
            new Trigger.State("Wait", new LinkedList<ICondition>([condition]), null, null),
            new Trigger.State("End", null, null, null),
        ]);
        var state = new TriggerState(trigger, null!);

        Assert.That(state.OnTick()?.Name, Is.EqualTo("End"));
        Assert.That(state.Stopped, Is.False);
    }
}
