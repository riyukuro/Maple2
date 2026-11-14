using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Model.Skill;

public class ReflectRecord {
    public readonly int SourceBuffId;
    public readonly AdditionalEffectMetadataReflect Metadata;

    public int Counter = 0;

    public ReflectRecord(int id, AdditionalEffectMetadataReflect metadata) {
        SourceBuffId = id;
        Metadata = metadata;
    }

    // The damage a hit on the holder sends back (chosen here; the client only loads and compares ReflectProperty,
    // additionalactionadditional.cpp:1306): the hit times the reflect rate of its attack type (physicalReflectionRate,
    // magicalReflectionRate; the larger one for an attack of both), less the reflectionDamageReduceRate part, rounded. The
    // attacker takes it as the next dot tick of the reflect effect (Buff.AddReflected). The rate limits and the
    // reflection values are not applied: no KR level sets them, and the client reads no reflection value.
    public static long Share(AdditionalEffectMetadataReflect metadata, AttackType attackType, long hit) {
        if (hit <= 0) {
            return 0;
        }
        float physical = metadata.Rates.GetValueOrDefault(BasicAttribute.PhysicalAtk);
        float magical = metadata.Rates.GetValueOrDefault(BasicAttribute.MagicalAtk);
        float rate = attackType switch {
            AttackType.Physical => physical,
            AttackType.Magic => magical,
            AttackType.All => Math.Max(physical, magical),
            _ => 0f,
        };
        if (rate <= 0) {
            return 0;
        }
        return (long) Math.Round(hit * (double) rate * (1 - (double) metadata.DamageReduceRate), MidpointRounding.AwayFromZero);
    }

    // A reflect whose attack type has a rate: its effect's ticks deal only the reflected damage.
    public static bool HasShare(AdditionalEffectMetadataReflect metadata) => metadata.Rates.Values.Any(rate => rate > 0);
}
