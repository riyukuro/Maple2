namespace Maple2.Server.Game.Trigger.Helpers;

public partial class Trigger {
    public List<State> States { get; }

    public Trigger(List<State>? states) {
        States = states ?? [];
    }

    // The combine spawn groups this script starts (start_combine_spawn with is_start) in any state: on enter, on
    // exit or under a condition.
    public IEnumerable<int> StartedCombineSpawnGroups() {
        foreach (State state in States) {
            IEnumerable<IAction> actions = (state.Enter?.Actions ?? Enumerable.Empty<IAction>())
                .Concat(state.Exit?.Actions ?? Enumerable.Empty<IAction>())
                .Concat(ConditionActions(state.Conditions));
            foreach (IAction action in actions) {
                if (action is StartCombineSpawn { IsStart: true } start) {
                    foreach (int groupId in start.GroupIds) {
                        yield return groupId;
                    }
                }
            }
        }
    }

    // Whether any state opens a boss gauge (shadow_expedition_open_boss_gauge): on enter, on exit or under a condition.
    public bool OpensShadowGauge() {
        return States.Any(state => (state.Enter?.Actions ?? Enumerable.Empty<IAction>())
            .Concat(state.Exit?.Actions ?? Enumerable.Empty<IAction>())
            .Concat(ConditionActions(state.Conditions))
            .Any(action => action is ShadowExpeditionOpenBossGauge));
    }

    private static IEnumerable<IAction> ConditionActions(IEnumerable<ICondition> conditions) {
        foreach (ICondition condition in conditions) {
            foreach (IAction action in condition.Actions) {
                yield return action;
            }
            if (condition is IGroupCondition group) {
                foreach (IAction action in ConditionActions(group.Conditions)) {
                    yield return action;
                }
            }
        }
    }

    public class State {
        public string Name { get; }

        public OnEnter? Enter { get; }
        public LinkedList<ICondition> Conditions { get; }
        public OnExit? Exit { get; }

        public State(string name, LinkedList<ICondition>? conditions, OnEnter? enter, OnExit? exit) {
            Name = name;
            Conditions = conditions ?? new LinkedList<ICondition>();
            Enter = enter;
            Exit = exit;
        }

        public class OnEnter {
            public string? NextState { get; set; }
            public LinkedList<IAction> Actions { get; }

            public OnEnter(LinkedList<IAction>? actions, string? nextState) {
                Actions = actions ?? new LinkedList<IAction>();
                NextState = nextState;
            }
        }

        public class OnExit {
            public LinkedList<IAction> Actions { get; }

            public OnExit(LinkedList<IAction>? actions) {
                Actions = actions ?? new LinkedList<IAction>();
            }
        }
    }
}

public interface IAction {
    void Execute(TriggerContext context);
}

public interface ICondition {
    string Name { get; }
    string? NextState { get; set; }
    LinkedList<IAction> Actions { get; set; }

    bool Evaluate(TriggerContext context);
}

public interface IGroupCondition : ICondition {
    LinkedList<ICondition> Conditions { get; set; }
}
