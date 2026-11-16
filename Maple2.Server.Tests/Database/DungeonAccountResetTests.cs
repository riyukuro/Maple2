using System;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Game.Dungeon;
using Maple2.Server.Tests.Game;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Database;

// The account reward rooms' counts reset with the characters' ones: the daily reset clears the day's timed clears,
// the weekly reset the week's clears whose period is over. The resets are MySQL SQL: the test needs MS2_TEST_GAME_DB,
// a scratch MySQL game database with the AddDungeonAccountRecord migration.
public class DungeonAccountResetTests {
    private static readonly string? Connection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");
    private const int Room = 29006001;

    private DbContextOptions options = null!;
    private TestGameSession test = null!;

    [SetUp]
    public void SetUp() {
        if (Connection == null) {
            Assert.Ignore("the reset is MySQL SQL: set MS2_TEST_GAME_DB");
        }
        options = new DbContextOptionsBuilder().UseMySql(Connection, ServerVersion.AutoDetect(Connection)).Options;
        test = new TestGameSession(options);
        test.Start();
    }

    private (byte Clears, byte SubClears) Stored() {
        using var context = new Ms2Context(options);
        Maple2.Database.Model.DungeonAccountRecord row = context.DungeonAccountRecord.AsNoTracking()
            .Single(record => record.AccountId == test.AccountId && record.DungeonId == Room);
        return (row.CurrentClears, row.CurrentSubClears);
    }

    [Test]
    public void ResetsClearTheAccountCounts() {
        long ended = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 60;
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.CreateDungeonAccountRecord(test.AccountId, new DungeonRecord(Room) {
                UnionClears = 4, UnionSubClears = 2, UnionCooldownTimestamp = ended, UnionSubCooldownTimestamp = ended,
            }), Is.True);
        }
        Assume.That(Stored(), Is.EqualTo(((byte) 4, (byte) 2)));

        using (GameStorage.Request db = test.Storage.Context()) {
            db.DailyReset();
        }
        Assert.That(Stored(), Is.EqualTo(((byte) 4, (byte) 0)));

        using (GameStorage.Request db = test.Storage.Context()) {
            db.WeeklyReset();
        }
        Assert.That(Stored(), Is.EqualTo(((byte) 0, (byte) 0)));
    }

    private long StoredCheckIn() {
        using var context = new Ms2Context(options);
        return context.Home.AsNoTracking().Single(home => home.AccountId == test.AccountId).DecorationRewardTimestamp;
    }

    private void CheckIn(long time) {
        using var context = new Ms2Context(options);
        context.Home.Single(home => home.AccountId == test.AccountId).DecorationRewardTimestamp = time;
        context.SaveChanges();
    }

    // The daily reset leaves what was claimed since midnight: a record whose day is not over (a clear since
    // midnight set its next midnight) keeps its daily clears, a check-in since midnight its time. Those from before
    // midnight are cleared.
    [TestCase(true)]
    [TestCase(false)]
    public void TheDailyReset_KeepsTheNewDaysClaims(bool sinceMidnight) {
        long midnight = new DateTimeOffset(DateTime.Today).ToUnixTimeSeconds();
        long nextReset = sinceMidnight ? midnight + 86400 : midnight;
        long checkIn = sinceMidnight ? midnight + 1 : midnight - 1;
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.CreateDungeonAccountRecord(test.AccountId, new DungeonRecord(Room) {
                UnionClears = 4, UnionSubClears = 2, UnionCooldownTimestamp = midnight + 7 * 86400, UnionSubCooldownTimestamp = nextReset,
            }), Is.True);
            Assert.That(db.CreateDungeonRecord(new DungeonRecord(Room + 1) {
                UnionClears = 1, UnionSubClears = 1, UnionCooldownTimestamp = midnight + 7 * 86400, UnionSubCooldownTimestamp = nextReset,
            }, test.CharacterId), Is.Not.Null);
        }
        CheckIn(checkIn);

        using (GameStorage.Request db = test.Storage.Context()) {
            db.DailyReset();
        }

        Assert.Multiple(() => {
            Assert.That(Stored(), Is.EqualTo(((byte) 4, (byte) (sinceMidnight ? 2 : 0))));
            Assert.That(StoredCheckIn(), Is.EqualTo(sinceMidnight ? checkIn : 0));
            using var context = new Ms2Context(options);
            Assert.That(context.DungeonRecord.AsNoTracking().Single(row => row.OwnerId == test.CharacterId && row.DungeonId == Room + 1).CurrentSubClears,
                Is.EqualTo((byte) (sinceMidnight ? 1 : 0)), "the character's record");
        });
    }
}
