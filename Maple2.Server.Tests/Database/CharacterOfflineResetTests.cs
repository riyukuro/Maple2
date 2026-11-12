using System;
using System.Linq;
using System.Threading;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Database;

// The World's start sets every character offline (Channel -1). LastModified is the row version, a MySQL column with
// ON UPDATE CURRENT_TIMESTAMP: the reset must keep it, or the first save of a character still online on a channel
// meets a concurrency conflict and every last-online time moves to the World's start. The reset is raw SQL, so the
// tests run only when MS2_TEST_GAME_DB holds the connection string of a scratch MySQL game database (and
// MS2_TEST_ACCOUNT_ID the id of an account in it).
public class CharacterOfflineResetTests {
    private static readonly string? Connection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");

    private static readonly long AccountId =
        long.TryParse(Environment.GetEnvironmentVariable("MS2_TEST_ACCOUNT_ID"), out long id) ? id : 1;

    private DbContextOptions options = null!;
    private GameStorage storage = null!;
    private long characterId;

    [SetUp]
    public void SetUp() {
        if (Connection == null) {
            Assert.Ignore("the offline reset needs a real database: set MS2_TEST_GAME_DB");
        }
        options = new DbContextOptionsBuilder().UseMySql(Connection, ServerVersion.AutoDetect(Connection)).Options;
        storage = new GameStorage(options, null!, null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);

        using (GameStorage.Request db = storage.Context()) {
            var character = new Character { AccountId = AccountId, Name = "Reset" + Guid.NewGuid().ToString("N")[..6], Mastery = new Mastery() };
            character.ReturnMaps.Push(2000062);
            Character? created = db.CreateCharacter(character);
            Assert.That(created, Is.Not.Null);
            characterId = created!.Id;
        }
        using var context = new Ms2Context(options);
        context.Character.Find(characterId)!.Channel = 2;
        context.SaveChanges();
    }

    [TearDown]
    public void TearDown() {
        if (Connection == null) {
            return;
        }
        using var context = new Ms2Context(options);
        context.Database.ExecuteSqlRaw("DELETE FROM `character` WHERE Id = {0}", characterId);
    }

    private Maple2.Database.Model.Character Stored() {
        using var context = new Ms2Context(options);
        return context.Character.AsNoTracking().Single(character => character.Id == characterId);
    }

    private void Reset() {
        // The column keeps microseconds: a reset in the same instant as the setup's save could not show a change.
        Thread.Sleep(20);
        using GameStorage.Request db = storage.Context();
        db.SetAllCharacterToOffline();
    }

    [Test]
    public void Reset_SetsOfflineAndKeepsTheRowVersion() {
        DateTime before = Stored().LastModified;

        Reset();

        Maple2.Database.Model.Character after = Stored();
        Assert.That(after.Channel, Is.EqualTo(-1));
        Assert.That(after.LastModified, Is.EqualTo(before));
    }

    // A channel that loaded the character before the World restarted saves it after, as SavePlayer does (Update of
    // the whole row with the row version it loaded): the version still matches, so the save goes through at once
    // and writes its channel back.
    [Test]
    public void Reset_AnOnlineCharactersNextSaveHasNoConflict() {
        Maple2.Database.Model.Character loaded = Stored();

        Reset();

        loaded.MapId = 2000066;
        using var channel = new Ms2Context(options);
        channel.Update(loaded);
        Assert.DoesNotThrow(() => channel.SaveChanges());
        Maple2.Database.Model.Character after = Stored();
        Assert.That(after.Channel, Is.EqualTo(2));
        Assert.That(after.MapId, Is.EqualTo(2000066));
    }
}
