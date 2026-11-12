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
}
