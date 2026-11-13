using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.World.Containers;
using Maple2.Server.World.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Database;

// character-player-config: the World's player config written per character and read back after a World restart, on
// an in-memory database (the json columns go through the same conversion).
public class PlayerConfigStorageTests {
    private const int Skill = 10200001;
    private const int Buff = 70000001;

    private GameStorage storage = null!;

    [SetUp]
    public void SetUp() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        storage = new GameStorage(options, null!, null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);
    }

    private long CreateCharacter() {
        using GameStorage.Request db = storage.Context();
        var model = new Character { AccountId = 7, Name = "Tester", Job = Job.Knight, Level = 50, Mastery = new Mastery() };
        model.ReturnMaps.Push(2000062);
        Character? character = db.CreateCharacter(model);
        Assert.That(character, Is.Not.Null);
        return character!.Id;
    }

    private static PlayerConfigSave Save(int cooldownMs) => new() {
        Buffs = [new PlayerConfigBuff { Id = Buff, Level = 2, MsRemaining = 60000, StopTime = 1000, Enabled = true, Stacks = 3 }],
        SkillCooldowns = [new PlayerConfigCooldown { SkillId = Skill, SkillLevel = 1, GroupId = 4, MsRemaining = cooldownMs, StopTime = 1000, Charges = 2, RechargeMs = 9000 }],
        PvpHeldCooldowns = [new PlayerConfigCooldown { SkillId = Skill + 1, SkillLevel = 1, MsRemaining = 25000, StopTime = 1000 }],
        PvpStay = true,
        DeathCount = 2,
        DeathMsRemaining = 20000,
        DeathStopTime = 1000,
    };

    [Test]
    public void SaveThenGet_RoundTrips_AndASecondSaveReplaces() {
        long characterId = CreateCharacter();
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.GetPlayerConfig(characterId), Is.Null);
            Assert.That(db.SavePlayerConfig(characterId, Save(30000)), Is.True);
        }
        using (GameStorage.Request db = storage.Context()) {
            PlayerConfigSave? read = db.GetPlayerConfig(characterId);
            Assert.That(read, Is.Not.Null);
            Assert.That(read!.Buffs.Select(b => (b.Id, b.Level, b.MsRemaining, b.StopTime, b.Enabled, b.Stacks)),
                Is.EqualTo(new[] { (Buff, 2, 60000, 1000L, true, 3) }));
            Assert.That(read.SkillCooldowns.Select(c => (c.SkillId, c.GroupId, c.MsRemaining, c.Charges, c.RechargeMs)),
                Is.EqualTo(new[] { (Skill, 4, 30000, 2, 9000L) }));
            Assert.That(read.PvpHeldCooldowns.Single().SkillId, Is.EqualTo(Skill + 1));
            Assert.That((read.PvpStay, read.DeathCount, read.DeathMsRemaining, read.DeathStopTime), Is.EqualTo((true, 2, 20000, 1000L)));
            Assert.That(db.SavePlayerConfig(characterId, Save(5000)), Is.True);
        }
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.GetPlayerConfig(characterId)!.SkillCooldowns.Single().MsRemaining, Is.EqualTo(5000));
        }
    }

    [Test]
    public void Save_ForACharacterThatIsGone_IsRefused() {
        using GameStorage.Request db = storage.Context();
        Assert.That(db.SavePlayerConfig(999999, Save(30000)), Is.False);
        Assert.That(db.GetPlayerConfig(999999), Is.Null);
    }

    [Test]
    public void WorldRestart_ReadsTheConfigBackFromTheDb() {
        long characterId = CreateCharacter();
        var store = new DbPlayerConfigStore(storage);
        var before = new PlayerConfigLookUp((_, _) => false, (_, _) => false, store);
        before.Save([], [new SkillCooldownInfo { SkillId = Skill, SkillLevel = 1, MsRemaining = 30000, StopTime = 1000, Charges = 1 }],
            new DeathInfo(), characterId);
        Assert.That(before.Flush(TimeSpan.FromSeconds(5)), Is.True);

        var after = new PlayerConfigLookUp((_, _) => false, (_, _) => false, store);
        Assert.That(after.Retrieve(characterId, 1010).SkillCooldowns.Single().MsRemaining, Is.EqualTo(20000));
    }
}
