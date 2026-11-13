using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Tools.Collision;

namespace Maple2.Server.Game.Util;

// Where an attack's search stands and faces, with the attack's direction (CDetectContext m_kPos and m_nSortMode, and
// the z rotation in degrees its m_kDir and m_kRight come from).
public readonly record struct TargetSort(int Direction, Vector3 Origin, float Degrees);

// A skill range's target choice, as the client's search makes it (CBaseCollision::Detect, after the shape test and
// before the target count): the buff, party and guild filter of targetSelectType, then the stat sort of
// targetStatCompareFunction or, without one, the attack's direction order, then the passing targets of a mode that
// keeps all first. The caller takes the target count from the result.
public static class RangeTargetSelect {
    public static bool Active(this SkillMetadataRangeSelect? select) => select is { Mode: not 0 } or { StatSort: not 0 };

    public static IEnumerable<IActor> Apply(SkillMetadataRangeSelect? select, IActor caster, IEnumerable<IActor> pool,
            SkillRegion shape = SkillRegion.None, TargetSort? sort = null, Random? random = null) {
        List<IActor> targets = pool.ToList();
        List<IActor> passed = [];
        if (select is { Mode: not 0 }) {
            passed = targets.Where(target => Passes(select, caster, target)).ToList();
            // Mode 2 keeps only the passing targets; mode 3 keeps them if there are any, else all; other modes keep
            // all and put the passing ones first once sorted.
            if (select.Mode == 2 || (select.Mode == 3 && passed.Count > 0)) {
                targets = passed;
                passed = [];
            }
        }
        if (select is { StatSort: not 0 }) {
            SortByStat(targets, select.StatSort, select.Stat);
        } else if (sort is { } frame) {
            SortByDirection(targets, shape, frame, random ?? Random.Shared);
        }
        if (passed.Count > 0) {
            var first = new HashSet<IActor>(passed);
            targets = targets.Where(first.Contains).Concat(targets.Where(target => !first.Contains(target))).ToList();
        }
        return targets;
    }

    // The client's exchange sort: each place swaps with every later target that should come before it, so ties keep
    // the order the swaps leave (not a stable sort). Each target's key is worked out once and moves with it.
    private static void ExchangeSort<TKey>(List<IActor> targets, Func<IActor, TKey> key, Func<TKey, TKey, bool> swaps) {
        if (targets.Count < 2) {
            return;
        }
        var keyed = targets.Select(target => (Target: target, Key: key(target))).ToArray();
        for (int i = 0; i < keyed.Length; i++) {
            for (int j = i + 1; j < keyed.Length; j++) {
                if (swaps(keyed[i].Key, keyed[j].Key)) {
                    (keyed[i], keyed[j]) = (keyed[j], keyed[i]);
                }
            }
        }
        for (int i = 0; i < keyed.Length; i++) {
            targets[i] = keyed[i].Target;
        }
    }

    private static void SortByStat(List<IActor> targets, int statSort, BasicAttribute stat) {
        switch (statSort) {
            case 1 or 2:
                ExchangeSort(targets, target => Current(target, stat), (a, b) => statSort == 1 ? a > b : a < b);
                break;
            case 3 or 4:
                ExchangeSort(targets, target => Ratio(target, stat), (a, b) => statSort == 3 ? a > b : a < b);
                break;
        }
    }

    // What the search measures for the direction order (CBaseCollision::TestShape and Detect): for a box or frustum
    // the target's signed offset along the search's right axis (Side, kept signed for directions 1 and 2) and its
    // offset along the facing (Forward), both from planes through the search's origin; for a cylinder the squared
    // distance from its center to the target's shape, for a hole cylinder the distance from its center to the target,
    // on the plane; for directions 5 and 6 the cosine between the facing and the way to the target.
    public readonly record struct Measure(float Side, float Forward, float Distance, float Facing);

    public static Measure Measured(IActor target, SkillRegion shape, TargetSort sort) {
        float radians = sort.Degrees * MathF.PI / 180;
        var forward = new Vector2(MathF.Sin(radians), -MathF.Cos(radians));
        var right = new Vector2(-MathF.Cos(radians), -MathF.Sin(radians));
        Vector2 offset = new Vector2(target.Position.X, target.Position.Y) - new Vector2(sort.Origin.X, sort.Origin.Y);
        float side = 0, ahead = 0, distance = 0, facing = 0;
        switch (shape) {
            case SkillRegion.Box or SkillRegion.Frustum when sort.Direction is 1 or 2 or 3 or 4:
                side = Vector2.Dot(right, offset);
                ahead = MathF.Abs(Vector2.Dot(forward, offset));
                if (sort.Direction is 3 or 4) {
                    side = MathF.Abs(side);
                }
                break;
            case SkillRegion.Cylinder:
                float radius = target.Shape.Polygon is Circle circle ? circle.Radius : 0;
                float gap = MathF.Max(0, offset.Length() - radius);
                distance = gap * gap;
                break;
            case SkillRegion.HoleCylinder:
                distance = offset.Length();
                break;
        }
        if (sort.Direction is 5 or 6) {
            float length = offset.Length();
            facing = length > 1e-6f ? Vector2.Dot(forward, offset / length) : 0;
        }
        return new Measure(side, ahead, distance, facing);
    }

    private static void SortByDirection(List<IActor> targets, SkillRegion shape, TargetSort sort, Random random) {
        if (sort.Direction == 0) {
            // The client's Fisher-Yates shuffle (ShuffleTargets).
            for (int i = targets.Count - 1; i >= 1; i--) {
                int j = random.Next(i + 1);
                if (i != j) {
                    (targets[i], targets[j]) = (targets[j], targets[i]);
                }
            }
            return;
        }
        bool round = shape is SkillRegion.Cylinder or SkillRegion.HoleCylinder;
        int direction = sort.Direction;
        ExchangeSort(targets, target => Measured(target, shape, sort), (a, b) => direction switch {
            2 => a.Side < b.Side,
            1 => a.Side > b.Side,
            3 => round && a.Distance > b.Distance || a.Forward > b.Forward,
            4 => round && a.Distance < b.Distance || a.Forward < b.Forward,
            5 => a.Facing < b.Facing,
            6 => a.Facing > b.Facing,
            _ => false,
        });
    }

    // The range's own exclusions (applyTargetIgnoreNpcRanks, targetHasNotBuffID), which the client's range loader does
    // not read. Chosen here: an npc of a listed class (npcdata class) and an actor holding an enabled buff of that id
    // are no targets of the range, taken out before the target choice and the target count.
    public static bool Filters(this SkillMetadataRange range) => range.IgnoreNpcClasses is { Length: > 0 } || range.HasNotBuffId != 0;

    public static bool Excludes(SkillMetadataRange range, IActor target) {
        if (range.IgnoreNpcClasses is { Length: > 0 } classes && target is FieldNpc npc && classes.Contains(npc.Value.Metadata.Basic.Class)) {
            return true;
        }
        return range.HasNotBuffId != 0 && target.Buffs.EnumerateBuffs(range.HasNotBuffId).Any(buff => buff.Enabled);
    }

    public static bool Passes(SkillMetadataRangeSelect select, IActor caster, IActor target) {
        if (select.BuffId != 0 && !target.Buffs.EnumerateBuffs(select.BuffId)
                .Any(buff => buff.Enabled && (!select.BuffFromCaster || buff.Caster.ObjectId == caster.ObjectId))) {
            return false;
        }
        // The party and guild tests apply between two players only; a party or guild of 0 matches no one.
        if (caster is FieldPlayer casterPlayer && target is FieldPlayer targetPlayer) {
            Maple2.Model.Game.Character owner = casterPlayer.Value.Character;
            Maple2.Model.Game.Character other = targetPlayer.Value.Character;
            if (select.SameParty && (owner.PartyId == 0 || owner.PartyId != other.PartyId)) {
                return false;
            }
            if (select.SameGuild && (owner.GuildId == 0 || owner.GuildId != other.GuildId)) {
                return false;
            }
        }
        return true;
    }

    private static long Current(IActor actor, BasicAttribute stat) => actor.Stats.Values[stat].Current;

    private static float Ratio(IActor actor, BasicAttribute stat) {
        var value = actor.Stats.Values[stat];
        return value.Total > 0 ? (float) value.Current / value.Total : 0f;
    }
}
