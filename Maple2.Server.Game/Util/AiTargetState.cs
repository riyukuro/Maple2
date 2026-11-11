using Maple2.Model.Enum;
using Maple2.Model.Game;

namespace Maple2.Server.Game.Util;

/// <summary>
/// The AI state condition (229 in 59 shipped files: holdme 169, grabTarget 60). The client's state sync names the
/// other actor of both states. A player who holds an actor (state 32, the nearest actor in reach; StateHoldData)
/// sends the held actor's object id in optional field 5 (the server's Flag6). A player that an npc's grab skill
/// pins to one of its nodes (state 43, StateDataGrabTarget) sends the grabber's object id in field 0 (Flag1).
/// holdme passes while the target holds this npc, grabTarget while this npc holds the target in its grab.
/// Npcs never take either state, so an npc target never passes.
/// </summary>
public static class AiTargetState {
    /// <summary>The other actor a state sync names for a hold or a grab, else 0.</summary>
    public static int ActorOf(StateSync sync) => sync.State switch {
        ActorState.Hold when sync.Flags.HasFlag(StateSync.Flag.Flag6) => sync.Flag6Unknown1,
        ActorState.GrabTarget when sync.Flags.HasFlag(StateSync.Flag.Flag1) => sync.Flag1Unknown,
        _ => 0,
    };

    public static bool Passes(AiConditionTargetState condition, ActorState targetState, int targetStateActor, int npcObjectId) {
        if (npcObjectId == 0 || targetStateActor != npcObjectId) {
            return false;
        }
        return condition switch {
            AiConditionTargetState.HoldMe => targetState == ActorState.Hold,
            AiConditionTargetState.GrabTarget => targetState == ActorState.GrabTarget,
            _ => false,
        };
    }
}
