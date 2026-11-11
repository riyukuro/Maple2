namespace Maple2.Model.Validators;

// UserSync 0x0012: the records a player sends are sent on to the other players in the field.
// The client's own record reader takes the byte after the speed only in 0..0x13 and fails the
// whole packet for any other value, so a record outside that range is not sent on.
public static class StateSyncRules {
    public const byte MaxByteAfterSpeed = 0x13;

    public static bool IsRelayable(byte byteAfterSpeed) {
        return byteAfterSpeed <= MaxByteAfterSpeed;
    }
}
