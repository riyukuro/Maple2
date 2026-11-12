using System.Text.Json.Serialization;
using Maple2.Model.Enum;
using Maple2.Model.Game;
// ReSharper disable InconsistentNaming

namespace Maple2.Model.Metadata;

public record GameEventTable(IReadOnlyDictionary<int, GameEventMetadata> Entries) : ServerTable;

public record GameEventMetadata(
    int Id,
    GameEventType Type,
    DateTime StartTime,
    DateTime EndTime,
    TimeSpan StartPartTime,
    TimeSpan EndPartTime,
    DayOfWeek[] ActiveDays,
    GameEventData Data,
    string Value1,
    string Value2,
    string Value3,
    string Value4
);

public record StringBoard(
    string Text,
    int StringId) : GameEventData;

public record StringBoardLink(
    string Link) : GameEventData;

public record MeretMarketNotice(
    string Text) : GameEventData;

public record TrafficOptimizer(
    int RideSyncInterval,
    int UserSyncInterval,
    int LinearMovementInterval,
    int GuideObjectSyncInterval) : GameEventData;

public record LobbyMap(
    int MapId) : GameEventData;

public record EventFieldPopup(
    int MapId) : GameEventData;

public record Rps(
    int GameTicketId,
    Rps.RewardData[] Rewards,
    string ActionsHtml) : GameEventData {

    public record RewardData(
        int PlayCount,
        RewardItem[] Rewards);
}

// value1 and value2 of a CoupleDance row, as CoupleDanceContext::LoadReward and LoadDance (gameeventcontext.cpp) read
// them: the daily rewards in "order", the good-end bonus, the dances, the round count, the start field and the eight
// steps (start, play.ready, play.select, play.waitResult, play.result.success, play.result.fail, end.normal, end.good).
public record CoupleDance(
    RewardItem[][] DailyRewards,
    int GoodEndCount,
    RewardItem[] GoodEndRewards,
    string[] Dances,
    int MaxRound,
    int FieldId,
    CoupleDance.Step[] Steps) : GameEventData {

    public record Step(
        string Action,
        string Message);
}

public record SaleChat(
    int WorldChatDiscount,
    int ChannelChatDiscount) : GameEventData;

public record AttendGift(
    RewardItem[] Items,
    string Name,
    string MailTitle,
    string MailContent,
    string Link,
    int RequiredPlaySeconds,
    AttendGift.Require? Requirement,
    AttendGift.PayAttend? Pay = null) : GameEventData {

    // value2's payAttend node: a missed day bought for Price mesos or merets, MaxCount times at most,
    // or for one item of CouponItemId.
    public record PayAttend(
        AttendGiftCurrencyType Type,
        long Price,
        int MaxCount,
        int CouponItemId);

    public record Require(
        AttendGiftRequirement Type,
        int Value1,
        int Value2); // Value2 is used for ItemID type. this is duration in days ?
};

public record BlueMarble(
    ItemComponent RequiredItem,
    BlueMarble.Round[] Rounds,
    BlueMarble.Slot[] Slots) : GameEventData {

    public record Round(
        int RoundCount,
        ItemComponent Item);

    public record Slot(
        BlueMarbleSlotType Type,
        int MoveAmount,
        ItemComponent Item);
}

public record ReturnUser(
    int SeasonId,
    int[] QuestIds,
    int RequiredUserValue,
    DateTimeOffset DateInactiveSince,
    int DaysInactive,
    int RequiredLevel) : GameEventData;

public record NewUser(
    int SeasonId,
    DateTimeOffset DateCreatedBy) : GameEventData;

public record ReturnUserCandidate(
    int Season,
    int SeasonId,
    int MinLevel,
    DateTimeOffset UnknownDate) : GameEventData;

public record ActiveUser(
    int MailId,
    int MailDaysExpire,
    int Meret,
    int MinLevel
    ) : GameEventData;

/// <summary>
/// Year round event that gives users a mail and an item.
/// </summary>
/// <param name="MailId">ID of the mail.</param>
/// <param name="Item">Item given in mail.</param>
/// <param name="MinDaysInactive">In the start of the event, the minimum amount of days a user has to be inactive.</param>
/// <param name="CooldownDays">After the user has already been considered a Return User, there must be this amount of days before the user can be considered a Return User again.</param>
public record ReturnUserYearRound(
    int MailId,
    RewardItem Item,
    int MinDaysInactive,
    int CooldownDays) : GameEventData;

public record FieldEffect(
    int[] MapIds,
    string Effect) : GameEventData;

/// <summary>
/// Lets players place reactors (CreateReactor items) whose reactor.xml row names this event.
/// MapIds: the fields a reactor may be placed in. DailyCount: the client refuses a placement once its
/// counter for the event reaches it.
/// </summary>
public record Reactor(
    int[] MapIds,
    int DailyCount) : GameEventData;

/// <summary>
/// Marks fields on the world map. Xml is the event's value1 as the client parses it:
/// &lt;ms&gt;&lt;v fieldID iconFrame eventStr offsetX offsetY scale /&gt;...&lt;/ms&gt;.
/// </summary>
public record WorldmapIcon(
    string Xml) : GameEventData;

/// <summary>
/// More dungeon reward counts while the event runs (GameEventDungeonExtraReward, gameevent.cpp:1880): value1 is
/// &lt;ms2&gt;&lt;v dungeonID count cooldownType cooldownValue /&gt;...&lt;/ms2&gt;. A clear of a room past its reward count
/// takes one of the room's Count until the cooldown ends (DungeonRecordCli::IsExtraExhausted). The client is sent
/// the room and the count only (DungeonExtraRewardCountCtx operator&lt;&lt;, gameeventcontext.cpp:740).
/// </summary>
public record DungeonExtraReward(
    DungeonExtraReward.Entry[] Entries) : GameEventData {
    public record Entry(
        int DungeonId,
        int Count,
        DungeonCooldownType CooldownType,
        int CooldownValue);
}

/// <summary>
/// Enables quests with eventTag to be enabled/active.
/// </summary>
public record QuestTag(
    string Tag) : GameEventData;

/// <summary>
/// Attendance-like event. Gives rewards for the specified duration a user is logged in.
/// </summary>
public record DTReward(
    DTReward.Entry[] Entries
) : GameEventData {
    public record Entry(
        int StartDuration,
        int EndDuration,
        int MailContentId,
        RewardItem Item);
}

/// <summary>
/// Enables the specified items to be shown/bought in the building menu
/// </summary>
public record ConstructShowItem(
    int CategoryId,
    string CategoryName,
    int[] ItemIds) : GameEventData;

/// <summary>
/// Enables building on the specified maps.
/// TODO: Still need to add player account ID to the list of participants. Check packet.
/// </summary>
public record MassiveConstructionEvent(
    int[] MapIds) : GameEventData;

/// <summary>
/// Discount amount for buying new UGC plots.
/// </summary>
/// <param name="DiscountAmount">Discount amount in a whole int. ex. 9000 = 90% discount.</param>
public record UGCMapContractSale(
    int DiscountAmount) : GameEventData;

/// <summary>
/// Discount amount for extending UGC plots.
/// </summary>
/// <param name="DiscountAmount">Discount amount in a whole int. ex. 9000 = 90% discount.</param>
public record UGCMapExtensionSale(
    int DiscountAmount) : GameEventData;

/// <summary>
/// Schwanda's Gallery event
/// </summary>
/// <param name="QuestIds">Quest Ids</param>
/// <param name="RewardItems"></param>
/// <param name="RevealDayLimit"></param>
/// <param name="Image"></param>
public record Gallery(
    int[] QuestIds,
    RewardItem[] RewardItems,
    int RevealDayLimit,
    string Image) : GameEventData;

/// <summary>
/// Bingo Event
/// </summary>
/// <param name="PencilItemId">Normal pencil item ID used for standard crossing out daily bingo numbers</param>
/// <param name="PencilPlusItemId">Plus pencil item ID used for crossing out any number</param>
/// <param name="Numbers">The bingo numbers selected per day. MUST have enough days for the amount of days the event will run for.</param>
/// <param name="Rewards">The rewards for the amount of bingos you get.</param>
public record BingoEvent(
    int PencilItemId,
    int PencilPlusItemId,
    int[][] Numbers,
    BingoEvent.BingoReward[] Rewards
) : GameEventData {

    public record BingoReward(
        RewardItem[] Items);
}

/// <summary>
/// Race Against Time Event
/// </summary>
/// <param name="StartItemId">Item needed to start the event.</param>
/// <param name="Quests">All the quests from using the StartItemId. Determines the distance user will travel and the day the quest is available. The timer on the event UI is determined by the start time of the quests.</param>
/// <param name="StepRewards">Rewards claimable for reaching a certain distance.</param>
/// <param name="FinalReward">Final reward</param>
public record TimeRunEvent(
    int StartItemId,
    TimeRunEvent.Quest[] Quests,
    IReadOnlyDictionary<int, RewardItem> StepRewards,
    RewardItem FinalReward) : GameEventData {
    public record Quest(
        int Id,
        int Distance,
        int OpeningDay);
}


/// <summary>
/// Stamp card: each stamp is a quest, done when the quest is completed. A reward row is claimed once as many
/// stamps of its kind (special or not) are done; the complete reward once all are.
/// </summary>
/// <param name="EventTag">value1 tag event: the dialog's frame name.</param>
/// <param name="Key">value4: 0, or 10001/10002 for the cards the client picks by a user value.</param>
/// <param name="Stamps">value1 stamp rows.</param>
/// <param name="Rewards">value2 reward rows.</param>
/// <param name="CompleteReward">value3 "id,rank,count": the reward for the full card.</param>
public record StampEvent(
    string EventTag,
    int Key,
    StampEvent.Stamp[] Stamps,
    StampEvent.Reward[] Rewards,
    RewardItem CompleteReward) : GameEventData {
    // x and y place the stamp on the card.
    public record Stamp(
        int QuestId,
        bool Special,
        int X,
        int Y);

    // count = the stamps of the row's kind it takes; item01 and item02 are "id,rank,count".
    public record Reward(
        int Count,
        bool Special,
        RewardItem Item01,
        RewardItem Item02);
}

/// <summary>
/// Long-term attendance calendar: a day is attended with an attend point (one a day) or for Merat merets, and
/// each reward row is claimed once its day is attended.
/// </summary>
/// <param name="Title">value1 info title: the dialog's title string key.</param>
/// <param name="EventTag">value1 info eventTag: the dialog's frame name.</param>
/// <param name="MaxAttendance">value1 info maxattendance: the days of the calendar.</param>
/// <param name="Merat">value1 info merat: the merets one missed day costs.</param>
/// <param name="Rewards">value2 reward rows.</param>
public record LongTermAttendGift(
    string Title,
    string EventTag,
    int MaxAttendance,
    int Merat,
    LongTermAttendGift.Reward[] Rewards) : GameEventData {
    // count = the attended day the row is claimed on; item01 and item02 are "id,rank,count".
    public record Reward(
        int Day,
        RewardItem Item01,
        RewardItem Item02);
}

/// <summary>
/// Enables Maple Survival (Mushking Royale). Possibly does not work.
/// </summary>
public record MapleSurvivalOpenPeriod : GameEventData;


/// <summary>
/// Closes Maple Survival (Mushking Royale)
/// </summary>
public record ShutdownMapleSurvival : GameEventData;

/// <summary>
/// Creates a popup for a On Time event. Clicking Participate triggers the user to send /AddOntimeEvent
/// </summary>
public record Ontime : GameEventData;

/// <summary>
/// Sends a mail with an item to the user every day.
/// </summary>
/// <param name="MailId"></param>
/// <param name="Item"></param>
public record DailyLoginReward(
    int MailId,
    RewardItem Item) : GameEventData;

public record SaleAutoPlayInstrument(
    int Discount,
    string ContentType) : GameEventData;

public record SaleAutoFishing(
    int Discount,
    string ContentType) : GameEventData;

// The level-up package (GameEventLevelUpPackage, commonfiles/gameevent.cpp:2541): value1 is the quest scroll item
// that starts the package's quests, value2 their ids as the row writes them ("a,b,c"). The client keeps both
// (CGameRule::SetLevelUpPackage) and its menu opens the package window while one of the quests is active.
public record LevelUpPackage(
    int ItemId,
    string QuestIds) : GameEventData;

// The hours a time-limited dungeon room is open (GameEventDungeonEnterTimeLimit, commonfiles/gameevent.cpp:2323):
// value1 is an xml list of dungeonID and rangeTime "hh:mm-hh:mm".
public record DungeonEnterTimeLimit(
    DungeonEnterTime[] Times) : GameEventData;

// Start and end as seconds of the day, both inside the range (Unk_DayTimeRange::Contains).
public record DungeonEnterTime(
    int DungeonId,
    int StartSeconds,
    int EndSeconds);

public record LoginNotice : GameEventData;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "!")]
[JsonDerivedType(typeof(StringBoard), "StringBoard")]
[JsonDerivedType(typeof(StringBoardLink), "StringBoardLink")]
[JsonDerivedType(typeof(MeretMarketNotice), "MeretMarketNotice")]
[JsonDerivedType(typeof(TrafficOptimizer), "TrafficOptimizer")]
[JsonDerivedType(typeof(LobbyMap), "LobbyMap")]
[JsonDerivedType(typeof(EventFieldPopup), "EventFieldPopup")]
[JsonDerivedType(typeof(Rps), "Rps")]
[JsonDerivedType(typeof(CoupleDance), "CoupleDance")]
[JsonDerivedType(typeof(SaleChat), "SaleChat")]
[JsonDerivedType(typeof(AttendGift), "AttendGift")]
[JsonDerivedType(typeof(BlueMarble), "BlueMarble")]
[JsonDerivedType(typeof(ReturnUser), "ReturnUser")]
[JsonDerivedType(typeof(NewUser), "NewUser")]
[JsonDerivedType(typeof(ReturnUserYearRound), "ReturnUserYearRound")]
[JsonDerivedType(typeof(ReturnUserCandidate), "ReturnUserCandidate")]
[JsonDerivedType(typeof(ActiveUser), "ActiveUser")]
[JsonDerivedType(typeof(LoginNotice), "LoginNotice")]
[JsonDerivedType(typeof(FieldEffect), "FieldEffect")]
[JsonDerivedType(typeof(QuestTag), "QuestTag")]
[JsonDerivedType(typeof(Reactor), "Reactor")]
[JsonDerivedType(typeof(WorldmapIcon), "WorldmapIcon")]
[JsonDerivedType(typeof(DTReward), "DTReward")]
[JsonDerivedType(typeof(ConstructShowItem), "ConstructShowItem")]
[JsonDerivedType(typeof(MassiveConstructionEvent), "MassiveConstructionEvent")]
[JsonDerivedType(typeof(UGCMapContractSale), "UGCMapContractSale")]
[JsonDerivedType(typeof(UGCMapExtensionSale), "UGCMapExtensionSale")]
[JsonDerivedType(typeof(Gallery), "Gallery")]
[JsonDerivedType(typeof(BingoEvent), "BingoEvent")]
[JsonDerivedType(typeof(TimeRunEvent), "TimeRunEvent")]
[JsonDerivedType(typeof(LongTermAttendGift), "LongTermAttendGift")]
[JsonDerivedType(typeof(StampEvent), "StampEvent")]
[JsonDerivedType(typeof(MapleSurvivalOpenPeriod), "MapleSurvivalOpenPeriod")]
[JsonDerivedType(typeof(ShutdownMapleSurvival), "ShutdownMapleSurvival")]
[JsonDerivedType(typeof(Ontime), "Ontime")]
[JsonDerivedType(typeof(DailyLoginReward), "DailyLoginReward")]
[JsonDerivedType(typeof(SaleAutoPlayInstrument), "SaleAutoPlayInstrument")]
[JsonDerivedType(typeof(SaleAutoFishing), "SaleAutoFishing")]
[JsonDerivedType(typeof(LevelUpPackage), "LevelUpPackage")]
[JsonDerivedType(typeof(DungeonEnterTimeLimit), "DungeonEnterTimeLimit")]
public abstract record GameEventData;
