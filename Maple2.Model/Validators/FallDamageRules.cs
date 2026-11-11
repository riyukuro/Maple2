namespace Maple2.Model.Validators;

// StateFallDamage 0x0034: one float, the height fallen. The client sends it only for a height above 0
// and never in a field whose fall damage is switched off.
public static class FallDamageRules {
    public const float FreeHeight = 1000f;

    // The height that hurts, 0 for none: the field allows fall damage, the float is a number, and the
    // fall is longer than the free height.
    public static float Distance(float height, bool fieldFallDamage) {
        if (!fieldFallDamage || !float.IsFinite(height) || height <= FreeHeight) {
            return 0f;
        }
        return height - FreeHeight;
    }
}
