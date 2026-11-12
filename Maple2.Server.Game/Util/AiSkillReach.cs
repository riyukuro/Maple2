using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

/// <summary>
/// How far a skill reaches for the AI's trace node and skillRange condition, read as the client reads a skill's reach
/// when it picks a target for it (PCBase::vfunc_101 @0x141672de0): a cylinder around the caster whose radius is the
/// level's autoTargeting maxDistance, or, when that is 0, the current attack's range value
/// (SkillLevelData::GetAttackRangeValue: the range's distance, or its endWidth for a hole cylinder). Its height is
/// autoTargeting maxHeight, or 150. The skill's detectProperty distance is not part of it: it is 0 in most npc skills
/// (147 of the 198 skills of the lv10-30 field bosses and elites), which left the condition true only within 10 units.
/// The client also moves the centre by the attack's offset unless the attack method is 1; that flag is not in our
/// data, so the centre stays on the caster (6 of those 198 skills have an offset).
/// </summary>
public static class AiSkillReach {
    public const float DefaultHeight = 150f;
    // The walk stops at the reach, so a target on the edge still counts (the server's old allowance).
    public const float Slack = 10f;

    public static float Radius(float autoTargetingMaxDistance, SkillMetadataRange? range) {
        if (autoTargetingMaxDistance > 0) {
            return autoTargetingMaxDistance;
        }
        if (range is null) {
            return 0;
        }
        return range.Type == SkillRegion.HoleCylinder ? range.EndWidth : range.Distance;
    }

    public static float Radius(SkillMetadataLevel level) {
        SkillMetadataRange? range = level.Motions.FirstOrDefault()?.Attacks.FirstOrDefault()?.Range;
        return Radius(level.AutoTargeting?.MaxDistance ?? 0, range);
    }

    public static float Height(SkillMetadataLevel level) {
        float height = level.AutoTargeting?.MaxHeight ?? 0;
        return height > 0 ? height : DefaultHeight;
    }

    /// <summary>
    /// Whether the target stands in the reach cylinder. The client's cylinder is either centred on the caster or
    /// stands on its feet (it lowers the centre by half a given height); a height above or below the caster's feet
    /// counts both ways here.
    /// </summary>
    public static bool InReach(Vector3 caster, Vector3 target, float radius, float height) {
        Vector2 flat = new(target.X - caster.X, target.Y - caster.Y);
        float reach = radius + Slack;
        return flat.LengthSquared() <= reach * reach && MathF.Abs(target.Z - caster.Z) <= height;
    }
}
