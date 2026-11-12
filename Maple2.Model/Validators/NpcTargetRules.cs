namespace Maple2.Model.Validators;

// A monster's target. A monster fights the living: when its target dies it lets go of it (it stops casting at it
// and, with nobody else to fight, goes back to its patrol), and it does not pick a dead actor as a new target. The
// player is a target again after the revive.
public static class NpcTargetRules {
    public static bool CanTarget(bool targetIsDead, bool invisibleFromNpc) => !targetIsDead && !invisibleFromNpc;

    // An AI node with isKeepBattle keeps the npc in battle, and its target, when the target is only out of sight
    // or the npc may not battle. A target that is gone (dead or off the field) is let go of always.
    public static bool LosesTarget(bool targetGone, bool outOfSightOrNoBattle, bool keepBattle) {
        return targetGone || (outOfSightOrNoBattle && !keepBattle);
    }

    // A walk to or from an actor is over once that actor is no longer the npc's battle target.
    public static bool ChaseEnds(int chasedObjectId, int targetObjectId) => chasedObjectId != targetObjectId;
}
