using System;
using System.Collections.Generic;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Database;

// The world champion rows on an in-memory database: the season's step, each character's damage, and the reward mark
// a login takes once. Made-up ids.
public class WorldChampionStorageTests {
    private GameStorage storage = null!;

    [SetUp]
    public void SetUp() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        storage = new GameStorage(options, null!, null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);
    }

    private long CreateCharacter(string name, long accountId = 7) {
        using GameStorage.Request db = storage.Context();
        var model = new Character { AccountId = accountId, Name = name, Job = Job.Knight, Mastery = new Mastery() };
        model.ReturnMaps.Push(2000062);
        Character? character = db.CreateCharacter(model);
        Assert.That(character, Is.Not.Null);
        return character!.Id;
    }

    [Test]
    public void SaveAndRead() {
        long a = CreateCharacter("champA");
        long b = CreateCharacter("champB", 8);
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.GetWorldChampionSeason(1), Is.Null);
            Assert.That(db.SaveWorldChampion(1, 2, 300, [new WorldChampionDamage(7, a, 1000), new WorldChampionDamage(8, b, 50)]), Is.True);
        }
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.SaveWorldChampion(1, 3, 10, [new WorldChampionDamage(7, a, 1600)]), Is.True);
        }
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.GetWorldChampionSeason(1), Is.EqualTo((3, 10L)));
            Assert.That(db.GetWorldChampionDamages(1), Is.EquivalentTo(new[] { new WorldChampionDamage(7, a, 1600), new WorldChampionDamage(8, b, 50) }));
            Assert.That(db.GetWorldChampionDamages(2), Is.Empty);
        }
    }

    // A character deleted before the save is skipped; the others are written.
    [Test]
    public void GoneCharacterSkipped() {
        long a = CreateCharacter("champA");
        using GameStorage.Request db = storage.Context();
        Assert.That(db.SaveWorldChampion(1, 1, 5, [new WorldChampionDamage(7, a, 5), new WorldChampionDamage(9, 987654321, 5)]), Is.True);
        Assert.That(db.GetWorldChampionDamages(1), Is.EqualTo(new[] { new WorldChampionDamage(7, a, 5) }));
    }

    // The reward is taken once, at the season's damage or more; a later damage save keeps the mark.
    [Test]
    public void ClaimOnce() {
        long a = CreateCharacter("champA");
        long b = CreateCharacter("champB", 8);
        using (GameStorage.Request db = storage.Context()) {
            db.SaveWorldChampion(1, 4, 0, [new WorldChampionDamage(7, a, 1500), new WorldChampionDamage(8, b, 1499)]);
        }
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.ClaimWorldChampionReward(1, b, 1500), Is.False);
            Assert.That(db.ClaimWorldChampionReward(2, a, 1500), Is.False);
            Assert.That(db.ClaimWorldChampionReward(1, a, 1500), Is.True);
        }
        using (GameStorage.Request db = storage.Context()) {
            db.SaveWorldChampion(1, 4, 0, [new WorldChampionDamage(7, a, 1700)]);
        }
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.ClaimWorldChampionReward(1, a, 1500), Is.False);
        }
    }
}
