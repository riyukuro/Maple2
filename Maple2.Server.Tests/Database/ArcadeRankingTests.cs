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

// The arcade boards of irrq.aspx (0x65-0x68) built from the best score per game and season (arcade-record), on an
// in-memory database. The answers are read the way the client's GameRanking::Arcade (GameRankingItem::Arcade::Read)
// and MyRank read them. Made-up ids.
public class ArcadeRankingTests {
    private const int Season = 205001;
    private const int OtherSeason = 205002;
    private const int Game = 2;
    private const int OtherGame = 3;
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
        var model = new Character { AccountId = accountId, Name = name, Job = job, Level = 50, Picture = $"pic/{name}.png", Mastery = new Mastery() };
        model.ReturnMaps.Push(2000062);
        Character? character = db.CreateCharacter(model);
        Assert.That(character, Is.Not.Null);
        return character!.Id;
    }

    private void Save(long characterId, int score, long time, int game = Game, int season = Season) {
        using GameStorage.Request db = storage.Context();
        Assert.That(db.SaveArcadeScore(characterId, game, season, score, time), Is.True);
    }

    private ByteWriter Board(GameRankingType type, string userName = "", int game = Game, int season = Season) {
        using GameStorage.Request db = storage.Context();
        return ArcadeRanking.Board(db, type, game, season, userName);
    }

    private int PersonalRank(GameRankingType type, long characterId, int game = Game, int season = Season) {
        using GameStorage.Request db = storage.Context();
        ByteWriter writer = ArcadeRanking.Personal(db, type, game, season, characterId);
        var reader = new ByteReader(writer.Buffer[..writer.Length], 0);
        Assert.That(reader.ReadInt(), Is.EqualTo((int) type));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadUnicodeStringWithLength(), Has.Length.EqualTo(19));
        int rank = reader.ReadInt();
        Assert.That(reader.Available, Is.EqualTo(0));
        return rank;
    }

    // GameRankingItem::Arcade::Read: rank, id, name, profile, m_38, m_44, m_3c, m_40, m_48 (the shown score), date.
    private static List<(int Rank, long Id, string Name, int Score)> ReadBoard(ByteWriter writer, GameRankingType type, int game = Game, int season = Season) {
        var reader = new ByteReader(writer.Buffer[..writer.Length], 0);
        Assert.That(reader.ReadInt(), Is.EqualTo((int) type));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadUnicodeStringWithLength(), Has.Length.EqualTo(19));
        int count = reader.ReadInt();
        var rows = new List<(int, long, string, int)>();
        for (int i = 0; i < count; i++) {
            int rank = reader.ReadInt();
            long id = reader.ReadLong();
            string name = reader.ReadUnicodeStringWithLength();
            Assert.That(reader.ReadUnicodeStringWithLength(), Is.EqualTo($"pic/{name}.png"));
            Assert.That(reader.ReadInt(), Is.EqualTo(game));
            Assert.That(reader.ReadInt(), Is.EqualTo(season));
            reader.ReadInt(); // job code
            Assert.That(reader.ReadInt(), Is.EqualTo(50));
            int score = reader.ReadInt();
            Assert.That(reader.ReadUnicodeStringWithLength(), Has.Length.EqualTo(19));
            rows.Add((rank, id, name, score));
        }
        Assert.That(reader.Available, Is.EqualTo(0));
        return rows;
    }

    [Test]
    public void Board_EmptyStore_HasNoRows() {
        CreateCharacter("alpha", Job.Knight);
        Assert.That(ReadBoard(Board(GameRankingType.Arcade), GameRankingType.Arcade), Is.Empty);
    }

    [Test]
    public void Board_HighestScoreFirst_ThenEarlier() {
        long alpha = CreateCharacter("alpha", Job.Knight);
        long beta = CreateCharacter("beta", Job.Wizard);
        long gamma = CreateCharacter("gamma", Job.Archer);
        Save(alpha, 500, 1_800_000_300);
        Save(beta, 900, 1_800_000_100);
        Save(gamma, 500, 1_800_000_200);

        Assert.That(ReadBoard(Board(GameRankingType.Arcade), GameRankingType.Arcade), Is.EqualTo(new[] {
            (1, beta, "beta", 900),
            (2, gamma, "gamma", 500),
            (3, alpha, "alpha", 500),
        }));
        Assert.That(PersonalRank(GameRankingType.ArcadePersonal, alpha), Is.EqualTo(3));
        Assert.That(PersonalRank(GameRankingType.ArcadePersonal, beta), Is.EqualTo(1));
    }

    [Test]
    public void Board_OneGameAndSeason() {
        long alpha = CreateCharacter("alpha", Job.Knight);
        long beta = CreateCharacter("beta", Job.Wizard);
        Save(alpha, 300, 1_800_000_300);
        Save(beta, 700, 1_800_000_100, game: OtherGame);
        Save(beta, 800, 1_800_000_200, season: OtherSeason);

        Assert.That(ReadBoard(Board(GameRankingType.Arcade), GameRankingType.Arcade),
            Is.EqualTo(new[] { (1, alpha, "alpha", 300) }));
        Assert.That(ReadBoard(Board(GameRankingType.Arcade, game: OtherGame), GameRankingType.Arcade, OtherGame),
            Is.EqualTo(new[] { (1, beta, "beta", 700) }));
        Assert.That(ReadBoard(Board(GameRankingType.ArcadePreviousSeason, season: OtherSeason), GameRankingType.ArcadePreviousSeason, season: OtherSeason),
            Is.EqualTo(new[] { (1, beta, "beta", 800) }));
        Assert.That(PersonalRank(GameRankingType.ArcadePersonalPreviousSeason, beta, season: OtherSeason), Is.EqualTo(1));
        Assert.That(PersonalRank(GameRankingType.ArcadePersonalPreviousSeason, alpha, season: OtherSeason), Is.EqualTo(0));
    }

    [Test]
    public void Board_Search_GivesThatRowOnly() {
        long alpha = CreateCharacter("alpha", Job.Knight);
        long beta = CreateCharacter("beta", Job.Wizard);
        Save(alpha, 300, 1_800_000_300);
        Save(beta, 500, 1_800_000_100);

        Assert.That(ReadBoard(Board(GameRankingType.Arcade, "alpha"), GameRankingType.Arcade),
            Is.EqualTo(new[] { (2, alpha, "alpha", 300) }));
        Assert.That(ReadBoard(Board(GameRankingType.Arcade, "nobody"), GameRankingType.Arcade), Is.Empty);
    }

    [Test]
    public void Board_NoSeasonOrGame_IsEmpty() {
        long alpha = CreateCharacter("alpha", Job.Knight);
        Save(alpha, 300, 1_800_000_300, season: 0);
        Save(alpha, 300, 1_800_000_300, game: 0);
        Assert.That(ReadBoard(Board(GameRankingType.Arcade, season: 0), GameRankingType.Arcade, season: 0), Is.Empty);
        Assert.That(ReadBoard(Board(GameRankingType.Arcade, game: 0), GameRankingType.Arcade, 0), Is.Empty);
        Assert.That(PersonalRank(GameRankingType.ArcadePersonal, alpha, season: 0), Is.EqualTo(0));
    }

    [Test]
    public void Board_DeletedCharacter_IsNotShown() {
        long alpha = CreateCharacter("alpha", Job.Knight, accountId: 9);
        long beta = CreateCharacter("beta", Job.Wizard);
        Save(alpha, 900, 1_800_000_300);
        Save(beta, 500, 1_800_000_100);
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.DeleteCharacter(9, alpha), Is.True);
        }

        Assert.That(ReadBoard(Board(GameRankingType.Arcade), GameRankingType.Arcade),
            Is.EqualTo(new[] { (1, beta, "beta", 500) }));
        Assert.That(PersonalRank(GameRankingType.ArcadePersonal, alpha), Is.EqualTo(0));
    }
}
