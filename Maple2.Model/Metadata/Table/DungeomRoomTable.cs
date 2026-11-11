using Maple2.Model.Enum;

namespace Maple2.Model.Metadata;

public record DungeonRoomTable(IReadOnlyDictionary<int, DungeonRoomMetadata> Entries) : Table;

public record DungeonRoomMetadata(
    int Id,
    short Level,
    DungeonPlayType PlayType,
    DungeonGroupType GroupType,
    DungeonCooldownType CooldownType,
    int CooldownValue,
    int DurationTick,
    int LobbyFieldId,
    int[] FieldIds,
    DungeonRoomRewardMetadata Reward,
    DungeonRoomLimitMetadata Limit,
    int PlayerCountFactorId,
    short CustomMonsterLevel,
    int HelperRequireClearCount,
    bool DisabledFindHelper,
    int RankTableId,
    int RoundId,
    bool LeaveAfterCloseReward,
    int[] PartyMissions,
    int[] UserMissions,
    bool MoveToBackupField,
    DungeonTimerType TimerType,
    bool IsExpireTimeOut,
    // The boss board id (BossRanking): the ranking dialog adds one boss board tab per room with one, and asks for
    // its boards with this id. 0 = no board.
    int BossRanking = 0
);

public record DungeonRoomRewardMetadata(
    bool AccountWide,
    int Count,
    int SubRewardCount,
    long Exp,
    float ExpRate,
    long Meso,
    int[] LimitedDropBoxIds,
    int[] UnlimitedDropBoxIds,
    int UnionRewardId,
    int SeasonRankRewardId,
    int ScoreBonusId
);

public record DungeonRoomLimitMetadata(
    int MinUserCount,
    int MaxUserCount,
    int GearScore,
    int MinLevel,
    int RequiredAchievementId,
    bool VipOnly,
    DayOfWeek[] DayOfWeeks,
    int[] ClearDungeonIds,
    int[] Buffs,
    bool EquippedRecommendedWeapon,
    bool PartyOnly,
    bool ChangeMaxUsers,
    bool DisableMeretRevival,
    bool DisableMesoRevival,
    int MaxRevivalCount
);
