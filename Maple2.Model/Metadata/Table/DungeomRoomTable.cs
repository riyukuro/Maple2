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
    int BossRanking = 0,
    // limitEnter: the room is entered only while its reward record has a count left (DungeonRoomRules.HasCountLeft).
    bool LimitEnter = false,
    // extraRewardTicketTableID with its coupon row: the ticket that buys an extra reward count. null without one.
    DungeonExtraRewardTicket? ExtraRewardTicket = null,
    // findHelperRewardDropBoxIds: the boxes a veteran's helper reward comes from (DungeonManager.GiveHelperReward).
    int[]? HelperRewardDropBoxIds = null
);

// A room's extra reward ticket: the room's extraRewardTicketBaseCount and the Table/DungeonRewardCoupon.xml row of
// its extraRewardTicketTableID (DungeonRewardCoupon::Load, commonfiles/dungeonrewardcoupon.cpp:74). BaseRates is the
// rate list of the coupon (baseRate v), MaxExtraCount 0 when the row sets none.
public record DungeonExtraRewardTicket(
    int CouponId,
    int BaseCount,
    ItemTag TicketTag,
    int BaseItemId,
    int MaxExtraCount,
    int[] BaseRates,
    bool CanBuyInMeratMarket);

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
