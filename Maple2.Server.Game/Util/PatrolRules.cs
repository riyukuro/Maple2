namespace Maple2.Server.Game.Util;

// How fast an npc follows a patrol (MS2PatrolData). The client plays what the server sends: each NpcControl entry
// places the npc and gives it the velocity it moves by until the next one (CNpc::ApplySyncBlock, CNpc::vfunc_53), so
// the speed is the server's.
public static class PatrolRules {
    // Air waypoints are flown at patrolSpeed x 100 units a second, whatever the npc's own run or walk speed. Chosen
    // here: the dock boats of 82000000 (patrols 201-224, speed 6) are spawned for lifeTimes that fit their rise and
    // path at about 600 u/s plus the two waypoint waits, and the balloon ride's speed 8 patrol fits its 32 s window
    // at 800 u/s.
    public const float AirUnitsPerSpeed = 100;

    public static float AirSpeed(int patrolSpeed) => patrolSpeed * AirUnitsPerSpeed;

    // The multiplier MovementState takes for a fly task: it scales the sequence's base speed (WalkSpeed for a Walk_
    // sequence, RunSpeed otherwise), so the air speed is divided by that base.
    public static float AirMultiplier(int patrolSpeed, string sequence, float walkSpeed, float runSpeed) {
        float baseSpeed = sequence.StartsWith("Walk_") ? walkSpeed : runSpeed;
        return baseSpeed > 0 ? AirSpeed(patrolSpeed) / baseSpeed : patrolSpeed / 2f;
    }
}
