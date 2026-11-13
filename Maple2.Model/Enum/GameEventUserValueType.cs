// ReSharper disable InconsistentNaming

namespace Maple2.Model.Enum;

public enum GameEventUserValueType {
    // Attendance Event
    AttendanceActive = 100, //?? maybe. String is "True"
    AttendanceCompletedTimestamp = 101,
    AttendanceRewardsClaimed = 102, // Also used for Cash Attendance and DT Attendance
    AttendanceEarlyParticipationRemaining = 103,
    AttendanceItemStartEnd = 104, // when an item-started (requirement kind 2) event runs out for the player; the client does not read 104, it gets the time in Attendance 6
    AttendanceNear = 105,
    AttendanceAccumulatedTime = 106,

    // ReturnUser
    ReturnUser = 320, // IsReturnUser

    // DTReward
    DTRewardStartTime = 700, // start time
    DTRewardCurrentTime = 701, // current item accumulated time
    DTRewardRewardIndex = 702, // unk value seen is "1"
    DTRewardTotalTime = 703, // TOTAL accumulated time

    // Blue Marble / Mapleopoly
    MapleopolyTotalSlotCount = 800,
    MapleopolyFreeRollAmount = 801,
    MapleopolyTotalTrips = 802, // unsure

    // Time run (TimeRunEvent), read by its dialog (uitimeruneventdialog.cpp)
    TimeRunFinalRewardReceived = 1500, // 1 once the final reward is claimed
    TimeRunStepRewardKey = 1501, // the key (distance) of the last step reward claimed

    // Gallery Event
    GalleryCardFlipCount = 1600,
    GalleryClaimReward = 1601,

    // Snowman Event
    SnowflakeCount = 1700,
    DailyCompleteCount = 1701,
    AccumCompleteCount = 1702,
    AccumCompleteRewardReceived = 1703,

    // Reactor event (flower pots, chocolate): rewards taken today; the client refuses at the event's limit.
    ReactorRewardCount = 1901,

    // Tree watering (TreeWatering): waterings and emotions rewarded today, read by TreeWateringAction
    // (actionkeyobject.cpp, user values 0x777 and 0x778 of the event).
    TreeWateringCount = 1911,
    TreeWateringEmotionCount = 1912,

    // Stamp card (StampEvent), read by its dialog (uistampeventdialog.cpp)
    StampCompleteRewardClaimed = 1200, // 1 once the complete reward is claimed
    StampRewardsClaimed = 1201, // counts of the claimed reward rows, "4,7"
    StampSpecialRewardsClaimed = 1202, // the same for the special rows

    // Rock Paper Scissors Event
    RPSDailyMatches = 1800,
    RPSRewardsClaimed = 1801,

    // Couple Dance
    CoupleDanceBannerOpen = 2100,
    CoupleDanceRewardState = 2101, // completed/bonus/received flags

    CollectItemGroup = 2200, // Meta Badge event. Serves as a flag for tiers

    // Event arcade (ArcadeOpen): rewards taken today. Not a KMS2 value; the client keeps any type in its list
    // (GameEventUserValue::ReadVariable) and reads this one nowhere.
    ArcadeOpenRewardCount = 2400,

    // Mentee attend reward (ReturnUser season): the streak level and the next claim time, sent as Mentor 10. Not
    // KMS2 values; the client keeps any type in its list and reads these nowhere.
    MentoringAttendLevel = 2410,
    MentoringAttendNext = 2411,

    // Daily login mail (DailyLoginReward): 1 once today's mail is sent. Not a KMS2 value; the client keeps any
    // type in its list and reads this one nowhere.
    DailyLoginRewardGiven = 2420,

    // Long-term attendance (LongTermAttendGift), read by its dialog (uilongtermattendgiftdialog.cpp)
    LongTermAttendClaimedDays = 2700, // claimed reward days, "5,11,20"
    LongTermAttendPoints = 2701,
    LongTermAttendDays = 2702, // days attended
    LongTermAttendPointTime = 2703, // when the last attend point was given, unix seconds; KMS2 sends it, the client does not read it

    // Bingo - TODO: These are not the actual confirmed values. Just using it as a way to store this data for now.
    BingoUid = 4000,
    BingoRewardsClaimed = 4001,
    BingoNumbersChecked = 4002,
}
