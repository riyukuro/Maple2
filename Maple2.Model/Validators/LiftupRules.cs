using Maple2.Model.Common;

namespace Maple2.Model.Validators;

// Weapon cubes of a field (rocks, signs and the like a player lifts). A lifted cube is away until its respawn
// time; the client hides it until the tick it is given (ResponseCube 17 and 27).
public static class LiftupRules {
    public static bool IsAway(long until, long now) {
        return until > now;
    }

    // The client compares against its own 32-bit tick count.
    public static int ClientTick(long tick) {
        return unchecked((int) tick);
    }

    // The cubes still away, with the tick each comes back at.
    public static List<(Vector3B Position, int RespawnTick)> Away(IEnumerable<KeyValuePair<Vector3B, long>> lifted, long now) {
        var result = new List<(Vector3B, int)>();
        foreach ((Vector3B position, long until) in lifted) {
            if (IsAway(until, now)) {
                result.Add((position, ClientTick(until)));
            }
        }
        return result;
    }
}
