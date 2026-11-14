using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game.Manager;

// The once-only marks of a dungeon's rewards are stored with (or before) the rewards: a clear's counts before any of
// its rewards (the items are rows at once), a season rank reward's ranks given with its mails, a season week's
// scores set to 0 with its mail. Before, the marks stayed in memory until the next session save, so a crash in
// between let the same reward be paid again. The ids are made up.
public class DungeonClaimOrderTests {
    // The session under test; the MySQL subclass gives one on a real database.
    protected virtual TestGameSession NewSession() => new();

    protected const int Room = 24090004, RewardId = 10005, FirstItem = 20301316, FirstMail = 53000006;
    protected const int SeasonRoom = 23000101, SeasonItem = 20300001, SeasonMail = 9001;

    protected TestGameSession test = null!;
    protected GameSession session => test.Session;

    // A room with meso, a season rank reward of 5 ranks (rank table 6: C 100 ... S+ 500), 3 counts.
    protected void StartInRoom() {
        test = NewSession();
        DungeonRoomMetadata room = TestGameSession.DungeonRoom(Room, 2000062, rewardCount: 3);
        room = room with {
            GroupType = DungeonGroupType.worldBossDungeon,
            Reward = room.Reward with { SeasonRankRewardId = RewardId, Meso = 1000 },
        };
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> { [Room] = room }));
        test.SetTable(new DungeonConfigTable(new Dictionary<int, int>(), new Dictionary<int, DungeonMissionRankMetadata> {
            [6] = new(6, string.Empty, 1000, [new(DungeonMissionRank.C, 100), new(DungeonMissionRank.B, 200),
                new(DungeonMissionRank.A, 300), new(DungeonMissionRank.S, 400), new(DungeonMissionRank.SPlus, 500)]),
        }));
        test.SetTable(new DungeonRankRewardTable(new Dictionary<int, DungeonRankRewardTable.Entry> {
            [RewardId] = new(RewardId, Enumerable.Range(1, 5)
                .Select(rank => new DungeonRankRewardTable.Entry.Item(rank, FirstItem + rank - 1, FirstMail + rank - 1)).ToArray()),
        }));
        test.SetTable(new DungeonSeasonTable([
            new DungeonSeasonTable.Entry(1002, DateTime.Today.AddDays(-30).AddHours(8), DateTime.Today.AddDays(30), 0, 53000000, 6, []),
        ]));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        for (int rank = 1; rank <= 5; rank++) {
            test.Register(TestGameSession.Metadata(FirstItem + rank - 1));
        }
        test.Start();
        test.EnterDungeon(room).State = DungeonState.Clear;
        test.TakeSent();
    }

    protected void Clear(short score, long now) {
        session.Dungeon.UserRecord!.Missions.Clear();
        session.Dungeon.UserRecord.Missions[1] = new DungeonMission(new DungeonMissionMetadata(1, DungeonMissionType.PlayTime, [], 0, score, 1, true));
        session.Dungeon.CompleteDungeon(now);
    }

    protected List<string> Mails() {
        using var context = new Ms2Context(test.Options);
        return context.Mail.AsNoTracking().Where(mail => mail.ReceiverId == test.CharacterId).OrderBy(mail => mail.Id)
            .Select(mail => mail.Content).ToList();
    }

    protected (int Clears, short Score)? StoredRecord(int dungeonId) {
        using var context = new Ms2Context(test.Options);
        return context.DungeonRecord.AsNoTracking().Where(record => record.OwnerId == test.CharacterId && record.DungeonId == dungeonId)
            .Select(record => new { Clears = (int) record.CurrentClears, Score = record.CurrentRecord })
            .AsEnumerable().Select(record => ((int, short)?) (record.Clears, record.Score)).FirstOrDefault();
    }

    protected int StoredRankClaimed() {
        using var context = new Ms2Context(test.Options);
        Maple2.Database.Model.CharacterUnlock unlock = context.CharacterUnlock.AsNoTracking().Single(row => row.CharacterId == test.CharacterId);
        return unlock.DungeonRankRewards.TryGetValue(RewardId, out Maple2.Database.Model.DungeonRankReward? reward) ? reward.RankClaimed : 0;
    }

    protected static Func<DbContext, bool> Fails(string type) => context =>
        context.ChangeTracker.Entries().Any(entry => entry.State is EntityState.Added or EntityState.Modified && entry.Metadata.ClrType.Name == type);

    // The clear's count is in the database when its rewards are given, with no session save.
    [Test]
    public void AClear_StoresItsCountWithItsRewards() {
        StartInRoom();
        Clear(250, DateTime.Now.ToEpochSeconds());

        Assert.That(session.Currency.Meso, Is.GreaterThan(0));
        Assert.That(StoredRecord(Room)?.Clears, Is.EqualTo(1));
        Assert.That(test.Errors, Is.Empty);
    }

    // A refused store of the counts gives nothing: no meso, no rank mail, and the count is back as it was.
    [Test]
    public void ARefusedCountStore_GivesNoReward() {
        StartInRoom();
        test.FailSave = Fails("DungeonRecord");
        Clear(250, DateTime.Now.ToEpochSeconds());
        test.FailSave = null;

        Assert.Multiple(() => {
            Assert.That(session.Currency.Meso, Is.EqualTo(0));
            Assert.That(session.Dungeon.Records.TryGetValue(Room, out DungeonRecord? record) ? record.UnionClears : 0, Is.EqualTo(0));
            Assert.That(session.Dungeon.UserRecord!.Rewards.Values.Sum(), Is.EqualTo(0));
            Assert.That(StoredRecord(Room)?.Clears ?? 0, Is.EqualTo(0));
        });
    }

    // The ranks given are stored with their mails, with no session save.
    [Test]
    public void ARankReward_IsStoredWithItsMails() {
        StartInRoom();
        Clear(250, DateTime.Now.ToEpochSeconds()); // B: C and B

        Assert.That(Mails(), Is.EqualTo(new[] { FirstMail.ToString(), (FirstMail + 1).ToString() }));
        Assert.That(StoredRankClaimed(), Is.EqualTo(2));
    }

    // A refused store of the ranks mails nothing and leaves the ranks given as they were, so the next clear gives them.
    [Test]
    public void ARefusedRankStore_MailsNothing() {
        StartInRoom();
        long now = DateTime.Now.ToEpochSeconds();
        test.FailSave = Fails("CharacterUnlock");
        Clear(250, now);
        test.FailSave = null;

        Assert.Multiple(() => {
            Assert.That(Mails(), Is.Empty);
            Assert.That(session.Player.Value.Unlock.DungeonRankRewards.ContainsKey(RewardId), Is.False);
            Assert.That(StoredRankClaimed(), Is.EqualTo(0));
        });

        Clear(250, now);
        Assert.That(Mails(), Has.Count.EqualTo(2));
        Assert.That(StoredRankClaimed(), Is.EqualTo(2));
    }

    // A chaos raid room in a season whose week ended with a score of 500 (the season reward at 100).
    protected DungeonRecord StartInSeasonRoom(long now) {
        test = NewSession();
        DungeonRoomMetadata room = TestGameSession.DungeonRoom(SeasonRoom, 2000062) with { GroupType = DungeonGroupType.chaosRaid };
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> { [SeasonRoom] = room }));
        test.SetTable(new DungeonConfigTable(new Dictionary<int, int>(), new Dictionary<int, DungeonMissionRankMetadata> {
            [3] = new(3, string.Empty, 1000, []),
        }));
        test.SetTable(new DungeonSeasonTable([
            new DungeonSeasonTable.Entry(1, DateTime.Today.AddDays(-30), DateTime.Today.AddDays(30), 0, SeasonMail, 6,
                [new DungeonSeasonTable.Reward(100, SeasonItem)]),
        ]));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        test.Register(TestGameSession.Metadata(SeasonItem));
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.CreateDungeonRecord(new DungeonRecord(SeasonRoom) { CurrentRecord = 500, CooldownTimestamp = now - 3600 }, test.CharacterId),
                Is.Not.Null);
        }
        test.Start();
        return session.Dungeon.Records[SeasonRoom];
    }

    // The week's mail and its scores set to 0 are stored together.
    [Test]
    public void ASeasonMail_IsStoredWithTheWeeksScores() {
        long now = DateTime.Now.ToEpochSeconds();
        DungeonRecord record = StartInSeasonRoom(now);

        session.Dungeon.MailSeasonRewards();

        Assert.That(Mails(), Is.EqualTo(new[] { SeasonMail.ToString() }));
        Assert.That(record.CurrentRecord, Is.EqualTo(0));
        Assert.That(StoredRecord(SeasonRoom)?.Score, Is.EqualTo((short) 0));
    }

    // A refused store mails nothing and keeps the week's scores, so the next run mails the week once.
    [Test]
    public void ARefusedSeasonStore_MailsNothing_AndKeepsTheScores() {
        long now = DateTime.Now.ToEpochSeconds();
        DungeonRecord record = StartInSeasonRoom(now);

        test.FailSave = Fails("DungeonRecord");
        session.Dungeon.MailSeasonRewards();
        test.FailSave = null;
        Assert.Multiple(() => {
            Assert.That(Mails(), Is.Empty);
            Assert.That(record.CurrentRecord, Is.EqualTo(500));
            Assert.That(StoredRecord(SeasonRoom)?.Score, Is.EqualTo((short) 500));
        });

        session.Dungeon.MailSeasonRewards();
        Assert.That(Mails(), Has.Count.EqualTo(1));
        Assert.That(record.CurrentRecord, Is.EqualTo(0));
    }
}
