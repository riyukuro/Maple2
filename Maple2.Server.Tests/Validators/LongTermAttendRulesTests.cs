using System;
using Maple2.File.Ingest.Helpers;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Validators;

// The long-term attendance calendar (GameEventLongTermAttendGift); made-up ids and items.
public class LongTermAttendRulesTests {
    private static readonly LongTermAttendGift Gift = new("s_test_title", "testtag", 30, 120, [
        new LongTermAttendGift.Reward(1, new RewardItem(900001, 1, 5), new RewardItem(900002, 2, 1)),
        new LongTermAttendGift.Reward(5, new RewardItem(900003, 1, 1), LongTermAttendRules.EmptyItem),
    ]);

    [TestCase("900001,3,7", 900001, 3, 7)]
    [TestCase("", 0, 1, 1)]
    [TestCase(null, 0, 1, 1)]
    [TestCase("900001,3", 0, 1, 1)]
    [TestCase("x,1,1", 0, 1, 1)]
    [TestCase("900001,1,0", 0, 1, 1)]
    public void ParseItem_IdRankCountOrEmptySlot(string? text, int itemId, int rarity, int amount) {
        Assert.That(LongTermAttendRules.ParseItem(text), Is.EqualTo(new RewardItem(itemId, (short) rarity, amount)));
    }

    // The first day counts; at most 80 (the client) and the calendar's days (the server).
    [TestCase("2030-06-15 08:00", "2030-06-15 23:00", 0, 30, 1)]
    [TestCase("2030-06-15 08:00", "2030-06-17 01:00", 1, 30, 2)]
    [TestCase("2030-06-15 08:00", "2030-06-17 01:00", 3, 30, 0)]
    [TestCase("2030-06-15 08:00", "2030-06-17 01:00", 5, 30, 0)]
    [TestCase("2030-06-15 08:00", "2030-09-30 00:00", 0, 90, 80)]
    [TestCase("2030-06-15 08:00", "2030-07-30 00:00", 10, 30, 20)]
    [TestCase("2030-06-15 08:00", "2030-06-14 00:00", 0, 30, 0)]
    public void MissedDays_DaysSinceBeginLessAttended(string begin, string now, int attended, int max, int expected) {
        Assert.That(LongTermAttendRules.MissedDays(DateTime.Parse(begin), DateTime.Parse(now), attended, max), Is.EqualTo(expected));
    }

    [TestCase(false, 2, 1, 0L, LongTermAttendRules.AttendStep.Point)]
    [TestCase(false, 0, 1, 500L, LongTermAttendRules.AttendStep.NoPoint)]
    [TestCase(false, 0, 0, 500L, LongTermAttendRules.AttendStep.NoPoint)]
    [TestCase(false, 2, 0, 500L, LongTermAttendRules.AttendStep.Full)]
    [TestCase(true, 0, 1, 120L, LongTermAttendRules.AttendStep.Merat)]
    [TestCase(true, 0, 1, 119L, LongTermAttendRules.AttendStep.NoMerat)]
    [TestCase(true, 0, 0, 500L, LongTermAttendRules.AttendStep.Full)]
    public void CheckAttend_PointOrMerat(bool withMerat, int points, int missed, long merets, LongTermAttendRules.AttendStep expected) {
        Assert.That(LongTermAttendRules.CheckAttend(withMerat, points, missed, merets, 120), Is.EqualTo(expected));
    }

    [Test]
    public void CheckClaim_AttendedUnclaimedRowOnly() {
        Assert.Multiple(() => {
            Assert.That(LongTermAttendRules.CheckClaim(Gift, 1, 1, "")?.Item01.ItemId, Is.EqualTo(900001));
            Assert.That(LongTermAttendRules.CheckClaim(Gift, 5, 7, "1")?.Item01.ItemId, Is.EqualTo(900003));
            Assert.That(LongTermAttendRules.CheckClaim(Gift, 5, 4, "1"), Is.Null); // not attended yet
            Assert.That(LongTermAttendRules.CheckClaim(Gift, 1, 7, "5,1"), Is.Null); // claimed
            Assert.That(LongTermAttendRules.CheckClaim(Gift, 3, 7, null), Is.Null); // no row
            Assert.That(LongTermAttendRules.CheckClaim(Gift, 0, 7, null), Is.Null);
        });
    }

    [Test]
    public void ClaimedDays_CsvAsTheClientParsesIt() {
        Assert.That(LongTermAttendRules.AddClaimedDay(null, 5), Is.EqualTo("5"));
        Assert.That(LongTermAttendRules.AddClaimedDay("5,11", 20), Is.EqualTo("5,11,20"));
        Assert.That(LongTermAttendRules.AddClaimedDay("5,11", 11), Is.EqualTo("5,11"));
        Assert.That(LongTermAttendRules.ClaimedDays("5,,x,11"), Is.EqualTo(new[] { 5, 11 }));
    }

    [Test]
    public void PointDue_OncePerDay() {
        var now = new DateTime(2030, 6, 15, 0, 5, 0);
        Assert.Multiple(() => {
            Assert.That(LongTermAttendRules.PointDue(null, now), Is.True);
            Assert.That(LongTermAttendRules.PointDue(new DateTime(2030, 6, 14, 23, 59, 0), now), Is.True);
            Assert.That(LongTermAttendRules.PointDue(new DateTime(2030, 6, 15, 0, 1, 0), now), Is.False);
        });
    }

    [Test]
    public void Reader_ParsesInfoAndRewards() {
        LongTermAttendGift? gift = LongTermAttendGiftReader.Parse(
            "<ms><info title=\"s_test_title\" maxattendance=\"30\" merat=\"120\" eventTag=\"testtag\" /></ms>",
            "<ms><reward count=\"1\" item01=\"900001,1,5\" item02=\"900002,2,1\" /><reward count=\"5\" item01=\"900003,1,1\" item02=\"\" /><reward item01=\"900004,1,1\" /></ms>");
        Assert.That(gift, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That((gift!.Title, gift.EventTag, gift.MaxAttendance, gift.Merat), Is.EqualTo(("s_test_title", "testtag", 30, 120)));
            Assert.That(gift.Rewards, Has.Length.EqualTo(2)); // the row without a count is skipped
            Assert.That(gift.Rewards[0], Is.EqualTo(Gift.Rewards[0]));
            Assert.That(gift.Rewards[1].Item02, Is.EqualTo(LongTermAttendRules.EmptyItem));
        });
        Assert.That(LongTermAttendGiftReader.Parse("<ms><info title=\"t\" merat=\"120\" /></ms>", "<ms></ms>"), Is.Null);
        Assert.That(LongTermAttendGiftReader.Parse("", "<ms></ms>"), Is.Null);
    }

    // LongTermAttendGiftContext::Read after GameEvent::vfunc_4's id.
    [Test]
    public void GameEvent_WritesWhatTheClientReads() {
        var metadata = new GameEventMetadata(990001, GameEventType.LongTermAttendGift, new DateTime(2030, 6, 15, 8, 0, 0),
            new DateTime(2030, 7, 21, 0, 0, 0), TimeSpan.Zero, TimeSpan.Zero, [], Gift, "", "", "", "");
        var gameEvent = new GameEvent(metadata);
        var writer = new ByteWriter();
        gameEvent.WriteTo(writer);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("LongTermAttendGift"));
        Assert.That(reader.ReadInt(), Is.EqualTo(990001));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("testtag"));
        Assert.That(reader.ReadLong(), Is.EqualTo(gameEvent.EndTime));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        AssertItem(reader, 900001, 1, 5);
        AssertItem(reader, 900002, 2, 1);
        Assert.That(reader.ReadInt(), Is.EqualTo(5));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        AssertItem(reader, 900003, 1, 1);
        AssertItem(reader, 0, 1, 1);
        Assert.That(reader.ReadLong(), Is.EqualTo(gameEvent.StartTime));
        Assert.That(reader.ReadLong(), Is.EqualTo(gameEvent.EndTime));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("s_test_title"));
        Assert.That(reader.ReadInt(), Is.EqualTo(120));
        Assert.That(reader.ReadInt(), Is.EqualTo(30));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
        Assert.That(gameEvent.SentToClient, Is.True);
    }

    private static void AssertItem(ByteReader reader, int itemId, short rarity, int amount) {
        Assert.That(reader.ReadInt(), Is.EqualTo(itemId));
        Assert.That(reader.ReadShort(), Is.EqualTo(rarity));
        Assert.That(reader.ReadInt(), Is.EqualTo(amount));
        for (int i = 0; i < 4; i++) {
            Assert.That(reader.ReadBool(), Is.False);
        }
    }

    // The replies carry no body.
    [Test]
    public void Replies_AreTheSubTypeOnly() {
        Assert.That(Bytes(EventRewardPacket.ClaimLongTermAttendReward()), Is.EqualTo(new byte[] { 0x19 }));
        Assert.That(Bytes(EventRewardPacket.LongTermAttend()), Is.EqualTo(new byte[] { 0x1a }));
        Assert.That(Bytes(EventRewardPacket.LongTermAttendPoint()), Is.EqualTo(new byte[] { 0x1b }));
    }

    private static byte[] Bytes(ByteWriter writer) {
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.EventReward));
        return reader.ReadBytes(writer.Length - reader.Position);
    }

    [Test]
    public void UserValueTypes_AreTheClientKeys() {
        Assert.That((int) GameEventUserValueType.LongTermAttendClaimedDays, Is.EqualTo(0xa8c));
        Assert.That((int) GameEventUserValueType.LongTermAttendPoints, Is.EqualTo(0xa8d));
        Assert.That((int) GameEventUserValueType.LongTermAttendDays, Is.EqualTo(0xa8e));
        Assert.That((int) GameEventUserValueType.LongTermAttendPointTime, Is.EqualTo(0xa8f));
    }

    // MS2_REPLAY_ATTEND_GIFT also replays the latest ended long-term event; its rows end to the second.
    [Test]
    public void Replay_PicksLongTermByTypeAndReadsSeconds() {
        Assert.That(AttendGiftReplay.ParseTime("2029-05-15-00-00-00"), Is.EqualTo(new DateTime(2029, 5, 15)));
        Assert.That(AttendGiftReplay.ParseTime("2029-05-15-07-30"), Is.EqualTo(new DateTime(2029, 5, 15, 7, 30, 0)));
        Assert.That(AttendGiftReplay.ParseTime("2029-05-15"), Is.EqualTo(DateTime.MinValue));
        Assert.That(AttendGiftReplay.Types, Is.EqualTo(new[] {
            GameEventType.AttendGift, GameEventType.LongTermAttendGift, GameEventType.StampEvent,
            GameEventType.SaleUgcDesign, GameEventType.SaleHousing, GameEventType.FindDungeonHelperBonus,
            GameEventType.SaleRemake, GameEventType.SaleBeautyShop, GameEventType.SAHotTime,
            GameEventType.DungeonBonusReward, GameEventType.CollectItemGroup, GameEventType.ArcadeOpen,
            GameEventType.TreeWatering, GameEventType.RPS, GameEventType.BingoEvent, GameEventType.TimeRunEvent,
            GameEventType.CoupleDance,
        }));

        var now = new DateTime(2030, 6, 15, 9, 30, 0);
        GameEventMetadata[] events = [
            Event(990010, GameEventType.LongTermAttendGift, "2029-01-08-08-00", "2029-02-15-00-00-00"),
            Event(990011, GameEventType.LongTermAttendGift, "2029-04-08-08-00", "2029-05-15-00-00-00"),
            Event(990012, GameEventType.AttendGift, "2029-06-01-00-00", "2029-06-10-23-59"),
        ];
        Assert.That(AttendGiftReplay.Pick(events, now, GameEventType.LongTermAttendGift)?.Id, Is.EqualTo(990011));
        Assert.That(AttendGiftReplay.Pick(events, now)?.Id, Is.EqualTo(990012));

        GameEventMetadata moved = AttendGiftReplay.Redate(events[1], now);
        Assert.That((moved.StartTime, moved.EndTime), Is.EqualTo((new DateTime(2030, 6, 15, 8, 0, 0), new DateTime(2030, 7, 22, 0, 0, 0))));
    }

    // A long-term reward with an item the client's item data lacks has no icon or name, so that calendar is passed
    // over; the empty slot (item 0) is not an item.
    [Test]
    public void Replay_PassesOverALongTermEventWithAnItemTheClientHasNoRankFor() {
        var now = new DateTime(2030, 6, 15, 9, 30, 0);
        int ClientRank(int itemId) => itemId is 900001 or 900002 or 900003 ? 1 : 0;
        GameEventMetadata known = Event(990020, GameEventType.LongTermAttendGift, "2029-01-08-08-00", "2029-02-15-00-00-00");
        GameEventMetadata unknown = Event(990021, GameEventType.LongTermAttendGift, "2029-04-08-08-00", "2029-05-15-00-00-00") with {
            Data = Gift with { Rewards = [new LongTermAttendGift.Reward(1, new RewardItem(900001, 1, 1), new RewardItem(900404, 1, 1))] },
        };
        Assert.That(AttendGiftReplay.Pick([known, unknown], now, GameEventType.LongTermAttendGift)?.Id, Is.EqualTo(990021));
        Assert.That(AttendGiftReplay.Pick([known, unknown], now, GameEventType.LongTermAttendGift, ClientRank)?.Id, Is.EqualTo(990020));
    }

    // Text that is not XML is a row that cannot be read, not an error that stops the ingest.
    [Test]
    public void Reader_ReturnsNullForTextThatIsNotXml() {
        const string info = "<ms><info title=\"t\" maxattendance=\"30\" merat=\"120\" /></ms>";
        Assert.That(LongTermAttendGiftReader.Parse("<ms><info", "<ms></ms>"), Is.Null);
        Assert.That(LongTermAttendGiftReader.Parse(info, "<ms><reward count=\"1\""), Is.Null);
        Assert.That(LongTermAttendGiftReader.Parse(info, "<ms></ms>"), Is.Not.Null);
    }

    // A later ingest moves a replayed event again. The values stored for the first run follow the moved end, so
    // they do not run out while the event is live and a claimed day stays claimed.
    [Test]
    public void StoredValues_FollowTheMovedEventEnd() {
        var first = new GameEvent(Event(990030, GameEventType.LongTermAttendGift, "2030-06-15-08-00", "2030-07-23-00-00-00"));
        var moved = new GameEvent(Event(990030, GameEventType.LongTermAttendGift, "2030-06-25-08-00", "2030-08-02-00-00-00"));
        var claimed = new GameEventUserValue(GameEventUserValueType.LongTermAttendClaimedDays, first.EndTime, 990030);
        claimed.SetValue("1,5");
        var days = new GameEventUserValue(GameEventUserValueType.LongTermAttendDays, first.EndTime, 990030);
        days.SetValue("10");

        GameEventUserValue claimedNow = GameEventManager.FollowEventEnd(claimed, moved);
        GameEventUserValue daysNow = GameEventManager.FollowEventEnd(days, moved);
        Assert.Multiple(() => {
            Assert.That((claimedNow.Type, claimedNow.EventId, claimedNow.Value, claimedNow.ExpirationTime),
                Is.EqualTo((GameEventUserValueType.LongTermAttendClaimedDays, 990030, "1,5", moved.EndTime)));
            Assert.That((daysNow.Int(), daysNow.ExpirationTime), Is.EqualTo((10, moved.EndTime)));
            // After the first end, still inside the moved run: the claimed rows stay refused.
            Assert.That(LongTermAttendRules.CheckClaim(Gift, 1, daysNow.Int(), claimedNow.Value), Is.Null);
            Assert.That(LongTermAttendRules.CheckClaim(Gift, 5, daysNow.Int(), claimedNow.Value), Is.Null);
            // An end that did not move, another value type and another event type are left as they are.
            Assert.That(GameEventManager.FollowEventEnd(claimed, first), Is.SameAs(claimed));
            var other = new GameEventUserValue(GameEventUserValueType.GalleryCardFlipCount, first.EndTime - 500, 990030);
            Assert.That(GameEventManager.FollowEventEnd(other, moved), Is.SameAs(other));
            var gallery = new GameEvent(Event(990031, GameEventType.Gallery, "2030-06-25-08-00", "2030-08-02-00-00"));
            Assert.That(GameEventManager.FollowEventEnd(claimed, gallery), Is.SameAs(claimed));
        });
    }

    private static GameEventMetadata Event(int id, GameEventType type, string start, string end) {
        return new GameEventMetadata(id, type, AttendGiftReplay.ParseTime(start), AttendGiftReplay.ParseTime(end), TimeSpan.Zero,
            TimeSpan.Zero, [], Gift, "", "", "", "");
    }
}
