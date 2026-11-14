using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Serilog;

namespace Maple2.Server.Game.Trigger.Helpers;

public class TriggerState {
    private readonly Trigger trigger;
    private readonly Trigger.State triggerState;
    private readonly TriggerContext triggerContext;

    public readonly string Name;
    // Set when a transition names a state the script does not have: the state then does nothing more, so
    // the condition's actions do not run again every tick.
    public bool Stopped { get; private set; }
    // No condition and no transition after onEnter: once entered, the state never changes on its own.
    public bool DeadEnd => triggerState.Conditions.Count == 0 && triggerState.Enter?.NextState is null;

    public TriggerState(Trigger trigger, TriggerContext context) {
        this.trigger = trigger;
        triggerState = trigger.States.First();
        triggerContext = context;
        Name = triggerState.Name;
    }

    public TriggerState(Trigger trigger, Trigger.State nextState, TriggerContext context) {
        this.trigger = trigger;
        triggerState = nextState;
        triggerContext = context;
        Name = triggerState.Name;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TriggerState? OnEnter() {
        Trigger.State.OnEnter? onEnter = triggerState.Enter;
        if (onEnter == null) return null;

        foreach (IAction actionNode in onEnter.Actions) {
            ExecuteAction(actionNode);
        }

        if (onEnter.NextState is not null && GetNextState(onEnter.NextState, out Trigger.State? nextState)) {
            return new TriggerState(trigger, nextState, triggerContext);
        }

        return null;
    }


    public TriggerState? OnTick() {
        LinkedList<ICondition> conditions = triggerState.Conditions;
        if (conditions.Count == 0 || Stopped) return null;

        // A state run without a context (the missing-state tests) has no roll to reset.
        triggerContext?.NewConditionPass();
        foreach (ICondition condition in conditions) {
            if (!CallCondition(condition)) continue;

            foreach (IAction actionNode in condition.Actions) {
                ExecuteAction(actionNode);
            }

            if (condition.NextState is not null && GetNextState(condition.NextState, out Trigger.State? nextState)) {
                return new TriggerState(trigger, nextState, triggerContext);
            }

            return null;
        }
        return null;
    }

    // The GM "Next Trigger" key (Trigger 0x04). Chosen here: the first condition of the state that names a
    // transition acts as if it had passed, without being tested: its actions run, then the state changes.
    // A state with no such condition stays.
    public TriggerState? ForceNext() {
        if (Stopped) return null;

        foreach (ICondition condition in triggerState.Conditions) {
            if (condition.NextState is null) continue;

            foreach (IAction actionNode in condition.Actions) {
                ExecuteAction(actionNode);
            }

            return GetNextState(condition.NextState, out Trigger.State? nextState)
                ? new TriggerState(trigger, nextState, triggerContext) : null;
        }
        return null;
    }

    public void OnExit() {
        Trigger.State.OnExit? onExit = triggerState.Exit;
        if (onExit == null) return;
        foreach (IAction actionNode in onExit.Actions) {
            ExecuteAction(actionNode);
        }
    }

    private bool GetNextState(string nextStateName, [NotNullWhen(true)] out Trigger.State? nextState) {
        nextState = trigger.States.FirstOrDefault(x => x.Name == nextStateName);
        if (nextState == null && !Stopped) {
            Stopped = true;
            // A gap in the game data, not a server fault: every such target is missing in the client's own trigger files too.
            Log.Logger.Debug("Trigger state '{State}' moves to '{Next}', which the script does not have: the state stops", Name, nextStateName);
            triggerContext?.ReleasePlayers(Name, nextStateName);
        }
        return nextState != null;
    }

    private void ExecuteAction(IAction action) {
        try {
            action.Execute(triggerContext);
        } catch (Exception e) {
            Log.Logger.Error(e, "Error executing action '{ActionName}'", action.GetType().Name);
        }
    }

    private bool CallCondition(ICondition condition) {
        try {
            return condition.Evaluate(triggerContext);
        } catch (Exception e) {
            Log.Logger.Error(e, "Error evaluating condition '{ConditionName}'", condition.GetType().Name);
            return false;
        }
    }
}
