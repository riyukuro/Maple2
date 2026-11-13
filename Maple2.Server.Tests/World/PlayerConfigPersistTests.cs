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
        public bool FailLoad;

        public PlayerConfigSave? Load(long characterId) {
            Loads++;
            if (FailLoad) {
                throw new InvalidOperationException("db down");
            }
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

    // The db writes run in the saving thread here, so a test reads the store right after a save.
    private static PlayerConfigLookUp World(IPlayerConfigStore store, bool inGameTime = false, Func<long>? clockMs = null) =>
        new((_, _) => inGameTime, (id, _) => id == Buff ? inGameTime : null, store, work => work(), clockMs);

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

    // The stored row is read while a newer save comes in: the save does not wait for the read, and the newer save is
    // what the World keeps.
    private sealed class SlowStore(JsonStore inner) : IPlayerConfigStore {
        public readonly System.Threading.ManualResetEventSlim Reading = new();
        public readonly System.Threading.ManualResetEventSlim Release = new();

        public PlayerConfigSave? Load(long characterId) {
            PlayerConfigSave? save = inner.Load(characterId);
            Reading.Set();
            Release.Wait();
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

        System.Threading.Tasks.Task<(List<BuffInfo>, List<SkillCooldownInfo>, DeathInfo)> read;
        try {
            read = System.Threading.Tasks.Task.Run(() => world.Retrieve(CharacterId, 1010));
            Assert.That(store.Reading.Wait(TimeSpan.FromSeconds(10)), Is.True);
            var save = System.Threading.Tasks.Task.Run(() => world.Save([],
                [new SkillCooldownInfo { SkillId = Skill, SkillLevel = 1, MsRemaining = 5000, StopTime = 1010, Charges = 1 }],
                new DeathInfo(), CharacterId));
            Assert.That(save.Wait(TimeSpan.FromSeconds(10)), Is.True, "the save does not wait for the read");
        } finally {
            store.Release.Set();
        }
        Assert.That(read.Wait(TimeSpan.FromSeconds(10)), Is.True);

        Assert.That(world.Retrieve(CharacterId, 1010).SkillCooldowns.Single().MsRemaining, Is.EqualTo(5000));
        Assert.That(World(rows).Retrieve(CharacterId, 1010).SkillCooldowns.Single().MsRemaining, Is.EqualTo(5000));
    }
    // A db write that does not end until it is let go (a slow or hung MySQL).
    private sealed class HungStore(JsonStore inner) : IPlayerConfigStore {
        public readonly System.Threading.ManualResetEventSlim Writing = new();
        public readonly System.Threading.ManualResetEventSlim Release = new();

        public PlayerConfigSave? Load(long characterId) => inner.Load(characterId);

        public bool Store(long characterId, PlayerConfigSave save) {
            Writing.Set();
            Release.Wait();
            return inner.Store(characterId, save);
        }
    }

    // The Game's logout saves through the World: the save ends without waiting for the db, a read of the character
    // in the meantime gets the memory copy, and the write still reaches the db (Flush, as at the World's stop).
    [Test]
    public void HungDb_DoesNotHoldUpTheSave() {
        var rows = new JsonStore();
        var store = new HungStore(rows);
        var world = new PlayerConfigLookUp((_, _) => false, (id, _) => id == Buff ? false : null, store);

        try {
            var save = System.Threading.Tasks.Task.Run(() => Logout(world));
            Assert.That(save.Wait(TimeSpan.FromSeconds(10)), Is.True, "the save does not wait for the db");
            Assert.That(store.Writing.Wait(TimeSpan.FromSeconds(10)), Is.True);
            var read = System.Threading.Tasks.Task.Run(() => world.Retrieve(CharacterId, 1010));
            Assert.That(read.Wait(TimeSpan.FromSeconds(10)), Is.True, "a read does not wait for the db");
            Assert.That(read.Result.SkillCooldowns.Single().MsRemaining, Is.EqualTo(20000));
            Assert.That(rows.Rows, Is.Empty);
        } finally {
            store.Release.Set();
        }
        Assert.That(world.Flush(TimeSpan.FromSeconds(5)), Is.True);
        Assert.That(World(rows).Retrieve(CharacterId, 1010).SkillCooldowns.Single().MsRemaining, Is.EqualTo(20000));
    }

    // Saves that come in while a write runs: the db ends with the newest one.
    [Test]
    public void SavesDuringAWrite_TheNewestIsWritten() {
        var rows = new JsonStore();
        var store = new HungStore(rows);
        var world = new PlayerConfigLookUp((_, _) => false, (_, _) => false, store);
        try {
            world.Save([], [new SkillCooldownInfo { SkillId = Skill, SkillLevel = 1, MsRemaining = 1000, StopTime = 1000, Charges = 1 }],
                new DeathInfo(), CharacterId);
            Assert.That(store.Writing.Wait(TimeSpan.FromSeconds(10)), Is.True);
            foreach (int ms in new[] { 2000, 3000 }) {
                world.Save([], [new SkillCooldownInfo { SkillId = Skill, SkillLevel = 1, MsRemaining = ms, StopTime = 1000, Charges = 1 }],
                    new DeathInfo(), CharacterId);
            }
        } finally {
            store.Release.Set();
        }
        Assert.That(world.Flush(TimeSpan.FromSeconds(5)), Is.True);
        Assert.That(World(rows).Retrieve(CharacterId, 1000).SkillCooldowns.Single().MsRemaining, Is.EqualTo(3000));
    }

    // While the db is down a login reads it once, not once per Retrieve and RetrievePvpHold; after the retry time
    // the next login reads it again, and gets the stored config once the db is back.
    [Test]
    public void FailedRead_NotRetriedUntilTheRetryTime() {
        var store = new JsonStore();
        Logout(World(store));
        store.FailLoad = true;
        long now = 0;
        PlayerConfigLookUp world = World(store, clockMs: () => now);

        Assert.That(world.Retrieve(CharacterId, 1010).SkillCooldowns, Is.Empty);
        Assert.That(world.RetrievePvpHold(CharacterId, 1010).Held, Is.Empty);
        Assert.That(world.Retrieve(CharacterId, 1010).SkillCooldowns, Is.Empty);
        Assert.That(store.Loads, Is.EqualTo(1));

        now = PlayerConfigLookUp.LoadRetryMs - 1;
        world.Retrieve(CharacterId, 1010);
        Assert.That(store.Loads, Is.EqualTo(1));

        store.FailLoad = false;
        now = PlayerConfigLookUp.LoadRetryMs;
        Assert.That(world.Retrieve(CharacterId, 1010).SkillCooldowns.Single().MsRemaining, Is.EqualTo(20000));
        Assert.That(store.Loads, Is.EqualTo(2));
    }

    // A login whose read failed, and its next login that fell in the retry time, play from an empty config: their
    // logouts do not write it over the stored row, and the next read that works gives the stored config back.
    [Test]
    public void UnreadCharacter_SaveDoesNotReplaceTheStoredRow() {
        var store = new JsonStore();
        Logout(World(store));
        string row = store.Rows[CharacterId];
        store.FailLoad = true;
        long now = 0;
        PlayerConfigLookUp world = World(store, clockMs: () => now);

        Assert.That(world.Retrieve(CharacterId, 1010).SkillCooldowns, Is.Empty);
        world.Save([], [], new DeathInfo(), CharacterId);
        store.FailLoad = false;
        now = PlayerConfigLookUp.LoadRetryMs - 1;
        Assert.That(world.Retrieve(CharacterId, 1010).SkillCooldowns, Is.Empty, "read skipped in the retry time");
        world.Save([], [], new DeathInfo(), CharacterId);
        Assert.That(store.Rows[CharacterId], Is.EqualTo(row));

        now = PlayerConfigLookUp.LoadRetryMs;
        Assert.That(world.Retrieve(CharacterId, 1010).SkillCooldowns.Single().MsRemaining, Is.EqualTo(20000));
        world.Save([], [new SkillCooldownInfo { SkillId = Skill, SkillLevel = 1, MsRemaining = 5000, StopTime = 1010, Charges = 1 }],
            new DeathInfo(), CharacterId);
        Assert.That(World(store).Retrieve(CharacterId, 1010).SkillCooldowns.Single().MsRemaining, Is.EqualTo(5000));
    }

    // The retry time is the failed character's own: another character's login in it still reads its row.
    [Test]
    public void FailedRead_DoesNotHoldUpAnotherCharactersRead() {
        const long Other = CharacterId + 1;
        var store = new JsonStore();
        World(store).Save([], [new SkillCooldownInfo { SkillId = Skill, SkillLevel = 1, MsRemaining = 40000, StopTime = 1000, Charges = 1 }],
            new DeathInfo(), Other);
        store.FailLoad = true;
        PlayerConfigLookUp world = World(store, clockMs: () => 0);

        Assert.That(world.Retrieve(CharacterId, 1010).SkillCooldowns, Is.Empty);
        store.FailLoad = false;
        Assert.That(world.Retrieve(Other, 1010).SkillCooldowns.Single().MsRemaining, Is.EqualTo(30000));
        Assert.That(store.Loads, Is.EqualTo(2));
    }

    // The merged death penalty is the higher count with the later end: a session of more deaths whose penalty ends
    // sooner than the row's does not cut the row's short.
    [Test]
    public void ReadAfterAFailedRead_DeathPenaltyKeepsTheLaterEnd() {
        var store = new JsonStore();
        Logout(World(store)); // count 2, ends at 1020
        store.FailLoad = true;
        long now = 0;
        PlayerConfigLookUp world = World(store, clockMs: () => now);
        Assert.That(world.Retrieve(CharacterId, 1010).SkillCooldowns, Is.Empty);
        world.Save([], [], new DeathInfo { Count = 3, MsRemaining = 1000, StopTime = 1010 }, CharacterId); // ends at 1011
        store.FailLoad = false;
        now = PlayerConfigLookUp.LoadRetryMs;

        DeathInfo death = world.Retrieve(CharacterId, 1010).Death;
        Assert.That((death.Count, death.MsRemaining), Is.EqualTo((3, 10000)));
    }

    // A read after a failed read finds the row's death penalty ended (at 1020) by the time of the read (1050): its
    // count does not come back over the session's newer, lower one.
    [Test]
    public void ReadAfterAFailedRead_AnEndedStoredDeathPenaltyAddsNoCount() {
        var store = new JsonStore();
        Logout(World(store)); // count 2, ends at 1020
        store.FailLoad = true;
        long now = 0;
        PlayerConfigLookUp world = World(store, clockMs: () => now);
        Assert.That(world.Retrieve(CharacterId, 1010).SkillCooldowns, Is.Empty);
        world.Save([], [], new DeathInfo { Count = 1, MsRemaining = 50000, StopTime = 1050 }, CharacterId); // ends at 1100
        store.FailLoad = false;
        now = PlayerConfigLookUp.LoadRetryMs;

        DeathInfo death = world.Retrieve(CharacterId, 1050).Death;
        Assert.That((death.Count, death.MsRemaining), Is.EqualTo((1, 50000)));
    }

    // A game-time cooldown runs down only in play, so its stop time says nothing about when it ends: the merge keeps
    // the one with more time left (the row's 30 s), not the one saved later (the session's 25 s at 2000).
    [Test]
    public void ReadAfterAFailedRead_GameTimeCooldownKeepsTheLongerTimeLeft() {
        var store = new JsonStore();
        Logout(World(store, inGameTime: true)); // 30 s cooldown at 1000
        store.FailLoad = true;
        long now = 0;
        PlayerConfigLookUp world = World(store, inGameTime: true, clockMs: () => now);
        Assert.That(world.Retrieve(CharacterId, 1010).SkillCooldowns, Is.Empty);
        world.Save([],
            [new SkillCooldownInfo { SkillId = Skill, SkillLevel = 1, MsRemaining = 25000, StopTime = 2000, Charges = 1 }],
            new DeathInfo(), CharacterId);
        store.FailLoad = false;
        now = PlayerConfigLookUp.LoadRetryMs;

        Assert.That(world.Retrieve(CharacterId, 2000).SkillCooldowns.Single().MsRemaining, Is.EqualTo(30000));
    }

    // A session that played on while its row was unread, then reads the row: the cooldown and death penalty it got
    // in the meantime stay (the row's cooldown of the same skill ends sooner), the row's other cooldown and its buff
    // come back, and the merged config is written.
    [Test]
    public void ReadAfterAFailedRead_MergesIntoWhatTheSessionHolds() {
        var store = new JsonStore();
        Logout(World(store));
        store.FailLoad = true;
        long now = 0;
        PlayerConfigLookUp world = World(store, clockMs: () => now);
        Assert.That(world.Retrieve(CharacterId, 1010).SkillCooldowns, Is.Empty);
        world.Save([],
            [new SkillCooldownInfo { SkillId = Skill, SkillLevel = 1, MsRemaining = 50000, StopTime = 1010, Charges = 1 }],
            new DeathInfo { Count = 3, MsRemaining = 40000, StopTime = 1010 }, CharacterId);
        store.FailLoad = false;
        now = PlayerConfigLookUp.LoadRetryMs;

        (List<BuffInfo> buffs, List<SkillCooldownInfo> cooldowns, DeathInfo death) = world.Retrieve(CharacterId, 1010);
        Assert.That(cooldowns.Single().MsRemaining, Is.EqualTo(50000));
        Assert.That((death.Count, death.MsRemaining), Is.EqualTo((3, 40000)));
        Assert.That(buffs.Single().Id, Is.EqualTo(Buff));
        Assert.That(world.RetrievePvpHold(CharacterId, 1010).Held.Single().SkillId, Is.EqualTo(Skill + 1));

        (List<BuffInfo> _, List<SkillCooldownInfo> written, DeathInfo writtenDeath) = World(store).Retrieve(CharacterId, 1010);
        Assert.That(written.Single().MsRemaining, Is.EqualTo(50000));
        Assert.That(writtenDeath.Count, Is.EqualTo(3));
    }
}
