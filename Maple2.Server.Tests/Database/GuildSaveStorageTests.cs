using System;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Database;

// SaveGuild answers whether the guild row and its members were written: the World server's fund spends and the
// rename give back what they took only when it answers false. In-memory store, with a save the test can refuse.
public class GuildSaveStorageTests {
    private GameStorage storage = null!;
    private readonly SaveFault fault = new();
    private Guild guild = null!;

    [SetUp]
    public void SetUp() {
        fault.Fail = false;
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(fault)
            .Options;
        storage = new GameStorage(options, null!, null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);

        using GameStorage.Request db = storage.Context();
        var leader = new Character { AccountId = 1, Name = "Leader", Mastery = new Mastery() };
        leader.ReturnMaps.Push(2000062);
        Character? character = db.CreateCharacter(leader);
        Assert.That(character, Is.Not.Null);
        guild = db.CreateGuild("Guild", character!.Id)!;
        Assert.That(guild, Is.Not.Null);
        var info = new PlayerInfo(new CharacterInfo(1, character.Id, "Leader", "", "", Gender.Male, Job.Newbie, 1), "", new AchievementInfo(), []);
        guild.Members.TryAdd(character.Id, new GuildMember { GuildId = guild.Id, Info = info, Rank = 0 });
    }

    [Test]
    public void SaveThatWorks_IsTrue() {
        guild.Funds = 500;
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.SaveGuild(guild), Is.True);
        }
        using GameStorage.Request check = storage.Context();
        Assert.That(check.GetGuild(guild.Id)!.Funds, Is.EqualTo(500));
    }

    [Test]
    public void SaveThatFails_IsFalse() {
        guild.Funds = 500;
        fault.Fail = true;
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.SaveGuild(guild), Is.False);
        }
        fault.Fail = false;
        using GameStorage.Request check = storage.Context();
        Assert.That(check.GetGuild(guild.Id)!.Funds, Is.Not.EqualTo(500));
    }

    private sealed class SaveFault : SaveChangesInterceptor {
        public bool Fail;

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) {
            if (Fail) {
                throw new DbUpdateException("The test made this save fail");
            }
            return result;
        }
    }
}
