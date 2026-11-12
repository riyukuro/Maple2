namespace Maple2.Server.Game.Util;

public static class AiRunaway {
    public const float DefaultRunawayDistance = 1200f;
    public const float MaxRunawayDistance = 5000f;

    /// <summary>
    /// The distance a "runaway" AI node keeps from its target: its till, else the skill's reach (AiSkillReach), else
    /// <see cref="DefaultRunawayDistance"/>. Never unbounded: an int cast of float.MaxValue is int.MinValue.
    /// </summary>

    public static float Distance(int till, float? skillDistance) {
        float distance = till > 0 ? till : skillDistance is > 0 ? skillDistance.Value : DefaultRunawayDistance;
        return Math.Min(distance, MaxRunawayDistance);
    }
}
