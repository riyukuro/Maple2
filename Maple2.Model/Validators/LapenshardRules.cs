using Maple2.Model.Metadata;

namespace Maple2.Model.Validators;

public static class LapenshardRules {
    // Lapenshard 5 (upgrade): each material entry is an item uid and an amount. The amount is at least 1 and
    // at most what the stack holds. The stack of the shard being upgraded may be a material only for what it
    // holds beyond the one shard that is upgraded.
    public static bool IsMaterialAmount(int amount, int owned, bool isUpgradedStack) {
        if (amount < 1) {
            return false;
        }

        return amount <= (isUpgradedStack ? owned - 1 : owned);
    }

    // The effects the worn lapenshards give that their wearer does not have: each shard item's additional
    // effects (not its pick-up ones), once each. These effects have no duration, so the logout save does not
    // keep them, and the client only records which shards are worn (MyPC::EquipLapenshard, mypc.cpp:4195);
    // it adds no effect itself. The server gives them when a shard is put on and at every field entry.
    public static List<(int Id, short Level)> MissingEffects(IEnumerable<int> wornItemIds,
            Func<int, IEnumerable<ItemMetadataAdditionalEffect>?> effectsOf, Func<int, short, bool> has) {
        var missing = new List<(int Id, short Level)>();
        foreach (int itemId in wornItemIds) {
            IEnumerable<ItemMetadataAdditionalEffect>? effects = effectsOf(itemId);
            if (effects == null) {
                continue;
            }
            foreach (ItemMetadataAdditionalEffect effect in effects) {
                if (effect.PickUpEffect || effect.Id <= 0 || has(effect.Id, effect.Level) || missing.Contains((effect.Id, effect.Level))) {
                    continue;
                }
                missing.Add((effect.Id, effect.Level));
            }
        }
        return missing;
    }
}
