namespace Maple2.Model.Validators;

// A monster's target. A monster fights the living: when its target dies it lets go of it (it stops casting at it
// and, with nobody else to fight, goes back to its patrol), and it does not pick a dead actor as a new target. The
// player is a target again after the revive.
public static class NpcTargetRules {
    public static bool CanTarget(bool targetIsDead, bool invisibleFromNpc) => !targetIsDead && !invisibleFromNpc;
}
