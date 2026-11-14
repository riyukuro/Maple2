using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Model.Validators;

/// <summary>
/// The special effects (SpecialEffectProperty) of the additional effects a player holds, which the server applies:
/// the client applies only type 4 (cooldown groups). The values are the client data's; what each type acts on is
/// chosen here from the data and the effects' tooltips, as no KMS2 packet shows one taking effect. Each effect rolls
/// its probability on its own (1 always takes effect), and its rates are extras on the base amount: 1 doubles it.
/// </summary>
public static class SpecialEffectRules {
    public const int NpcRankDamage = 2; // damage taken from the npcs of the ranks, less by reduceRate
    public const int WorldBossReward = 3; // no values: not applied
    public const int Enchant = 5; // enchantLevel, improveRate: no base reward to raise, not applied
    public const int CraftCopy = 6; // no values: not applied
    public const int QuestReward = 7; // the exp, meso and rue of a quest of the repeat type
    public const int ItemBoxAgain = 8; // an item box of the ids opens a second time
    public const int Fishing = 9; // fishing exp and mastery
    public const int Music = 10; // music score exp
    public const int ValorToken = 11; // the valor tokens of a won ranked duel
    public const int Rest = 12; // no values: not applied
    public const int ChestDropBox = 13; // a chest of the exp type gives an individual drop box more

    public static bool Rolls(SpecialEffectMetadata effect, Random random) {
        return effect.Probability >= 1 || random.NextDouble() < effect.Probability;
    }

    // The effects of a type that take effect this time: those that apply and win their roll, in the order held.
    public static List<SpecialEffectMetadata> Taken(IEnumerable<SpecialEffectMetadata> held, int type, Random random,
                                                    Func<SpecialEffectMetadata, bool>? applies = null) {
        return held.Where(effect => effect.Type == type && (applies == null || applies(effect)) && Rolls(effect, random)).ToList();
    }

    // The messages of the effects taken (activateMsg string keys), each once.
    public static IEnumerable<string> Messages(IEnumerable<SpecialEffectMetadata> taken) {
        return taken.Select(effect => effect.ActivateMessage).Where(message => !string.IsNullOrEmpty(message)).Distinct();
    }

    // Type 9: a fish caught. onlyBigSize takes effect on a prize fish only (one at the top of its big size).
    public static bool FishingApplies(SpecialEffectMetadata effect, bool prizeFish) {
        return !effect.OnlyBigSize || prizeFish;
    }

    // The exp and mastery factors of the fishing effects taken (1 with none).
    public static (float Exp, float Mastery) FishingFactors(IEnumerable<SpecialEffectMetadata> taken) {
        float exp = 1, mastery = 1;
        foreach (SpecialEffectMetadata effect in taken) {
            exp += effect.ExpRate;
            mastery += effect.MasteryRate;
        }
        return (exp, mastery);
    }

    // Type 10: a score played. An effect that takes effect by chance (110001025, "when played to completion")
    // needs the whole score played; one that always does counts for any score stop.
    public static bool MusicApplies(SpecialEffectMetadata effect, bool wholeScore) {
        return effect.Probability >= 1 || wholeScore;
    }

    // Types 9 and 10: the exp factor of the effects taken.
    public static float ExpFactor(IEnumerable<SpecialEffectMetadata> taken) {
        return 1 + taken.Sum(effect => effect.ExpRate);
    }

    // Type 7: a quest of the effect's repeat type (the quest's Basic repeat, 2 daily) whose rewards have an amount
    // the effect raises (110001021 the exp, 110001022 the meso, 110001023 the rue).
    public static bool QuestApplies(SpecialEffectMetadata effect, int questRepeat, long exp, long meso, long rue) {
        return effect.RepeatType == questRepeat
               && (exp > 0 && effect.ExpRate > 0 || meso > 0 && effect.MesoRate > 0 || rue > 0 && effect.RueRate > 0);
    }

    // Type 7: the extra exp, meso and rue of a quest's rewards, by the effects taken.
    public static (long Exp, long Meso, long Rue) QuestExtra(IEnumerable<SpecialEffectMetadata> taken, long exp, long meso, long rue) {
        float expRate = 0, mesoRate = 0, rueRate = 0;
        foreach (SpecialEffectMetadata effect in taken) {
            expRate += effect.ExpRate;
            mesoRate += effect.MesoRate;
            rueRate += effect.RueRate;
        }
        return ((long) (exp * expRate), (long) (meso * mesoRate), (long) (rue * rueRate));
    }

    // Type 11: the extra valor tokens of a ranked duel win.
    public static int ValorExtra(IEnumerable<SpecialEffectMetadata> taken, int reward) {
        return reward <= 0 ? 0 : (int) (reward * taken.Sum(effect => effect.RewardRate));
    }

    // Type 13: a chest whose opening gives this exp type.
    public static bool ChestApplies(SpecialEffectMetadata effect, ExpType chestExp) {
        return chestExp != ExpType.none && effect.ExpType == chestExp && (effect.IndividualDropBoxId != 0 || effect.GlobalDropBoxId != 0);
    }

    // Type 8: an item box of the effect's item ids.
    public static bool ItemBoxApplies(SpecialEffectMetadata effect, int itemId) {
        return effect.ItemIds?.Contains(itemId) == true;
    }

    // Type 2: the factor of the damage a holder takes from an npc of a rank (its Basic class).
    public static float NpcRankDamageFactor(IEnumerable<SpecialEffectMetadata> held, int npcRank, Random random) {
        float factor = 1;
        foreach (SpecialEffectMetadata effect in Taken(held, NpcRankDamage, random, effect => effect.NpcRanks?.Contains(npcRank) == true)) {
            factor *= Math.Max(0, 1 - effect.ReduceRate);
        }
        return factor;
    }
}
