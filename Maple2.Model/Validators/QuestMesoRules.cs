using Maple2.Model.Enum;

namespace Maple2.Model.Validators;

// The meso a quest's complete reward gives, as the client shows it in the reward line (FUN_1407622b0 and its caller
// FUN_140761cc0, questmasktable.cpp:855): with a relativeMeso, the reward contents row's meso at the player's level,
// else the fixed money; then the multicontrol questRewardMeso factor of the quest type, unless it is 1; then the
// guild bonus (guild quests with a relativeMeso only). The quest's own meso rate (CGloabalFactor::GetMesoRate) is 1
// on this server, which sends every global rate as 1.
public static class QuestMesoRules {
    // relativeMeso: the row's meso (RewardContentTable.RewardMeso) or null without a relativeMeso.
    // guildMesoFactor: the guild quest npc level's userMeso factor, or null when it does not apply.
    public static long Meso(int fixedMoney, long? relativeMeso, float typeFactor, float? guildMesoFactor) {
        long value = relativeMeso ?? fixedMoney;
        float bonus = 0f;
        if (relativeMeso != null && guildMesoFactor is >= 1.0f) {
            bonus = guildMesoFactor.Value - 1.0f;
        }
        if (Math.Abs(1.0f - typeFactor) > 1e-6f) {
            value = (long) ((float) value * typeFactor);
        }
        return (long) ((float) value * (1.0f + bonus));
    }
}
