using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model.Enum;
using System.Numerics;
using static Maple2.Server.Game.Model.ActorStateComponent.TaskState;

namespace Maple2.Server.Game.Model.ActorStateComponent;

public partial class MovementState {
    private enum WalkType {
        None,
        Direction,
        MoveTo,
        ToTarget,
        FromTarget,
    }

    private Vector3 walkDirection;
    private Vector3 walkTargetPosition;
    private float walkTargetDistance;
    private WalkType walkType = WalkType.None;
    private (Vector3 start, Vector3 end) walkSegment;
    private bool walkSegmentSet;
    private bool walkLookWhenDone = false;
    private AnimationSequenceMetadata? walkSequence = null;
    private float walkSpeed;
    private NpcTask? walkTask = null;
    private bool isFlying;

    private void UpdateMoveSpeed(float speed) {
        Stat moveSpeed = actor.Stats.Values[BasicAttribute.MovementSpeed];

        speedOverride = speed;
        Speed = speed == 0 ? (float) moveSpeed.Current / 100 : speed;
        Speed *= baseSpeed;
    }

    private void StartWalking(string sequence, NpcTask task, float baseOverride = 0) {
        sequence = sequence == "" ? "Run_A" : sequence;
        walkSegmentSet = false;

        emoteActionTask?.Cancel();

        bool isWalking = sequence.StartsWith("Walk_");

        baseSpeed = baseOverride > 0 ? baseOverride : isWalking ? actor.Value.Metadata.Action.WalkSpeed : actor.Value.Metadata.Action.RunSpeed;
        // The caller's UpdateMoveSpeed ran with the last walk's base speed: the speed is taken again with this one.
        UpdateMoveSpeed(speedOverride);
        walkSpeed = Speed;

        // The walk and run sequences play at the model's own rate, not scaled by the move speed: retail captures send 1
        // in 228,300 of 229,224 Walk_ entries (0 in the rest) and in all 94,877 Run_A entries. The client plays an entry's
        // speed from the start of a sequence (CNpc::ApplySyncBlock, CAnimationManager::PlayAnimation), so a move speed of
        // 120 played the walk 120 times over.
        if (actor.Animation.PlayingSequence?.Name == sequence || actor.Animation.TryPlaySequence(sequence, aniSpeed, AnimationType.Misc)) {
            stateSequence = actor.Animation.PlayingSequence;
            walkSequence = stateSequence;
            walkTask = task;

            SetState(ActorState.Walk);
        } else {
            task.Cancel();

            Idle();
        }
    }

    // The npc was put somewhere else under its walk (off a breakable that broke): the segment it was on is dropped,
    // so the next update asks the navigation again and the walk ends there when its path was cleared.
    public void DropWalkSegment() {
        walkSegmentSet = false;
        Velocity = Vector3.Zero;
    }

    public bool IsMovingToTarget() {
        return State == ActorState.Walk && walkType switch {
            WalkType.MoveTo => true,
            WalkType.FromTarget => true,
            WalkType.ToTarget => true,
            _ => false,
        };
    }

    private void StateWalkDirectionUpdate(long tickCount, long tickDelta, float delta) {
        Vector3 newPosition = actor.Position + delta * Speed * walkDirection;

        actor.Navigation!.UpdatePosition(newPosition);

        Velocity = Speed * walkDirection;

        UpdateDebugMarker(actor.Position, debugNpc, tickCount);
        UpdateDebugMarker(actor.Navigation.GetAgentPosition(), debugAgent, tickCount);
    }

    private void StateWalkUpdate(long tickCount, long tickDelta) {
        if (actor.Navigation is null) {
            return;
        }

        UpdateMoveSpeed(speedOverride);

        float delta = (float) tickDelta / 1000;
        if (walkType == WalkType.Direction) {
            StateWalkDirectionUpdate(tickCount, tickDelta, delta);
            return;
        }

        // --- FLYING ADVANCE LOGIC ---
        // If isFlying, use direct advance
        if (walkSequence != null && isFlying) {
            Vector3 target = walkTargetPosition;
            (Vector3 newPos, bool reachedFlying) = actor.Navigation.FlyAdvance(actor.Position, target, Speed, delta);

            if (delta > 0) {
                Velocity = (newPos - actor.Position) / delta;
            }
            actor.Position = newPos;

            if (walkLookWhenDone && (target - actor.Position).LengthSquared() > 0) {
                actor.Transform.LookTo(Vector3.Normalize(target - actor.Position));
            }

            if (reachedFlying) {
                Velocity = Vector3.Zero;
                walkTask?.Completed();
            }
            return;
        }

        Vector3 offset = walkSegmentSet ? walkSegment.end - actor.Position : new Vector3(0, 0, 0);
        float distanceSquared = offset.LengthSquared();
        float tickDistance = delta * Speed;

        if (!walkSegmentSet || distanceSquared < tickDistance * tickDistance) {
            if (walkSegmentSet) {
                actor.Position = walkSegment.end;

                tickDistance -= (float) Math.Sqrt(distanceSquared);
            }

            walkSegment = actor.Navigation.Advance(TimeSpan.FromSeconds(0.15), Speed, out walkSegmentSet);

            offset = walkSegmentSet ? walkSegment.end - actor.Position : new Vector3(0, 0, 0);
            distanceSquared = offset.LengthSquared();

            if (!walkSegmentSet || distanceSquared == 0) {
                Vector3 walkTargetOffset = walkTargetPosition - actor.Position;
                float walkTargetDistance = walkTargetOffset.LengthSquared();

                if (walkLookWhenDone && walkTargetDistance > 0) {
                    actor.Transform.LookTo(Vector3.Normalize(walkTargetOffset));
                }

                walkTask?.Cancel();

                return;
            }

            float distance = (float) Math.Sqrt(distanceSquared);

            walkDirection = (1 / distance) * offset;
            tickDistance = Math.Min(tickDistance, distance);

            actor.Transform.LookTo(walkDirection);
        }

        Velocity = Speed * walkDirection;
        actor.Position += tickDistance * walkDirection;

        Vector3 targetOffset = walkTargetPosition - actor.Position;
        float targetDistance = targetOffset.LengthSquared();
        float travelDistance = Speed * delta;

        UpdateDebugMarker(actor.Position, debugNpc, tickCount);
        UpdateDebugMarker(walkTargetPosition, debugTarget, tickCount);
        UpdateDebugMarker(actor.Navigation.GetAgentPosition(), debugAgent, tickCount);

        bool reached;

        if (walkType == WalkType.MoveTo) {
            reached = targetDistance < travelDistance * travelDistance;
        } else if (walkType == WalkType.ToTarget) {
            reached = targetDistance < walkTargetDistance * walkTargetDistance;
        } else {
            reached = targetDistance >= walkTargetDistance * walkTargetDistance;
        }

        if (reached) {
            Velocity = new Vector3(0, 0, 0);

            if (walkLookWhenDone) {
                actor.Transform.LookTo(Vector3.Normalize(targetOffset));
            }

            walkTask?.Completed();
        }
    }

    public void StateWalkEvent(string keyName) {
        switch (keyName) {
            case "end":
                if (State == ActorState.Walk) {
                    if (actor.Animation.TryPlaySequence(stateSequence!.Name, aniSpeed, AnimationType.Misc)) {
                        stateSequence = actor.Animation.PlayingSequence;
                    }

                    return;
                }
                walkTask?.Cancel();

                break;
            default:
                break;
        }
    }
}
