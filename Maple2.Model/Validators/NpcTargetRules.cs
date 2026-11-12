namespace Maple2.Model.Validators;

// A monster's target. A monster fights the living: when its target dies it lets go of it (it stops casting at it
// and, with nobody else to fight, goes back to its patrol), and it does not pick a dead actor as a new target. The
// player is a target again after the revive.
public static class NpcTargetRules {
    public static bool CanTarget(bool targetIsDead, bool invisibleFromNpc) => !targetIsDead && !invisibleFromNpc;

    // Whether an npc fights another npc of the other side (a monster, friendly 0, against a friendly npc, or the
    // other way round). Npcs carry an attack group and a defense group (npcdata npcAttackGroup, npcDefenseGroup;
    // the client only reads them, the fight is the server's). Chosen here: the groups are bit sets and an npc hits
    // one whose defense group shares a bit with its attack group. The data fits this: monsters are 1/2 and battle
    // npcs 2/1, so they fight each other, while the npcs of 2/4 (a cannon, a guarded object) are not hit by
    // monsters and the monsters of 4/2 or 4/1 only hit those. A talk npc (friendly 2) is never a monster's target.
    public static bool FightsNpc(int attackerFriendly, int attackGroup, int targetFriendly, int defenseGroup) {
        bool otherSide = attackerFriendly == 0 ? targetFriendly == 1 : targetFriendly == 0;
        return otherSide && (attackGroup & defenseGroup) != 0;
    }

    // A script marker: an npc of the Dummy model (nothing is drawn) whose bar the client does not show either
    // (npc propertyTags big_hp_bar, always_display_hp_bar). Scripts spawn it to wait on (a wave runs until a
    // marker of 22 health is destroyed), so a monster that hit it would end the script early; a monster does not
    // fight it. A Dummy with a bar is an object to guard, which monsters do hit. Chosen here, the fight between
    // npcs being the server's.
    public static bool IsScriptMarker(string model, bool showsHpBar) => model == "Dummy" && !showsHpBar;

    // The health change an npc takes: an npc tagged no_die (npc propertyTags) is not brought below 1 health by damage,
    // so a story ally a script waits on stays on the field however long monsters hit it. The client has no rule
    // of its own for the tag (the fight between npcs is the server's); chosen here: its health is held at 1.
    public static long HealthChange(long current, long change, bool noDie) {
        if (!noDie || change >= 0) {
            return change;
        }
        return Math.Max(change, Math.Min(0, 1 - current));
    }

    // An AI node with isKeepBattle keeps the npc in battle, and its target, when the target is only out of sight
    // or the npc may not battle. A target that is gone (dead or off the field) is let go of always.
    public static bool LosesTarget(bool targetGone, bool outOfSightOrNoBattle, bool keepBattle) {
        return targetGone || (outOfSightOrNoBattle && !keepBattle);
    }

    // A walk to or from an actor is over once that actor is no longer the npc's battle target.
    public static bool ChaseEnds(int chasedObjectId, int targetObjectId) => chasedObjectId != targetObjectId;
}
