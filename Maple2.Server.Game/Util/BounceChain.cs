using Maple2.Model.Enum;

namespace Maple2.Server.Game.Util;

// The bounce chain of an arrow attack, as the client builds it (StateSkill::PickAttackTargets and its bounce step):
// from every first hit, up to BounceCount more hits, each one the nearest object around the last hit (a cylinder of
// the arrow's bounce radius) that the bounce type lets it take.
// - Range and any bounce that may not overlap: no object already in the chain.
// - Chain: no object already in the chain; when none is left and the bounce may overlap, any object around the last
//   hit except the last hit itself (the client picks it at random and starts the chain list again).
// - Pierce and Boomerang are not picked here: their extra hits come from the arrow's flight, one target packet per
//   contact.
public static class BounceChain {
    public static bool PicksAround(BounceType type) => type is BounceType.Range or BounceType.Chain;

    /// <param name="first">The first hit.</param>
    /// <param name="near">The objects around a hit, nearest first (the hit itself may be among them).</param>
    /// <param name="pick">Picks one of the candidates when the chain may start again (the client's random pick).</param>
    public static List<T> Build<T>(T first, int bounceCount, BounceType type, bool overlap, Func<T, IReadOnlyList<T>> near,
            Func<IReadOnlyList<T>, T>? pick = null) where T : class {
        var hits = new List<T> { first };
        if (bounceCount <= 0 || !PicksAround(type)) {
            return hits;
        }

        var chain = new List<T> { first };
        T last = first;
        for (int bounce = 0; bounce < bounceCount; bounce++) {
            IReadOnlyList<T> around = near(last);
            List<T> left = around.Where(obj => !chain.Contains(obj)).ToList();
            T? next;
            if (left.Count > 0) {
                next = left[0];
            } else if (type == BounceType.Chain && overlap) {
                List<T> others = around.Where(obj => obj != last).ToList();
                if (others.Count == 0) {
                    break;
                }
                next = pick != null ? pick(others) : others[Random.Shared.Next(others.Count)];
                chain.Clear();
            } else {
                break;
            }

            hits.Add(next);
            chain.Add(next);
            last = next;
        }
        return hits;
    }
}
