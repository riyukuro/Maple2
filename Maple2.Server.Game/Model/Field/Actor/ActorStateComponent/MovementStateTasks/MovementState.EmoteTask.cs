using Maple2.Model.Enum;
using Maple2.Server.Game.Model.Enum;
using static Maple2.Server.Game.Model.ActorStateComponent.TaskState;

namespace Maple2.Server.Game.Model.ActorStateComponent;

public partial class MovementState {
    public class NpcEmoteTask : NpcTask {
        private readonly MovementState movement;
        public string Sequence { get; init; }
        private bool IsIdle { get; init; }
        public override bool CancelOnInterrupt => true;
        private float Duration { get; init; }
        // The names of a trigger sequence list still to play after Sequence, in order.
        private readonly Queue<string> next = new();

        public NpcEmoteTask(TaskState taskState, MovementState movement, string sequence, NpcTaskPriority priority, bool isIdle, float duration) : base(taskState, priority) {
            this.movement = movement;
            this.movement.emoteLimitTick = 0;

            Sequence = sequence;
            IsIdle = isIdle;
            Duration = duration;
        }

        public NpcEmoteTask(TaskState taskState, MovementState movement, IReadOnlyList<string> sequences, NpcTaskPriority priority, bool isIdle)
            : this(taskState, movement, sequences[0], priority, isIdle, -1f) {
            for (int i = 1; i < sequences.Count; i++) {
                next.Enqueue(sequences[i]);
            }
        }

        public bool TryNext(out string sequence) => next.TryDequeue(out sequence!);

        protected override void TaskResumed() {
            movement.Emote(this, Sequence, IsIdle, Duration);
        }

        protected override void TaskFinished(bool isCompleted) {
            movement.emoteLimitTick = 0;
            movement.Idle();
        }

        public override string ToString() {
            return $"{GetType().Name} (Priority: {Priority}, Status: {Status}, Sequence: {Sequence})";
        }
    }

    private void Emote(NpcTask task, string sequence, bool isIdle, float duration) {
        if (!CanTransitionToState(ActorState.Emotion)) {
            task.Cancel();
            return;
        }

        if (!actor.Animation.TryPlaySequence(sequence, 1, AnimationType.Misc)) {
            task.Cancel();
            return;
        }

        if (duration > 0) {
            emoteLimitTick = actor.Field.FieldTick + (long) duration;
        }

        emoteActionTask = task;
        stateSequence = actor.Animation.PlayingSequence;

        SetState(isIdle ? ActorState.Idle : ActorState.Emotion);
    }

    // A list plays its next name when one ends; a name the rig cannot play is skipped. False when none is left.
    private bool PlayNext(NpcEmoteTask task) {
        while (task.TryNext(out string sequence)) {
            if (actor.Animation.TryPlaySequence(sequence, 1, AnimationType.Misc)) {
                stateSequence = actor.Animation.PlayingSequence;
                return true;
            }
        }
        return false;
    }
}
