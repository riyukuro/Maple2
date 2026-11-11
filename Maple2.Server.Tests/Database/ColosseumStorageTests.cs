using System;
using System.Collections.Generic;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Web.Controllers;
using Maple2.Tools.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Database;

// The Colosseum best run per season (colosseum-record) and the irrq.aspx answers built from it, on an in-memory
// database. The answers are read the way the client's GameRanking::BossColosseum and MyRank read them. Made-up ids.
public class ColosseumStorageTests {
    private const int Season = 205001;
    private const int OtherSeason = 205002;
    private GameStorage storage = null!;

    [SetUp]
    public void SetUp() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        storage = new GameStorage(options, null!, null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);
    }

    private long CreateCharacter(string name, Job job, long accountId = 7) {
        using GameStorage.Request db = storage.Context();
        var model = new Character { AccountId = accountId, Name = name, Job = job, Picture = $"pic/{name}.png", Mastery = new Mastery() };
        model.ReturnMaps.Push(2000062);
        Character? character = db.CreateCharacter(model);
        Assert.That(character, Is.Not.Null);
        return character!.Id;
    }

    private bool Save(long characterId, int rounds, int seconds, long time, int season = Season) {
        using GameStorage.Request db = storage.Context();
        return db.SaveColosseumRun(characterId, season, rounds, seconds, time);
    }

    private ColosseumBest? Best(long characterId, int season = Season) {
        using GameStorage.Request db = storage.Context();
        return db.GetColosseumBest(characterId, season);
    }

    #region Store rule
    [Test]
    public void Replaces_FirstRun_MoreRounds_OrSameRoundsFaster() {
        Assert.That(ColosseumBest.Replaces(null, 1, 900), Is.True);
        var best = new ColosseumBest(5, 300, 1000);
        Assert.That(ColosseumBest.Replaces(best, 6, 900), Is.True);
        Assert.That(ColosseumBest.Replaces(best, 5, 299), Is.True);
        Assert.That(ColosseumBest.Replaces(best, 5, 300), Is.False);
        Assert.That(ColosseumBest.Replaces(best, 5, 301), Is.False);
        Assert.That(ColosseumBest.Replaces(best, 4, 10), Is.False);
    }

    [Test]
    public void Store_BetterReplaces_EqualAndWorseKeepTheOldRecord() {
        long id = CreateCharacter("alpha", Job.Knight);
        Assert.That(Save(id, 5, 300, 1_800_000_000), Is.True);
        Assert.That(Save(id, 5, 300, 1_800_000_500), Is.False);
        Assert.That(Save(id, 4, 100, 1_800_000_600), Is.False);
        Assert.That(Best(id), Is.EqualTo(new ColosseumBest(5, 300, 1_800_000_000)));

        Assert.That(Save(id, 5, 250, 1_800_001_000), Is.True);
        Assert.That(Best(id), Is.EqualTo(new ColosseumBest(5, 250, 1_800_001_000)));
    }

    [Test]
    public void Store_OneBestPerSeason() {
        long id = CreateCharacter("alpha", Job.Knight);
        Save(id, 8, 600, 1_800_000_000);
        Assert.That(Save(id, 2, 100, 1_800_700_000, OtherSeason), Is.True);
        Assert.That(Best(id), Is.EqualTo(new ColosseumBest(8, 600, 1_800_000_000)));
        Assert.That(Best(id, OtherSeason), Is.EqualTo(new ColosseumBest(2, 100, 1_800_700_000)));
    }

    [Test]
    public void Store_UnknownCharacter_IsNotSaved() {
        Assert.That(Save(424242, 3, 100, 1_800_000_000), Is.False);
        Assert.That(Best(424242), Is.Null);
    }
    #endregion

    #region Season
    private static readonly IReadOnlyDictionary<int, SeasonDataTable.Entry> Seasons = new Dictionary<int, SeasonDataTable.Entry> {
        [Season] = new(Season, new DateTime(2050, 1, 7), new DateTime(2050, 1, 14), []),
        [OtherSeason] = new(OtherSeason, new DateTime(2050, 1, 14), new DateTime(2050, 1, 21), []),
    };

    [Test]
    public void SeasonAt_StartInclusive_EndExclusive_NoneOutside() {
        Assert.That(ColosseumBest.SeasonAt(Seasons, new DateTime(2050, 1, 7)), Is.EqualTo(Season));
        Assert.That(ColosseumBest.SeasonAt(Seasons, new DateTime(2050, 1, 13, 23, 59, 59)), Is.EqualTo(Season));
        Assert.That(ColosseumBest.SeasonAt(Seasons, new DateTime(2050, 1, 14)), Is.EqualTo(OtherSeason));
        Assert.That(ColosseumBest.SeasonAt(Seasons, new DateTime(2050, 1, 6)), Is.EqualTo(0));
        Assert.That(ColosseumBest.SeasonAt(Seasons, new DateTime(2050, 1, 21)), Is.EqualTo(0));
    }

    [Test]
    public void RequestSeason_CurrentForTheCurrentTab_AskedForTheOthers() {
        var now = new DateTime(2050, 1, 15);
        Assert.That(ColosseumRanking.Season(GameRankingType.Colosseum, 0, Seasons, now), Is.EqualTo(OtherSeason));
        Assert.That(ColosseumRanking.Season(GameRankingType.ColosseumPersonal, 0, Seasons, now), Is.EqualTo(OtherSeason));
        Assert.That(ColosseumRanking.Season(GameRankingType.ColosseumPreviousSeason, Season, Seasons, now), Is.EqualTo(Season));
        Assert.That(ColosseumRanking.Season(GameRankingType.ColosseumPersonalPreviousSeason, Season, Seasons, now), Is.EqualTo(Season));
    }
    #endregion

    #region Web answers
    private ByteWriter Board(GameRankingType type, int jobCode, string userName = "", int season = Season) {
        using GameStorage.Request db = storage.Context();
        return ColosseumRanking.Board(db, type, season, jobCode, userName);
    }

    private int PersonalRank(GameRankingType type, int jobCode, long characterId, int season = Season) {
        using GameStorage.Request db = storage.Context();
        ByteWriter writer = ColosseumRanking.Personal(db, type, season, jobCode, characterId);
        var reader = new ByteReader(writer.Buffer[..writer.Length], 0);
        Assert.That(reader.ReadInt(), Is.EqualTo((int) type));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadUnicodeStringWithLength(), Has.Length.EqualTo(19));
        int rank = reader.ReadInt();
        Assert.That(reader.Available, Is.EqualTo(0));
        return rank;
    }

    private static List<(int Rank, long Id, string Name, int Rounds, int Seconds)> ReadBoard(ByteWriter writer, GameRankingType type) {
        var reader = new ByteReader(writer.Buffer[..writer.Length], 0);
        Assert.That(reader.ReadInt(), Is.EqualTo((int) type));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadUnicodeStringWithLength(), Has.Length.EqualTo(19));
        int count = reader.ReadInt();
        var rows = new List<(int, long, string, int, int)>();
        for (int i = 0; i < count; i++) {
            int rank = reader.ReadInt();
            long id = reader.ReadLong();
            string name = reader.ReadUnicodeStringWithLength();
            Assert.That(reader.ReadUnicodeStringWithLength(), Is.EqualTo($"pic/{name}.png"));
            Assert.That(reader.ReadInt(), Is.EqualTo(0));
            Assert.That(reader.ReadInt(), Is.EqualTo(0));
            Assert.That(reader.ReadLong(), Is.EqualTo(0));
            int rounds = reader.ReadInt();
            int seconds = reader.ReadInt();
            Assert.That(reader.ReadInt(), Is.EqualTo(0));
            Assert.That(reader.ReadUnicodeStringWithLength(), Has.Length.EqualTo(19));
            Assert.That(reader.ReadUnicodeStringWithLength(), Has.Length.EqualTo(19));
            rows.Add((rank, id, name, rounds, seconds));
        }
        Assert.That(reader.Available, Is.EqualTo(0));
        return rows;
    }

    [Test]
    public void Board_EmptyStore_HasNoRows() {
        CreateCharacter("alpha", Job.Knight);
        Assert.That(ReadBoard(Board(GameRankingType.Colosseum, 0), GameRankingType.Colosseum), Is.Empty);
    }

    [Test]
    public void Board_MostRoundsFirst_ThenFaster_ThenEarlier() {
        long alpha = CreateCharacter("alpha", Job.Knight);
        long beta = CreateCharacter("beta", Job.Wizard);
        long gamma = CreateCharacter("gamma", Job.KnightII);
        long delta = CreateCharacter("delta", Job.Archer);
        Save(alpha, 6, 400, 1_800_000_300);
        Save(beta, 9, 900, 1_800_000_100);
        Save(gamma, 6, 350, 1_800_000_400);
        Save(delta, 6, 400, 1_800_000_200);

        Assert.That(ReadBoard(Board(GameRankingType.Colosseum, 0), GameRankingType.Colosseum), Is.EqualTo(new[] {
            (1, beta, "beta", 9, 900),
            (2, gamma, "gamma", 6, 350),
            (3, delta, "delta", 6, 400),
            (4, alpha, "alpha", 6, 400),
        }));
        Assert.That(PersonalRank(GameRankingType.ColosseumPersonal, 0, alpha), Is.EqualTo(4));
        Assert.That(PersonalRank(GameRankingType.ColosseumPersonal, 0, delta), Is.EqualTo(3));
    }

    [Test]
    public void Board_SeasonAndJobTab() {
        long alpha = CreateCharacter("alpha", Job.Knight);
        long beta = CreateCharacter("beta", Job.Wizard);
        long gamma = CreateCharacter("gamma", Job.KnightII);
        Save(alpha, 3, 100, 1_800_000_300);
        Save(beta, 5, 100, 1_800_000_100);
        Save(gamma, 7, 100, 1_800_000_200, OtherSeason);

        Assert.That(ReadBoard(Board(GameRankingType.Colosseum, (int) JobCode.Knight), GameRankingType.Colosseum),
            Is.EqualTo(new[] { (1, alpha, "alpha", 3, 100) }));
        Assert.That(ReadBoard(Board(GameRankingType.ColosseumPreviousSeason, (int) JobCode.Knight, season: OtherSeason), GameRankingType.ColosseumPreviousSeason),
            Is.EqualTo(new[] { (1, gamma, "gamma", 7, 100) }));
        Assert.That(PersonalRank(GameRankingType.ColosseumPersonal, (int) JobCode.Knight, alpha), Is.EqualTo(1));
        Assert.That(PersonalRank(GameRankingType.ColosseumPersonal, 0, alpha), Is.EqualTo(2));
        Assert.That(PersonalRank(GameRankingType.ColosseumPersonalPreviousSeason, 0, alpha, OtherSeason), Is.EqualTo(0));
    }

    [Test]
    public void Board_Search_GivesThatRowOnly() {
        long alpha = CreateCharacter("alpha", Job.Knight);
        long beta = CreateCharacter("beta", Job.Wizard);
        Save(alpha, 3, 100, 1_800_000_300);
        Save(beta, 5, 100, 1_800_000_100);

        Assert.That(ReadBoard(Board(GameRankingType.Colosseum, 0, "alpha"), GameRankingType.Colosseum),
            Is.EqualTo(new[] { (2, alpha, "alpha", 3, 100) }));
        Assert.That(ReadBoard(Board(GameRankingType.Colosseum, 0, "nobody"), GameRankingType.Colosseum), Is.Empty);
    }

    [Test]
    public void Board_NoSeason_IsEmpty() {
        long alpha = CreateCharacter("alpha", Job.Knight);
        Save(alpha, 3, 100, 1_800_000_300, 0);
        Assert.That(ReadBoard(Board(GameRankingType.Colosseum, 0, season: 0), GameRankingType.Colosseum), Is.Empty);
        Assert.That(PersonalRank(GameRankingType.ColosseumPersonal, 0, alpha, 0), Is.EqualTo(0));
    }

    [Test]
    public void Board_DeletedCharacter_IsNotShown() {
        long alpha = CreateCharacter("alpha", Job.Knight, accountId: 9);
        long beta = CreateCharacter("beta", Job.Wizard);
        Save(alpha, 3, 100, 1_800_000_300);
        Save(beta, 5, 100, 1_800_000_100);
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.DeleteCharacter(9, alpha), Is.True);
        }

        Assert.That(ReadBoard(Board(GameRankingType.Colosseum, 0), GameRankingType.Colosseum),
            Is.EqualTo(new[] { (1, beta, "beta", 5, 100) }));
        Assert.That(PersonalRank(GameRankingType.ColosseumPersonal, 0, alpha), Is.EqualTo(0));
    }
    #endregion
}
