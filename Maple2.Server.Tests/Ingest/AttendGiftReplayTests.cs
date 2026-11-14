using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.File.Ingest.Helpers;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Ingest;

// MS2_REPLAY_ATTEND_GIFT: one ended attendance gift event is kept and moved to start on the ingest day.
public class AttendGiftReplayTests {
    private static readonly DateTime Now = new(2030, 6, 15, 9, 30, 0);

    private static GameEventMetadata Event(int id, GameEventType type, string start, string end) {
        return new GameEventMetadata(id, type, DateTime.Parse(start), DateTime.Parse(end), TimeSpan.Zero, TimeSpan.Zero, [],
            new AttendGift([], "", "", "", "", 0, null), "", "", "", "");
    }

    // The ingest ends an event against the server's local clock, as the server reads the event times
    // (GameEvent.IsActive(DateTime.Now)). With the UTC clock a host west of UTC ended events still running for the
    // server (and one east of it kept ended ones); an event ending in one minute was ended on a UTC-6 host.
    [Test]
    public void IngestNow_IsTheServersLocalClock() {
        Assume.That(TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow), Is.Not.EqualTo(TimeSpan.Zero), "the host is on UTC");
        DateTime local = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified);
        GameEventMetadata running = Event(800, GameEventType.AttendGift, local.AddDays(-3).ToString("yyyy-MM-dd HH:mm:ss"),
            local.AddMinutes(1).ToString("yyyy-MM-dd HH:mm:ss"));
        GameEventMetadata ended = Event(801, GameEventType.AttendGift, local.AddDays(-3).ToString("yyyy-MM-dd HH:mm:ss"),
            local.AddMinutes(-1).ToString("yyyy-MM-dd HH:mm:ss"));
        Assert.That(AttendGiftReplay.Candidates([running, ended], AttendGiftReplay.IngestNow()).Select(e => e.Id), Is.EqualTo(new[] { 801 }));
    }

    [TestCase(null, false)]
    [TestCase("", false)]
    [TestCase("0", false)]
    [TestCase("false", false)]
    [TestCase("1", true)]
    [TestCase(" True ", true)]
    public void Enabled_DefaultOff(string? value, bool expected) {
        Assert.That(AttendGiftReplay.Enabled(value), Is.EqualTo(expected));
    }

    [TestCase("yes")]
    [TestCase("2")]
    public void Enabled_OtherValueThrows(string value) {
        Assert.Throws<ArgumentException>(() => AttendGiftReplay.Enabled(value));
    }

    [Test]
    public void Pick_LatestEndedAttendGift() {
        GameEventMetadata[] events = [
            Event(500, GameEventType.AttendGift, "2029-01-01 00:00", "2029-01-28 23:59"),
            Event(501, GameEventType.AttendGift, "2029-03-01 00:00", "2029-03-10 23:59"),
            Event(502, GameEventType.DailyLoginReward, "2029-05-01 00:00", "2029-05-31 23:59"),
            Event(503, GameEventType.AttendGift, "2030-06-01 00:00", "2030-06-30 23:59"), // not ended
        ];
        Assert.That(AttendGiftReplay.Pick(events, Now)?.Id, Is.EqualTo(501));
    }

    // On the same end: the longer event, then the higher id.
    [Test]
    public void Pick_SameEndTakesLongerThenHigherId() {
        GameEventMetadata[] events = [
            Event(600, GameEventType.AttendGift, "2029-03-01 00:00", "2029-03-10 23:59"),
            Event(601, GameEventType.AttendGift, "2029-03-09 00:00", "2029-03-10 23:59"),
        ];
        Assert.That(AttendGiftReplay.Pick(events, Now)?.Id, Is.EqualTo(600));

        GameEventMetadata[] same = [
            Event(700, GameEventType.AttendGift, "2029-03-01 00:00", "2029-03-10 23:59"),
            Event(701, GameEventType.AttendGift, "2029-03-01 00:00", "2029-03-10 23:59"),
        ];
        Assert.That(AttendGiftReplay.Pick(same, Now)?.Id, Is.EqualTo(701));
    }

    [Test]
    public void Pick_SkipsUnreadableTimes() {
        GameEventMetadata[] events = [
            Event(800, GameEventType.AttendGift, "2029-03-01 00:00", "2029-03-10 23:59"),
            Event(801, GameEventType.AttendGift, "0001-01-01 00:00", "2029-04-10 23:59"),
            Event(802, GameEventType.AttendGift, "2029-05-10 00:00", "2029-05-01 23:59"),
        ];
        Assert.That(AttendGiftReplay.Pick(events, Now)?.Id, Is.EqualTo(800));
        Assert.That(AttendGiftReplay.Pick([], Now), Is.Null);
    }

    private static GameEventMetadata Gift(int id, string start, string end, params int[] itemIds) {
        return Event(id, GameEventType.AttendGift, start, end) with {
            Data = new AttendGift(itemIds.Select(itemId => new RewardItem(itemId, 5, 1)).ToArray(), "", "", "", "", 600, null),
        };
    }

    // The client takes an AttendGift reward's rank from its item data (ItemGift, itemgift.cpp:32) and asserts on 0.
    // Shaped like KR's latest row: four grade 5 items the client's item data does not have (made-up ids here).
    [Test]
    public void Pick_PassesOverAnEventWithAnItemTheClientHasNoRankFor() {
        var ranks = new Dictionary<int, int> { [900101] = 3, [900102] = 1, [900103] = 0 };
        int ClientRank(int itemId) => ranks.GetValueOrDefault(itemId);
        GameEventMetadata[] events = [
            Gift(700, "2029-01-01 00:00", "2029-01-05 23:59", 900101, 900102),
            Gift(701, "2029-03-01 00:00", "2029-03-05 23:59", 900101, 900103), // in the item data without a rank
            Gift(702, "2029-05-01 00:00", "2029-05-05 23:59", 900201, 900202, 900203, 900204), // not in the item data
        ];

        Assert.That(AttendGiftReplay.Pick(events, Now)?.Id, Is.EqualTo(702));
        GameEventMetadata? picked = AttendGiftReplay.Pick(events, Now, clientRank: ClientRank);
        Assert.That(picked?.Id, Is.EqualTo(700));
        Assert.That(((AttendGift) picked!.Data).Items.Select(item => ClientRank(item.ItemId)), Has.None.EqualTo(0));
        Assert.That(AttendGiftReplay.Candidates(events, Now).Select(e => e.Id), Is.EqualTo(new[] { 702, 701, 700 }));
        Assert.That(AttendGiftReplay.Pick(events[1..], Now, clientRank: ClientRank), Is.Null);
    }

    [Test]
    public void Redate_UtcDayKeepsNoTimeZone() {
        GameEventMetadata moved = AttendGiftReplay.Redate(Event(901, GameEventType.AttendGift, "2029-03-01 07:00", "2029-03-02 07:00"),
            new DateTime(2030, 6, 15, 1, 0, 0, DateTimeKind.Utc));
        Assert.That((moved.StartTime.Kind, moved.EndTime.Kind), Is.EqualTo((DateTimeKind.Unspecified, DateTimeKind.Unspecified)));
        Assert.That(moved.StartTime, Is.EqualTo(new DateTime(2030, 6, 15, 7, 0, 0)));
    }

    [Test]
    public void Redate_StartsOnTheDayWithItsLength() {
        GameEventMetadata moved = AttendGiftReplay.Redate(Event(900, GameEventType.AttendGift, "2029-03-01 07:00", "2029-03-10 23:59"), Now);
        Assert.Multiple(() => {
            Assert.That(moved.Id, Is.EqualTo(900));
            Assert.That(moved.StartTime, Is.EqualTo(new DateTime(2030, 6, 15, 7, 0, 0)));
            Assert.That(moved.EndTime, Is.EqualTo(new DateTime(2030, 6, 24, 23, 59, 0)));
            Assert.That(moved.StartTime.Kind, Is.EqualTo(DateTimeKind.Unspecified));
        });
    }

    // The event minigames (rock paper scissors, bingo, time run, couple dance) and the rows that feed them.
    private const int Ticket = 900301;
    private const int Prize = 900302;
    private const int Unknown = 900399;

    private static int MinigameRank(int itemId) => itemId == Unknown ? 0 : 1;

    private static GameEventMetadata With(int id, GameEventType type, string start, string end, GameEventData data) {
        return Event(id, type, start, end) with { Data = data };
    }

    private static Rps RpsData(int ticket, int prize) =>
        new(ticket, [new Rps.RewardData(1, [new RewardItem(prize, 1, 1)])], "");

    private static DailyLoginReward Daily(int itemId) => new(0, [new RewardItem(itemId, 1, 15)]);

    private static Snowman Snowman(RewardItem[] rewards, RewardItem[] accumRewards) =>
        new("", [new Snowman.Entry(10, 1, 1, rewards, accumRewards)], "", []);

    [Test]
    public void Replay_TakesTheMinigames() {
        Assert.That(AttendGiftReplay.Types, Is.SupersetOf(new[] {
            GameEventType.RPS, GameEventType.BingoEvent, GameEventType.TimeRunEvent, GameEventType.CoupleDance,
            GameEventType.ItemBox, GameEventType.Snowman,
        }));
        Assert.That(AttendGiftReplay.NeedData, Does.Contain(GameEventType.ItemBox));
        Assert.That(AttendGiftReplay.NeedData, Does.Contain(GameEventType.Snowman));
    }

    // Each minigame's play item and rewards must be items of the client's item data.
    [Test]
    public void ClientHasRewards_MinigameItems() {
        GameEventMetadata Of(GameEventType type, GameEventData data) => With(1, type, "2029-01-01 00:00", "2029-01-05 23:59", data);
        var timeRunQuests = new[] { new TimeRunEvent.Quest(800001, 10, 0) };
        var steps = new Dictionary<int, RewardItem>();
        RewardItem[] prizes = [new RewardItem(Prize, 1, 5)];
        RewardItem[] unknown = [new RewardItem(Unknown, 1, 5)];
        Assert.Multiple(() => {
            Assert.That(AttendGiftReplay.ClientHasRewards(Of(GameEventType.RPS, RpsData(Ticket, Prize)), MinigameRank), Is.True);
            Assert.That(AttendGiftReplay.ClientHasRewards(Of(GameEventType.RPS, RpsData(Unknown, Prize)), MinigameRank), Is.False);
            Assert.That(AttendGiftReplay.ClientHasRewards(Of(GameEventType.RPS, RpsData(Ticket, Unknown)), MinigameRank), Is.False);
            Assert.That(AttendGiftReplay.ClientHasRewards(Of(GameEventType.BingoEvent,
                new BingoEvent(Ticket, 0, [], [new BingoEvent.BingoReward(prizes)])), MinigameRank), Is.True);
            Assert.That(AttendGiftReplay.ClientHasRewards(Of(GameEventType.BingoEvent,
                new BingoEvent(Ticket, Unknown, [], [new BingoEvent.BingoReward(prizes)])), MinigameRank), Is.False);
            Assert.That(AttendGiftReplay.ClientHasRewards(Of(GameEventType.TimeRunEvent,
                new TimeRunEvent(Ticket, timeRunQuests, steps, new RewardItem(Prize, 1, 1))), MinigameRank), Is.True);
            Assert.That(AttendGiftReplay.ClientHasRewards(Of(GameEventType.TimeRunEvent,
                new TimeRunEvent(Ticket, timeRunQuests, steps, new RewardItem(Unknown, 1, 1))), MinigameRank), Is.False);
            Assert.That(AttendGiftReplay.ClientHasRewards(Of(GameEventType.CoupleDance,
                new CoupleDance([prizes], 3, unknown, [], 5, 63000062, [])), MinigameRank), Is.False);
            Assert.That(AttendGiftReplay.ClientHasRewards(Of(GameEventType.DailyLoginReward, Daily(Unknown)), MinigameRank), Is.False);
            Assert.That(AttendGiftReplay.ClientHasRewards(Of(GameEventType.ItemBox, new ItemBox(2000064, Prize, "", "", "")), MinigameRank), Is.True);
            Assert.That(AttendGiftReplay.ClientHasRewards(Of(GameEventType.ItemBox, new ItemBox(2000064, Unknown, "", "", "")), MinigameRank), Is.False);
            Assert.That(AttendGiftReplay.ClientHasRewards(Of(GameEventType.Snowman, Snowman(prizes, prizes)), MinigameRank), Is.True);
            Assert.That(AttendGiftReplay.ClientHasRewards(Of(GameEventType.Snowman, Snowman(prizes, unknown)), MinigameRank), Is.False);
            Assert.That(AttendGiftReplay.ClientHasRewards(Of(GameEventType.Snowman, Snowman(unknown, prizes)), MinigameRank), Is.False);
        });
    }

    // The time run's start item starts all its quests, so each must be in the client's quest table.
    [Test]
    public void ClientHasQuests_TimeRunQuests() {
        GameEventMetadata timeRun = With(1, GameEventType.TimeRunEvent, "2029-01-01 00:00", "2029-01-05 23:59",
            new TimeRunEvent(Ticket, [new TimeRunEvent.Quest(800001, 10, 0), new TimeRunEvent.Quest(800002, 10, 1)],
                new Dictionary<int, RewardItem>(), new RewardItem(Prize, 1, 1)));
        Assert.That(AttendGiftReplay.ClientHasQuests(timeRun, questId => questId == 800001), Is.False);
        Assert.That(AttendGiftReplay.ClientHasQuests(timeRun, _ => true), Is.True);
    }

    // The rows that give the play item during the event (KR 900090 and 120105): a row of another item or of
    // another period is not one.
    [Test]
    public void Companions_RowsThatGiveThePlayItemDuringTheEvent() {
        GameEventMetadata rps = With(900, GameEventType.RPS, "2029-02-14 07:00", "2029-03-14 07:00", RpsData(Ticket, Prize));
        GameEventMetadata[] ended = [
            rps,
            With(120, GameEventType.DailyLoginReward, "2029-02-14 07:00", "2029-03-14 07:00", Daily(Ticket)),
            With(121, GameEventType.DailyLoginReward, "2029-02-14 07:00", "2029-03-14 07:00", Daily(Prize)),
            With(122, GameEventType.DailyLoginReward, "2028-02-14 07:00", "2028-03-14 07:00", Daily(Ticket)),
            With(160, GameEventType.DTReward, "2029-03-01 07:00", "2029-03-20 07:00",
                new DTReward([new DTReward.Entry(0, 1800, 0, new RewardItem(Ticket, 1, 1))])),
        ];
        Assert.That(AttendGiftReplay.Companions(rps, ended).Select(e => e.Id), Is.EqualTo(new[] { 120, 160 }));
        Assert.That(AttendGiftReplay.Companions(With(901, GameEventType.CoupleDance, "2029-02-14 07:00", "2029-03-14 07:00",
            new CoupleDance([], 0, [], [], 5, 63000062, [])), ended), Is.Empty);
    }

    // An RPS no row feeds is passed over for an older one that has its ticket row; the companion moves with it.
    [Test]
    public void Pick_PassesOverAMinigameNoRowFeeds_CompanionMovesWithIt() {
        GameEventMetadata older = With(900, GameEventType.RPS, "2029-02-14 07:00", "2029-03-14 07:00", RpsData(Ticket, Prize));
        GameEventMetadata newer = With(901, GameEventType.RPS, "2029-04-14 07:00", "2029-05-14 07:00", RpsData(Ticket, Prize));
        GameEventMetadata row = With(120, GameEventType.DailyLoginReward, "2029-02-15 07:00", "2029-03-14 07:00", Daily(Ticket));
        GameEventMetadata[] ended = [older, newer, row];

        GameEventMetadata? picked = AttendGiftReplay.Pick(ended, Now, GameEventType.RPS, MinigameRank,
            playable: e => AttendGiftReplay.Companions(e, ended).Length > 0);
        Assert.That(picked?.Id, Is.EqualTo(900));

        GameEventMetadata moved = AttendGiftReplay.Redate(picked!, Now);
        GameEventMetadata movedRow = AttendGiftReplay.Shift(row, moved.StartTime - picked!.StartTime);
        Assert.That(movedRow.StartTime, Is.EqualTo(new DateTime(2030, 6, 16, 7, 0, 0)));
        Assert.That(movedRow.EndTime, Is.EqualTo(moved.EndTime));
    }
}
