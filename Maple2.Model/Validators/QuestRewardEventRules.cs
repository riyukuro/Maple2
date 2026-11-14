using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Model.Validators;

// The quest reward event (GameEventQuestAdditionalReward): while it runs, a completed quest of its quest type and
// repeat type pays each reward kind raised by its rate, in ten-thousandths of the amount (10000 doubles it).
public static class QuestRewardEventRules {
    // The eight kinds in the order of QuestAdditionalRewardContext::m_rates (gameeventcontext.h:146) and of the value3
    // attributes; lu is the rue, karma the treva (the quest reward's own names, QuestMapper).
    public static readonly string[] Kinds = ["exp", "meso", "guild_fund", "guild_exp", "guild_coin", "lu", "karma", "menteeCoin"];
    public const int Exp = 0;
    public const int Meso = 1;
    public const int GuildFund = 2;
    public const int GuildExp = 3;
    public const int GuildCoin = 4;
    public const int Rue = 5;
    public const int Treva = 6;
    public const int MenteeCoin = 7;

    // The event of a quest among the events running: the client keeps one per (quest type, repeat type), the last one
    // started (CGameRule::SetQuestAdditionalReward replaces it), so the latest start wins, then the larger id.
    public static QuestAdditionalReward? Pick(IEnumerable<GameEventMetadata> running, QuestType questType, int repeat) {
        return running.Where(metadata => metadata.Data is QuestAdditionalReward data && data.QuestType == (int) questType && data.Repeat == repeat)
            .OrderByDescending(metadata => metadata.StartTime)
            .ThenByDescending(metadata => metadata.Id)
            .Select(metadata => (QuestAdditionalReward) metadata.Data!)
            .FirstOrDefault();
    }

    // The rate of a kind (0 when the row has none).
    public static int Rate(QuestAdditionalReward? reward, int kind) {
        return reward != null && kind >= 0 && kind < reward.Rates.Length ? Math.Max(reward.Rates[kind], 0) : 0;
    }

    // The rate of a reward item (0 when the row does not name it).
    public static int ItemRate(QuestAdditionalReward? reward, int itemId) {
        return reward?.Items.FirstOrDefault(item => item.ItemId == itemId)?.Rate ?? 0;
    }

    // An amount raised by a rate, in float as the client's reward line raises the exp and the meso
    // (CQuest::ShowRewards, questmanager_1.cpp:342 and 354: amount * (1 + rate / 10000)).
    public static long Raise(long amount, int rate) {
        if (amount <= 0 || rate <= 0) {
            return amount;
        }
        return Math.Max(amount, (long) ((float) amount * (1f + (float) rate / 10000f)));
    }

    public static long Raise(long amount, QuestAdditionalReward? reward, int kind) => Raise(amount, Rate(reward, kind));
}
