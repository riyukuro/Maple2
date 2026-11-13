using System.Numerics;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Game.Util;

// The GM mob-attack command (debug only): which mob casts, and the watch that reports each attack point's hits.
public static class MobAttackRules {
    public readonly record struct Candidate(int ObjectId, Vector3 Position, bool Dead);

    // The mob with the given object id, else the living mob nearest to the GM; 0 when there is none.
    public static int Pick(IEnumerable<Candidate> mobs, int? objectId, Vector3 from) {
        if (objectId is not null) {
            return mobs.Any(mob => mob.ObjectId == objectId.Value) ? objectId.Value : 0;
        }
        int best = 0;
        float bestDistance = float.MaxValue;
        foreach (Candidate mob in mobs) {
            if (mob.Dead) {
                continue;
            }
            float distance = Vector3.DistanceSquared(mob.Position, from);
            if (distance < bestDistance) {
                best = mob.ObjectId;
                bestDistance = distance;
            }
        }
        return best;
    }

    public static string Describe(int skillId, byte attackPoint, IReadOnlyList<(int ObjectId, string Name)> targets) {
        if (targets.Count == 0) {
            return $"mob-attack {skillId} point {attackPoint}: nothing in range";
        }
        return $"mob-attack {skillId} point {attackPoint}: in range " + string.Join(", ", targets.Select(target => $"{target.Name} ({target.ObjectId})"));
    }

    // Reports for 10 seconds after the command (chosen here), for the watched skill only.
    public const long WatchMillis = 10_000;

    public sealed class Watch {
        public int SkillId { get; }
        private readonly long until;
        private readonly Action<string> send;

        public Watch(int skillId, long now, Action<string> send) {
            SkillId = skillId;
            until = now + WatchMillis;
            this.send = send;
        }

        public bool Expired(long now) => now > until;

        public void Report(byte attackPoint, IReadOnlyList<IActor> targets) {
            send(Describe(SkillId, attackPoint, targets.Select(target => (target.ObjectId, Name(target))).ToList()));
        }

        private static string Name(IActor target) => target switch {
            FieldPlayer player => player.Value.Character.Name,
            FieldNpc npc => npc.Value.Metadata.Name ?? "npc",
            _ => "actor",
        };
    }
}
