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

// The move cycle clock of a moving breakable as the client runs it (CBreakableObject::Update,
// breakableobject.cpp:382-392): nTime = server tick + the entity's moveTimeOffset + the packet's added time
// (m_nSyncB), all unsigned 32-bit; a stopped one (m_nSyncA 0) holds m_nSyncC % cycle.
public static class BreakableCycle {
    public static int Clock(int serverTick, int moveTimeOffset, int addedTime) => unchecked(serverTick + moveTimeOffset + addedTime);

    // A restart at now of a cycle stopped at stoppedAt: the stopped span comes off the added time, so the clock
    // goes on from the value it froze at.
    public static int Resume(int addedTime, int stoppedAt, int now) => unchecked(addedTime - (now - stoppedAt));
}
