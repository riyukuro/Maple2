using System;
using System.Collections.Generic;
using Maple2.Database.Context;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Core.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Club = Maple2.Model.Game.Club.Club;

namespace Maple2.Server.Tests.Database;

// A club's creation time (Club.WriteTo, the club window's date) is a datetime column read back as the server's local
// time (ToEpochSeconds), like every other datetime column. CreateClub wrote the UTC clock into it, so a club loaded
// from MySQL was off by the server's UTC offset, and its members' join times (filled by the database in local time)
// were that much apart from it. The in-memory store keeps the kind, so the tests need MS2_TEST_GAME_DB, and a host off
// UTC.
public class ClubCreationTimeStorageTests {
    private static readonly string? Connection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");

    private sealed class Players(Dictionary<long, PlayerInfo> infos) : IPlayerInfoProvider {
        public PlayerInfo? GetPlayerInfo(long id) => infos.GetValueOrDefault(id);
    }

    private DbContextOptions options = null!;
    private GameStorage storage = null!;
    private long accountId;
    private long characterId;
    private long clubId;

    [SetUp]
    public void SetUp() {
        if (Connection == null) {
            Assert.Ignore("the stored kind is MySQL's: set MS2_TEST_GAME_DB");
        }
        Assume.That(TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow), Is.Not.EqualTo(TimeSpan.Zero), "the host is on UTC");
        options = GameDbModule.Options(Connection);
        storage = new GameStorage(options, null!, null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);
        string name = "club" + Guid.NewGuid().ToString("N")[..8];
        using GameStorage.Request db = storage.Context();
        accountId = db.CreateAccount(new Account { Username = name }, "pw").Id;
        var character = new Character { AccountId = accountId, Name = name, Mastery = new Mastery() };
        character.ReturnMaps.Push(2000062);
        characterId = db.CreateCharacter(character)!.Id;
    }

    [TearDown]
    public void TearDown() {
        if (Connection == null || accountId == 0) {
            return;
        }
        using var context = new Ms2Context(options);
        context.Database.ExecuteSqlRaw("DELETE FROM `club-member` WHERE `ClubId` = {0}", clubId);
        context.Database.ExecuteSqlRaw("DELETE FROM `club` WHERE `Id` = {0}", clubId);
        context.Database.ExecuteSqlRaw("DELETE FROM `character` WHERE `Id` = {0}", characterId);
        context.Database.ExecuteSqlRaw("DELETE FROM `ugcmap` WHERE `OwnerId` = {0}", accountId);
        context.Database.ExecuteSqlRaw("DELETE FROM `home` WHERE `AccountId` = {0}", accountId);
        context.Database.ExecuteSqlRaw("DELETE FROM `account` WHERE `Id` = {0}", accountId);
    }

    [Test]
    public void CreatedClub_LoadsWithItsCreationTimeNow() {
        PlayerInfo info = new PlayerInfo(new CharacterInfo(accountId, characterId, "leader", "", "", Gender.Male, Job.Newbie, 20), "",
            new AchievementInfo(), []);
        var players = new Players(new Dictionary<long, PlayerInfo> { [characterId] = info });
        long before = DateTime.Now.ToEpochSeconds();
        using (GameStorage.Request db = storage.Context()) {
            Club? created = db.CreateClub(players, "c" + Guid.NewGuid().ToString("N")[..8], characterId, [info]);
            Assert.That(created, Is.Not.Null);
            clubId = created!.Id;
        }
        long after = DateTime.Now.ToEpochSeconds();

        using GameStorage.Request read = storage.Context();
        Club? loaded = read.GetClub(players, clubId);
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.CreationTime, Is.InRange(before - 2, after + 2));
        Assert.That(loaded.Members[characterId].JoinTime, Is.InRange(before - 2, after + 2));
    }
}
