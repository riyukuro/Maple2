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
}
