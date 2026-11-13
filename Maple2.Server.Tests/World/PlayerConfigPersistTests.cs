using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Maple2.Model.Game;
using Maple2.Server.World.Containers;
using Maple2.Server.World.Service;

namespace Maple2.Server.Tests.World;

// The World's player config outlives a World restart: each save is also written to the store (the game db), and a
// new lookup (the restarted World) reads it back, run down by the time since the save like the memory copy.
public class PlayerConfigPersistTests {
    private const long CharacterId = 7001;
    private const int Skill = 10200001;
    private const int Buff = 70000001;

    // The db in the test: the save goes through JSON, like the json columns of character-player-config.
    private sealed class JsonStore : IPlayerConfigStore {
        public readonly Dictionary<long, string> Rows = [];
        public int Loads;
        public bool Fail;

        public PlayerConfigSave? Load(long characterId) {
            Loads++;
            return Rows.TryGetValue(characterId, out string? json) ? JsonSerializer.Deserialize<PlayerConfigSave>(json) : null;
        }

        public bool Store(long characterId, PlayerConfigSave save) {
            if (Fail) {
                throw new InvalidOperationException("db down");
            }
            Rows[characterId] = JsonSerializer.Serialize(save);
            return true;
        }
    }

    private static PlayerConfigLookUp World(IPlayerConfigStore store, bool inGameTime = false) =>
        new((_, _) => inGameTime, (id, _) => id == Buff ? inGameTime : null, store);

    // A logout at epoch second 1000: a 30 s cooldown, a 60 s buff, a 20 s death penalty (count 2), a 25 s held cooldown.
    private static void Logout(PlayerConfigLookUp world, int buffId = Buff) {
        world.Save(
            [new BuffInfo { Id = buffId, Level = 1, MsRemaining = 60000, StopTime = 1000, Enabled = true, Stacks = 2 }],
            [new SkillCooldownInfo { SkillId = Skill, SkillLevel = 1, GroupId = 3, MsRemaining = 30000, StopTime = 1000, Charges = 1 }],
            new DeathInfo { Count = 2, MsRemaining = 20000, StopTime = 1000 },
            CharacterId,
            [new SkillCooldownInfo { SkillId = Skill + 1, SkillLevel = 1, MsRemaining = 25000, StopTime = 1000 }],
            true);
    }

    [Test]
    public void Restart_KeepsCooldownsBuffsDeathAndHold() {
        var store = new JsonStore();
        Logout(World(store));

        PlayerConfigLookUp restarted = World(store);
        (List<BuffInfo> buffs, List<SkillCooldownInfo> cooldowns, DeathInfo death) = restarted.Retrieve(CharacterId, 1010);
        (List<SkillCooldownInfo> held, bool stay) = restarted.RetrievePvpHold(CharacterId, 1010);

        Assert.That(cooldowns.Select(c => (c.SkillId, c.GroupId, c.MsRemaining)), Is.EqualTo(new[] { (Skill, 3, 20000) }));
        Assert.That(buffs.Select(b => (b.Id, b.Stacks, b.Enabled, b.MsRemaining)), Is.EqualTo(new[] { (Buff, 2, true, 50000) }));
        Assert.That((death.Count, death.MsRemaining), Is.EqualTo((2, 10000)));
        Assert.That(held.Select(c => (c.SkillId, c.MsRemaining)), Is.EqualTo(new[] { (Skill + 1, 15000) }));
        Assert.That(stay, Is.True);
    }

    [Test]
    public void Restart_DropsWhatExpiredSinceTheSave() {
        var store = new JsonStore();
        Logout(World(store));

        PlayerConfigLookUp restarted = World(store);
        (List<BuffInfo> buffs, List<SkillCooldownInfo> cooldowns, DeathInfo death) = restarted.Retrieve(CharacterId, 1100);
        (List<SkillCooldownInfo> held, _) = restarted.RetrievePvpHold(CharacterId, 1100);

        Assert.That(buffs, Is.Empty);
        Assert.That(cooldowns, Is.Empty);
        Assert.That((death.Count, death.MsRemaining), Is.EqualTo((0, 0)));
        Assert.That(held, Is.Empty);
    }

    [Test]
    public void Restart_InGameTimeEntries_DoNotRunDown() {
        var store = new JsonStore();
        Logout(World(store, inGameTime: true));

        (List<BuffInfo> buffs, List<SkillCooldownInfo> cooldowns, _) = World(store, inGameTime: true).Retrieve(CharacterId, 5000);
        Assert.That(buffs.Single().MsRemaining, Is.EqualTo(60000));
        Assert.That(cooldowns.Single().MsRemaining, Is.EqualTo(30000));
    }

    [Test]
    public void Memory_IsTheFirstSource() {
        var store = new JsonStore();
        PlayerConfigLookUp world = World(store);
        Logout(world);
        store.Rows.Clear();

        (_, List<SkillCooldownInfo> cooldowns, _) = world.Retrieve(CharacterId, 1010);
        Assert.That(cooldowns.Single().MsRemaining, Is.EqualTo(20000));
        Assert.That(store.Loads, Is.Zero);
    }

    [Test]
    public void NoRow_ReadOnce_ThenEmpty() {
        var store = new JsonStore();
        PlayerConfigLookUp world = World(store);
        Assert.That(world.Retrieve(CharacterId, 1010).SkillCooldowns, Is.Empty);
        Assert.That(world.RetrievePvpHold(CharacterId, 1010), Is.EqualTo((new List<SkillCooldownInfo>(), false)));
        Assert.That(world.Retrieve(CharacterId, 1020).Buffs, Is.Empty);
        Assert.That(store.Loads, Is.EqualTo(1));
    }

    [Test]
    public void SecondRead_DoesNotTakeTheTimeOffTwice() {
        PlayerConfigLookUp world = World(new JsonStore());
        Logout(world);
        world.Retrieve(CharacterId, 1010);

        (List<BuffInfo> buffs, List<SkillCooldownInfo> cooldowns, DeathInfo death) = world.Retrieve(CharacterId, 1010);
        Assert.That(buffs.Single().MsRemaining, Is.EqualTo(50000));
        Assert.That(cooldowns.Single().MsRemaining, Is.EqualTo(20000));
        Assert.That(death.MsRemaining, Is.EqualTo(10000));
    }

    [Test]
    public void UnknownBuff_IsDropped() {
        var store = new JsonStore();
        Logout(World(store), buffId: 1);
        Assert.That(World(store).Retrieve(CharacterId, 1010).Buffs, Is.Empty);
    }

    [Test]
    public void StoreDown_SaveStaysInMemory() {
        var store = new JsonStore { Fail = true };
        PlayerConfigLookUp world = World(store);
        Assert.DoesNotThrow(() => Logout(world));
        Assert.That(world.Retrieve(CharacterId, 1010).SkillCooldowns.Single().MsRemaining, Is.EqualTo(20000));
    }

    // The stored row is read while a newer save comes in: the newer save is what the World keeps.
    private sealed class SlowStore(JsonStore inner) : IPlayerConfigStore {
        public readonly System.Threading.ManualResetEventSlim Reading = new();
        public readonly System.Threading.ManualResetEventSlim Release = new();

        public PlayerConfigSave? Load(long characterId) {
            PlayerConfigSave? save = inner.Load(characterId);
            Reading.Set();
            Release.Wait(TimeSpan.FromSeconds(5));
            return save;
        }

        public bool Store(long characterId, PlayerConfigSave save) => inner.Store(characterId, save);
    }

    [Test]
    public void SaveDuringTheFirstRead_Wins() {
        var rows = new JsonStore();
        Logout(World(rows));
        var store = new SlowStore(rows);
        PlayerConfigLookUp world = World(store);

        var read = System.Threading.Tasks.Task.Run(() => world.Retrieve(CharacterId, 1010));
        Assert.That(store.Reading.Wait(TimeSpan.FromSeconds(5)), Is.True);
        var save = System.Threading.Tasks.Task.Run(() => world.Save([],
            [new SkillCooldownInfo { SkillId = Skill, SkillLevel = 1, MsRemaining = 5000, StopTime = 1010, Charges = 1 }],
            new DeathInfo(), CharacterId));
        Assert.That(save.Wait(200), Is.False, "the save waits for the read");
        store.Release.Set();
        Assert.That(System.Threading.Tasks.Task.WaitAll([read, save], TimeSpan.FromSeconds(5)), Is.True);

        Assert.That(world.Retrieve(CharacterId, 1010).SkillCooldowns.Single().MsRemaining, Is.EqualTo(5000));
        Assert.That(World(rows).Retrieve(CharacterId, 1010).SkillCooldowns.Single().MsRemaining, Is.EqualTo(5000));
    }
}
