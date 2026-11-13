using System.Collections.Concurrent;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Metadata;
using Maple2.Server.World.Service;

namespace Maple2.Server.World.Containers;

public class PlayerConfigLookUp {
    private readonly SkillMetadataStorage skillMetadataStorage;
    // Whether a skill's cooldown runs on in-game time only (its stateAttr useInGameTime); throws for an unknown skill.
    private readonly Func<int, short, bool> usesGameTime;

    private readonly ConcurrentDictionary<long, ConcurrentDictionary<int, BuffInfo>> buffs;
    private readonly ConcurrentDictionary<long, ConcurrentDictionary<int, SkillCooldownInfo>> skillCooldowns;
    private readonly ConcurrentDictionary<long, DeathInfo> deaths;
    // The town cooldowns held through a PvP-zone stay (the game server's PvpCooldownHold) and whether the stay runs.
    private readonly ConcurrentDictionary<long, (ConcurrentDictionary<int, SkillCooldownInfo> Held, bool Stay)> pvpHolds = [];

    public PlayerConfigLookUp(SkillMetadataStorage skillMetadataStorage) : this(skillMetadataStorage, (id, level) =>
        skillMetadataStorage.TryGet(id, level, out SkillMetadata? skill) ? skill.State.UseInGameTime : throw new InvalidDataException($"Skill not found: {id}")) { }

    internal PlayerConfigLookUp(SkillMetadataStorage skillMetadataStorage, Func<int, short, bool> usesGameTime) {
        this.skillMetadataStorage = skillMetadataStorage;
        this.usesGameTime = usesGameTime;
        buffs = [];
        skillCooldowns = [];
        deaths = [];
    }

    public void Save(List<BuffInfo> saveBuffs, List<SkillCooldownInfo> skillCooldownInfos, DeathInfo death, long characterId,
                     List<SkillCooldownInfo>? pvpHeld = null, bool pvpStay = false) {
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
        foreach (SkillCooldownInfo info in pvpHeld ?? []) {
            held.TryAdd(info.SkillId, info);
        }
        pvpHolds[characterId] = (held, pvpStay);

        // Save death
        if (!deaths.TryGetValue(characterId, out DeathInfo? _)) {
            deaths.TryAdd(characterId, death);
        } else {
            deaths[characterId] = death;
        }
    }

    public (List<BuffInfo> Buffs, List<SkillCooldownInfo> SkillCooldowns, DeathInfo Death) Retrieve(long characterId) {
        long currentTime = DateTime.Now.ToEpochSeconds();
        List<BuffInfo> buffs = RetrieveBuffs(characterId, currentTime);
        List<SkillCooldownInfo> skillCooldowns = RetrieveSkillCooldowns(characterId, currentTime);
        DeathInfo death = RetrieveDeath(characterId);
        return (buffs, skillCooldowns, death);
    }

    // The town cooldowns held through the character's PvP-zone stay, run down by the time away like skill_cooldowns,
    // and whether the stay was still on at the save. Nothing held (a World restart lost it) is an empty list.
    public (List<SkillCooldownInfo> Held, bool Stay) RetrievePvpHold(long characterId) =>
        RetrievePvpHold(characterId, DateTime.Now.ToEpochSeconds());

    internal (List<SkillCooldownInfo> Held, bool Stay) RetrievePvpHold(long characterId, long currentTime) {
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
            if (!skillMetadataStorage.TryGetEffect(buffInfo.Id, (short) buffInfo.Level, out AdditionalEffectMetadata? metadata)) {
                throw new InvalidDataException($"Buff not found: {buffInfo.Id}, level: {buffInfo.Level}");
            }

            if (!metadata.Property.UseInGameTime) {
                long msSurpassed = (long) ((currentTime - buffInfo.StopTime) * TimeSpan.FromSeconds(1).TotalMilliseconds);
                if (msSurpassed > buffInfo.MsRemaining) {
                    removeBuffs.Add(buffInfo);
                } else {
                    buffInfo.MsRemaining -= (int) msSurpassed;
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
            if (!usesGameTime(skillCooldownInfo.SkillId, (short) skillCooldownInfo.SkillLevel)) {
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

    private DeathInfo RetrieveDeath(long characterId) {
        if (!deaths.TryGetValue(characterId, out DeathInfo? death)) {
            return new DeathInfo();
        }

        long msSurpassed = (long) ((DateTime.Now.ToEpochSeconds() - death.StopTime) * TimeSpan.FromSeconds(1).TotalMilliseconds);
        if (msSurpassed > death.MsRemaining) {
            deaths.Remove(characterId, out _);
            return new DeathInfo();
        }

        death.MsRemaining -= (int) msSurpassed;
        if (death.MsRemaining <= 0) {
            deaths.Remove(characterId, out _);
            return new DeathInfo();
        }

        return death;
    }

}
