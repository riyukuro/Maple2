using Maple2.PacketLib.Tools;

namespace Maple2.Model.Validators;

// Attendance (c2s 0x0066). Modes 0, 1, 2 and 3 carry nothing; mode 4 is i32 event id + i64 coupon item
// uid (0 when the box's checkbox is not ticked); mode 8 is i32 event id.
public static class AttendanceRules {
    public static (int EventId, long CouponUid) ReadEarlyParticipation(IByteReader packet) {
        int eventId = packet.ReadInt();
        long couponUid = packet.ReadLong();
        return (eventId, couponUid);
    }

    public static int ReadSelectedEvent(IByteReader packet) {
        return packet.ReadInt();
    }

    // The list index of the reward for the next claim: the first claim gives the first reward.
    // -1 when every reward of the list was claimed (or the stored count is bad).
    public static int RewardIndex(int claimed, int rewardCount) {
        return claimed >= 0 && claimed < rewardCount ? claimed : -1;
    }
}
