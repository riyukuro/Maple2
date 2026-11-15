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
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Database;

// The Maple Survival boards of irrq.aspx (solo 0xab-0xae, squad 0xbf-0xc2) built from the survival records, on an
// in-memory database. The answers are read the way the restored client reads them (GMS GameRankingItem::Survival::Read
// 0x72d580 for the rows, MyRankPercent for the own rank). Made-up ids.
public class SurvivalRankingTests {
    private const int Season = 1;
    private const int NextSeason = 2;
    private GameStorage storage = null!;

    [SetUp]
    public void SetUp() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        storage = new GameStorage(options, null!, null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);
    }

    private long CreateCharacter(string name) {
        using GameStorage.Request db = storage.Context();
        var model = new Character { AccountId = 7, Name = name, Job = Job.Knight, Picture = $"pic/{name}.png", Mastery = new Mastery() };
        model.ReturnMaps.Push(2000062);
        Character? character = db.CreateCharacter(model);
        Assert.That(character, Is.Not.Null);
        return character!.Id;
    }

    // The game's path (MapleSurvivalProgress.Result): load the season's record, change it, save.
    private void Play(long characterId, int recordSeason, int rating, int wins = 0, long lastGame = 1_900_000_000) {
        using GameStorage.Request db = storage.Context();
        SurvivalSeasonRecord record = db.GetSurvivalRecord(characterId, recordSeason);
        record.Rating = rating;
        record.Games++;
        record.Wins = wins;
        record.LastGame = lastGame;
        Assert.That(db.SaveSurvivalRecord(characterId, record), Is.True);
    }

    private static readonly int[] Grades = [1, 1000, 1500, 2000, 2500, 3000, 3500];

    private static readonly IReadOnlyDictionary<int, SeasonDataTable.Entry> Seasons = new Dictionary<int, SeasonDataTable.Entry> {
        [Season] = new(Season, new DateTime(2050, 1, 1), new DateTime(2050, 2, 1), Grades),
        [NextSeason] = new(NextSeason, new DateTime(2050, 2, 1, 0, 0, 1), new DateTime(2050, 3, 1), Grades),
    };

    // The open periods (the solo record seasons): the same ids and spans as Seasons.
    private static readonly MapleSurvivalTable Periods = PeriodTable(
        new MapleSurvivalTable.Period(Season, 0, new DateTime(2050, 1, 1), new DateTime(2050, 2, 1), 0),
        new MapleSurvivalTable.Period(NextSeason, 0, new DateTime(2050, 2, 1, 0, 0, 1), new DateTime(2050, 3, 1), 0));

    private static MapleSurvivalTable PeriodTable(params MapleSurvivalTable.Period[] periods) {
        var byId = new Dictionary<int, MapleSurvivalTable.Period>();
        foreach (MapleSurvivalTable.Period period in periods) {
            byId[period.Id] = period;
        }
        return new MapleSurvivalTable(byId, new Dictionary<int, long>(), new Dictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>>(),
            new Dictionary<int, IReadOnlyDictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>>>(),
            new Dictionary<int, IReadOnlyDictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>>>());
    }

    private ByteWriter Board(GameRankingType type, int season, string userName = "") {
        using GameStorage.Request db = storage.Context();
        return SurvivalRanking.Board(db, type, season, Seasons, userName);
    }

    private int PersonalRank(GameRankingType type, int season, long characterId) {
        using GameStorage.Request db = storage.Context();
        ByteWriter writer = SurvivalRanking.Personal(db, type, season, characterId);
        var reader = new ByteReader(writer.Buffer[..writer.Length], 0);
        Assert.That(reader.ReadInt(), Is.EqualTo((int) type));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadUnicodeStringWithLength(), Has.Length.EqualTo(19));
        Assert.That(reader.ReadInt(), Is.EqualTo(0)); // kind 0: the plain rank
        int rank = reader.ReadInt();
        Assert.That(reader.Available, Is.EqualTo(0));
        return rank;
    }

    // Read: rank, id, name, profile, grade, long, rating, int, wins, 2 ints, long, last game time.
    private static List<(int Rank, long Id, string Name, int Grade, int Rating, int Wins, string LastGame)> ReadBoard(ByteWriter writer, GameRankingType type) {
        var reader = new ByteReader(writer.Buffer[..writer.Length], 0);
        Assert.That(reader.ReadInt(), Is.EqualTo((int) type));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadUnicodeStringWithLength(), Has.Length.EqualTo(19));
        int count = reader.ReadInt();
        var rows = new List<(int, long, string, int, int, int, string)>();
        for (int i = 0; i < count; i++) {
            int rank = reader.ReadInt();
            long id = reader.ReadLong();
            string name = reader.ReadUnicodeStringWithLength();
            Assert.That(reader.ReadUnicodeStringWithLength(), Is.EqualTo($"pic/{name}.png"));
            int grade = reader.ReadInt();
            Assert.That(reader.ReadLong(), Is.EqualTo(0));
            int rating = reader.ReadInt();
            Assert.That(reader.ReadInt(), Is.EqualTo(0));
            int wins = reader.ReadInt();
            Assert.That(reader.ReadInt(), Is.EqualTo(0));
            Assert.That(reader.ReadInt(), Is.EqualTo(0));
            Assert.That(reader.ReadLong(), Is.EqualTo(0));
            string lastGame = reader.ReadUnicodeStringWithLength();
            rows.Add((rank, id, name, grade, rating, wins, lastGame));
        }
        Assert.That(reader.Available, Is.EqualTo(0));
        return rows;
    }

    private static string Time(long seconds) => DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    [Test]
    public void RequestSeason_RunningForTheCurrentTabs_AskedForTheOthers() {
        var now = new DateTime(2050, 2, 15);
        Assert.That(SurvivalRanking.Season(GameRankingType.Survival, Season, Seasons, Periods, now), Is.EqualTo(NextSeason));
        Assert.That(SurvivalRanking.Season(GameRankingType.SurvivalSquadPersonal, 0, Seasons, Periods, now), Is.EqualTo(NextSeason));
        Assert.That(SurvivalRanking.Season(GameRankingType.SurvivalPreviousSeason, Season, Seasons, Periods, now), Is.EqualTo(Season));
        Assert.That(SurvivalRanking.Season(GameRankingType.SurvivalSquadPersonalPreviousSeason, Season, Seasons, Periods, now), Is.EqualTo(Season));
        Assert.That(SurvivalRanking.Season(GameRankingType.SurvivalSquad, 0, Seasons, Periods, new DateTime(2049, 1, 1)), Is.EqualTo(0));
        Assert.That(SurvivalRanking.Season(GameRankingType.Survival, 0, Seasons, null, now), Is.EqualTo(0));
    }

    // The current solo tabs read the season the game writes the records in (SurvivalRules.CurrentPeriod over the open
    // periods, the lowest id holding the time), not SeasonData_MapleSurvival; the squad tabs read the squad season
    // table (SurvivalRules.SeasonAt), as MapleSurvivalProgress does.
    [Test]
    public void RequestSeason_SoloFollowsTheOpenPeriod_WhenTheTablesDiffer() {
        var now = new DateTime(2050, 2, 1);
        // Periods 5 and 6 both hold the time (touching spans): the game writes period 5. The season table holds 2 there.
        MapleSurvivalTable periods = PeriodTable(
            new MapleSurvivalTable.Period(6, 0, new DateTime(2050, 2, 1), new DateTime(2050, 3, 1), 0),
            new MapleSurvivalTable.Period(5, 0, new DateTime(2050, 1, 1), new DateTime(2050, 2, 1), 0));
        var seasons = new Dictionary<int, SeasonDataTable.Entry> {
            [Season] = new(Season, new DateTime(2050, 1, 1), new DateTime(2050, 2, 1), Grades),
            [NextSeason] = new(NextSeason, new DateTime(2050, 2, 1), new DateTime(2050, 3, 1), Grades),
        };
        Assert.That(SurvivalRules.CurrentPeriod(periods, now)!.Id, Is.EqualTo(5));
        Assert.That(SurvivalRanking.Season(GameRankingType.Survival, 0, seasons, periods, now), Is.EqualTo(5));
        Assert.That(SurvivalRanking.Season(GameRankingType.SurvivalPersonal, 0, seasons, periods, now), Is.EqualTo(5));
        Assert.That(SurvivalRanking.Season(GameRankingType.SurvivalSquad, 0, seasons, periods, now), Is.EqualTo(NextSeason));
        Assert.That(SurvivalRanking.Season(GameRankingType.SurvivalSquadPersonal, 0, seasons, periods, now), Is.EqualTo(NextSeason));
    }

    [Test]
    public void Seasons_SquadBoardsReadTheSquadTable() {
        var solo = new Dictionary<int, SeasonDataTable.Entry>();
        var squad = new Dictionary<int, SeasonDataTable.Entry>();
        var table = new SeasonDataTable(new Dictionary<int, SeasonDataTable.Entry>(), new Dictionary<int, SeasonDataTable.Entry>(),
            new Dictionary<int, SeasonDataTable.Entry>(), new Dictionary<int, SeasonDataTable.Entry>(), solo, squad,
            new Dictionary<int, SeasonDataTable.Entry>(), new Dictionary<int, SeasonDataTable.Entry>(), new Dictionary<int, SeasonDataTable.Entry>());
        Assert.That(SurvivalRanking.Seasons(GameRankingType.Survival, table), Is.SameAs(solo));
        Assert.That(SurvivalRanking.Seasons(GameRankingType.SurvivalPersonalPreviousSeason, table), Is.SameAs(solo));
        Assert.That(SurvivalRanking.Seasons(GameRankingType.SurvivalSquad, table), Is.SameAs(squad));
        Assert.That(SurvivalRanking.Seasons(GameRankingType.SurvivalSquadPersonal, table), Is.SameAs(squad));
    }

    [Test]
    public void Board_HighestRatingFirst_ThenMoreWins_WithGradeAndLastGame() {
        long alpha = CreateCharacter("alpha");
        long beta = CreateCharacter("beta");
        long gamma = CreateCharacter("gamma");
        Play(alpha, Season, 1100, wins: 1, lastGame: 1_900_000_100);
        Play(beta, Season, 1600, lastGame: 1_900_000_200);
        Play(gamma, Season, 1100, wins: 3, lastGame: 1_900_000_300);

        // Grades 1, 1000, 1500, ...: 1100 is past 1000 (grade 2), 1600 past 1500 (grade 3).
        Assert.That(ReadBoard(Board(GameRankingType.Survival, Season), GameRankingType.Survival), Is.EqualTo(new[] {
            (1, beta, "beta", 3, 1600, 0, Time(1_900_000_200)),
            (2, gamma, "gamma", 2, 1100, 3, Time(1_900_000_300)),
            (3, alpha, "alpha", 2, 1100, 1, Time(1_900_000_100)),
        }));
        Assert.That(PersonalRank(GameRankingType.SurvivalPersonal, Season, beta), Is.EqualTo(1));
        Assert.That(PersonalRank(GameRankingType.SurvivalPersonal, Season, alpha), Is.EqualTo(3));
    }

    [Test]
    public void SoloAndSquad_ReadTheirOwnRecords() {
        long alpha = CreateCharacter("alpha");
        long beta = CreateCharacter("beta");
        Play(alpha, Season, 1300);
        Play(beta, SurvivalRules.SquadSeasonOffset + Season, 1200);

        List<(int Rank, long Id, string Name, int Grade, int Rating, int Wins, string LastGame)> solo = ReadBoard(Board(GameRankingType.Survival, Season), GameRankingType.Survival);
        Assert.That(solo.ConvertAll(row => row.Id), Is.EqualTo(new[] { alpha }));
        List<(int Rank, long Id, string Name, int Grade, int Rating, int Wins, string LastGame)> squad = ReadBoard(Board(GameRankingType.SurvivalSquadPreviousSeason, Season), GameRankingType.SurvivalSquadPreviousSeason);
        Assert.That(squad.ConvertAll(row => row.Id), Is.EqualTo(new[] { beta }));

        Assert.That(PersonalRank(GameRankingType.SurvivalSquadPersonal, Season, alpha), Is.EqualTo(0));
        Assert.That(PersonalRank(GameRankingType.SurvivalSquadPersonal, Season, beta), Is.EqualTo(1));
        Assert.That(PersonalRank(GameRankingType.SurvivalPersonalPreviousSeason, Season, beta), Is.EqualTo(0));
    }

    [Test]
    public void Board_OtherSeasonAndNoSeason_HaveNoRows() {
        long alpha = CreateCharacter("alpha");
        Play(alpha, Season, 1300);
        Assert.That(ReadBoard(Board(GameRankingType.SurvivalPreviousSeason, NextSeason), GameRankingType.SurvivalPreviousSeason), Is.Empty);
        Assert.That(ReadBoard(Board(GameRankingType.Survival, 0), GameRankingType.Survival), Is.Empty);
        Assert.That(PersonalRank(GameRankingType.SurvivalPersonal, 0, alpha), Is.EqualTo(0));
    }

    [Test]
    public void Search_FindsTheCharacterRow_WithItsBoardRank() {
        long alpha = CreateCharacter("alpha");
        long beta = CreateCharacter("beta");
        Play(alpha, Season, 1300);
        Play(beta, Season, 1200);

        List<(int Rank, long Id, string Name, int Grade, int Rating, int Wins, string LastGame)> rows = ReadBoard(Board(GameRankingType.Survival, Season, "BETA"), GameRankingType.Survival);
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That((rows[0].Rank, rows[0].Id), Is.EqualTo((2, beta)));
        Assert.That(ReadBoard(Board(GameRankingType.Survival, Season, "nobody"), GameRankingType.Survival), Is.Empty);
    }

    [Test]
    public void Record_KeepsTheLastGameTime() {
        long alpha = CreateCharacter("alpha");
        Play(alpha, Season, 1300, lastGame: 1_900_000_500);
        using GameStorage.Request db = storage.Context();
        Assert.That(db.GetSurvivalRecord(alpha, Season).LastGame, Is.EqualTo(1_900_000_500));
    }
}
