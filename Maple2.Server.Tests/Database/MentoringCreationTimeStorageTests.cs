using System;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Server.Core.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Database;

// A mentoring row's CreationTime is a datetime column, which the server keeps in its local time like every other
// (ToEpochSeconds reads it so). AddMentoringRequest wrote the UTC clock into it. Nothing reads it yet, so the test
// reads the row. The in-memory store keeps the kind, so the test needs MS2_TEST_GAME_DB, and a host off UTC.
public class MentoringCreationTimeStorageTests {
    private static readonly string? Connection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");

    private DbContextOptions options = null!;
    private GameStorage storage = null!;
    private long accountId;
    private long mentorId;
    private long menteeId;

    [SetUp]
    public void SetUp() {
        if (Connection == null) {
            Assert.Ignore("the stored kind is MySQL's: set MS2_TEST_GAME_DB");
        }
        Assume.That(TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow), Is.Not.EqualTo(TimeSpan.Zero), "the host is on UTC");
        options = GameDbModule.Options(Connection);
        storage = new GameStorage(options, null!, null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);
        string name = "mt" + Guid.NewGuid().ToString("N")[..8];
        using GameStorage.Request db = storage.Context();
        accountId = db.CreateAccount(new Account { Username = name }, "pw").Id;
        mentorId = Create(db, name + "a");
        menteeId = Create(db, name + "b");
    }

    private long Create(GameStorage.Request db, string name) {
        var character = new Character { AccountId = accountId, Name = name, Mastery = new Mastery() };
        character.ReturnMaps.Push(2000062);
        return db.CreateCharacter(character)!.Id;
    }

    [TearDown]
    public void TearDown() {
        if (Connection == null || accountId == 0) {
            return;
        }
        using var context = new Ms2Context(options);
        context.Database.ExecuteSqlRaw("DELETE FROM `mentoring` WHERE `MentorId` = {0}", mentorId);
        context.Database.ExecuteSqlRaw("DELETE FROM `character` WHERE `AccountId` = {0}", accountId);
        context.Database.ExecuteSqlRaw("DELETE FROM `ugcmap` WHERE `OwnerId` = {0}", accountId);
        context.Database.ExecuteSqlRaw("DELETE FROM `home` WHERE `AccountId` = {0}", accountId);
        context.Database.ExecuteSqlRaw("DELETE FROM `account` WHERE `Id` = {0}", accountId);
    }

    [Test]
    public void MentoringRequest_IsStoredWithTheLocalTime() {
        long before = DateTime.Now.ToEpochSeconds();
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.AddMentoringRequest(mentorId, menteeId, 1, 1, 1, out MentoringEntry? entry), Is.Null);
            Assert.That(entry, Is.Not.Null);
        }
        long after = DateTime.Now.ToEpochSeconds();

        using var context = new Ms2Context(options);
        DateTime stored = context.Mentoring.AsNoTracking().Single(row => row.MentorId == mentorId).CreationTime;
        Assert.That(stored.ToEpochSeconds(), Is.InRange(before - 2, after + 2));
    }
}
