using System;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.World.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Database;

// character-player-config on MySQL: the foreign key to the character, which the in-memory store does not enforce,
// refuses a row of no character and drops the row with its character, and the json columns round-trip. Runs only
// when MS2_TEST_GAME_DB holds the connection string of a scratch MySQL game database with the migrations applied
// (with oldguids=true, as the servers connect).
public class PlayerConfigMySqlTests {
    private static readonly string? Connection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");
    private const int Skill = 10200001;

    private DbContextOptions options = null!;
    private GameStorage storage = null!;
    private long accountId;
    private long characterId;

    [SetUp]
    public void SetUp() {
        if (Connection == null) {
            Assert.Ignore("the character foreign key needs a real database: set MS2_TEST_GAME_DB");
        }
        options = new DbContextOptionsBuilder().UseMySql(Connection, ServerVersion.AutoDetect(Connection)).Options;
        storage = new GameStorage(options, null!, null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);
        string name = "pc" + Guid.NewGuid().ToString("N")[..8];
        using GameStorage.Request db = storage.Context();
        accountId = db.CreateAccount(new Account { Username = name }, "pw").Id;
        var model = new Character { AccountId = accountId, Name = name, Job = Job.Knight, Level = 50, Mastery = new Mastery() };
        model.ReturnMaps.Push(2000062);
        Character? character = db.CreateCharacter(model);
        Assert.That(character, Is.Not.Null);
        characterId = character!.Id;
    }

    [TearDown]
    public void TearDown() {
        if (Connection == null) {
            return;
        }
        using var context = new Ms2Context(options);
        context.CharacterPlayerConfig.Where(row => row.CharacterId == characterId).ExecuteDelete();
        context.Character.Where(character => character.Id == characterId).ExecuteDelete();
        context.Account.Where(account => account.Id == accountId).ExecuteDelete();
    }

    private static PlayerConfigSave Save(int cooldownMs) => new() {
        SkillCooldowns = [new PlayerConfigCooldown { SkillId = Skill, SkillLevel = 1, GroupId = 4, MsRemaining = cooldownMs, StopTime = 1000, Charges = 2, RechargeMs = 9000 }],
        DeathCount = 1,
    };

    [Test]
    public void SaveThenGet_RoundTrips() {
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.SavePlayerConfig(characterId, Save(30000)), Is.True);
            Assert.That(db.SavePlayerConfig(characterId, Save(25000)), Is.True);
        }
        using (GameStorage.Request db = storage.Context()) {
            PlayerConfigSave? read = db.GetPlayerConfig(characterId);
            Assert.That(read, Is.Not.Null);
            Assert.That(read!.SkillCooldowns.Select(c => (c.SkillId, c.MsRemaining, c.Charges, c.RechargeMs)),
                Is.EqualTo(new[] { (Skill, 25000, 2, 9000) }));
            Assert.That(read.DeathCount, Is.EqualTo(1));
        }
    }

    [Test]
    public void DeletedCharacter_TakesItsRowWithIt() {
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.SavePlayerConfig(characterId, Save(30000)), Is.True);
        }
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.DeleteCharacter(accountId, characterId), Is.True);
        }
        using var context = new Ms2Context(options);
        Assert.That(context.CharacterPlayerConfig.Any(row => row.CharacterId == characterId), Is.False);
    }

    // The row of a character that is gone: the foreign key refuses it even past the save's own check.
    [Test]
    public void RowOfNoCharacter_IsRefusedByTheForeignKey() {
        long missing = characterId + 1_000_000_000;
        using (var context = new Ms2Context(options)) {
            context.CharacterPlayerConfig.Add(new Maple2.Database.Model.CharacterPlayerConfig { CharacterId = missing });
            Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        }
        using GameStorage.Request db = storage.Context();
        Assert.That(db.SavePlayerConfig(missing, Save(30000)), Is.False);
    }
}
