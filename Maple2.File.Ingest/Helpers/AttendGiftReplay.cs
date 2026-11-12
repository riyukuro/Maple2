using System.Globalization;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.File.Ingest.Helpers;

// Every attendance gift event (AttendGift), long-term attendance event (LongTermAttendGift) and stamp card
// (StampEvent) in the server pack has ended, and the ingest drops ended events, so the client never shows either
// attendance calendar or the stamp card. MS2_REPLAY_ATTEND_GIFT=1 keeps one ended event of each type and moves it
// to start on the ingest day with its original length. Unset, the ingest is unchanged. The same holds for the
// design price sale (SaleUgcDesign), the cube price sale (SaleHousing) and the dungeon helper bonus
// (FindDungeonHelperBonus), the change of attributes sale (SaleRemake), the beauty fee sale (SaleBeautyShop), the
// special attribute bonus (SAHotTime), the dungeon clear bonus (DungeonBonusReward) and the item collection
// (CollectItemGroup): every row has ended, and the switch replays one of each. The event arcades' openings
// (ArcadeOpen) have ended too; one is replayed per arcade, since each opens one arcade.
public static class AttendGiftReplay {
    public const string Option = "MS2_REPLAY_ATTEND_GIFT";

    // The event types replayed, one event each.
    public static readonly GameEventType[] Types = [
        GameEventType.AttendGift, GameEventType.LongTermAttendGift, GameEventType.StampEvent,
        GameEventType.SaleUgcDesign, GameEventType.SaleHousing, GameEventType.FindDungeonHelperBonus,
        GameEventType.SaleRemake, GameEventType.SaleBeautyShop, GameEventType.SAHotTime,
        GameEventType.DungeonBonusReward, GameEventType.CollectItemGroup, GameEventType.ArcadeOpen,
    ];

    // The events of a type that one replay is picked from: all of them, or for ArcadeOpen those of each arcade
    // (in arcade id order). An ArcadeOpen event whose data was not read names no arcade and is left out.
    public static IEnumerable<GameEventMetadata[]> Pools(IEnumerable<GameEventMetadata> events, GameEventType type) {
        GameEventMetadata[] ofType = events.Where(e => e.Type == type).ToArray();
        if (type != GameEventType.ArcadeOpen) {
            return [ofType];
        }
        return ofType.Where(e => e.Data is ArcadeOpen)
            .GroupBy(e => ((ArcadeOpen) e.Data!).ArcadeId)
            .OrderBy(group => group.Key)
            .Select(group => group.ToArray());
    }

    private static readonly string[] TimeFormats = ["yyyy-MM-dd-HH-mm", "yyyy-MM-dd-HH-mm-ss"];

    // Unset, empty, 0 or false: off. 1 or true: on. Anything else stops the ingest, so a typo is not silently off.
    public static bool Enabled(string? value) {
        return value?.Trim().ToLowerInvariant() switch {
            null or "" or "0" or "false" => false,
            "1" or "true" => true,
            _ => throw new ArgumentException($"{Option} must be 1/true or 0/false, not '{value}'"),
        };
    }

    // An eventStart/eventEnd text, to the minute or to the second; DateTime.MinValue when it is neither.
    public static DateTime ParseTime(string? text) {
        return DateTime.TryParseExact(text, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime time)
            ? time : DateTime.MinValue;
    }

    // The ended event of the type to replay: the latest end; on the same end the longer event (more days to
    // attend), then the higher id. Events without a readable start or end time (DateTime.MinValue) or that end
    // before they start are not taken, nor (with clientRank) an event with a reward the client cannot show, nor
    // (with clientQuest) a stamp card with a quest the client does not have.
    public static GameEventMetadata? Pick(IEnumerable<GameEventMetadata> events, DateTime now,
                                          GameEventType type = GameEventType.AttendGift, Func<int, int>? clientRank = null,
                                          Func<int, bool>? clientQuest = null) {
        return Candidates(events, now, type)
            .Where(e => clientRank == null || ClientHasRewards(e, clientRank))
            .Where(e => clientQuest == null || ClientHasQuests(e, clientQuest))
            .FirstOrDefault();
    }

    // A stamp is done when its quest is rewarded (UIStampEventDialog, IsQuestRewarded of the stamp's quest), and
    // the server starts the card's quests; a quest the client's quest table lacks can be neither sent nor done,
    // so such a card could never be filled. An event without data (not read) is not taken either.
    public static bool ClientHasQuests(GameEventMetadata gameEvent, Func<int, bool> clientQuest) {
        return gameEvent.Data is not StampEvent stampEvent || stampEvent.Stamps.All(stamp => clientQuest(stamp.QuestId));
    }

    // The ended events of the type in pick order, whatever their rewards.
    public static IEnumerable<GameEventMetadata> Candidates(IEnumerable<GameEventMetadata> events, DateTime now,
                                                            GameEventType type = GameEventType.AttendGift) {
        return events
            .Where(e => e.Type == type && e.StartTime != DateTime.MinValue
                        && e.EndTime != DateTime.MinValue && e.StartTime <= e.EndTime && e.EndTime < now)
            .OrderByDescending(e => e.EndTime)
            .ThenByDescending(e => e.EndTime - e.StartTime)
            .ThenByDescending(e => e.Id);
    }

    // The client reads an AttendGift reward as an ItemGift, whose rank is the item data's rank, not the one sent
    // (ItemGift::vfunc_o18_1, commonfiles/itemgift.cpp:32, GetItemRank of the item id), and it asserts when that
    // is 0: the item is not in its item data or its property element has no rank. clientRank gives that rank for
    // an item id. KR AttendGift rows list items the KR item data does not have (99000056's four), so such an
    // event cannot be shown. A LongTermAttendGift reward is a plain ItemEntity shown with the rank sent, so an
    // unknown item there does not assert, but it has no icon or name; such a calendar is passed over too (an empty
    // slot, item 0, is not an item). The same holds for a StampEvent's reward rows and complete reward, for a
    // CollectItemGroup's condition items and result, and for an ArcadeOpen's reward items (shown in the arcade
    // result dialog).
    public static bool ClientHasRewards(GameEventMetadata gameEvent, Func<int, int> clientRank) {
        return gameEvent.Data switch {
            AttendGift attendGift => attendGift.Items.All(item => clientRank(item.ItemId) > 0),
            LongTermAttendGift longTerm => longTerm.Rewards
                .SelectMany(reward => new[] { reward.Item01, reward.Item02 })
                .All(item => item.ItemId == 0 || clientRank(item.ItemId) > 0),
            StampEvent stampEvent => stampEvent.Rewards
                .SelectMany(reward => new[] { reward.Item01, reward.Item02 })
                .Append(stampEvent.CompleteReward)
                .All(item => item.ItemId == 0 || clientRank(item.ItemId) > 0),
            CollectItemGroup collect => collect.Conditions
                .Append(collect.Result)
                .All(item => clientRank(item.ItemId) > 0),
            ArcadeOpen arcadeOpen => arcadeOpen.Rewards.All(item => clientRank(item.ItemId) > 0),
            _ => true,
        };
    }

    // The event moved to start on the given day at its original time of day, with its original length. The times
    // keep no time zone, like the other events' (the server compares them with its local clock).
    public static GameEventMetadata Redate(GameEventMetadata gameEvent, DateTime day) {
        DateTime start = DateTime.SpecifyKind(day.Date, DateTimeKind.Unspecified) + gameEvent.StartTime.TimeOfDay;
        TimeSpan length = gameEvent.EndTime - gameEvent.StartTime;
        return gameEvent with { StartTime = start, EndTime = start + length };
    }
}
