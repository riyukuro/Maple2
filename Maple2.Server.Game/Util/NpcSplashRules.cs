using System.Numerics;
using Maple2.Model.Metadata;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Util;

// Where an npc attack puts its region (splash) skills.
// The skill data marks a splash that needs no hit target (nonTargetActive). An attack that fires along a magic path
// (distance and velocity set) delivers such a splash where the path ends: KMS2 adds the region on the cell of the
// monster the shot was aimed at, about distance / velocity after the cast, far outside the attack's own range.
public static class NpcSplashRules {
    public enum Place {
        None, // no splash without a target
        Targets, // at the targets hit in the attack's range
        Caster, // at the cast position (the cube magic path's points when the attack has one): chosen here
        Landing, // where each magic path ends
    }

    public static bool NoTarget(SkillEffectMetadata effect) => effect.Splash is { NonTargetActive: true };

    public static bool Flies(MagicPath path) => path.Distance > 0 && path.Velocity > 0;

    public static Place Where(bool noTargetSplash, bool flies, int targetsInRange) {
        if (noTargetSplash && flies) {
            return Place.Landing;
        }
        if (targetsInRange > 0) {
            return Place.Targets;
        }
        return noTargetSplash ? Place.Caster : Place.None;
    }

    // A shot aimed at an actor lands on its cell.
    public static Vector3 LandPoint(Vector3 aimed) => aimed.Align();

    // A shot without an aim flies the path's distance along its direction (turned by the caster's facing when the
    // path rotates) and ends at the path's end offset height: chosen here.
    public static Vector3 EndPoint(Vector3 from, Vector3 rotation, MagicPath path) {
        Vector3 direction = path.Direction.LengthSquared() > 1e-6f ? Vector3.Normalize(path.Direction) : Vector3.UnitY;
        if (path.Rotate) {
            direction = direction.Rotate(rotation);
        }
        return (from + direction * path.Distance + new Vector3(0, 0, path.ControlEndOffsetValue.Z)).Align();
    }

    // The flight lasts the ground distance at the path's velocity.
    public static TimeSpan Flight(Vector3 from, Vector3 to, MagicPath path) {
        if (path.Velocity <= 0) {
            return TimeSpan.Zero;
        }
        float distance = new Vector2(to.X - from.X, to.Y - from.Y).Length();
        return TimeSpan.FromSeconds(distance / path.Velocity);
    }

    // The actors a shot can be aimed at: inside the path's distance on the ground, nearest first.
    public static IEnumerable<T> Aims<T>(IEnumerable<T> pool, Func<T, Vector3> position, Vector3 from, MagicPath path) {
        return pool
            .Select(actor => (Actor: actor, Distance: new Vector2(position(actor).X - from.X, position(actor).Y - from.Y).Length()))
            .Where(entry => entry.Distance <= path.Distance + AimSlack)
            .OrderBy(entry => entry.Distance)
            .Select(entry => entry.Actor);
    }

    // One block past the path's distance still counts: chosen here.
    public const float AimSlack = 150f;
}
