using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game.Manager;

// The weekly season reward of a week that ended while the player is online: a clear of a season rank room after the
// week's end would write the new week's score over the ended one, so the ended week is mailed first, once.
public class DungeonSeasonMailTests {
    private const int Room = 23000101, OtherRoom = 23000102, RewardItem = 20300001, MailId = 9001;

    // seasonStartHour: the season's start time of day, its weekly end. stored: season records written before the
    // session (room, score, expiry).
    private static (TestGameSession Test, GameSession Session) StartInSeasonRoom(int seasonStartHour = 0,
        params (int Room, short Score, long Expiry)[] stored) {
        var test = new TestGameSession();
        DungeonRoomMetadata room = TestGameSession.DungeonRoom(Room, 2000062) with { GroupType = DungeonGroupType.chaosRaid };
        DungeonRoomMetadata other = TestGameSession.DungeonRoom(OtherRoom, 2000062) with { GroupType = DungeonGroupType.chaosRaid };
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> { [Room] = room, [OtherRoom] = other }));
        // Chaos raids use rank table 3 (DungeonRoomData::GetRankTableID).
        test.SetTable(new DungeonConfigTable(new Dictionary<int, int>(), new Dictionary<int, DungeonMissionRankMetadata> {
            [3] = new(3, string.Empty, 1000, []),
        }));
        test.SetTable(new DungeonSeasonTable([
            new DungeonSeasonTable.Entry(1, DateTime.Today.AddDays(-30).AddHours(seasonStartHour), DateTime.Today.AddDays(30), 0, MailId, 6,
                [new DungeonSeasonTable.Reward(100, RewardItem)]),
        ]));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        test.Register(TestGameSession.Metadata(RewardItem));
        using (Maple2.Database.Storage.GameStorage.Request db = test.Storage.Context()) {
            foreach ((int id, short score, long expiry) in stored) {
                Assert.That(db.CreateDungeonRecord(new DungeonRecord(id) { CurrentRecord = score, CooldownTimestamp = expiry }, test.CharacterId),
                    Is.Not.Null);
            }
        }
        GameSession session = test.Start();
        test.EnterDungeon(room).State = DungeonState.Clear;
        test.TakeSent();
        return (test, session);
    }

    private static List<Maple2.Database.Model.Mail> Mails(TestGameSession test) {
        using var context = new Ms2Context(test.Options);
        return context.Mail.AsNoTracking().Where(mail => mail.ReceiverId == test.CharacterId).ToList();
    }

    [Test]
    public void ClearAfterTheWeekEnded_MailsTheEndedWeekBeforeItsScoreIsReplaced() {
        (TestGameSession test, GameSession session) = StartInSeasonRoom();
        long now = DateTime.Now.ToEpochSeconds();
        DungeonRecord record = session.Dungeon.Records[Room];
        record.CurrentRecord = 500;
        record.CooldownTimestamp = now - 3600; // the week ended an hour ago, while the player was online

        session.Dungeon.CompleteDungeon(now);

        List<Maple2.Database.Model.Mail> mails = Mails(test);
        Assert.That(mails.Select(mail => mail.Content), Is.EqualTo(new[] { MailId.ToString() }));
        Assert.That(record.CooldownTimestamp, Is.GreaterThan(now), "the clear starts the new week");

        // The week is mailed once: the weekly reset and the next login find nothing more to send.
        session.Dungeon.MailSeasonRewards();
        session.Dungeon.ResetWeeklyClears();
        Assert.That(Mails(test), Has.Count.EqualTo(1));
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void ClearInTheRunningWeek_MailsNothing() {
        (TestGameSession test, GameSession session) = StartInSeasonRoom();
        long now = DateTime.Now.ToEpochSeconds();
        DungeonRecord record = session.Dungeon.Records[Room];
        record.CurrentRecord = 500;
        record.CooldownTimestamp = now + 3600;

        session.Dungeon.CompleteDungeon(now);

        Assert.That(Mails(test), Is.Empty);
        Assert.That(record.CurrentRecord, Is.EqualTo(500), "the week's best score stays");
    }

    // The deploy week of the week end moved from midnight to the season's start time (08:00): records of the running
    // week written before it end at that Friday's midnight, records cleared after it at 08:00. Read at login, the
    // midnight end is the same week's end, so the rooms cleared before and after are one week with one total (one
    // mail at 08:00), and a clear between midnight and 08:00 keeps the score of the room.
    [Test]
    public void AMidnightWeekEnd_WrittenBeforeTheStartTimeEnd_IsTheSameWeek() {
        DateTime end = DungeonScoreRules.SeasonExpiry(DateTime.Now, 6, TimeSpan.FromHours(8), DateTime.Today.AddDays(30));
        long oldEnd = end.AddHours(-8).ToEpochSeconds();
        (TestGameSession test, GameSession session) = StartInSeasonRoom(8, (Room, 500, oldEnd), (OtherRoom, 300, oldEnd));
        Assert.That(session.Dungeon.Records.Values.Where(record => record.DungeonId is Room or OtherRoom).Select(record => record.CooldownTimestamp),
            Is.EqualTo(new[] { end.ToEpochSeconds(), end.ToEpochSeconds() }));

        session.Dungeon.CompleteDungeon(DateTime.Now.ToEpochSeconds());

        List<IGrouping<long, DungeonRecord>> weeks = DungeonScoreRules.FinishedWeeks(session.Dungeon.Records.Values, end.ToEpochSeconds()).ToList();
        Assert.That(weeks.Select(week => (week.Key, DungeonScoreRules.SeasonTotal(week.Select(record => record.CurrentRecord), 0))),
            Is.EqualTo(new[] { (end.ToEpochSeconds(), 800) }));
        Assert.That(Mails(test), Is.Empty);
        Assert.That(test.Errors, Is.Empty);
    }

    // A midnight end in a season that starts at midnight, or one not on the season's day, is left as it is.
    [Test]
    public void AMidnightWeekEnd_OfAMidnightSeason_IsLeft() {
        DateTime friday = DungeonScoreRules.SeasonExpiry(DateTime.Now, 6, TimeSpan.Zero, DateTime.Today.AddDays(30));
        var midnight = new DungeonSeasonTable([
            new DungeonSeasonTable.Entry(1, DateTime.Today.AddDays(-30), DateTime.Today.AddDays(30), 0, MailId, 6, []),
        ]);
        var eight = new DungeonSeasonTable([
            new DungeonSeasonTable.Entry(1, DateTime.Today.AddDays(-30).AddHours(8), DateTime.Today.AddDays(30), 0, MailId, 6, []),
        ]);
        Assert.That(DungeonScoreRules.NormalizeSeasonExpiry(friday.ToEpochSeconds(), midnight), Is.EqualTo(friday.ToEpochSeconds()));
        Assert.That(DungeonScoreRules.NormalizeSeasonExpiry(friday.AddDays(-1).ToEpochSeconds(), eight), Is.EqualTo(friday.AddDays(-1).ToEpochSeconds()));
        Assert.That(DungeonScoreRules.NormalizeSeasonExpiry(friday.ToEpochSeconds(), eight), Is.EqualTo(friday.AddHours(8).ToEpochSeconds()));
    }
}
