using System;
using System.Collections.Generic;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Pvp;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Web.Controllers;
using Maple2.Tools.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Database;

// The rank duel boards of irrq.aspx (0x29-0x2c) built from the rank duel records and the kept standings of the seasons
// before (rank-duel-season), on an in-memory database. The answers are read the way the client's GameRanking::UserPvp
// (GameRankingItem::UserPvp::Read) and MyRank read them. Made-up ids.
public class RankDuelRankingTests {
    private const int Season = 205001;
    private const int NextSeason = 205002;
    private GameStorage storage = null!;

    private readonly RaceArchive race = new();

    [SetUp]
    public void SetUp() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(race)
            .Options;
        race.Options = options;
        race.CharacterId = 0;
        race.Saves = 0;
        storage = new GameStorage(options, null!, null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);
    }

    private long CreateCharacter(string name, long accountId = 7) {
        using GameStorage.Request db = storage.Context();
        var model = new Character { AccountId = accountId, Name = name, Job = Job.Knight, Picture = $"pic/{name}.png", Mastery = new Mastery() };
        model.ReturnMaps.Push(2000062);
        Character? character = db.CreateCharacter(model);
        Assert.That(character, Is.Not.Null);
        return character!.Id;
    }

    // The game's path: load, start the season (RankDuelStanding.Load), change the rating, save.
    private void Fight(long characterId, int season, int rating, int best = 0) {
        using GameStorage.Request db = storage.Context();
        RankDuelRecord record = db.GetRankDuelRecord(characterId);
        record.StartSeason(season);
        record.Rating = rating;
        record.BestRating = Math.Max(best, rating);
        Assert.That(db.SaveRankDuelRecord(characterId, record), Is.True);
    }

    private ByteWriter Board(GameRankingType type, int season, string userName = "") {
        using GameStorage.Request db = storage.Context();
        return RankDuelRanking.Board(db, type, season, userName);
    }

    private int PersonalRank(GameRankingType type, int season, long characterId) {
        using GameStorage.Request db = storage.Context();
        ByteWriter writer = RankDuelRanking.Personal(db, type, season, characterId);
        var reader = new ByteReader(writer.Buffer[..writer.Length], 0);
        Assert.That(reader.ReadInt(), Is.EqualTo((int) type));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadUnicodeStringWithLength(), Has.Length.EqualTo(19));
        int rank = reader.ReadInt();
        Assert.That(reader.Available, Is.EqualTo(0));
        return rank;
    }

    // GameRankingItem::UserPvp::Read: rank, id, name, profile, m_38 (the shown rating), m_3c.
    private static List<(int Rank, long Id, string Name, int Rating, int Best)> ReadBoard(ByteWriter writer, GameRankingType type) {
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
            int rating = reader.ReadInt();
            int best = reader.ReadInt();
            rows.Add((rank, id, name, rating, best));
        }
        Assert.That(reader.Available, Is.EqualTo(0));
        return rows;
    }

    private static readonly IReadOnlyDictionary<int, SeasonDataTable.Entry> Seasons = new Dictionary<int, SeasonDataTable.Entry> {
        [Season] = new(Season, new DateTime(2050, 1, 1), new DateTime(2050, 2, 1), []),
        [NextSeason] = new(NextSeason, new DateTime(2050, 2, 1, 0, 0, 1), new DateTime(2050, 3, 1), []),
    };

    [Test]
    public void RequestSeason_CurrentForTheCurrentTab_AskedForTheOthers() {
        var now = new DateTime(2050, 2, 15);
        Assert.That(RankDuelRanking.Season(GameRankingType.Pvp, 0, Seasons, now), Is.EqualTo(NextSeason));
        Assert.That(RankDuelRanking.Season(GameRankingType.PvpPersonal, 0, Seasons, now), Is.EqualTo(NextSeason));
        Assert.That(RankDuelRanking.Season(GameRankingType.PvpPreviousSeason, Season, Seasons, now), Is.EqualTo(Season));
        Assert.That(RankDuelRanking.Season(GameRankingType.PvpPersonalPreviousSeason, Season, Seasons, now), Is.EqualTo(Season));
        Assert.That(RankDuelRanking.Season(GameRankingType.Pvp, 0, Seasons, new DateTime(2049, 1, 1)), Is.EqualTo(0));
    }

    [Test]
    public void Board_EmptyStore_HasNoRows() {
        CreateCharacter("alpha");
        Assert.That(ReadBoard(Board(GameRankingType.Pvp, Season), GameRankingType.Pvp), Is.Empty);
    }

    [Test]
    public void Board_HighestRatingFirst_ThenHigherBest() {
        long alpha = CreateCharacter("alpha");
        long beta = CreateCharacter("beta");
        long gamma = CreateCharacter("gamma");
        Fight(alpha, Season, 1100, 1300);
        Fight(beta, Season, 1250);
        Fight(gamma, Season, 1100, 1150);

        Assert.That(ReadBoard(Board(GameRankingType.Pvp, Season), GameRankingType.Pvp), Is.EqualTo(new[] {
            (1, beta, "beta", 1250, 1250),
            (2, alpha, "alpha", 1100, 1300),
            (3, gamma, "gamma", 1100, 1150),
        }));
        Assert.That(PersonalRank(GameRankingType.PvpPersonal, Season, gamma), Is.EqualTo(3));
        Assert.That(PersonalRank(GameRankingType.PvpPersonal, Season, beta), Is.EqualTo(1));
    }

    [Test]
    public void PreviousSeason_KeepsTheClosedStanding_AfterTheRecordMovesOn() {
        long alpha = CreateCharacter("alpha");
        long beta = CreateCharacter("beta");
        Fight(alpha, Season, 1400);
        Fight(beta, Season, 1200);
        // Alpha fights in the next season: its record starts over, its closed standing is kept. Beta does not fight
        // again and stays in its closed season's record.
        Fight(alpha, NextSeason, 1050);

        Assert.That(ReadBoard(Board(GameRankingType.PvpPreviousSeason, Season), GameRankingType.PvpPreviousSeason), Is.EqualTo(new[] {
            (1, alpha, "alpha", 1400, 1400),
            (2, beta, "beta", 1200, 1200),
        }));
        Assert.That(ReadBoard(Board(GameRankingType.Pvp, NextSeason), GameRankingType.Pvp),
            Is.EqualTo(new[] { (1, alpha, "alpha", 1050, 1050) }));
        Assert.That(PersonalRank(GameRankingType.PvpPersonalPreviousSeason, Season, alpha), Is.EqualTo(1));
        Assert.That(PersonalRank(GameRankingType.PvpPersonalPreviousSeason, Season, beta), Is.EqualTo(2));
        Assert.That(PersonalRank(GameRankingType.PvpPersonal, NextSeason, beta), Is.EqualTo(0));

        // A second save in the next season keeps the first closed standing.
        Fight(alpha, NextSeason, 1500);
        Assert.That(PersonalRank(GameRankingType.PvpPersonalPreviousSeason, Season, alpha), Is.EqualTo(1));
        Assert.That(ReadBoard(Board(GameRankingType.PvpPreviousSeason, Season), GameRankingType.PvpPreviousSeason)[0],
            Is.EqualTo((1, alpha, "alpha", 1400, 1400)));
    }

    [Test]
    public void Board_BothSources_First200Merged_PersonalRankCountsBoth() {
        // 205 standings of one season, every second one kept after its record moved on: the board is the first 200 of
        // both tables in one order, and a personal rank counts the rows before it in both.
        var ids = new List<long>();
        for (int i = 0; i < 205; i++) {
            long id = CreateCharacter($"c{i:D3}");
            Fight(id, Season, 1000 + i);
            if (i % 2 == 1) {
                Fight(id, NextSeason, 1000);
            }
            ids.Add(id);
        }

        var board = ReadBoard(Board(GameRankingType.PvpPreviousSeason, Season), GameRankingType.PvpPreviousSeason);
        Assert.That(board, Has.Count.EqualTo(200));
        for (int rank = 1; rank <= 200; rank++) {
            int i = 205 - rank;
            Assert.That(board[rank - 1], Is.EqualTo((rank, ids[i], $"c{i:D3}", 1000 + i, 1000 + i)));
        }
        Assert.That(PersonalRank(GameRankingType.PvpPersonalPreviousSeason, Season, ids[0]), Is.EqualTo(205));
        Assert.That(PersonalRank(GameRankingType.PvpPersonalPreviousSeason, Season, ids[1]), Is.EqualTo(204));
        Assert.That(PersonalRank(GameRankingType.PvpPersonalPreviousSeason, Season, ids[204]), Is.EqualTo(1));
    }

    [Test]
    public void Save_ArchiveRaced_SecondTrySeesTheSeasonRow() {
        long alpha = CreateCharacter("alpha");
        Fight(alpha, Season, 1400);
        // Another save writes alpha's closed-season row after this save checked for it: the first try fails on the
        // row's key, the second stages again, skips the row and saves the new season.
        race.CharacterId = alpha;
        race.Season = Season;
        race.Saves = 0;
        Fight(alpha, NextSeason, 1050);

        Assert.That(race.Saves, Is.EqualTo(2));
        Assert.That(ReadBoard(Board(GameRankingType.PvpPreviousSeason, Season), GameRankingType.PvpPreviousSeason),
            Is.EqualTo(new[] { (1, alpha, "alpha", 1300, 1400) }));
        Assert.That(ReadBoard(Board(GameRankingType.Pvp, NextSeason), GameRankingType.Pvp),
            Is.EqualTo(new[] { (1, alpha, "alpha", 1050, 1050) }));
    }

    // On the first save after it is armed, writes the closed-season row of CharacterId through another context (the
    // other save of the race), with a standing of its own to tell it from this save's.
    private sealed class RaceArchive : SaveChangesInterceptor {
        public DbContextOptions Options = null!;
        public long CharacterId;
        public int Season;
        public int Saves;
        private DbContext? racing;

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) {
            if (CharacterId == 0 || eventData.Context is not { } context || context == racing) {
                return result;
            }
            Saves++;
            if (Saves == 1) {
                using var other = new Maple2.Database.Context.Ms2Context(Options);
                racing = other;
                other.RankDuelSeasonRecord.Add(new Maple2.Database.Model.RankDuelSeasonRecord {
                    CharacterId = CharacterId,
                    Season = Season,
                    Rating = 1300,
                    BestRating = 1400,
                });
                other.SaveChanges();
            }
            return result;
        }
    }

    [Test]
    public void Board_Search_GivesThatRowOnly() {
        long alpha = CreateCharacter("alpha");
        long beta = CreateCharacter("beta");
        Fight(alpha, Season, 1100);
        Fight(beta, Season, 1200);

        Assert.That(ReadBoard(Board(GameRankingType.Pvp, Season, "alpha"), GameRankingType.Pvp),
            Is.EqualTo(new[] { (2, alpha, "alpha", 1100, 1100) }));
        Assert.That(ReadBoard(Board(GameRankingType.Pvp, Season, "nobody"), GameRankingType.Pvp), Is.Empty);
    }

    [Test]
    public void Board_NoSeason_IsEmpty() {
        long alpha = CreateCharacter("alpha");
        Fight(alpha, Season, 1100);
        Assert.That(ReadBoard(Board(GameRankingType.Pvp, 0), GameRankingType.Pvp), Is.Empty);
        Assert.That(PersonalRank(GameRankingType.PvpPersonal, 0, alpha), Is.EqualTo(0));
    }

    [Test]
    public void Board_DeletedCharacter_IsNotShown() {
        long alpha = CreateCharacter("alpha", accountId: 9);
        long beta = CreateCharacter("beta");
        Fight(alpha, Season, 1400);
        Fight(alpha, NextSeason, 1000);
        Fight(beta, Season, 1100);
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.DeleteCharacter(9, alpha), Is.True);
        }

        Assert.That(ReadBoard(Board(GameRankingType.PvpPreviousSeason, Season), GameRankingType.PvpPreviousSeason),
            Is.EqualTo(new[] { (1, beta, "beta", 1100, 1100) }));
        Assert.That(PersonalRank(GameRankingType.PvpPersonal, NextSeason, alpha), Is.EqualTo(0));
    }
}
