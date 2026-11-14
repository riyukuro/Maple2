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

    // A ground patrol moves at its patrolSpeed x 100 u/s too. Chosen here: the mine cart of 52000066 (npc 11001898,
    // run 400) is sent along MS2PatrolData_201 (speed 7, 5,175 units of track) by train03's GetInTheTrain03, which
    // waits 7 s, then 3 s, then destroys it; chase01 waits for the rider in boxes 9500-9502 at the track's end. At
    // 700 u/s the cart arrives in about 7.4 s; at its run speed it was destroyed about 4,000 units down the track,
    // the rider left on the rails. A patrol with no speed (or 0) keeps the sequence's own speed, and a speed of 100
    // or more is already in u/s (only 52000085_qd and 52000167_qd: 220-450), so it is taken as it is.
    public const int GroundUnitsThreshold = 100;

    public static float GroundMultiplier(int patrolSpeed, string sequence, float walkSpeed, float runSpeed) {
        if (patrolSpeed <= 0) {
            return 1;
        }
        float baseSpeed = sequence.StartsWith("Walk_") ? walkSpeed : runSpeed;
        if (baseSpeed <= 0) {
            return 1;
        }
        return GroundSpeed(patrolSpeed) / baseSpeed;
    }

    public static float GroundSpeed(int patrolSpeed) => patrolSpeed >= GroundUnitsThreshold ? patrolSpeed : AirSpeed(patrolSpeed);

    // An npc whose model gives no speed for the sequence still moves a patrol that has a speed, at that speed. The
    // client reads an npc's walk and run speed from its model's walkSpeed and runSpeed and keeps 0 without them
    // (npcdata.cpp:654, 1526; both 0 is a stationary npc, CNpcData::IsStationary, npcdata.cpp:798), and 0 x the
    // multiplier left such an npc standing in Walk: 02000026_in's Anos (11003313) on MS2PatrolData_Anos_00 (speed 2),
    // 02000389's 24002926 on MS2PatrolData_2004 (speed 10). The result is the speed MovementState multiplies by 1;
    // 0 keeps the npc's own speed and the multiplier above (also for such an npc on a patrol without a speed).
    public static float SpeedlessBase(int patrolSpeed, string sequence, float walkSpeed, float runSpeed, bool air) {
        float own = sequence.StartsWith("Walk_") ? walkSpeed : runSpeed;
        if (own > 0 || patrolSpeed <= 0) {
            return 0;
        }
        return air ? AirSpeed(patrolSpeed) : GroundSpeed(patrolSpeed);
    }
}
