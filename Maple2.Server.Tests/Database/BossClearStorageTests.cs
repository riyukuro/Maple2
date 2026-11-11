using System;
using System.Collections.Generic;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Web.Controllers;
using Maple2.Tools.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Database;

// The boss clears (boss-clear, boss-clear-member) and the irrq.aspx boss boards built from them, on an in-memory
// database. The answers are read the way the client's GameRanking::BossKillCount, BossKillRecord, BossRumbleCount,
// BossRumbleRecord and MyRank read them.
public class BossClearStorageTests {
    private const int Board1 = 70001;
    private const int Board2 = 70002;

    private GameStorage storage = null!;

    [SetUp]
    public void SetUp() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        storage = new GameStorage(options, null!, null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);
    }

    private long CreateCharacter(string name, Job job, short level = 50, long accountId = 7) {
        using GameStorage.Request db = storage.Context();
        var model = new Character { AccountId = accountId, Name = name, Job = job, Level = level, Picture = $"pic/{name}.png", Mastery = new Mastery() };
        model.ReturnMaps.Push(2000062);
        Character? character = db.CreateCharacter(model);
        Assert.That(character, Is.Not.Null);
        return character!.Id;
    }

    private long Clear(int rankingId, long leaderId, int seconds, long time, params long[] members) {
        using GameStorage.Request db = storage.Context();
        return db.AddBossClear(rankingId, 23000001, leaderId, seconds, time, members);
    }

    private ByteWriter Board(GameRankingType type, int rankingId, string userName = "") {
        using GameStorage.Request db = storage.Context();
        return BossRanking.Board(db, type, rankingId, userName);
    }

    private int PersonalRank(int rankingId, long characterId) {
        using GameStorage.Request db = storage.Context();
        ByteWriter writer = BossRanking.Personal(db, rankingId, characterId);
        var reader = new ByteReader(writer.Buffer[..writer.Length], 0);
        Assert.That(reader.ReadInt(), Is.EqualTo((int) GameRankingType.BossClearCountPersonal));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadUnicodeStringWithLength(), Has.Length.EqualTo(19));
        int rank = reader.ReadInt();
        Assert.That(reader.Available, Is.EqualTo(0));
        return rank;
    }

    private static (int Count, ByteReader Reader) ReadHeader(ByteWriter writer, GameRankingType type) {
        var reader = new ByteReader(writer.Buffer[..writer.Length], 0);
        Assert.That(reader.ReadInt(), Is.EqualTo((int) type));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadUnicodeStringWithLength(), Has.Length.EqualTo(19));
        return (reader.ReadInt(), reader);
    }

    private record Member(long Id, string Name, string Image, int Level, int JobCode);

    private record ClearRow(int Rank, long RowId, long LeaderId, int RankingId, int Seconds, string Date, List<Member> Members);

    private static List<ClearRow> ReadClears(ByteWriter writer, GameRankingType type) {
        (int count, ByteReader reader) = ReadHeader(writer, type);
        var rows = new List<ClearRow>();
        for (int i = 0; i < count; i++) {
            int rank = reader.ReadInt();
            long rowId = reader.ReadLong();
            long leaderId = reader.ReadLong();
            int rankingId = reader.ReadInt();
            int seconds = reader.ReadInt();
            string date = reader.ReadUnicodeStringWithLength();
            int memberCount = reader.ReadInt();
            var members = new List<Member>();
            for (int j = 0; j < memberCount; j++) {
                members.Add(new Member(reader.ReadLong(), reader.ReadUnicodeStringWithLength(), reader.ReadUnicodeStringWithLength(),
                    reader.ReadInt(), reader.ReadInt()));
            }
            rows.Add(new ClearRow(rank, rowId, leaderId, rankingId, seconds, date, members));
        }
        Assert.That(reader.Available, Is.EqualTo(0));
        return rows;
    }

    private static List<(int Rank, long Id, string Name, int Clears)> ReadCounts(ByteWriter writer, int rankingId,
            GameRankingType type = GameRankingType.BossClearCount) {
        (int count, ByteReader reader) = ReadHeader(writer, type);
        var rows = new List<(int, long, string, int)>();
        for (int i = 0; i < count; i++) {
            int rank = reader.ReadInt();
            long id = reader.ReadLong();
            string name = reader.ReadUnicodeStringWithLength();
            Assert.That(reader.ReadUnicodeStringWithLength(), Is.EqualTo($"pic/{name}.png"));
            Assert.That(reader.ReadInt(), Is.EqualTo(rankingId));
            int clears = reader.ReadInt();
            Assert.That(reader.ReadInt(), Is.EqualTo(0));
            rows.Add((rank, id, name, clears));
        }
        Assert.That(reader.Available, Is.EqualTo(0));
        return rows;
    }

    #region Store
    [Test]
    public void Add_KeepsOnlyExistingCharacters() {
        long alpha = CreateCharacter("alpha", Job.Knight);
        Assert.That(Clear(Board1, alpha, 300, 1_800_000_000, alpha, 424242, alpha), Is.GreaterThan(0));

        List<ClearRow> rows = ReadClears(Board(GameRankingType.BossFirstClears, Board1), GameRankingType.BossFirstClears);
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].Members, Has.Count.EqualTo(1));
        Assert.That(rows[0].Members[0].Id, Is.EqualTo(alpha));
    }

    [Test]
    public void Add_NoKnownMember_KeepsNothing() {
        Assert.That(Clear(Board1, 424242, 300, 1_800_000_000, 424242), Is.EqualTo(0));
        Assert.That(ReadHeader(Board(GameRankingType.BossFirstClears, Board1), GameRankingType.BossFirstClears).Count, Is.EqualTo(0));
        Assert.That(ReadHeader(Board(GameRankingType.BossClearCount, Board1), GameRankingType.BossClearCount).Count, Is.EqualTo(0));
    }
    #endregion

    #region Clear boards
    [Test]
    public void EmptyStore_BoardsHaveNoRows() {
        long alpha = CreateCharacter("alpha", Job.Knight);
        Assert.That(ReadClears(Board(GameRankingType.BossFirstClears, Board1), GameRankingType.BossFirstClears), Is.Empty);
        Assert.That(ReadClears(Board(GameRankingType.BossShortestClears, Board1), GameRankingType.BossShortestClears), Is.Empty);
        Assert.That(ReadCounts(Board(GameRankingType.BossClearCount, Board1), Board1), Is.Empty);
        Assert.That(PersonalRank(Board1, alpha), Is.EqualTo(0));
    }

    [Test]
    public void FirstClears_EarliestFirst_EqualTimeByRowId_RowLayout() {
        long alpha = CreateCharacter("alpha", Job.Knight, level: 60);
        long beta = CreateCharacter("beta", Job.Wizard, level: 55);
        long gamma = CreateCharacter("gamma", Job.Priest);
        long late = Clear(Board1, alpha, 100, 1_800_000_300, beta, alpha);
        long early = Clear(Board1, beta, 500, 1_800_000_100, beta);
        long tie = Clear(Board1, gamma, 400, 1_800_000_100, gamma);
        Clear(Board2, gamma, 50, 1_700_000_000, gamma);

        List<ClearRow> rows = ReadClears(Board(GameRankingType.BossFirstClears, Board1), GameRankingType.BossFirstClears);
        Assert.That(rows.ConvertAll(row => (row.Rank, row.RowId)), Is.EqualTo(new[] { (1, early), (2, tie), (3, late) }));

        ClearRow row = rows[2];
        Assert.That(row.LeaderId, Is.EqualTo(alpha));
        Assert.That(row.RankingId, Is.EqualTo(Board1));
        Assert.That(row.Seconds, Is.EqualTo(100));
        Assert.That(row.Date, Is.EqualTo(DateTimeOffset.FromUnixTimeSeconds(1_800_000_300).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")));
        Assert.That(row.Members, Is.EqualTo(new[] {
            new Member(alpha, "alpha", "pic/alpha.png", 60, (int) JobCode.Knight),
            new Member(beta, "beta", "pic/beta.png", 55, (int) JobCode.Wizard),
        }));
    }

    [Test]
    public void ShortestClears_FastestFirst_ThenEarlier_ThenRowId() {
        long alpha = CreateCharacter("alpha", Job.Knight);
        long slow = Clear(Board1, alpha, 900, 1_800_000_000, alpha);
        long fastLate = Clear(Board1, alpha, 200, 1_800_000_500, alpha);
        long fastEarly = Clear(Board1, alpha, 200, 1_800_000_100, alpha);
        long fastEarlyTie = Clear(Board1, alpha, 200, 1_800_000_100, alpha);
        Clear(Board2, alpha, 10, 1_800_000_000, alpha);

        List<ClearRow> rows = ReadClears(Board(GameRankingType.BossShortestClears, Board1), GameRankingType.BossShortestClears);
        Assert.That(rows.ConvertAll(row => row.RowId), Is.EqualTo(new[] { fastEarly, fastEarlyTie, fastLate, slow }));
        Assert.That(rows.ConvertAll(row => row.Rank), Is.EqualTo(new[] { 1, 2, 3, 4 }));
    }

    // The in-memory store does not run the boss-clear-member cascade (the migration's FK does on MySQL): this shows
    // the read side, which joins the members to their characters.
    [Test]
    public void DeletedCharacter_NotRead_AndAClearWithNoMemberLeftIsSkipped() {
        long alpha = CreateCharacter("alpha", Job.Knight, accountId: 9);
        long beta = CreateCharacter("beta", Job.Wizard);
        long both = Clear(Board1, alpha, 300, 1_800_000_100, alpha, beta);
        Clear(Board1, alpha, 200, 1_800_000_200, alpha);
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.DeleteCharacter(9, alpha), Is.True);
        }

        List<ClearRow> rows = ReadClears(Board(GameRankingType.BossShortestClears, Board1), GameRankingType.BossShortestClears);
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].RowId, Is.EqualTo(both));
        // The leader is gone; the row keeps its id and the client shows the first member instead.
        Assert.That(rows[0].LeaderId, Is.EqualTo(alpha));
        Assert.That(rows[0].Members.ConvertAll(member => member.Id), Is.EqualTo(new[] { beta }));

        Assert.That(ReadCounts(Board(GameRankingType.BossClearCount, Board1), Board1), Is.EqualTo(new[] { (1, beta, "beta", 1) }));
        Assert.That(PersonalRank(Board1, alpha), Is.EqualTo(0));
    }
    #endregion

    #region Clear count board
    [Test]
    public void Counts_MostFirst_EqualByEarlierLastClear_ThenId() {
        long alpha = CreateCharacter("alpha", Job.Knight);
        long beta = CreateCharacter("beta", Job.Wizard);
        long gamma = CreateCharacter("gamma", Job.Priest);
        long delta = CreateCharacter("delta", Job.Archer);
        Clear(Board1, alpha, 300, 1_800_000_100, alpha, beta);
        Clear(Board1, alpha, 300, 1_800_000_200, alpha, gamma);
        Clear(Board1, beta, 300, 1_800_000_300, beta, delta);
        Clear(Board1, alpha, 300, 1_800_000_400, alpha);
        Clear(Board2, gamma, 300, 1_800_000_000, gamma, delta);

        // alpha 3; beta 2 (last 300); gamma and delta 1 each: gamma last 200, delta last 300.
        Assert.That(ReadCounts(Board(GameRankingType.BossClearCount, Board1), Board1), Is.EqualTo(new[] {
            (1, alpha, "alpha", 3),
            (2, beta, "beta", 2),
            (3, gamma, "gamma", 1),
            (4, delta, "delta", 1),
        }));
        Assert.That(PersonalRank(Board1, alpha), Is.EqualTo(1));
        Assert.That(PersonalRank(Board1, delta), Is.EqualTo(4));
        Assert.That(PersonalRank(Board2, delta), Is.EqualTo(2));
        Assert.That(PersonalRank(Board2, alpha), Is.EqualTo(0));
    }

    [Test]
    public void Counts_EqualLastClear_RankByCharacterId() {
        long alpha = CreateCharacter("alpha", Job.Knight);
        long beta = CreateCharacter("beta", Job.Wizard);
        Clear(Board1, alpha, 300, 1_800_000_100, beta, alpha);

        (long first, long second) = alpha < beta ? (alpha, beta) : (beta, alpha);
        Assert.That(ReadCounts(Board(GameRankingType.BossClearCount, Board1), Board1).ConvertAll(row => (row.Rank, row.Id)),
            Is.EqualTo(new[] { (1, first), (2, second) }));
        Assert.That(PersonalRank(Board1, second), Is.EqualTo(2));
    }

    [Test]
    public void Counts_Search_GivesThatRowOnly() {
        long alpha = CreateCharacter("alpha", Job.Knight);
        long beta = CreateCharacter("beta", Job.Wizard);
        CreateCharacter("gamma", Job.Priest);
        Clear(Board1, alpha, 300, 1_800_000_100, alpha);
        Clear(Board1, beta, 300, 1_800_000_200, beta);
        Clear(Board1, beta, 300, 1_800_000_300, beta);

        Assert.That(ReadCounts(Board(GameRankingType.BossClearCount, Board1, "alpha"), Board1), Is.EqualTo(new[] { (2, alpha, "alpha", 1) }));
        Assert.That(ReadCounts(Board(GameRankingType.BossClearCount, Board1, "gamma"), Board1), Is.Empty);
        Assert.That(ReadCounts(Board(GameRankingType.BossClearCount, Board1, "nobody"), Board1), Is.Empty);
    }
    #endregion

    #region Fame challenge
    private ByteWriter FameBoard(GameRankingType type, int rankingId, string userName = "") {
        using GameStorage.Request db = storage.Context();
        return BossRanking.FameChallengeBoard(db, type, rankingId, userName);
    }

    private int FamePersonalRank(GameRankingType type, int rankingId, long characterId) {
        using GameStorage.Request db = storage.Context();
        (int rank, ByteReader reader) = ReadHeader(BossRanking.FameChallengePersonal(db, type, rankingId, characterId), type);
        Assert.That(reader.Available, Is.EqualTo(0));
        return rank;
    }

    private record FameRow(int Rank, long RowId, long CharacterId, string Name, string Image, int Level, int JobCode, int RankingId,
        int Seconds, string Date);

    // GameRanking::BossRumbleRecord: one character per row.
    private static List<FameRow> ReadFameClears(ByteWriter writer, GameRankingType type) {
        (int count, ByteReader reader) = ReadHeader(writer, type);
        var rows = new List<FameRow>();
        for (int i = 0; i < count; i++) {
            rows.Add(new FameRow(reader.ReadInt(), reader.ReadLong(), reader.ReadLong(), reader.ReadUnicodeStringWithLength(),
                reader.ReadUnicodeStringWithLength(), reader.ReadInt(), reader.ReadInt(), reader.ReadInt(), reader.ReadInt(),
                reader.ReadUnicodeStringWithLength()));
        }
        Assert.That(reader.Available, Is.EqualTo(0));
        return rows;
    }

    [Test]
    public void FameFirstClears_OneRowPerCharacter_ItsFirstClear() {
        long alpha = CreateCharacter("alpha", Job.Knight, 60);
        long beta = CreateCharacter("beta", Job.Wizard, 55);
        long a1 = Clear(Board1, alpha, 400, 1_800_000_100, alpha);
        Clear(Board1, alpha, 200, 1_800_000_300, alpha);
        long b1 = Clear(Board1, beta, 300, 1_800_000_200, beta);
        Clear(Board2, beta, 100, 1_700_000_000, beta);

        List<FameRow> rows = ReadFameClears(FameBoard(GameRankingType.FameChallengeFirstClears, Board1), GameRankingType.FameChallengeFirstClears);
        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0], Is.EqualTo(new FameRow(1, a1, alpha, "alpha", "pic/alpha.png", 60, (int) JobCode.Knight, Board1, 400,
            DateTimeOffset.FromUnixTimeSeconds(1_800_000_100).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"))));
        Assert.That(rows[1].Rank, Is.EqualTo(2));
        Assert.That((rows[1].RowId, rows[1].CharacterId, rows[1].JobCode, rows[1].Seconds), Is.EqualTo((b1, beta, (int) JobCode.Wizard, 300)));
    }

    [Test]
    public void FameShortestClears_OneRowPerCharacter_ItsFastestClear_ThenTheEarlierOne() {
        long alpha = CreateCharacter("alpha", Job.Knight);
        long beta = CreateCharacter("beta", Job.Wizard);
        long gamma = CreateCharacter("gamma", Job.Archer);
        Clear(Board1, alpha, 400, 1_800_000_100, alpha);
        long a2 = Clear(Board1, alpha, 250, 1_800_000_400, alpha);
        long b1 = Clear(Board1, beta, 250, 1_800_000_300, beta);
        long g1 = Clear(Board1, gamma, 500, 1_800_000_000, gamma);

        List<FameRow> rows = ReadFameClears(FameBoard(GameRankingType.FameChallengeShortestClears, Board1), GameRankingType.FameChallengeShortestClears);
        Assert.That(rows.ConvertAll(row => (row.Rank, row.RowId, row.CharacterId, row.Seconds)), Is.EqualTo(new[] {
            (1, b1, beta, 250), (2, a2, alpha, 250), (3, g1, gamma, 500),
        }));

        Assert.That(FamePersonalRank(GameRankingType.FameChallengeShortestClearPersonal, Board1, beta), Is.EqualTo(1));
        Assert.That(FamePersonalRank(GameRankingType.FameChallengeShortestClearPersonal, Board1, gamma), Is.EqualTo(3));
        Assert.That(FamePersonalRank(GameRankingType.FameChallengeShortestClearPersonal, Board2, gamma), Is.EqualTo(0));
    }

    [Test]
    public void FameClearCount_TheCountRowsWithTheFameType_AndItsOwnRank() {
        long alpha = CreateCharacter("alpha", Job.Knight);
        long beta = CreateCharacter("beta", Job.Wizard);
        Clear(Board1, alpha, 400, 1_800_000_100, alpha);
        Clear(Board1, beta, 300, 1_800_000_200, beta);
        Clear(Board1, beta, 300, 1_800_000_300, beta);

        Assert.That(ReadCounts(FameBoard(GameRankingType.FameChallengeClearCount, Board1), Board1, GameRankingType.FameChallengeClearCount),
            Is.EqualTo(new[] { (1, beta, "beta", 2), (2, alpha, "alpha", 1) }));
        Assert.That(ReadCounts(FameBoard(GameRankingType.FameChallengeClearCount, Board1, "alpha"), Board1, GameRankingType.FameChallengeClearCount),
            Is.EqualTo(new[] { (2, alpha, "alpha", 1) }));
        Assert.That(FamePersonalRank(GameRankingType.FameChallengeClearCountPersonal, Board1, alpha), Is.EqualTo(2));
        Assert.That(FamePersonalRank(GameRankingType.FameChallengeClearCountPersonal, Board2, alpha), Is.EqualTo(0));
    }

    [Test]
    public void FameBoards_ADeletedCharacterHasNoRow() {
        long alpha = CreateCharacter("alpha", Job.Knight);
        long beta = CreateCharacter("beta", Job.Wizard);
        Clear(Board1, alpha, 400, 1_800_000_100, alpha);
        Clear(Board1, beta, 300, 1_800_000_200, beta);
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.DeleteCharacter(7, alpha), Is.True);
        }

        foreach (GameRankingType type in new[] { GameRankingType.FameChallengeFirstClears, GameRankingType.FameChallengeShortestClears }) {
            List<FameRow> rows = ReadFameClears(FameBoard(type, Board1), type);
            Assert.That(rows.ConvertAll(row => (row.Rank, row.CharacterId)), Is.EqualTo(new[] { (1, beta) }), type.ToString());
        }
        Assert.That(FamePersonalRank(GameRankingType.FameChallengeShortestClearPersonal, Board1, beta), Is.EqualTo(1));
    }
    #endregion
}
