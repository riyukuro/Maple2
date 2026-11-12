using Maple2.Model.Enum;

namespace Maple2.Model.Metadata;

public record AchievementMetadata(
    int Id,
    string? Name,
    bool AccountWide,
    AchievementCategory Category,
    string[] CategoryTags,
    IReadOnlyDictionary<int, AchievementMetadataGrade> Grades,
    // The achieve XML's locking attribute, of the data folder's pack or the played client's
    // (PlayedClientAchievements): the client shows the trophy locked and KMS2 does not count it.
    bool Locked = false) : ISearchResult;

public record AchievementMetadataGrade(
    int Grade,
    ConditionMetadata Condition,
    AchievementMetadataReward? Reward);

public record AchievementMetadataReward(
    AchievementRewardType Type,
    int Code,
    int Value,
    int Rank);
