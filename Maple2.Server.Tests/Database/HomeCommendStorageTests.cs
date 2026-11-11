using System;
using System.Collections.Generic;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Tools.Extensions;
using Maple2.Server.Web.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Database;

// The house commendation (RequestCube 0x19), its once-a-day rule, the home's scores and the irrq.aspx board 6 built
// from home-commend, on an in-memory database. The answers are read the way the client's
// GameRanking::UgcMapCommendation and MyRank read them. Made-up ids and names.
public class HomeCommendStorageTests {
    private const int Month = 205003;
    private const int OtherMonth = 205004;
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.Local;
    private DbContextOptions options = null!;
    private GameStorage storage = null!;

    [SetUp]
    public void SetUp() {
        options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        storage = new GameStorage(options, null!, null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);
    }

    private static long At(int yearMonth, int day, int hour = 12) {
        (long begin, long _) = HomeCommendRules.MonthBounds(yearMonth, Zone)!.Value;
        return begin + (day - 1) * 86400L + hour * 3600L;
    }

    // An account with its home (CreateAccount adds the indoor plot) and a character.
    private long CreateOwner(string name, string? homeName = null) {
        long accountId;
        using (GameStorage.Request db = storage.Context()) {
            accountId = db.CreateAccount(new Account { Username = name }, "pw").Id;
            var character = new Character { AccountId = accountId, Name = name, Job = Job.Knight, Picture = $"pic/{name}.png", Mastery = new Mastery() };
            character.ReturnMaps.Push(2000062);
            Assert.That(db.CreateCharacter(character), Is.Not.Null);
        }
        if (homeName != null) {
            using var context = new Ms2Context(options);
            foreach (Maple2.Database.Model.UgcMap map in context.UgcMap) {
                if (map.OwnerId == accountId) {
                    map.Name = homeName;
                }
            }
            context.SaveChanges();
        }
        return accountId;
    }

    private bool Commend(long ownerId, long accountId, long time) {
        using GameStorage.Request db = storage.Context();
        return db.AddHomeCommend(ownerId, accountId, time);
    }

    private (long Begin, long End) Span(int yearMonth) => HomeCommendRules.MonthBounds(yearMonth, Zone)!.Value;

    #region Rules
    [Test]
    public void MayCommend_FirstTime_NextDay_OrAfterADay() {
        TimeZoneInfo utc = TimeZoneInfo.Utc;
        long noon = new DateTimeOffset(2050, 3, 10, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        Assert.That(HomeCommendRules.MayCommend(0, noon, utc), Is.True);
        Assert.That(HomeCommendRules.MayCommend(noon, noon + 3600, utc), Is.False);
        Assert.That(HomeCommendRules.MayCommend(noon, noon + 11 * 3600 + 3599, utc), Is.False);
        Assert.That(HomeCommendRules.MayCommend(noon, noon + 12 * 3600, utc), Is.True); // the next calendar day
        Assert.That(HomeCommendRules.MayCommend(noon, noon + 86401, utc), Is.True);
        Assert.That(HomeCommendRules.MayCommend(noon, noon, utc), Is.False);
        Assert.That(HomeCommendRules.MayCommend(noon, noon - 10, utc), Is.False);
    }

    [Test]
    public void Check_NoOwner_OwnHome_Duplicate_InThatOrder() {
        TimeZoneInfo utc = TimeZoneInfo.Utc;
        long now = new DateTimeOffset(2050, 3, 10, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        Assert.That(HomeCommendRules.Check(0, 5, 0, now, utc), Is.EqualTo(UgcMapError.s_ugcmap_no_owner_to_commend));
        Assert.That(HomeCommendRules.Check(5, 5, now - 60, now, utc), Is.EqualTo(UgcMapError.s_ugcmap_cant_commend_myself));
        Assert.That(HomeCommendRules.Check(6, 5, now - 60, now, utc), Is.EqualTo(UgcMapError.s_ugcmap_cant_commend_duplicate));
        Assert.That(HomeCommendRules.Check(6, 5, now - 86400 * 2, now, utc), Is.Null);
        Assert.That(HomeCommendRules.Check(6, 5, 0, now, utc), Is.Null);
    }

    [Test]
    public void Month_OfATime_AndItsBounds() {
        TimeZoneInfo utc = TimeZoneInfo.Utc;
        long time = new DateTimeOffset(2050, 12, 31, 23, 59, 59, TimeSpan.Zero).ToUnixTimeSeconds();
        Assert.That(HomeCommendRules.MonthOf(time, utc), Is.EqualTo(205012));
        Assert.That(HomeCommendRules.MonthBounds(205012, utc), Is.EqualTo(((long Begin, long End)?) (time - 31 * 86400 + 1, time + 1)));
        Assert.That(HomeCommendRules.MonthSpan(time, utc), Is.EqualTo((time - 31 * 86400 + 1, time + 1)));
        Assert.That(HomeCommendRules.MonthBounds(0, utc), Is.Null);
        Assert.That(HomeCommendRules.MonthBounds(205000, utc), Is.Null);
        Assert.That(HomeCommendRules.MonthBounds(205013, utc), Is.Null);
    }
    #endregion

    #region Store
    [Test]
    public void Commend_IsKept_AndIsTheLastCommendTime() {
        long owner = CreateOwner("alpha");
        long voter = CreateOwner("beta");
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.GetLastHomeCommend(voter), Is.EqualTo(0));
        }
        Assert.That(Commend(owner, voter, At(Month, 3)), Is.True);
        Assert.That(Commend(owner, voter, At(Month, 5)), Is.True);
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.GetLastHomeCommend(voter), Is.EqualTo(At(Month, 5)));
            Assert.That(db.GetLastHomeCommend(owner), Is.EqualTo(0));
        }
    }

    [Test]
    public void Commend_OfNoHome_OrByNoAccount_IsRefused() {
        long owner = CreateOwner("alpha");
        Assert.That(Commend(999_001, owner, At(Month, 3)), Is.False);
        Assert.That(Commend(owner, 999_002, At(Month, 3)), Is.False);
    }

    [Test]
    public void Score_IsThisMonth_AndAllTime() {
        long owner = CreateOwner("alpha");
        long beta = CreateOwner("beta");
        long gamma = CreateOwner("gamma");
        Commend(owner, beta, At(Month, 2));
        Commend(owner, gamma, At(Month, 28));
        Commend(owner, beta, At(OtherMonth, 1, 0));
        using GameStorage.Request db = storage.Context();
        (long begin, long end) = Span(Month);
        Assert.That(db.GetHomeCommendScore(owner, begin, end), Is.EqualTo((2, 3)));
        (begin, end) = Span(OtherMonth);
        Assert.That(db.GetHomeCommendScore(owner, begin, end), Is.EqualTo((1, 3)));
        Assert.That(db.GetHomeCommendScore(beta, begin, end), Is.EqualTo((0, 0)));
    }
    #endregion

    #region Board
    private ByteWriter Board(GameRankingType type, int seasonId, string userName = "") {
        using GameStorage.Request db = storage.Context();
        return HomeCommendRanking.Board(db, type, HomeCommendRanking.Month(type, seasonId, At(Month, 20), Zone), userName);
    }

    private int MyRank(GameRankingType type, int seasonId, long accountId) {
        using GameStorage.Request db = storage.Context();
        ByteWriter writer = HomeCommendRanking.Personal(db, type, HomeCommendRanking.Month(type, seasonId, At(Month, 20), Zone), accountId);
        var reader = new ByteReader(writer.Buffer[..writer.Length], 0);
        Assert.That(reader.ReadInt(), Is.EqualTo((int) type));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadUnicodeStringWithLength(), Has.Length.EqualTo(19));
        int rank = reader.ReadInt();
        Assert.That(reader.Available, Is.EqualTo(0));
        return rank;
    }

    private static List<(int Rank, string Name, int Commends, long AccountId, string HomeName)> ReadBoard(ByteWriter writer, GameRankingType type) {
        var reader = new ByteReader(writer.Buffer[..writer.Length], 0);
        Assert.That(reader.ReadInt(), Is.EqualTo((int) type));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadUnicodeStringWithLength(), Has.Length.EqualTo(19));
        int count = reader.ReadInt();
        var rows = new List<(int, string, int, long, string)>();
        for (int i = 0; i < count; i++) {
            int rank = reader.ReadInt();
            Assert.That(reader.ReadLong(), Is.Not.EqualTo(0)); // the owner's character
            string name = reader.ReadUnicodeStringWithLength();
            Assert.That(reader.ReadUnicodeStringWithLength(), Is.EqualTo($"pic/{name}.png"));
            int commends = reader.ReadInt();
            Assert.That(reader.ReadInt(), Is.EqualTo(0));
            long accountId = reader.ReadLong();
            string homeName = reader.ReadUnicodeStringWithLength();
            rows.Add((rank, name, commends, accountId, homeName));
        }
        Assert.That(reader.Available, Is.EqualTo(0));
        return rows;
    }

    [Test]
    public void Board_MostCommendsFirst_TieByWhoReachedItFirst() {
        long alpha = CreateOwner("alpha", "alpha house");
        long beta = CreateOwner("beta", "beta house");
        long gamma = CreateOwner("gamma");
        long delta = CreateOwner("delta");
        Commend(alpha, delta, At(Month, 5));
        Commend(beta, delta, At(Month, 4));
        Commend(beta, gamma, At(Month, 6));
        Commend(gamma, delta, At(Month, 3));
        Commend(gamma, alpha, At(OtherMonth, 3)); // another month

        List<(int Rank, string Name, int Commends, long AccountId, string HomeName)> rows = ReadBoard(Board(GameRankingType.Ugc, 0), GameRankingType.Ugc);
        Assert.That(rows, Is.EqualTo(new List<(int, string, int, long, string)> {
            (1, "beta", 2, beta, "beta house"),
            (2, "gamma", 1, gamma, string.Empty),
            (3, "alpha", 1, alpha, "alpha house"),
        }));

        Assert.That(MyRank(GameRankingType.UgcPersonal, 0, alpha), Is.EqualTo(3));
        Assert.That(MyRank(GameRankingType.UgcPersonal, 0, delta), Is.EqualTo(0));
    }

    [Test]
    public void Board_OfAnEarlierMonth_AsksByItsDate() {
        long alpha = CreateOwner("alpha");
        long beta = CreateOwner("beta");
        Commend(alpha, beta, At(Month, 5));
        Commend(beta, alpha, At(OtherMonth, 5));

        // the current month is the board's month here (At(Month, 20)), the date of the earlier tabs picks the month
        Assert.That(ReadBoard(Board(GameRankingType.UgcPreviousSeason, OtherMonth), GameRankingType.UgcPreviousSeason),
            Is.EqualTo(new List<(int, string, int, long, string)> { (1, "beta", 1, beta, string.Empty) }));
        Assert.That(MyRank(GameRankingType.UgcPersonalPreviousSeason, OtherMonth, beta), Is.EqualTo(1));
        Assert.That(MyRank(GameRankingType.UgcPersonalPreviousSeason, OtherMonth, alpha), Is.EqualTo(0));
        Assert.That(ReadBoard(Board(GameRankingType.UgcPreviousSeason, 205099), GameRankingType.UgcPreviousSeason), Is.Empty);
        Assert.That(MyRank(GameRankingType.UgcPersonalPreviousSeason, 0, alpha), Is.EqualTo(0));
    }

    [Test]
    public void Board_SearchByName_GivesThatHomesRow() {
        long alpha = CreateOwner("alpha");
        long beta = CreateOwner("beta");
        Commend(alpha, beta, At(Month, 5));
        Commend(beta, alpha, At(Month, 6));
        Commend(beta, CreateOwner("gamma"), At(Month, 7));

        Assert.That(ReadBoard(Board(GameRankingType.Ugc, 0, "ALPHA"), GameRankingType.Ugc),
            Is.EqualTo(new List<(int, string, int, long, string)> { (2, "alpha", 1, alpha, string.Empty) }));
        Assert.That(ReadBoard(Board(GameRankingType.Ugc, 0, "nobody"), GameRankingType.Ugc), Is.Empty);
    }

    [Test]
    public void Board_OutdoorPlotNameBeforeTheIndoorOne() {
        long alpha = CreateOwner("alpha", "inside");
        long beta = CreateOwner("beta");
        using (var context = new Ms2Context(options)) {
            context.UgcMap.Add(new Maple2.Database.Model.UgcMap { OwnerId = alpha, MapId = 2000100, Number = 3, Indoor = false, Name = "garden" });
            context.SaveChanges();
        }
        Commend(alpha, beta, At(Month, 5));
        Assert.That(ReadBoard(Board(GameRankingType.Ugc, 0), GameRankingType.Ugc)[0].HomeName, Is.EqualTo("garden"));
    }
    #endregion

    [Test]
    public void LoadHome_CarriesTheLastCommendTime() {
        ByteWriter writer = Maple2.Server.Game.Packets.HomeCommandPacket.LoadHome(77, 1_800_000_000);
        var reader = new ByteReader(writer.Buffer[..writer.Length], 0);
        reader.ReadShort(); // op
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadLong(), Is.EqualTo(77));
        Assert.That(reader.ReadLong(), Is.EqualTo(1_800_000_000));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}
