using Maple2.Model.Enum;

namespace Maple2.Model.Validators;

// Breakable: the entity id, i64 skill uid, i32 skill id, i16 level, u8 motion point, u8 attack point.
public static class BreakableRules {
    // A broken breakable gives nothing again until the server shows it once more: a repeat of the hit packet
    // must not drop or count a second time. Any other state breaks: a box a trigger makes visible keeps the
    // Hide state it was built with.
    public static bool CanBreak(BreakableState state) {
        return state != BreakableState.Break;
    }

    // The hit comes from a tag of one of the player's casts (castKnown), with the cast's skill id and level
    // (sameSkill), after the cast's attack point packets declared at least one tag (pointAttacks).
    public static bool FromCast(bool castKnown, bool sameSkill, int pointAttacks) {
        return castKnown && sameSkill && pointAttacks > 0;
    }
}
