using Maple2.Server.Game.Model.Enum;
using Maple2.Server.Game.Util;
using System.Numerics;
using static Maple2.Server.Game.Model.ActorStateComponent.TaskState;

namespace Maple2.Server.Game.Model.ActorStateComponent;

public partial class MovementState {
    public class NpcStandbyTask : NpcTask {
        private readonly MovementState movement;
        public IActor? Target { get; init; }
        public Vector3 FacePos { get; init; }
        private string Sequence { get; init; }
        private bool IsIdle { get; init; }
        public override bool CancelOnInterrupt => true;

        public NpcStandbyTask(TaskState taskState, MovementState movement, string sequence, NpcTaskPriority priority, bool isIdle) : base(taskState, priority) {
            this.movement = movement;
            Sequence = sequence;
            IsIdle = isIdle;
        }

        protected override void TaskResumed() {
            movement.Standby(this, Target, IsIdle, Sequence, FacePos);
        }
    }

    private void Standby(NpcTask task, IActor? target, bool isIdle, string sequence, Vector3 facePos) {
        Idle(sequence);
        dontIdleOnStateEnd = isIdle;

        // facePos turns the npc to a fixed spot (KR: 614 standby nodes, e.g. the parade cars of
        // AI_CarParadeBlackbeanBroken), ahead of faceTarget as on the skill node.
        Vector3? direction = facePos != default ? AiStandbyFace.Direction(actor.Position, facePos)
            : target is not null ? AiStandbyFace.Direction(actor.Position, target.Position)
            : null;
        if (direction is not null) {
            actor.Transform.LookTo(direction.Value);
        }
    }
}
