using Maple2.Model.Enum;

namespace Maple2.Model.Metadata;

public record AchievementMetadata(
    int Id,
    string? Name,
    bool AccountWide,
    // The client's category, the id's millions digit (CAchieveData m_nCategory, achievedata.cpp:911): 1 combat,
    // 2 adventure, 3 life; any other digit is None. The trophy totals go by it (AchievementInfo.Add).
    AchievementCategory Category,
    string[] CategoryTags,
    IReadOnlyDictionary<int, AchievementMetadataGrade> Grades,
    // The achieve XML's locking attribute, of the data folder's pack or the played client's
    // (PlayedClientAchievements): the client shows the trophy locked and KMS2 does not count it.
    bool Locked = false,
    // The client's type, the id's ten-millions digit (CAchieveData m_nType, achievedata.cpp:910): only type 2
    // counts for the trophy totals (CAchieveManager::Tally, achievemanager.cpp:725). The ingest sets it for every
    // trophy; the default is for hand-made metadata.
    int Type = 2) : ISearchResult;

public record AchievementMetadataGrade(
    int Grade,
    ConditionMetadata Condition,
    AchievementMetadataReward? Reward);

public record AchievementMetadataReward(
    AchievementRewardType Type,
    int Code,
    int Value,
    int Rank);
