using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game.Manager;

// A room's season rank reward (seasonRankRewardID): a clear mails each rank it reaches above the ones given in the
// season week, and RoomDungeon 20 tells the client the count and the week's end. A World Invasion room
// (worldBossDungeon, rank table 6), not a season rank room, as the KR room 24090004 with reward 10005.
public class DungeonRankRewardTests {
    private const int Room = 24090004, RewardId = 10005, FirstItem = 20301316, FirstMail = 53000006, SeasonMail = 53000000;

    // ranks: the reward's ranks (1 = C ... 5 = S+), each with item FirstItem + rank - 1 and mail FirstMail + rank - 1.
    private static (TestGameSession Test, GameSession Session) StartInRoom(int ranks = 5) {
        var test = new TestGameSession();
        DungeonRoomMetadata room = TestGameSession.DungeonRoom(Room, 2000062);
        room = room with {
            GroupType = DungeonGroupType.worldBossDungeon,
            Reward = room.Reward with { SeasonRankRewardId = RewardId },
        };
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> { [Room] = room }));
        // Rank table 6: C 100, B 200, A 300, S 400, S+ 500.
        test.SetTable(new DungeonConfigTable(new Dictionary<int, int>(), new Dictionary<int, DungeonMissionRankMetadata> {
            [6] = new(6, string.Empty, 1000, [new(DungeonMissionRank.C, 100), new(DungeonMissionRank.B, 200),
                new(DungeonMissionRank.A, 300), new(DungeonMissionRank.S, 400), new(DungeonMissionRank.SPlus, 500)]),
        }));
        test.SetTable(new DungeonRankRewardTable(new Dictionary<int, DungeonRankRewardTable.Entry> {
            [RewardId] = new(RewardId, Enumerable.Range(1, ranks)
                .Select(rank => new DungeonRankRewardTable.Entry.Item(rank, FirstItem + rank - 1, FirstMail + rank - 1)).ToArray()),
        }));
        test.SetTable(new DungeonSeasonTable([
            new DungeonSeasonTable.Entry(1002, DateTime.Today.AddDays(-30).AddHours(8), DateTime.Today.AddDays(30), 0, SeasonMail, 6, []),
        ]));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        for (int rank = 1; rank <= 5; rank++) {
            test.Register(TestGameSession.Metadata(FirstItem + rank - 1));
        }
        GameSession session = test.Start();
        test.EnterDungeon(room).State = DungeonState.Clear;
        test.TakeSent();
        return (test, session);
    }

    // A clear whose score is the one penalty mission's full score.
    private static void Clear(GameSession session, short score, long now) {
        session.Dungeon.UserRecord!.Missions.Clear();
        session.Dungeon.UserRecord.Missions[1] = new DungeonMission(new DungeonMissionMetadata(1, DungeonMissionType.PlayTime, [], 0, score, 1, true));
        session.Dungeon.CompleteDungeon(now);
    }

    private static List<string> Mails(TestGameSession test) {
        using var context = new Ms2Context(test.Options);
        return context.Mail.AsNoTracking().Where(mail => mail.ReceiverId == test.CharacterId).OrderBy(mail => mail.Id)
            .Select(mail => mail.Content).ToList();
    }

    private static string[] MailIds(params int[] ranks) => ranks.Select(rank => (FirstMail + rank - 1).ToString()).ToArray();

    private static byte[][] RankRewardPackets(TestGameSession test) =>
        test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.RoomDungeon && packet[2] == 20).ToArray();

    [Test]
    public void AClear_MailsEachRankUpToItsOwn_AndTellsTheClient() {
        (TestGameSession test, GameSession session) = StartInRoom();
        long now = DateTime.Now.ToEpochSeconds();

        Clear(session, 250, now); // B

        Assert.That(Mails(test), Is.EqualTo(MailIds(1, 2)));
        DungeonRankReward reward = session.Player.Value.Unlock.DungeonRankRewards[RewardId];
        Assert.That(reward.RankClaimed, Is.EqualTo(2));
        long weekEnd = Maple2.Model.Validators.DungeonScoreRules.SeasonExpiry(now.FromEpochSeconds(), 6, TimeSpan.FromHours(8),
            DateTime.Today.AddDays(30)).ToEpochSeconds();
        Assert.That(reward.ExpiryTimestamp, Is.EqualTo(weekEnd));

        // RoomDungeon 20 (OnAddReward): the reward alone, i32 id, i32 count, i64 expiry.
        byte[][] packets = RankRewardPackets(test);
        Assert.That(packets, Has.Length.EqualTo(1));
        Assert.That(packets[0].Length, Is.EqualTo(3 + 16));
        Assert.That(BitConverter.ToInt32(packets[0], 3), Is.EqualTo(RewardId));
        Assert.That(BitConverter.ToInt32(packets[0], 7), Is.EqualTo(2));
        Assert.That(BitConverter.ToInt64(packets[0], 11), Is.EqualTo(weekEnd));
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void InTheSameWeek_OnlyHigherRanksAreGiven() {
        (TestGameSession test, GameSession session) = StartInRoom();
        long now = DateTime.Now.ToEpochSeconds();
        Clear(session, 250, now); // B: C and B
        Clear(session, 120, now); // C: nothing
        Clear(session, 250, now); // B again: nothing
        Assert.That(Mails(test), Is.EqualTo(MailIds(1, 2)));
        RankRewardPackets(test);

        Clear(session, 310, now); // A: A only
        Assert.That(Mails(test), Is.EqualTo(MailIds(1, 2, 3)));
        Assert.That(session.Player.Value.Unlock.DungeonRankRewards[RewardId].RankClaimed, Is.EqualTo(3));
        Assert.That(RankRewardPackets(test), Has.Length.EqualTo(1));
    }

    [Test]
    public void AfterTheWeekEnds_TheRanksAreGivenAgain() {
        (TestGameSession test, GameSession session) = StartInRoom();
        long now = DateTime.Now.ToEpochSeconds();
        session.Player.Value.Unlock.DungeonRankRewards[RewardId] = new DungeonRankReward(RewardId) { RankClaimed = 5, ExpiryTimestamp = now - 1 };

        Clear(session, 250, now);

        Assert.That(Mails(test), Is.EqualTo(MailIds(1, 2)));
        DungeonRankReward reward = session.Player.Value.Unlock.DungeonRankRewards[RewardId];
        Assert.That(reward.RankClaimed, Is.EqualTo(2));
        Assert.That(reward.ExpiryTimestamp, Is.GreaterThan(now));
    }

    // The Reverse Raid reward 10003 has 4 ranks (C to S): an S+ clear gives those 4.
    [Test]
    public void ARankAboveTheRewardsHighest_GivesUpToTheHighest() {
        (TestGameSession test, GameSession session) = StartInRoom(ranks: 4);
        Clear(session, 600, DateTime.Now.ToEpochSeconds());
        Assert.That(Mails(test), Is.EqualTo(MailIds(1, 2, 3, 4)));
        Assert.That(session.Player.Value.Unlock.DungeonRankRewards[RewardId].RankClaimed, Is.EqualTo(4));
    }

    [Test]
    public void AnFClear_GivesNothing() {
        (TestGameSession test, GameSession session) = StartInRoom();
        Clear(session, 50, DateTime.Now.ToEpochSeconds());
        Assert.That(Mails(test), Is.Empty);
        Assert.That(session.Player.Value.Unlock.DungeonRankRewards, Does.Not.ContainKey(RewardId));
        Assert.That(RankRewardPackets(test), Is.Empty);
    }
}
