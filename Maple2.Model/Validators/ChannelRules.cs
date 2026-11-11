namespace Maple2.Model.Validators;

// Channel: i16 channel number.
public static class ChannelRules {
    // The client refuses the change by itself for the channel it is in and while its player is dead.
    // A change to the own channel saved and disconnected the player for nothing; a dead player left its death behind.
    public static bool CanChange(int current, short target, bool dead) {
        return target > 0 && target != current && !dead;
    }
}
