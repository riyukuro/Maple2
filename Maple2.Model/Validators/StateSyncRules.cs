namespace Maple2.Model.Validators;

// UserSync 0x0012: the records a player sends are sent on to the other players in the field.
// The client's own record reader takes the byte after the speed only in 0..0x13 and fails the
// whole packet for any other value, so a record outside that range is not sent on.
// It does the same for a sub state byte of 179 or more. The state byte itself is not checked there.
public static class StateSyncRules {
    public const byte MaxByteAfterSpeed = 0x13;
    public const byte SubStateCount = 179;

    public static bool IsRelayable(byte byteAfterSpeed) {
        return byteAfterSpeed <= MaxByteAfterSpeed;
    }

    public static bool IsRelayable(byte subState, byte byteAfterSpeed) {
        return subState < SubStateCount && IsRelayable(byteAfterSpeed);
    }
}
