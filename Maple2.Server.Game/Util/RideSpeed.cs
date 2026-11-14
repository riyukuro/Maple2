using Maple2.Model.Metadata;
using Maple2.Model.Validators;

namespace Maple2.Server.Game.Util;

// How fast the client moves a mount, and so how far its RideSync positions may go in MoveRules.WindowMs.
// The client sets the rider's speed from the ride's row: Ridee::GetRunSpeed (ridee.cpp:1029) is runSpeed times
// 1 + the RidingSpeed rate (SAType 0xae), Ridee::GetRideSpeed (ridee.cpp:1014, the dash and the swim) runXSpeed times
// 1 + the RidingSpeed rate when runSpeed equals runXSpeed, else 1 + the RidingRunSpeed rate (0xa5); a walk is
// walkSpeed. The move then is that speed times CalcMsiR of the move speed stat (PCBase::vfunc_95, pcbase.cpp:2238;
// the script's calc_msiR is the stat / 100 clipped to 0.07 .. 1.7) times a scale the sync sets to 1.
// A speed of 1 is 100 units a second (the player's own 3.5 is MoveRules' 350 at move speed 100).
public static class RideSpeed {
    public const float UnitsPerSpeed = 100f;
    public const float MaxMoveRate = 1.7f;
    // Server rule: the most the riding rates add, whatever the server's stats hold. KR's additional effects stacked
    // give RidingSpeed 0.11 + 0.10 + 0.04 and RidingRunSpeed 0.21.
    public const float RateAllowance = 0.25f;
    // Server rule: room on top of the client's speed for positions sent in bursts.
    public const float Slack = 1.25f;

    // The farthest the client moves the ride in a second, at the highest move speed rate.
    public static float MaxPerSecond(RideMetadataSpeed speed, double ridingRate, double ridingRunRate) {
        float rate = 1f + MathF.Max(RateAllowance, (float) Math.Max(ridingRate, ridingRunRate));
        float fastest = Max(speed.WalkSpeed, speed.RunSpeed * rate, speed.RunXSpeed * rate, speed.SwimSpeed * rate);
        return fastest * MaxMoveRate * UnitsPerSpeed;
    }

    // The distance MoveWindow allows a ride's positions in MoveRules.WindowMs, never less than a walk's.
    public static float MaxDistance(RideMetadataSpeed speed, double ridingRate, double ridingRunRate) =>
        MathF.Max(MoveRules.MaxDistance,
            MaxPerSecond(speed, ridingRate, ridingRunRate) * MoveRules.WindowMs / 1000f * Slack + InteractRules.ReachSlack);

    private static float Max(params float[] values) {
        float max = 0f;
        foreach (float value in values) {
            if (float.IsFinite(value) && value > max) {
                max = value;
            }
        }
        return max;
    }
}
