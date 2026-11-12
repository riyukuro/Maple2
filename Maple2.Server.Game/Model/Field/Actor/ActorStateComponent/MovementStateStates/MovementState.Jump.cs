using System.Numerics;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.Model.ActorStateComponent;

public partial class MovementState {
    // The AI jump in flight: the client flies the npc from the one jump packet over its duration and does not place it
    // from the sync packets until it lands, so the server moves its own position along the same arc meanwhile (the
    // in-air skill's attacks and the players' hits then meet the npc where the client shows it).
    private (Vector3 From, Vector3 To, float Scale, long StartTick, long Duration)? jump;

    public bool IsJumping => jump != null;

    public void StartJump(Vector3 from, Vector3 to, float durationSeconds, float scale) {
        long duration = (long) Math.Max(1, MathF.Round(durationSeconds * 1000));
        jump = (from, to, scale, actor.Field.FieldTick, duration);
        actor.Position = from;
    }

    private void JumpUpdate(long tickCount) {
        if (jump is not { } arc) {
            return;
        }

        long elapsed = tickCount - arc.StartTick;
        if (elapsed >= arc.Duration) {
            actor.Position = arc.To;
            jump = null;
            return;
        }
        actor.Position = AiJump.Point(arc.From, arc.To, arc.Scale, (float) elapsed / arc.Duration);
    }
}
