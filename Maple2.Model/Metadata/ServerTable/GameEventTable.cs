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
/// GameEventFestival: value1 and value2 as numbers (FestivalContext m_18 and m_1c), a byte each on the wire.
/// A FestivalEvent item whose itemUseType is not 0 is used only while a festival with that UseType runs.
/// </summary>
public record Festival(
    int Value1,
    int UseType) : GameEventData;

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
/// The guild vs game's registration time (GameEventGuildVsGameOpen: OnStart opens the client's registration, its
/// end closes it). No values.
/// </summary>
public record GuildVsGameOpen : GameEventData;

/// <summary>
/// The guild championship's time (GameEventGuildChampionship: OnStart opens CGameRule's championship flag when the
/// client's GuildPvP feature is on, its end closes it). The guild PvP registration is taken only while one runs. No
/// values.
/// </summary>
public record GuildChampionship : GameEventData;

/// <summary>
/// The free air taxi's time (GameEventFreeAirTaxi: OnStart adds the event to CGameRule's free air taxi list, its end
/// removes it). While one runs the client rides to a visited field with no ticket and no fare. No values.
/// </summary>
public record FreeAirTaxi : GameEventData;

/// <summary>
/// The bound skin transfer (GameEventTransferItemBound, commonfiles/gameevent.cpp:3800): while one runs, an item with
/// a skin type and no gem leaves the storage or a fitting doll for another character of the account
/// (Unk_Inventory::FUN_140a66200, inventory.cpp:1673; StoreSlot::vfunc_26, storeslot.cpp:441). value1 is the flag the
/// event sets (true when the row leaves it empty, as all KR rows do); the packet carries the id and the flag.
/// </summary>
public record TransferItemBound(
    bool Enabled) : GameEventData;


/// <summary>
/// Closes Maple Survival (Mushking Royale)
/// </summary>
public record ShutdownMapleSurvival : GameEventData;

/// <summary>
/// Creates a popup for a On Time event. Clicking Participate triggers the user to send /AddOntimeEvent
/// </summary>
public record Ontime : GameEventData;

/// <summary>
/// Sends a mail with the items to the user once a day while the event runs.
/// </summary>
/// <param name="MailId">The mail content id (value1), the text the client shows for the mail.</param>
/// <param name="Items">The items of value2, "id,count,rank,0" separated by '|'.</param>
/// <param name="MinLevel">The lowest character level that gets the mail (value3's first field).</param>
public record DailyLoginReward(
    int MailId,
    RewardItem[] Items,
    int MinLevel = 0) : GameEventData;

public record SaleAutoPlayInstrument(
    int Discount,
    string ContentType) : GameEventData;

public record SaleAutoFishing(
    int Discount,
    string ContentType) : GameEventData;

// One rate in 1/10000 (value1), for the events whose client class keeps a single int (GameEventSale::vfunc_4,
// commonfiles/gameevent.cpp:1952, and the classes that read the same): the event's type says what the rate lowers or
// raises. The packet carries the id and the rate.
public record EventRate(
    int Rate) : GameEventData;

// The black market fee event (GameEventBlackMarketFeeDiscount, commonfiles/gameevent.cpp:1283): value1 is the fee
// rate in 1/10000 that replaces the normal one while the event runs (GetBlackMarketCostRate,
// commonfiles/blackmarketitemoption.cpp:233). The client asserts on a rate outside 0..10000; 0 is a free sale.
public record BlackMarketFeeDiscount(
    int Rate) : GameEventData;

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

// A dungeon open period (GameEventDungeonOpenPeriod, commonfiles/gameevent.cpp:1738): value1 is the tag; rooms whose
// openPeriodTag is that tag are open while the event runs (DungeonRoomRules.OpenPeriodError).
public record DungeonOpenPeriod(
    string Tag) : GameEventData;

// The design price sale (GameEventSaleUGCDesign, commonfiles/gameevent.cpp:2258): value1 is the rate out of 10000
// taken off the price of making a design (CGameRule::GetSaleUgcDesignRate, EventSaleRules.UgcDesignPrice).
public record SaleUgcDesign(
    int Rate) : GameEventData;

// The cube price sale (GameEventSaleHousing, commonfiles/gameevent.cpp:2395): value1, value2 and value3 are lists
// of the same length, one cube row per place (EventSaleRules.HousingPrice).
public record SaleHousing(
    SaleHousingCube[] Cubes) : GameEventData;

// The item's housing filter, the price's money type (1 meso, 3 meret) and the rate out of 10000 taken off.
public record SaleHousingCube(
    short Filter,
    short MoneyType,
    int Rate);

// The dungeon helper bonus (GameEventFindDungeonHelperBonus, commonfiles/gameevent.cpp:4560): value1 is the bonus
// (EventSaleRules.HelperRewardTimes).
public record FindDungeonHelperBonus(
    int Bonus) : GameEventData;

// The change of attributes sale (GameEventSaleRemake, commonfiles/gameevent.cpp:1966): value1 is the rate out of
// 10000 taken off the ingredient counts, value2 the item class it is for (EventSaleRules.RemakeSaleType, 0 all).
public record SaleRemake(
    int Rate,
    byte ItemType) : GameEventData;

// The exchange scroll fee sale (GameEventExchangeScrollSale, commonfiles/gameevent.cpp:6313): value1 is a comma list
// of exchange scroll table rows, value2 the rate out of 10000 taken off each row's meso fee
// (EventSaleRules.ExchangeScrollFee).
public record ExchangeScrollSale(
    int Rate,
    int[] ExchangeIds) : GameEventData;

// The meret market sale (GameEventPremiumMarketSale, commonfiles/gameevent.cpp:1386): value1 is the rate out of
// 10000 taken off a product's sale price, value2 a comma list of market categories it is for (0 every category, a
// category ending in 00 its sub categories too), value3 the currency item it is for (100000 merets, 100001 red
// merets, 100002 any currency on a coupon product). EventSaleRules.PremiumMarketPrice.
public record PremiumMarketSale(
    int Rate,
    int[] CategoryIds,
    int Currency) : GameEventData;

// The beauty fee sale (GameEventSaleBeautyShop, commonfiles/gameevent.cpp:760): value1 is the beauty shop's id,
// value2 the rate out of 10000 taken off its fees, value3 the flag of a PC room row, which lowers nothing here
// (EventSaleRules.BeautyShopRate).
public record SaleBeautyShop(
    int ShopId,
    int Rate,
    bool Flag) : GameEventData;

// The special attribute bonus (GameEventSAHotTime, commonfiles/gameevent.cpp:664): value1 is a comma list of
// special attribute ids, value2 the list of their values, value4 the text of the event icon's tooltip. While the
// event runs the client adds each value to the character's total of the attribute (CEquipment::GetAbilityTotal,
// equipment.cpp:740). Value is what is added: a rate in thousandths, a plain value as it is.
public record SAHotTime(
    SAHotTimeBonus[] Bonus) : GameEventData;

public record SAHotTimeBonus(
    SpecialAttribute Attribute,
    int Value);

// The dungeon clear bonus (GameEventDungeonBonusReward, commonfiles/gameevent.cpp:976): value1 is
// <ms2><v dungeonID groupType mesoRate expRate itemRolls customDropBoxIds/></ms2>, of which the client reads the
// first element. The event is for a room (DungeonId) or for every room of a group (GroupType), 0 when not named.
// The client only shows the items of DropBoxIds in the room's reward preview; ItemRolls, MesoRate and ExpRate are
// the server's (EventSaleRules.DungeonBonus).
public record DungeonBonusReward(
    int DungeonId,
    DungeonGroupType GroupType,
    float MesoRate,
    float ExpRate,
    int ItemRolls,
    int[] DropBoxIds) : GameEventData;

// The quest reward rates (GameEventQuestAdditionalReward, commonfiles/gameevent.cpp:3143): value1 the quest type, value2
// the repeat type (the quest's basic repeatable), value3 either one rate for every kind or
// <reward exp meso guild_fund guild_exp guild_coin lu karma menteeCoin><item id rate/>...</reward>
// (QuestAdditionalRewardContext::Load, gameeventcontext.cpp:938). Rates are in ten-thousandths of the reward added
// (10000 doubles it); Rates holds the eight kinds in that order, Items the rate of each reward item named. The client
// keys the event by (QuestType, Repeat) and its reward line shows the exp and meso raised (CQuest::ShowRewards); the
// server pays every kind raised (QuestRewardEventRules).
public record QuestAdditionalReward(
    int QuestType,
    int Repeat,
    int[] Rates,
    QuestAdditionalReward.ItemRate[] Items) : GameEventData {
    public record ItemRate(int ItemId, int Rate);
}

// The item collection (GameEventCollectItemGroup, commonfiles/gameevent.cpp:6474): value1 is
// <ms2><info name/><result itemID count grade/><condition itemID count grade/>...</ms2>. A player who has every
// condition item claims the result once (EventSaleRules.CollectMissing). Name is the key of the tab's title.
public record CollectItemGroup(
    string Name,
    RewardItem[] Conditions,
    RewardItem Result) : GameEventData;

// The burning levelup (GameEventBurningLevelup::vfunc_2, commonfiles/gameevent.cpp:5923): value1
// <ms2><v beginLevel endLevel burningBonus additionalEffectId setCharacterPopupStringKey
// setCharacterPopupBottomStringKey descPopupStringKey rewardUiTitleStringKey equipNametagSymbolID/></ms2>, its root's
// first element whatever its name; value2 <items><v itemID count grade/>...</items>, the reward at the end level;
// value3 a comma list of job codes (Targets; none, every job); value4
// <ms2><jumpingOption tutorialEndFieldId jumpingItemDescPopupStringKey jumpingItemNotifyStringKey
// jumpingCompleteStringKey><item itemID count grade/></jumpingOption></ms2>. A new character's list entry offers the
// pick (EventReward 0x10); the picked character gets Bonus extra levels on its way from BeginLevel to EndLevel
// (BurningLevelupRules.BonusLevel) and claims the rewards at EndLevel (EventReward 0x0f).
// The jumping item is a character-bound LevelPotion with a targetLevel; the client drops the option otherwise, and
// so does the ingest. The game gives it on entering the jumping's tutorialEndFieldId below its target level, once
// (2003, with the notice EventReward 0x11), and sets the jumping done (2004) when the level crosses the target.
// A level potion or GM `level` lands on its level plus that level's bonus (BurningLevelupRules.Landing).
public record BurningLevelup(
    int BeginLevel,
    int EndLevel,
    int Bonus,
    int AdditionalEffectId,
    string SetCharacterPopupStringKey,
    string SetCharacterPopupBottomStringKey,
    string DescPopupStringKey,
    string RewardUiTitleStringKey,
    short NametagSymbolId,
    int[] Targets,
    RewardItem[] Rewards,
    BurningLevelup.JumpingOption? Jumping) : GameEventData {
    public record JumpingOption(
        RewardItem Item,
        int TargetLevel,
        int TutorialEndFieldId,
        string DescPopupStringKey,
        string NotifyStringKey,
        string CompleteStringKey);
}

// An event arcade's opening (GameEventArcadeOpen, commonfiles/gameevent.cpp:6657): value1 is
// <ms2><info arcadeId rewardCountPerDay/><reward itemID count grade/>...</ms2>. While it runs the client lists the
// arcade in its lobby (CGameEventValueArcadeOpen::Get of the arcade id). The client receives only the arcade id; the
// reward rows and the daily count are the server's (ArcadeEventRules).
public record ArcadeOpen(
    int ArcadeId,
    int RewardCountPerDay,
    RewardItem[] Rewards) : GameEventData;

// A mini game's bonus reward: value1 is a comma list of mini game names, the game_name of the scripts' end_mini_game
// (oxquiz, crazyrunner, springbeach, dancedancestop, escape, trapmaster, finalsurvivor, christmasdancedancestop),
// value2 <items><v itemID grade count/>...</items>. The client has no class for this type (GameEventManagerCli
// finds no GameEventMiniGameBonusReward), so it is not sent; the server gives the items to the winners at
// end_mini_game (MiniGameBonus) and the result window lists them with the game's own rewards.
public record MiniGameBonusReward(
    string[] GameNames,
    RewardItem[] Items) : GameEventData;

// The items an npc shop shows while the event runs: value1 the shop id, value2 the shop's item ids, comma separated.
// The client has no class for this type (no GameEventNpcShopShowItem), so it is not sent and the shop window shows
// what the server loads. An item that a row of its shop names (running or ended) shows only while one of those rows
// runs; the shop's other items always show. The ingest keeps the ended rows for this.
public record NpcShopShowItem(
    int ShopId,
    int[] ItemIds) : GameEventData;

// The system mail a new character gets: value1 the id of its string/systemmailcontent row (KR 110000: 160331001).
// The client has no class for the type (no GameEventCharacterFirstConnect).
public record CharacterFirstConnectMail(
    int ContentId) : GameEventData;

// A system mail sent to the players value1 names: value2 "<systemmailcontent id>,<mail kind>", value3 a number of
// days. KR has one row, 10100100: ReturnUser, "170531002,3", 90, the mail a character gets when it enters after 90
// days or more away. The client has no class for the type (no GameEventSendMail).
public record SendMailEvent(
    string Target,
    int ContentId,
    MailType Kind,
    int Days) : GameEventData;

// An npc the event puts in a field: value1 <npc fieldID spawnPointID npcID shopID/>, its root read whatever its name
// (the client's table export reads the root's npcID and fieldID and lists the npc in that field for the event's
// time, tableexporter.cpp:906). spawnPointID is the id of a region spawn (MS2RegionSpawn) of the field, shopID the
// shop the npc opens instead of its own (0: its own). The client has no class for this type, so it is not sent.
public record SpawnNpcEvent(
    int FieldId,
    int SpawnPointId,
    int NpcId,
    int ShopId) : GameEventData;

// Tree watering (GameEventTreeWatering::ReadTreeWateringData, commonfiles/gameevent.cpp:5664). value1 is
// <ms2><tree_step><step step begin end/>...</tree_step><emotion_day><time begin end/>...</emotion_day></ms2>, value2
// <ms2><watering_reward MaxRewardCountDaily itemID grade count/><emotion_reward .../></ms2>, value3
// <ms2><resource FieldID TreeNameTag WaterPotItemID WateringAnimation EmotionAnimation EmotionDayEffect/>
// <tree_resource><step step nif_id/>...</tree_resource></ms2>. A player near the field's entity named TreeName waters
// it (WaterLimit times a day, holding the water pot) and on an emotion day, once watered that many times, admires
// it (EmotionLimit times); each gives its reward (TreeWateringRules).
public record TreeWatering(
    TreeWatering.Step[] Steps,
    TreeWatering.Period[] EmotionDays,
    int WaterLimit,
    RewardItem WaterReward,
    int EmotionLimit,
    RewardItem EmotionReward,
    TreeWatering.Resource[] Resources,
    int FieldId,
    string TreeName,
    int WaterPotItemId,
    string WaterAnimation,
    string EmotionAnimation,
    string EmotionDayEffect) : GameEventData {
    public record Step(int Id, DateTime Begin, DateTime End);

    public record Period(DateTime Begin, DateTime End);

    public record Resource(int Step, string Nif);
}

// Start and end as seconds of the day, both inside the range (Unk_DayTimeRange::Contains).
public record DungeonEnterTime(
    int DungeonId,
    int StartSeconds,
    int EndSeconds);

public record LoginNotice : GameEventData;

// value1 <v fieldID boxItemID bannerPath title desc/> as ItemBoxEventContext::Load reads it (gameeventcontext.cpp):
// the box the open box dialog offers once a day in the field while the event runs.
public record ItemBox(
    int FieldId,
    int BoxItemId,
    string Title,
    string Desc,
    string BannerPath) : GameEventData;

// value1 <option thema=""/> and its <snowman> nodes (<condition snowflakeCount dailyMaxCount accumMaxCount/>,
// <rewardItems>, <accumRewardItems>) as GameEventSnowman::ReadSnowmanData reads them (gameevent.cpp), and value2, the
// dialog's layout the event list sends as it is (GameEventSnowman::vfunc_3). Sources are the server table's
// promotionContents rows of promotionType Snowman that run over the same dates: each gives Point snowflakes with
// the chance Prop / 10000 when the condition Type (a ConditionType name) is counted.
public record Snowman(
    string Theme,
    Snowman.Entry[] Snowmen,
    string Layout,
    Snowman.Source[] Sources) : GameEventData {

    public record Entry(
        int Goal,
        int DailyMax,
        int AccumMax,
        RewardItem[] Rewards,
        RewardItem[] AccumRewards);

    public record Source(
        ConditionType Type,
        int Point,
        int Prop);
}

// value1, the group of user condition rows the event runs (GameEventUserCondition::vfunc_2 reads it as an unsigned
// number; the rows' gameEventID), and value2, the page the notice dialog links to (GameRule::SetUserConditionText).
public record UserCondition(
    int Group,
    string Url) : GameEventData;

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
[JsonDerivedType(typeof(ItemBox), "ItemBox")]
[JsonDerivedType(typeof(Snowman), "Snowman")]
[JsonDerivedType(typeof(UserCondition), "UserCondition")]
[JsonDerivedType(typeof(FieldEffect), "FieldEffect")]
[JsonDerivedType(typeof(Festival), "Festival")]
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
[JsonDerivedType(typeof(GuildVsGameOpen), "GuildVsGameOpen")]
[JsonDerivedType(typeof(GuildChampionship), "GuildChampionship")]
[JsonDerivedType(typeof(FreeAirTaxi), "FreeAirTaxi")]
[JsonDerivedType(typeof(ShutdownMapleSurvival), "ShutdownMapleSurvival")]
[JsonDerivedType(typeof(Ontime), "Ontime")]
[JsonDerivedType(typeof(DailyLoginReward), "DailyLoginReward")]
[JsonDerivedType(typeof(SaleAutoPlayInstrument), "SaleAutoPlayInstrument")]
[JsonDerivedType(typeof(SaleAutoFishing), "SaleAutoFishing")]
[JsonDerivedType(typeof(EventRate), "EventRate")]
[JsonDerivedType(typeof(TransferItemBound), "TransferItemBound")]
[JsonDerivedType(typeof(BlackMarketFeeDiscount), "BlackMarketFeeDiscount")]
[JsonDerivedType(typeof(LevelUpPackage), "LevelUpPackage")]
[JsonDerivedType(typeof(DungeonEnterTimeLimit), "DungeonEnterTimeLimit")]
[JsonDerivedType(typeof(DungeonOpenPeriod), "DungeonOpenPeriod")]
[JsonDerivedType(typeof(SaleUgcDesign), "SaleUgcDesign")]
[JsonDerivedType(typeof(SaleHousing), "SaleHousing")]
[JsonDerivedType(typeof(FindDungeonHelperBonus), "FindDungeonHelperBonus")]
[JsonDerivedType(typeof(SaleRemake), "SaleRemake")]
[JsonDerivedType(typeof(ExchangeScrollSale), "ExchangeScrollSale")]
[JsonDerivedType(typeof(PremiumMarketSale), "PremiumMarketSale")]
[JsonDerivedType(typeof(SaleBeautyShop), "SaleBeautyShop")]
[JsonDerivedType(typeof(SAHotTime), "SAHotTime")]
[JsonDerivedType(typeof(DungeonBonusReward), "DungeonBonusReward")]
[JsonDerivedType(typeof(QuestAdditionalReward), "QuestAdditionalReward")]
[JsonDerivedType(typeof(CollectItemGroup), "CollectItemGroup")]
[JsonDerivedType(typeof(ArcadeOpen), "ArcadeOpen")]
[JsonDerivedType(typeof(BurningLevelup), "BurningLevelup")]
[JsonDerivedType(typeof(TreeWatering), "TreeWatering")]
[JsonDerivedType(typeof(MiniGameBonusReward), "MiniGameBonusReward")]
[JsonDerivedType(typeof(SpawnNpcEvent), "SpawnNpc")]
[JsonDerivedType(typeof(NpcShopShowItem), "NpcShopShowItem")]
[JsonDerivedType(typeof(CharacterFirstConnectMail), "CharacterFirstConnect")]
[JsonDerivedType(typeof(SendMailEvent), "SendMail")]
public abstract record GameEventData;
