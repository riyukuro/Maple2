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
    // 0 keeps the npc's own speed and the multiplier above. On a patrol without a speed such an npc takes its other
    // speed (Chosen here: 645 KR npcs have a walk speed and no run speed; 52000068_qd's guard 11001667, walk 120, runs
    // MS2PatrolData_soldier with Run_A and no speed, and stood where it spawned).
    public static float SpeedlessBase(int patrolSpeed, string sequence, float walkSpeed, float runSpeed, bool air) {
        bool walking = sequence.StartsWith("Walk_");
        if ((walking ? walkSpeed : runSpeed) > 0) {
            return 0;
        }
        if (patrolSpeed <= 0) {
            return Math.Max(walking ? runSpeed : walkSpeed, 0);
        }
        return air ? AirSpeed(patrolSpeed) : GroundSpeed(patrolSpeed);
    }

    // How long an npc holds a waypoint's ArriveAnimation (MS2WayPoint.ArriveAnimationTime, in ms; the emote loops the
    // sequence until then). The client does not read patrols, so the time is the server's. In the KR maps 549
    // waypoints give a time, 508 of them 100 or more (3000 on 166, 1000 on 73, 1666 on 48: 30 fps frame counts in ms).
    // Chosen here: the 41 under 100 (1 to 99 on 22 maps: 52000049_qd's 1, 52000045_qd's 15 and 60) do not read as ms, so they keep
    // one play of the sequence (-1), as a waypoint without a time does.
    public const int ArriveTimeThreshold = 100;

    public static float ArriveDuration(int arriveAnimationTime) => arriveAnimationTime >= ArriveTimeThreshold ? arriveAnimationTime : -1f;

    // A waypoint's sequence name as the npc's model names it: 53 KR waypoints name "Run_A " with a trailing space,
    // which no model has, so the npc walked those legs (Walk_A, the fallback) at its walk speed instead of running.
    public static string SequenceName(string? name) => name?.Trim() ?? string.Empty;
}
