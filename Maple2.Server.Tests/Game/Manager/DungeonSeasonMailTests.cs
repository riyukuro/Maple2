using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game.Manager;

// The weekly season reward of a week that ended while the player is online: a clear of a season rank room after the
// week's end would write the new week's score over the ended one, so the ended week is mailed first, once.
public class DungeonSeasonMailTests {
    private const int Room = 23000101, RewardItem = 20300001, MailId = 9001;

    private static (TestGameSession Test, GameSession Session) StartInSeasonRoom() {
        var test = new TestGameSession();
        DungeonRoomMetadata room = TestGameSession.DungeonRoom(Room, 2000062) with { GroupType = DungeonGroupType.chaosRaid };
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> { [Room] = room }));
        // Chaos raids use rank table 3 (DungeonRoomData::GetRankTableID).
        test.SetTable(new DungeonConfigTable(new Dictionary<int, int>(), new Dictionary<int, DungeonMissionRankMetadata> {
            [3] = new(3, string.Empty, 1000, []),
        }));
        test.SetTable(new DungeonSeasonTable([
            new DungeonSeasonTable.Entry(1, DateTime.Today.AddDays(-30), DateTime.Today.AddDays(30), 0, MailId, 6,
                [new DungeonSeasonTable.Reward(100, RewardItem)]),
        ]));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        test.Register(TestGameSession.Metadata(RewardItem));
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
}
