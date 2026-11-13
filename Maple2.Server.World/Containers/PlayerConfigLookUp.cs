using System.Collections.Concurrent;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.World.Service;
using Serilog;

namespace Maple2.Server.World.Containers;

// Where the World's player config outlives the World: the game db (character-player-config).
public interface IPlayerConfigStore {
    PlayerConfigSave? Load(long characterId);
    bool Store(long characterId, PlayerConfigSave save);
}

public sealed class DbPlayerConfigStore(GameStorage gameStorage) : IPlayerConfigStore {
    public PlayerConfigSave? Load(long characterId) {
        using GameStorage.Request db = gameStorage.Context();
        return db.GetPlayerConfig(characterId);
    }

    public bool Store(long characterId, PlayerConfigSave save) {
        using GameStorage.Request db = gameStorage.Context();
        return db.SavePlayerConfig(characterId, save);
    }
}

// The player config a character takes from one login to the next. The memory here is the first source; each save is
// also written to the store, and a character this World has nothing for (a World restart) is read back from it.
public class PlayerConfigLookUp {
    private static readonly ILogger Logger = Log.Logger.ForContext<PlayerConfigLookUp>();

    // Whether a skill's cooldown runs on in-game time only (its stateAttr useInGameTime); null for an unknown skill.
    private readonly Func<int, short, bool?> usesGameTime;
    // The same for a buff (its additional effect's useInGameTime); null for an unknown effect.
    private readonly Func<int, short, bool?> buffUsesGameTime;
    private readonly IPlayerConfigStore? store;

    private readonly ConcurrentDictionary<long, ConcurrentDictionary<int, BuffInfo>> buffs;
    private readonly ConcurrentDictionary<long, ConcurrentDictionary<int, SkillCooldownInfo>> skillCooldowns;
    private readonly ConcurrentDictionary<long, DeathInfo> deaths;
    // The town cooldowns held through a PvP-zone stay (the game server's PvpCooldownHold) and whether the stay runs.
    private readonly ConcurrentDictionary<long, (ConcurrentDictionary<int, SkillCooldownInfo> Held, bool Stay)> pvpHolds = [];
    // A save and a first read from the store of one character never interleave. Striped: a fixed set of locks by
    // character id, so the World keeps no lock per character it has seen.
    private const int LockStripes = 64;
    private readonly object[] locks = Enumerable.Range(0, LockStripes).Select(_ => new object()).ToArray();
    // The db writes run behind the save, out of its lock: a slow or hung db does not hold up the Game's logout. One
    // writer at a time takes the newest save of each character, so the db sees the saves in order.
    private readonly ConcurrentDictionary<long, PlayerConfigSave> pendingWrites = [];
    private readonly Action<Action> runWriter;
    private int writerRunning;
    // After a failed read the store is not read again for a while: while the db is down a login does not wait on it
    // again and again (the config of that login is the memory copy, or none).
    internal const long LoadRetryMs = 30_000;
    private readonly Func<long> clockMs;
    private long loadRetryAt = long.MinValue;
    // Characters whose stored row was not read (the read failed, or fell in the retry time): their saves stay in
    // memory and are not written, so a config begun from nothing never replaces the stored row. The next read that
    // works takes the stored row over the memory copy.
    private readonly ConcurrentDictionary<long, byte> unread = [];

    public PlayerConfigLookUp(SkillMetadataStorage skillMetadataStorage, GameStorage gameStorage) : this(
        (id, level) => skillMetadataStorage.TryGet(id, level, out SkillMetadata? skill) ? skill.State.UseInGameTime : null,
        (id, level) => skillMetadataStorage.TryGetEffect(id, level, out AdditionalEffectMetadata? effect) ? effect.Property.UseInGameTime : null,
        new DbPlayerConfigStore(gameStorage)) { }

    // No store: the config lives as long as this instance.
    internal PlayerConfigLookUp(SkillMetadataStorage? _, Func<int, short, bool?> usesGameTime) : this(usesGameTime, (_, _) => false, null) { }

    // runWriter runs the db writer (the thread pool by default); clockMs is the clock of the read retry.
    internal PlayerConfigLookUp(Func<int, short, bool?> usesGameTime, Func<int, short, bool?> buffUsesGameTime, IPlayerConfigStore? store,
                                Action<Action>? runWriter = null, Func<long>? clockMs = null) {
        this.usesGameTime = usesGameTime;
        this.buffUsesGameTime = buffUsesGameTime;
        this.store = store;
        this.runWriter = runWriter ?? (work => Task.Run(work));
        this.clockMs = clockMs ?? (() => Environment.TickCount64);
        buffs = [];
        skillCooldowns = [];
        deaths = [];
    }

    public void Save(List<BuffInfo> saveBuffs, List<SkillCooldownInfo> skillCooldownInfos, DeathInfo death, long characterId,
                     List<SkillCooldownInfo>? pvpHeld = null, bool pvpStay = false) {
        bool write;
        lock (LockOf(characterId)) {
            Keep(saveBuffs, skillCooldownInfos, death, characterId, pvpHeld ?? [], pvpStay);
            write = store != null && !unread.ContainsKey(characterId);
            if (write) {
                pendingWrites[characterId] = ToSave(saveBuffs, skillCooldownInfos, death, pvpHeld ?? [], pvpStay);
            }
        }
        if (write) {
            StartWriter();
        }
    }

    private object LockOf(long characterId) => locks[(int) ((ulong) characterId % LockStripes)];

    private void StartWriter() {
        if (Interlocked.CompareExchange(ref writerRunning, 1, 0) == 0) {
            runWriter(WritePending);
        }
    }

    private void WritePending() {
        while (true) {
            foreach (long characterId in pendingWrites.Keys) {
                if (pendingWrites.TryRemove(characterId, out PlayerConfigSave? save)) {
                    Write(characterId, save);
                }
            }
            // A full fence: the check below must not read the dictionary before the flag is seen as 0.
            Interlocked.Exchange(ref writerRunning, 0);
            // A save that came in after the last pass, while no writer was taking it.
            if (pendingWrites.IsEmpty || Interlocked.CompareExchange(ref writerRunning, 1, 0) != 0) {
                return;
            }
        }
    }

    private void Write(long characterId, PlayerConfigSave save) {
        try {
            if (!store!.Store(characterId, save)) {
                Logger.Warning("Player config of Character:{CharacterId} not written to the db; kept in memory only", characterId);
            }
        } catch (Exception ex) {
            Logger.Warning(ex, "Player config of Character:{CharacterId} not written to the db; kept in memory only", characterId);
        }
    }

    // Waits for the saves not yet written (the World's shutdown); false when the timeout ends first.
    public bool Flush(TimeSpan timeout) =>
        SpinWait.SpinUntil(() => pendingWrites.IsEmpty && Volatile.Read(ref writerRunning) == 0, timeout);

    private void Keep(IEnumerable<BuffInfo> saveBuffs, IEnumerable<SkillCooldownInfo> skillCooldownInfos, DeathInfo? death, long characterId,
                      IEnumerable<SkillCooldownInfo> pvpHeld, bool pvpStay) {
        var newBuffList = new ConcurrentDictionary<int, BuffInfo>();
        foreach (BuffInfo buff in saveBuffs) {
            newBuffList.TryAdd(buff.Id, buff);
        }

        buffs[characterId] = newBuffList;

        var newSkillList = new ConcurrentDictionary<int, SkillCooldownInfo>();
        foreach (SkillCooldownInfo skillCooldown in skillCooldownInfos) {
            newSkillList.TryAdd(skillCooldown.SkillId, skillCooldown);
        }

        skillCooldowns[characterId] = newSkillList;

        var held = new ConcurrentDictionary<int, SkillCooldownInfo>();
        foreach (SkillCooldownInfo info in pvpHeld) {
            held.TryAdd(info.SkillId, info);
        }
        pvpHolds[characterId] = (held, pvpStay);

        // Save death (a save without one has none)
        deaths[characterId] = death ?? new DeathInfo();
    }

    // Fills the memory from the store for a character this World has nothing for (none saved since it started), or
    // whose stored row was not read before. A character with no row is remembered as empty, so the next login does
    // not read again. The read runs out of the lock: a slow db holds up no save.
    private void LoadIfUnknown(long characterId) {
        if (store == null || (skillCooldowns.ContainsKey(characterId) && !unread.ContainsKey(characterId))) {
            return;
        }
        PlayerConfigSave? save = null;
        bool read = clockMs() >= Interlocked.Read(ref loadRetryAt);
        if (read) {
            try {
                save = store.Load(characterId);
            } catch (Exception ex) {
                read = false;
                Interlocked.Exchange(ref loadRetryAt, clockMs() + LoadRetryMs);
                Logger.Warning(ex, "Player config of Character:{CharacterId} not read from the db; not read again for {Seconds} s",
                    characterId, LoadRetryMs / 1000);
            }
        }
        lock (LockOf(characterId)) {
            // A save of a read character that came in during the read wins over the stored row.
            if (skillCooldowns.ContainsKey(characterId) && !unread.ContainsKey(characterId)) {
                return;
            }
            if (!read) {
                unread.TryAdd(characterId, 0);
                return;
            }
            unread.TryRemove(characterId, out _);
            if (save == null) {
                skillCooldowns.TryAdd(characterId, []);
                return;
            }
            Keep(save.Buffs.Select(ToInfo), save.SkillCooldowns.Select(ToInfo),
                new DeathInfo { Count = save.DeathCount, MsRemaining = save.DeathMsRemaining, StopTime = save.DeathStopTime },
                characterId, save.PvpHeldCooldowns.Select(ToInfo), save.PvpStay);
        }
    }

    internal static PlayerConfigSave ToSave(IEnumerable<BuffInfo> saveBuffs, IEnumerable<SkillCooldownInfo> skillCooldownInfos, DeathInfo? death,
                                            IEnumerable<SkillCooldownInfo> pvpHeld, bool pvpStay) => new() {
        Buffs = saveBuffs.Select(buff => new PlayerConfigBuff {
            Id = buff.Id,
            Level = buff.Level,
            MsRemaining = buff.MsRemaining,
            StopTime = buff.StopTime,
            Enabled = buff.Enabled,
            Stacks = buff.Stacks,
        }).ToList(),
        SkillCooldowns = skillCooldownInfos.Select(ToCooldown).ToList(),
        PvpHeldCooldowns = pvpHeld.Select(ToCooldown).ToList(),
        PvpStay = pvpStay,
        DeathCount = death?.Count ?? 0,
        DeathMsRemaining = death?.MsRemaining ?? 0,
        DeathStopTime = death?.StopTime ?? 0,
    };

    private static PlayerConfigCooldown ToCooldown(SkillCooldownInfo info) => new() {
        SkillId = info.SkillId,
        SkillLevel = info.SkillLevel,
        GroupId = info.GroupId,
        MsRemaining = info.MsRemaining,
        StopTime = info.StopTime,
        Charges = info.Charges,
        RechargeMs = info.RechargeMs,
    };

    private static BuffInfo ToInfo(PlayerConfigBuff buff) => new() {
        Id = buff.Id,
        Level = buff.Level,
        MsRemaining = buff.MsRemaining,
        StopTime = buff.StopTime,
        Enabled = buff.Enabled,
        Stacks = buff.Stacks,
    };

    private static SkillCooldownInfo ToInfo(PlayerConfigCooldown cooldown) => new() {
        SkillId = cooldown.SkillId,
        SkillLevel = cooldown.SkillLevel,
        GroupId = cooldown.GroupId,
        MsRemaining = cooldown.MsRemaining,
        StopTime = cooldown.StopTime,
        Charges = cooldown.Charges,
        RechargeMs = cooldown.RechargeMs,
    };

    public (List<BuffInfo> Buffs, List<SkillCooldownInfo> SkillCooldowns, DeathInfo Death) Retrieve(long characterId) =>
        Retrieve(characterId, DateTime.Now.ToEpochSeconds());

    internal (List<BuffInfo> Buffs, List<SkillCooldownInfo> SkillCooldowns, DeathInfo Death) Retrieve(long characterId, long currentTime) {
        LoadIfUnknown(characterId);
        List<BuffInfo> buffs = RetrieveBuffs(characterId, currentTime);
        List<SkillCooldownInfo> skillCooldowns = RetrieveSkillCooldowns(characterId, currentTime);
        DeathInfo death = RetrieveDeath(characterId, currentTime);
        return (buffs, skillCooldowns, death);
    }

    // The town cooldowns held through the character's PvP-zone stay, run down by the time away like skill_cooldowns,
    // and whether the stay was still on at the save. Nothing held (a World restart lost it) is an empty list.
    public (List<SkillCooldownInfo> Held, bool Stay) RetrievePvpHold(long characterId) =>
        RetrievePvpHold(characterId, DateTime.Now.ToEpochSeconds());

    internal (List<SkillCooldownInfo> Held, bool Stay) RetrievePvpHold(long characterId, long currentTime) {
        LoadIfUnknown(characterId);
        if (!pvpHolds.TryGetValue(characterId, out (ConcurrentDictionary<int, SkillCooldownInfo> Held, bool Stay) hold)) {
            return ([], false);
        }
        return (SettleCooldowns(hold.Held, currentTime), hold.Stay);
    }

    private List<BuffInfo> RetrieveBuffs(long characterId, long currentTime) {
        if (!buffs.TryGetValue(characterId, out ConcurrentDictionary<int, BuffInfo>? list)) {
            return [];
        }

        var removeBuffs = new List<BuffInfo>();
        foreach ((int buffId, BuffInfo buffInfo) in list) {
            bool? inGameTime = buffUsesGameTime(buffInfo.Id, (short) buffInfo.Level);
            if (inGameTime == null) {
                // An effect the data no longer has (a re-ingest since the save) is dropped, not sent.
                Logger.Warning("Saved buff {BuffId} level {Level} not in the data; dropped", buffInfo.Id, buffInfo.Level);
                removeBuffs.Add(buffInfo);
                continue;
            }

            if (inGameTime == false) {
                long msSurpassed = (long) ((currentTime - buffInfo.StopTime) * TimeSpan.FromSeconds(1).TotalMilliseconds);
                if (msSurpassed > buffInfo.MsRemaining) {
                    removeBuffs.Add(buffInfo);
                } else {
                    buffInfo.MsRemaining -= (int) msSurpassed;
                    // Run down from now on, so a second read does not take the same time off again.
                    buffInfo.StopTime = currentTime;
                }
            }
        }

        foreach (BuffInfo buffInfo in removeBuffs) {
            list.Remove(buffInfo.Id, out _);
        }

        return list.Values.ToList();
    }

    private List<SkillCooldownInfo> RetrieveSkillCooldowns(long characterId, long currentTime) {
        if (!skillCooldowns.TryGetValue(characterId, out ConcurrentDictionary<int, SkillCooldownInfo>? list)) {
            return [];
        }
        return SettleCooldowns(list, currentTime);
    }

    private List<SkillCooldownInfo> SettleCooldowns(ConcurrentDictionary<int, SkillCooldownInfo> list, long currentTime) {
        var removeSkillCooldowns = new List<SkillCooldownInfo>();
        foreach ((int skillId, SkillCooldownInfo skillCooldownInfo) in list) {
            bool? inGameTime = usesGameTime(skillCooldownInfo.SkillId, (short) skillCooldownInfo.SkillLevel);
            if (inGameTime == null) {
                Logger.Warning("Saved cooldown of skill {SkillId} level {Level} not in the data; dropped", skillCooldownInfo.SkillId, skillCooldownInfo.SkillLevel);
                removeSkillCooldowns.Add(skillCooldownInfo);
                continue;
            }
            if (inGameTime == false) {
                long msSurpassed = (long) ((currentTime - skillCooldownInfo.StopTime) * TimeSpan.FromSeconds(1).TotalMilliseconds);
                // A skill with recharge uses gets one use back each time its cooldown ends; the next one then runs
                // its length again (the game server's SkillCastRules.SettleRecharge).
                (long remaining, int charges) = SettleRecharge(skillCooldownInfo.MsRemaining - msSurpassed, skillCooldownInfo.Charges, skillCooldownInfo.RechargeMs);
                skillCooldownInfo.Charges = charges;
                if (remaining < 0) {
                    removeSkillCooldowns.Add(skillCooldownInfo);
                } else {
                    skillCooldownInfo.MsRemaining = (int) remaining;
                    skillCooldownInfo.StopTime = currentTime;
                }
            }
        }

        foreach (SkillCooldownInfo skillCooldownInfo in removeSkillCooldowns) {
            list.Remove(skillCooldownInfo.SkillId, out _);
        }

        return list.Values.ToList();
    }

    // The cooldown left of a skill with recharge uses after the time away (remaining under 0 once its cooldown has
    // ended): each ended cooldown gives one use back and the next runs its length again, down to the last use.
    public static (long Remaining, int Charges) SettleRecharge(long remaining, int charges, long rechargeMs) {
        while (remaining < 0 && charges > 1 && rechargeMs > 0) {
            charges--;
            remaining += rechargeMs;
        }
        return (remaining, charges);
    }

    private DeathInfo RetrieveDeath(long characterId, long currentTime) {
        if (!deaths.TryGetValue(characterId, out DeathInfo? death)) {
            return new DeathInfo();
        }

        long msSurpassed = (long) ((currentTime - death.StopTime) * TimeSpan.FromSeconds(1).TotalMilliseconds);
        if (msSurpassed > death.MsRemaining) {
            deaths.Remove(characterId, out _);
            return new DeathInfo();
        }

        death.MsRemaining -= (int) msSurpassed;
        death.StopTime = currentTime;
        if (death.MsRemaining <= 0) {
            deaths.Remove(characterId, out _);
            return new DeathInfo();
        }

        return death;
    }

}
