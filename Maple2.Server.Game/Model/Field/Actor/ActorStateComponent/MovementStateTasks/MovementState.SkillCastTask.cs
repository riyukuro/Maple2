using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model.Enum;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Util;
using System.Numerics;
using static Maple2.Server.Game.Model.ActorStateComponent.TaskState;

namespace Maple2.Server.Game.Model.ActorStateComponent;

public partial class MovementState {
    public class NpcSkillCastTask : NpcTask {
        private readonly MovementState movement;

        public SkillRecord? Cast;
        public int SkillId { get; init; }
        public short SkillLevel { get; init; }
        public long SkillUid { get; init; }
        public int FaceTarget;
        public Vector3 FacePos;

        public NpcSkillCastTask(TaskState queue, MovementState movement, int id, short level, int faceTarget, Vector3 facePos, long uid) : base(queue, NpcTaskPriority.BattleAction) {
            this.movement = movement;
            SkillId = id;
            SkillLevel = level;
            SkillUid = uid;
            FaceTarget = faceTarget;
            FacePos = facePos;
        }

        protected override void TaskResumed() {
            movement.SkillCast(this, SkillId, SkillLevel, SkillUid, 0);
        }

        protected override void TaskFinished(bool isCompleted) {
            movement.castSkill = null;
            movement.CastTask = null;
            movement.Idle();
            movement.actor.AppendDebugMessage((isCompleted ? "Finished" : "Canceled") + " cast\n");
        }
    }

    private void SkillCastFaceTarget(SkillMetadata metadata, byte motion, IActor target, int faceTarget) {
        Vector3 offset = target.Position - actor.Position;
        float distance = offset.LengthSquared();
        // A target on the caster's own spot (the caster itself, or one standing on it) gives no direction: keep the facing.
        if (!float.IsFinite(distance) || distance < 1e-5f) {
            return;
        }

        if (faceTarget != 1) {
            if (!metadata.Data.Motions[motion].MotionProperty.FaceTarget || metadata.Data.AutoTargeting is null) {
                return;
            }

            SkillMetadataAutoTargeting autoTargeting = metadata.Data.AutoTargeting;
            if (!AutoTargetFace.Passes(offset, actor.Transform.FrontAxis, autoTargeting.MaxDegree, autoTargeting.MaxDistance, autoTargeting.MaxHeight)) {
                return;
            }

            distance = (float) Math.Sqrt(distance);
            offset *= (1 / distance);
        } else {
            distance = (float) Math.Sqrt(distance);
            offset *= (1 / distance);
        }

        actor.Transform.LookTo(offset);
    }

    private void SkillCast(NpcSkillCastTask task, int id, short level, long uid, byte motion) {
        if (CastTask != task) {
            CastTask?.Cancel();
        }

        if (!CanTransitionToState(ActorState.PcSkill)) {
            task.Cancel();

            return;
        }

        Velocity = new Vector3(0, 0, 0);

        // The npc turns before the cast goes out: the cast and the control entry sent with it carry the new facing.
        if (task.FacePos != new Vector3(0, 0, 0)) {
            actor.Transform.LookTo(Vector3.Normalize(task.FacePos - actor.Position));
        } else if (actor.BattleState.Target is not null && actor.Field.SkillMetadata.TryGet(id, level, out SkillMetadata? metadata) && motion < metadata.Data.Motions.Length) {
            SkillCastFaceTarget(metadata, motion, actor.BattleState.Target, task.FaceTarget);
        }

        SkillRecord? cast = actor.CastSkill(id, level, uid, (int) actor.Field.FieldTick, motionPoint: motion);

        if (cast is null) {
            task.Cancel();

            return;
        }

        AnimationSequenceMetadata? sequence = null;
        SkillCastRules.CastStart start = SkillCastRules.StartOf(cast.Metadata.Data.Motions,
            () => actor.Animation.TryPlaySequence(cast.Motion.MotionProperty.SequenceName, cast.Motion.MotionProperty.SequenceSpeed, AnimationType.Skill, out sequence, skill: cast.Metadata));
        if (start == SkillCastRules.CastStart.Cancel) {
            task.Cancel();

            return;
        }

        CastTask = task;
        castSkill = cast;
        task.Cast = cast;

        if (start == SkillCastRules.CastStart.FireNow) {
            // No animation and no state: the first attack goes out with the cast.
            actor.SkillState.SkillCastAttack(cast, 0, CastTargets());
            task.Completed();

            return;
        }

        //if (faceTarget && actor.BattleState.Target is not null) {
        //    actor.Transform.LookTo(Vector3.Normalize(actor.BattleState.Target.Position - actor.Position));
        //}

        SetState(ActorState.PcSkill);

        stateSequence = sequence;
    }
}
