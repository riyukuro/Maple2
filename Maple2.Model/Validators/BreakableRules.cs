using Maple2.Model.Enum;

namespace Maple2.Model.Validators;

// Breakable: the entity id, i64 skill uid, i32 skill id, i16 level, u8 motion point, u8 attack point.
public static class BreakableRules {
    // The client reports a hit only on a breakable that is up. One that is broken or hidden gives nothing again
    // until the server shows it once more.
    public static bool CanBreak(BreakableState state, bool visible) {
        return visible && state == BreakableState.Show;
    }
}
