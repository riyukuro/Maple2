using System.Collections.Generic;
using System.Linq;
using Maple2.Server.Game.Trigger.Helpers;
using static Maple2.Server.Game.Trigger.Helpers.Trigger;

namespace Maple2.Server.Tests.Game;

// What the field reads from a map's trigger scripts before they run: the combine spawn groups a script starts
// (start_combine_spawn with is_start) and whether it opens a shadow expedition boss gauge. Both look at every state's
// enter and exit actions and at the actions under its conditions, nested groups (all_of, any_one) included.
public class TriggerScriptScanTests {
    private static LinkedList<IAction> Actions(params IAction[] actions) => new(actions);

    private static State StateOf(IAction[]? enter = null, IAction[]? exit = null, params ICondition[] conditions) =>
        new("state", new LinkedList<ICondition>(conditions), enter == null ? null : new State.OnEnter(Actions(enter), null),
            exit == null ? null : new State.OnExit(Actions(exit)));

    private static ICondition Group(IGroupCondition group, IAction[] actions, params ICondition[] conditions) {
        group.Actions = Actions(actions);
        group.Conditions = new LinkedList<ICondition>(conditions);
        return group;
    }

    [Test]
    public void CombineSpawnGroups_AreReadFromEveryPartOfEveryState() {
        var trigger = new Trigger([
            StateOf(enter: [new StartCombineSpawn([1, 2], true)], exit: [new StartCombineSpawn([3], true)]),
            StateOf(null, null,
                Group(new GroupAllOf(), [new StartCombineSpawn([4], true)],
                    Group(new GroupAnyOne(), [new StartCombineSpawn([5], true)]))),
        ]);

        Assert.That(trigger.StartedCombineSpawnGroups(), Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
    }

    // A start_combine_spawn that stops the groups (is_start false) starts none.
    [Test]
    public void ACombineSpawnStop_StartsNoGroup() {
        var trigger = new Trigger([StateOf(enter: [new StartCombineSpawn([1], false)])]);

        Assert.That(trigger.StartedCombineSpawnGroups(), Is.Empty);
    }

    [Test]
    public void AScriptWithNoState_HasNothing() {
        var trigger = new Trigger(null);

        Assert.That(trigger.StartedCombineSpawnGroups(), Is.Empty);
        Assert.That(trigger.OpensShadowGauge(), Is.False);
    }

    [Test]
    public void TheBossGauge_IsFoundUnderANestedCondition() {
        var trigger = new Trigger([
            StateOf(enter: [new StartCombineSpawn([1], true)]),
            StateOf(null, null, Group(new GroupAnyOne(), [], Group(new GroupAllOf(), [new ShadowExpeditionOpenBossGauge(100, "boss")]))),
        ]);

        Assert.That(trigger.OpensShadowGauge(), Is.True);
    }

    [Test]
    public void TheBossGauge_IsNotFoundInAScriptWithoutIt() {
        var trigger = new Trigger([StateOf(enter: [new StartCombineSpawn([1], true)], null, Group(new GroupAnyOne(), []))]);

        Assert.That(trigger.OpensShadowGauge(), Is.False);
    }
}
