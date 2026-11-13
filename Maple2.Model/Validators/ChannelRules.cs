namespace Maple2.Model.Validators;

// Channel: i16 channel number.
public static class ChannelRules {
    // The client refuses the change by itself (WorldService::FUN_141bc80c0) for the channel it is in, outside a
    // normal field (the field type of RequestFieldEnter is not 0: a dungeon, a random room), in combat and while its
    // player is dead. A change to the own channel saved and disconnected the player for nothing; a dead player left
    // its death behind; a dungeon or a fight was left by a crafted request.
    public static bool CanChange(int current, short target, bool dead, bool inBattle, bool normalField) {
        return target > 0 && target != current && !dead && !inBattle && normalField;
    }
}
