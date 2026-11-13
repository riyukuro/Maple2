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
// special attribute bonus (SAHotTime), the dungeon clear bonus (DungeonBonusReward), the item collection
// (CollectItemGroup) and the exchange scroll fee sale (ExchangeScrollSale): every row has ended, and the switch replays one of each. The event arcades' openings
// (ArcadeOpen) have ended too; one is replayed per arcade, since each opens one arcade. The tree watering event
// (TreeWatering) has ended too; its steps and emotion days move with it. Of the event minigames, the rock paper
// scissors (RPS), the bingo (BingoEvent), the time run (TimeRunEvent) and the couple dance (CoupleDance) are
// replayed: one event each, with the ended play-time (DTReward) and daily login (DailyLoginReward) rows that give
// its play item during it, moved by the same amount (Companions). An RPS, bingo or time run that no such row
// feeds is not taken: nothing else gives its ticket, pencil or start item.
public static class AttendGiftReplay {
    public const string Option = "MS2_REPLAY_ATTEND_GIFT";

    // The event types replayed, one event each.
    public static readonly GameEventType[] Types = [
        GameEventType.AttendGift, GameEventType.LongTermAttendGift, GameEventType.StampEvent,
        GameEventType.SaleUgcDesign, GameEventType.SaleHousing, GameEventType.FindDungeonHelperBonus,
        GameEventType.SaleRemake, GameEventType.SaleBeautyShop, GameEventType.SAHotTime,
        GameEventType.DungeonBonusReward, GameEventType.CollectItemGroup, GameEventType.ArcadeOpen,
        GameEventType.TreeWatering, GameEventType.RPS, GameEventType.BingoEvent, GameEventType.TimeRunEvent,
        GameEventType.CoupleDance, GameEventType.ExchangeScrollSale,
    ];

    // The replayed types whose events are not taken without data (not read): the minigames, whose rules are all
    // in it.
    public static readonly GameEventType[] NeedData = [
        GameEventType.StampEvent, GameEventType.RPS, GameEventType.BingoEvent, GameEventType.TimeRunEvent,
        GameEventType.CoupleDance,
    ];

    // The item a round of the event takes: the RPS ticket, the bingo's daily pencil, the time run's start item
    // (a quest scroll of its quests); 0 for an event that takes none.
    public static int PlayItem(GameEventData? data) {
        return data switch {
            Rps rps => rps.GameTicketId,
            BingoEvent bingo => bingo.PencilItemId,
            TimeRunEvent timeRun => timeRun.StartItemId,
            _ => 0,
        };
    }

    // The items a play-time or daily login row gives.
    public static IEnumerable<int> GivenItems(GameEventData? data) {
        return data switch {
            DTReward dtReward => dtReward.Entries.Select(entry => entry.Item.ItemId),
            DailyLoginReward daily => daily.Items.Select(item => item.ItemId),
            _ => [],
        };
    }

    // The ended DTReward and DailyLoginReward rows that give the event's play item and run during it, in id order
    // (KR 900090's tickets come from 120105, 900600's pencils from 160087 on weekdays and 160088 on weekends).
    public static GameEventMetadata[] Companions(GameEventMetadata gameEvent, IEnumerable<GameEventMetadata> ended) {
        int playItem = PlayItem(gameEvent.Data);
        if (playItem == 0) {
            return [];
        }
        return ended
            .Where(e => e.Id != gameEvent.Id && e.Type is GameEventType.DTReward or GameEventType.DailyLoginReward
                        && e.StartTime < gameEvent.EndTime && gameEvent.StartTime < e.EndTime
                        && GivenItems(e.Data).Contains(playItem))
            .OrderBy(e => e.Id)
            .ToArray();
    }

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
                                          Func<int, bool>? clientQuest = null, Func<GameEventMetadata, bool>? playable = null) {
        return Candidates(events, now, type)
            .Where(e => clientRank == null || ClientHasRewards(e, clientRank))
            .Where(e => clientQuest == null || ClientHasQuests(e, clientQuest))
            .Where(e => playable == null || playable(e))
            .FirstOrDefault();
    }

    // A stamp is done when its quest is rewarded (UIStampEventDialog, IsQuestRewarded of the stamp's quest), and
    // the server starts the card's quests; a quest the client's quest table lacks can be neither sent nor done,
    // so such a card could never be filled. An event without data (not read) is not taken either.
    // The same holds for a time run's quests (its start item starts them all, and the final reward needs all done).
    public static bool ClientHasQuests(GameEventMetadata gameEvent, Func<int, bool> clientQuest) {
        return gameEvent.Data switch {
            StampEvent stampEvent => stampEvent.Stamps.All(stamp => clientQuest(stamp.QuestId)),
            TimeRunEvent timeRun => timeRun.Quests.All(quest => clientQuest(quest.Id)),
            _ => true,
        };
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
    // result dialog), and for a TreeWatering's water pot and rewards (an empty reward, item 0, is none). An RPS's
    // ticket and rewards, a bingo's pencils (no plus pencil: item 0) and rewards, a time run's start item and
    // rewards and a couple dance's daily and good-end rewards are items of the client's bags, dialogs and mails
    // too; so are a play-time or daily login row's items.
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
            TreeWatering tree => clientRank(tree.WaterPotItemId) > 0
                                 && new[] { tree.WaterReward, tree.EmotionReward }
                                     .All(item => item.ItemId == 0 || clientRank(item.ItemId) > 0),
            Rps rps => clientRank(rps.GameTicketId) > 0
                       && rps.Rewards.SelectMany(reward => reward.Rewards).All(item => clientRank(item.ItemId) > 0),
            BingoEvent bingo => clientRank(bingo.PencilItemId) > 0
                                && (bingo.PencilPlusItemId == 0 || clientRank(bingo.PencilPlusItemId) > 0)
                                && bingo.Rewards.SelectMany(reward => reward.Items).All(item => clientRank(item.ItemId) > 0),
            TimeRunEvent timeRun => clientRank(timeRun.StartItemId) > 0
                                    && timeRun.StepRewards.Values.Append(timeRun.FinalReward).All(item => clientRank(item.ItemId) > 0),
            CoupleDance dance => dance.DailyRewards.SelectMany(rewards => rewards).Concat(dance.GoodEndRewards)
                .All(item => clientRank(item.ItemId) > 0),
            DTReward dtReward => dtReward.Entries.All(entry => clientRank(entry.Item.ItemId) > 0),
            DailyLoginReward daily => daily.Items.All(item => clientRank(item.ItemId) > 0),
            _ => true,
        };
    }

    // The event moved to start on the given day at its original time of day, with its original length. The times
    // keep no time zone, like the other events' (the server compares them with its local clock). A TreeWatering's
    // step and emotion day times move by the same amount.
    public static GameEventMetadata Redate(GameEventMetadata gameEvent, DateTime day) {
        DateTime start = DateTime.SpecifyKind(day.Date, DateTimeKind.Unspecified) + gameEvent.StartTime.TimeOfDay;
        return Shift(gameEvent, start - gameEvent.StartTime);
    }

    // The event moved by the given amount (a companion row by its lead's): its start, its end and a TreeWatering's
    // step and emotion day times.
    public static GameEventMetadata Shift(GameEventMetadata gameEvent, TimeSpan shift) {
        GameEventData data = gameEvent.Data;
        if (data is TreeWatering tree) {
            data = tree with {
                Steps = tree.Steps.Select(step => step with { Begin = step.Begin + shift, End = step.End + shift }).ToArray(),
                EmotionDays = tree.EmotionDays.Select(period => new TreeWatering.Period(period.Begin + shift, period.End + shift)).ToArray(),
            };
        }
        return gameEvent with { StartTime = gameEvent.StartTime + shift, EndTime = gameEvent.EndTime + shift, Data = data };
    }
}
