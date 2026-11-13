using System.Collections.Concurrent;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;
using Maple2.Server.World.Service;
using Maple2.Tools.Extensions;
using Serilog;

namespace Maple2.Server.Game.Manager;

public class BuffManager : IUpdatable {
    #region ObjectId
    private int idCounter;

    /// <summary>
    /// Generates an ObjectId unique to this specific actor instance.
    /// </summary>
    /// <returns>Returns a local ObjectId</returns>
    private int NextLocalId() => Interlocked.Increment(ref idCounter);
    #endregion
    public IActor Actor { get; private set; }
    private readonly ConcurrentDictionary<int, List<Buff>> buffs = [];
    public ConcurrentDictionary<int, long> CooldownTimes { get; } = new(); // TODO: Cache this
    public ReflectRecord? Reflect;
    private readonly ILogger logger = Log.ForContext<BuffManager>();

    public BuffManager(IActor actor) {
        Actor = actor;
    }

    public void Initialize() {
        // Load buffs that are not broadcasted to the field
        if (Actor is FieldPlayer player) {
            player.Session.Config.Skill.UpdatePassiveBuffs(false);
            player.Stats.Refresh();
        }
    }

    public void ResetActor(IActor actor) {
        Actor = actor;
        foreach ((int id, List<Buff> buffDict) in buffs) {
            foreach (Buff buff in buffDict) {
                buff.ResetActor(actor);
            }
        }
    }

    public void Clear() {
        foreach (Buff buff in EnumerateBuffs()) {
            Remove(buff.Id, buff.Caster.ObjectId);
        }
    }

    public void LoadFieldBuffs() {
        // Game Events: the only event kind whose client record names an effect is the burning level-up
        // (BurningLevelupContext, gameevent.h:213), and the server has no record for that kind.
        EnterField();
        if (Actor is FieldPlayer player) {
            RefreshPrestigeBuffs();
            player.Session.Config.RefreshLapenshardBuffs();
            player.Session.Config.RefreshCharacterAbilityBuffs();
            player.Session.Config.RefreshPremiumClubBuffs();
            player.Session.Guild.LoadSkillBuffs();

            // The worn insignia's effect: picking the insignia gives it (InsigniaHandler), but a login
            // can arrive without it (seen after a server restart), and the client does not ask again at login.
            player.Session.TableMetadata.InsigniaTable.Entries.TryGetValue(player.Value.Character.Insignia, out InsigniaTable.Entry? insignia);
            (int insigniaBuffId, short insigniaBuffLevel) = InsigniaRules.EntryBuff(insignia,
                insignia != null && InsigniaRules.Shown(player.Session, insignia),
                insignia != null && HasBuff(insignia.BuffId, insignia.BuffLevel));
            if (insigniaBuffId > 0) {
                AddBuff(Actor, Actor, insigniaBuffId, insigniaBuffLevel, Actor.Field.FieldTick);
            }
        }
    }

    public void AddBuff(IActor caster, IActor owner, int id, short level, long startTick, int stacks = 0, int durationMs = -1, bool notifyField = true, EventConditionType type = EventConditionType.Activate) {
        if (!owner.Field.SkillMetadata.TryGetEffect(id, level, out AdditionalEffectMetadata? additionalEffect)) {
            logger.Error("Invalid buff: {SkillId},{Level}", id, level);
            return;
        }

        if (type != EventConditionType.Activate && CooldownTimes.TryGetValue(id, out long cooldownTick) && startTick < cooldownTick) {
            return;
        }

        if (durationMs < 0) {
            durationMs = additionalEffect.Property.DurationTick;
        }

        // The caster's invokes on this effect lengthen it: client type 3 by a rate, type 4 by milliseconds (both
        // IncreaseEffectDuration here). Chosen here: the caster's buffs count, as both the invoke and the effect it
        // names are of the caster's job skills (10400281 lengthens 10400182, both priest effects). Before, the holder's invokes on a skill of the effect's id counted,
        // which no duration invoke is, so none counted.
        (int invokeValue, float invokeRate) = caster.Buffs.GetEffectInvokeValues(InvokeEffectType.IncreaseEffectDuration, id, additionalEffect.Property.InvokeGroupId);
        durationMs = Math.Max(0, invokeValue + (int) ((1 + invokeRate) * durationMs));
        if (Actor is FieldPlayer target) {
            durationMs = Buff.ReducedDuration(additionalEffect.Property, durationMs,
                target.Stats.Values[SpecialAttribute.ReduceStun].Total, target.Stats.Values[SpecialAttribute.ReduceDebuff].Total);
        }

        stacks = Buff.AddStacks(stacks, additionalEffect.Property.MaxCount);

        // Check if immune to any present buffs
        if (!CheckImmunity(id, additionalEffect.Property.Category)) {
            return;
        }

        // Boom Boom Ocean: a buff the game scores (the pickups), each time a game player gets it.
        if (Actor is FieldPlayer arcadePlayer) {
            arcadePlayer.Field.BoomBoomOceanScored(arcadePlayer, id);
        }

        if (type != EventConditionType.Activate) {
            CooldownTimes[id] = startTick + additionalEffect.Property.CooldownTime;
        }
        Buff? existing = GetBuff(id, additionalEffect.Property.CasterIndividualBuff ? caster.ObjectId : 0);
        long endTick = startTick + durationMs;

        switch (additionalEffect.Property.ResetCondition) {
            case BuffResetCondition.ResetEndTick:
            case BuffResetCondition.Reset2: // This isn't correct, but it seems to behave VERY close to ResetEndTick
                // endTick = startTick + durationMs;
                break;
            case BuffResetCondition.PersistEndTick:
                if (existing != null) {
                    endTick = existing.EndTick;
                }
                break;
            case BuffResetCondition.Replace:
                if (existing != null) {
                    Remove(existing.Id, existing.Caster.ObjectId);
                    existing = null;
                }
                //endTick = startTick + durationMs;
                break;
        }
        if (existing != null) {
            if (existing.Level < level) {
                // Technically in sniffs it should just update the buff level. This should still be the same?
                Remove(existing.Id, existing.Caster.ObjectId);
            } else {
                bool timeChanged = Buff.RestartsOnReapply(additionalEffect.Property, durationMs)
                    ? existing.Refresh(startTick, endTick)
                    : existing.UpdateEndTime(endTick);
                bool stacked = existing.Stack(stacks);
                if (timeChanged | stacked) {
                    if (notifyField) {
                        owner.Field.Broadcast(BuffPacket.Update(existing));
                    }
                    TriggerEvent(Actor, Actor, Actor, EventConditionType.OnEffectApplied, skillId: existing.Id);
                }
                return;
            }
        }

        // Remove existing buff if it's in the same group
        if (additionalEffect.Property.Group > 0) {
            foreach (Buff existingBuff in EnumerateBuffs().Where(b => b.Metadata.Property.Group == additionalEffect.Property.Group)) {
                Remove(existingBuff.Id, existingBuff.Caster.ObjectId);
            }
        }

        var buff = new Buff(additionalEffect, NextLocalId(), caster, owner, startTick, endTick, stacks);

        if (!TryAdd(buff)) {
            logger.Error("Could not add buff {Id} to {Object}", buff.Id, Actor.ObjectId);
            return;
        }

        // A player gaining a buff: the GainBuff missions of a dungeon room.
        if (Actor is FieldPlayer missionPlayer && Actor.Field is DungeonFieldManager dungeonField) {
            dungeonField.CountMissions(missionPlayer, mission => mission.AddBuff(id));
        }

        // Set Reflect if applicable
        SetReflect(buff);
        SetShield(buff);
        SetUpdates(buff);
        ModifyBuffStackCount(buff);

        // The buff's constructor took its enabled flag from the condition; it keeps its procs for when it is enabled.
        if (!buff.Enabled) {
            InvalidateSums();
        }

        // refresh stats if needed
        if (HasStatus(buff.Metadata)) {
            Actor.Stats.Refresh();
        }

        logger.Verbose("{Id} AddBuff to {ObjectId}: {SkillId},{Level} for {Tick}ms", buff.ObjectId, owner.ObjectId, id, level, buff.EndTick - buff.StartTick);
        if (owner is FieldPlayer player) {
            if (additionalEffect.Property.Exp > 0) { // Assuming this doesnt proc s
                player.Session.Exp.AddStaticExp(additionalEffect.Property.Exp);
            }

            player.Session.ConditionUpdate(ConditionType.buff, codeLong: buff.Id);
            player.Session.Dungeon.UpdateDungeonEnterLimit();
        }
        if (notifyField) {
            owner.Field.Broadcast(BuffPacket.Add(buff));
        }
        SetMount(buff);
    }

    private Buff? GetBuff(int buffId, int casterObjectId = 0) {
        if (casterObjectId > 0) {
            if (this.buffs.TryGetValue(buffId, out List<Buff>? buffs)) {
                return buffs.FirstOrDefault(buff => buff.Caster.ObjectId == casterObjectId);
            }
        } else {
            if (this.buffs.TryGetValue(buffId, out List<Buff>? buffs)) {
                return buffs.FirstOrDefault();
            }
        }
        return null;
    }

    private bool TryAdd(Buff buff) {
        if (this.buffs.TryGetValue(buff.Id, out List<Buff>? buffs)) {
            Buff? casterBuff = buffs.FirstOrDefault(b => b.Caster.ObjectId == buff.Caster.ObjectId);
            if (casterBuff != null) {
                return false;
            }
            buffs.Add(buff);
            InvalidateSums();
            return true;
        }

        bool added = this.buffs.TryAdd(buff.Id, [buff]);
        InvalidateSums();
        return added;
    }

    public List<Buff> EnumerateBuffs() => buffs.Values.SelectMany(list => list).ToList();
    public List<Buff> EnumerateBuffs(int buffId) => this.buffs.TryGetValue(buffId, out List<Buff>? buffs) ? buffs.ToList() : [];

    public bool HasBuff(int effectId, short effectLevel = 1, int stacks = 0) {
        List<Buff> buffs = EnumerateBuffs(effectId);
        if (buffs.Count == 0) {
            return false;
        }

        foreach (Buff buff in buffs) {
            if (stacks != 0 && buff.Stacks < stacks) {
                continue;
            }

            if (effectLevel != 0 && buff.Level < effectLevel) {
                continue;
            }

            return true;
        }
        return false;
    }

    public bool HasBuff(BuffEventType eventType) {
        return EnumerateBuffs().Any(buff => buff.Metadata.Property.EventType == eventType);
    }

    private void SetReflect(Buff buff) {
        if (buff.Metadata.Reflect.EffectId == 0 || !Actor.Field.SkillMetadata.TryGetEffect(buff.Metadata.Reflect.EffectId, buff.Metadata.Reflect.EffectLevel,
            out AdditionalEffectMetadata? _)) {
            return;
        }

        // Does this get overwritten if a new reflect is applied?
        var record = new ReflectRecord(buff.Id, buff.Metadata.Reflect);
        Reflect = record;
    }


    // The sums of the enabled buffs only: the client's resistance, invincible, stun and invoke sums skip a buff
    // without m_bEnabled (AdditionalCliList::FUN_140972a00, FUN_1409713b0, CollectInvokeEffectStats).
    public static BuffSums SumsOf(IEnumerable<Buff> held) {
        return new BuffSums(held.Where(buff => buff.Enabled).Select(buff => (buff.Metadata, buff.Stacks)));
    }

    // A held buff's begin condition began or stopped holding (Buff.UpdateEnabled): the sums and the stat totals
    // follow it, as they do a stack change.
    public void EnabledChanged(Buff buff) {
        InvalidateSums();
        if (HasStatus(buff.Metadata)) {
            Actor.Stats.Refresh();
        }
    }

    // A buff's stack count changed (Buff.Stack calls this): the owner's stat totals follow it, and the invoke and
    // resistance sums are taken again at the next read.
    public void StacksChanged(Buff buff) {
        InvalidateSums();
        if (HasStatus(buff.Metadata)) {
            Actor.Stats.Refresh();
        }
    }

    // The buff changes its holder's stats: by its own status, or by an invoke that raises the damage of another buff
    // (StatsManager.InvokeDamageBonus).
    internal static bool HasStatus(AdditionalEffectMetadata metadata) {
        return metadata.Status.Values.Count > 0 || metadata.Status.Rates.Count > 0
            || metadata.Status.SpecialValues.Count > 0 || metadata.Status.SpecialRates.Count > 0
            || metadata.InvokeEffect?.Types.Any(type => type is InvokeEffectType.IncreaseOffensivePhysicalDamage or InvokeEffectType.IncreaseOffensiveMagicalDamage) == true;
    }

    // The compulsion rate of a type (block, evasion or critical override) over the enabled buffs held, each buff id once,
    // those naming skills only for one of them. The client never reads these rates (they are loaded with the status,
    // additionalactionadditional.cpp:1223), so the server chooses: a disabled buff gives none, as in the client's other
    // sums. Before, a buff's rate counted from its add until a removal by id, enabled or not, and stayed after an expiry.
    public float TotalCompulsionRate(BuffCompulsionEventType type, int skillId = 0) => CompulsionRate(EnumerateBuffs(), type, skillId);

    public static float CompulsionRate(IEnumerable<Buff> held, BuffCompulsionEventType type, int skillId = 0) {
        return held.Where(buff => buff.Enabled && buff.Metadata.Status.Compulsion?.Type == type)
            .DistinctBy(buff => buff.Id)
            .Select(buff => buff.Metadata.Status.Compulsion!)
            .Where(compulsion => skillId == 0 || compulsion.SkillIds.Length == 0 || compulsion.SkillIds.Contains(skillId))
            .Sum(compulsion => compulsion.Rate);
    }

    // A revive in place is refused while an enabled buff has disableRevivalHere: the client takes the flag from enabled
    // buffs only (AdditionalCliList::FUN_140972a00, additionalcli_2.cpp:1065).
    public static bool BlocksRevival(IEnumerable<Buff> held) {
        return RevivalRules.BuffBlocks(held.Where(buff => buff.Enabled).Select(buff => buff.Metadata.Property.DisableRevivalHere));
    }

    // The shields that take a hit, in the order held: an enabled buff's only, as the client's shield total
    // (AdditionalCliList::GetTotalShieldValue) skips a buff without m_bEnabled.
    public static IEnumerable<Buff> Shields(IEnumerable<Buff> held) {
        return held.Where(buff => buff.Enabled && buff.Metadata.Shield != null);
    }

    // The sums are taken from the buffs held on the first read after a change (BuffSums). The version keeps a sum
    // taken while a buff changed from being stored.
    private BuffSums? sums;
    private int sumsVersion;

    // A buff with the invincible flag is held: skill hits on the actor are sent as Invincible with amount 0 and a
    // damage over time tick as 0.
    public bool Invincible => Sums().Invincible;
    // A buff that stuns a monster is held.
    public bool Stunned => Sums().Stun;
    // An enabled buff held has deathResistanceHP: hits and dot ticks take the holder no lower than 1 health.
    public bool DeathResistance => Sums().DeathResistance;
    // The type of that hit: InvincibleReflection when the buff's invincible value is 2.
    public DamageType InvincibleHitType => Sums().InvincibleReflect ? DamageType.InvincibleReflection : DamageType.Invincible;

    private void InvalidateSums() {
        Interlocked.Increment(ref sumsVersion);
        Volatile.Write(ref sums, null);
    }

    private BuffSums Sums() {
        BuffSums? current = Volatile.Read(ref sums);
        if (current != null) {
            return current;
        }
        int version = Volatile.Read(ref sumsVersion);
        current = SumsOf(EnumerateBuffs());
        if (version == Volatile.Read(ref sumsVersion)) {
            Volatile.Write(ref sums, current);
        }
        return current;
    }

    // The consume spirit rates of the enabled buffs held, each times its stack count: -1 makes casts free.
    public float ConsumeSpRate => EnumerateBuffs().Where(buff => buff.Enabled).Sum(buff => buff.Metadata.Consume.SpRate * buff.Stacks);

    // An enabled buff held lists the skill among the ones its holder cannot cast.
    public bool SkillUseDisabled(int skillId) {
        return Util.SkillCastRules.DisabledByBuff(EnumerateBuffs().Where(buff => buff.Enabled).Select(buff => buff.Metadata.Property.SkillUseDisableIds), skillId);
    }

    // The skillUseDisable main-type masks of the enabled buffs held, OR'd (AdditionalCliList::FUN_140972a00).
    public static int SkillUseDisableMask(IEnumerable<Buff> held) {
        int mask = 0;
        foreach (Buff buff in held) {
            if (buff.Enabled) {
                mask |= buff.Metadata.Property.SkillUseDisableMask;
            }
        }
        return mask;
    }

    public int SkillUseDisableMask() => SkillUseDisableMask(EnumerateBuffs());

    // The share the enabled buffs held cut from the cooldowns of a cooldown group (SkillCastRules.GroupReduceRate).
    public float CooldownGroupRate(int groupId) {
        return Util.SkillCastRules.GroupReduceRate(EnumerateBuffs().Where(buff => buff.Enabled).Select(buff => buff.Metadata.Property.CooldownGroupRates), groupId);
    }

    // The resistance rates of the buffs held, each times its stack count (Buff.StackFactor).
    public float GetResistance(BasicAttribute attribute) => Sums().Resistance(attribute);

    // The damage taken rate of the buffs held for physical (true) or magical (false) hits.
    public float DamageTakenRate(bool physical) => physical ? Sums().PhysicalDamageTaken : Sums().MagicalDamageTaken;

    // The recovery rate of the buffs held for potion heals (true) or other heals (false).
    public float RecoveryRate(bool item) => item ? Sums().ItemRecovery : Sums().HealRecovery;

    public (int, float) GetInvokeValues(InvokeEffectType invokeType, int skillId, params int[] skillGroup) {
        return Sums().Invokes(invokeType, skillId, skillGroup);
    }

    public (int, float) GetEffectInvokeValues(InvokeEffectType invokeType, int effectId, int effectGroupId) {
        return Sums().EffectInvokes(invokeType, effectId, effectGroupId);
    }

    // The invoke values and rates of a type for a skill, summed over every buff held, each times its stack count
    // (Buff.StackFactor), as the client sums them (additionalcli_2.cpp:938). The same buff from two casters counts
    // twice, and a removed buff leaves nothing behind. An invoke is keyed on the first of skillID, effectID,
    // skillGroupID and effectGroupID it has (CollectInvokeEffectStats, additionalcli_2.cpp:851): one with an effectID
    // and no skillID is an effect's, and counts for no skill.
    public static (int, float) SumInvokes(IEnumerable<(AdditionalEffectMetadataInvokeEffect? Invoke, int Stacks)> held,
            InvokeEffectType invokeType, int skillId, params int[] skillGroup) {
        return Sum(held, invokeType, meta => meta.SkillId != 0 ? meta.SkillId == skillId
            : meta.EffectId == 0 && meta.SkillGroupId != 0 && skillGroup.Contains(meta.SkillGroupId));
    }

    // The invoke values and rates of a type for an additional effect: the invokes keyed on its id (effectID) or on
    // its effect group (effectGroupID, the played client's groupIDs: AdditionalEffectMetadataProperty.InvokeGroupId),
    // summed as SumInvokes does. The client counts these for its own display only (dpsmetersystem.cpp:1145), so what
    // each type changes is chosen here (BuffManager.AddBuff, Buff.ApplyRecovery, StatsManager.AddBuffs).
    public static (int, float) SumEffectInvokes(IEnumerable<(AdditionalEffectMetadataInvokeEffect? Invoke, int Stacks)> held,
            InvokeEffectType invokeType, int effectId, int effectGroupId) {
        return Sum(held, invokeType, meta => EffectKeyed(meta, effectId, effectGroupId));
    }

    // Whether an invoke is an effect's and names this effect (its id, or its effect group when it has one).
    public static bool EffectKeyed(AdditionalEffectMetadataInvokeEffect meta, int effectId, int effectGroupId) {
        if (meta.SkillId != 0) {
            return false;
        }
        if (meta.EffectId != 0) {
            return meta.EffectId == effectId;
        }
        return meta.SkillGroupId == 0 && meta.EffectGroupId != 0 && meta.EffectGroupId == effectGroupId;
    }

    private static (int, float) Sum(IEnumerable<(AdditionalEffectMetadataInvokeEffect? Invoke, int Stacks)> held,
            InvokeEffectType invokeType, Func<AdditionalEffectMetadataInvokeEffect, bool> keyed) {
        float value = 0, rate = 0;
        foreach ((AdditionalEffectMetadataInvokeEffect? meta, int stacks) in held) {
            if (meta == null || !keyed(meta)) {
                continue;
            }
            for (int i = 0; i < meta.Types.Length; i++) {
                if (meta.Types[i] == invokeType) {
                    value += Buff.ByStacks(meta.Values[i], stacks);
                    rate += Buff.ByStacks(meta.Rates[i], stacks);
                }
            }
        }

        return ((int) value, rate);
    }

    private void SetShield(Buff buff) {
        if (buff.Metadata.Shield == null) {
            return;
        }

        if (buff.Metadata.Shield.HpValue > 0) {
            buff.ShieldHealth = buff.Metadata.Shield.HpValue;
        } else if (buff.Metadata.Shield.HpByTargetMaxHp > 0f) {
            buff.ShieldHealth = (long) (Actor.Stats.Values[BasicAttribute.Health].Total * buff.Metadata.Shield.HpByTargetMaxHp);
        }
    }

    private void ModifyBuffStackCount(Buff buff) {
        if (buff.Metadata.ModifyOverlapCount.Length <= 0) {
            return;
        }
        foreach (AdditionalEffectMetadataModifyOverlapCount modifyOverlapCount in buff.Metadata.ModifyOverlapCount) {
            foreach (Buff buffResult in EnumerateBuffs(modifyOverlapCount.Id).Where(b => b.Stack(modifyOverlapCount.OffsetCount))) {
                if (buffResult.Stacks <= 0) {
                    Remove(buffResult.Id, buffResult.Caster.ObjectId);
                    continue;
                }
                Actor.Field.Broadcast(BuffPacket.Update(buffResult));
            }
        }
    }

    private void SetMount(Buff buff) {
        if (buff.Metadata.Property.RideId == 0 || Actor is not FieldPlayer player) {
            return;
        }

        if (!player.Session.Ride.Mount(buff.Metadata)) {
            logger.Error("Failed to mount {Id} on {Object}", buff.Id, Actor.ObjectId);
            Remove(buff.Id, Actor.ObjectId);
        }
    }

    private void SetUpdates(Buff buff) {
        if (buff.Metadata.Update.Cancel != null) {
            CancelBuffs(buff, buff.Metadata.Update.Cancel);
        }

        // Reset skill cooldowns
        if (buff.Metadata.Update.ResetCooldown.Length > 0 && Actor is FieldPlayer player) {
            foreach (int skillId in buff.Metadata.Update.ResetCooldown) {
                player.Session.Config.SetSkillCooldown(skillId);
            }
        }
    }
    public void TriggerEvent(IActor caster, IActor owner, IActor target, EventConditionType type, int skillId = 0, int buffId = 0) {
        foreach (Buff buff in EnumerateBuffs()) {
            if (!buff.Enabled) {
                continue;
            }
            owner.ApplyEffects(buff.Metadata.Skills, caster, owner, type, skillId, buffId, target);
        }
    }

    private void CancelBuffs(Buff buff, AdditionalEffectMetadataUpdate.CancelEffect cancel) {
        List<(int buffId, int casterId)> buffsToRemove = [];
        foreach (int cancelId in cancel.Ids) {
            foreach (Buff buffResult in EnumerateBuffs(cancelId)) {
                if (cancel.CheckSameCaster) {
                    if (buffResult.Caster != buff.Caster) {
                        continue;
                    }
                }
                buffsToRemove.Add((buffResult.Id, buffResult.Caster.ObjectId));
            }
        }

        foreach (BuffCategory category in cancel.Categories) {
            foreach (Buff cancelCategoryBuff in EnumerateBuffs().Where(b => b.Metadata.Property.Category == category)) {
                buffsToRemove.Add((cancelCategoryBuff.Id, cancelCategoryBuff.Caster.ObjectId));
            }
        }
        Remove(buffsToRemove.ToArray());
    }

    public void ModifyDuration(int buffId, int modifyValue) {
        foreach (Buff buff in EnumerateBuffs(buffId)) {
            buff.UpdateEndTime(modifyValue);
        }
    }

    public virtual void Update(long tickCount) {
        foreach (Buff buff in EnumerateBuffs()) {
            buff.Update(tickCount);
        }
    }

    public void LeaveField() {
        if (Actor.Field == null) {
            return;
        }
        List<(int id, int casterId)> buffsToRemove = [];
        foreach (MapEntranceBuff buff in Actor.Field.Metadata.EntranceBuffs) {
            buffsToRemove.Add((buff.Id, Actor.ObjectId));
        }
        foreach (Buff buff in EnumerateBuffs().Where(b => b.Metadata.Property.RemoveOnLeaveField)) {
            buffsToRemove.Add((buff.Id, buff.Caster.ObjectId));
        }
        Remove(buffsToRemove.ToArray());
    }

    private void EnterField() {
        foreach (MapEntranceBuff buff in Actor.Field.Metadata.EntranceBuffs) {
            AddBuff(Actor, Actor, buff.Id, buff.Level, Actor.Field.FieldTick);
        }

        if (Actor.Field.Metadata.Property.Type == MapType.Pvp) {
            List<(int id, int casterId)> buffsToRemove = [];
            foreach (Buff buff in EnumerateBuffs()) {
                if (buff.Metadata.Property.RemoveOnPvpZone) {
                    buffsToRemove.Add((buff.Id, buff.Caster.ObjectId));
                }

                if (!buff.Metadata.Property.KeepOnEnterPvpZone) {
                    buffsToRemove.Add((buff.Id, buff.Caster.ObjectId));
                }
            }
            Remove(buffsToRemove.ToArray());
        }
        // The effects that clear their cooldown there can come again at once (SkillCastRules.PvpZoneClears).
        foreach (int id in SkillCastRules.PvpZoneClears(Actor.Field.Metadata.Property.Type, CooldownTimes.Keys,
                     id => Actor.Field.SkillMetadata.TryGetEffect(id, 1, out AdditionalEffectMetadata? effect) && effect.Property.ClearCooldownOnPvpZone)) {
            CooldownTimes.TryRemove(id, out _);
        }

        if (Actor.Field.Metadata.Property.Region == MapRegion.ShadowWorld) {
            if (Actor is FieldPlayer player) {
                AddBuff(Actor, Actor, player.Session.ServerTableMetadata.ConstantsTable.shadowWorldBuffHpUp, 1, Actor.Field.FieldTick);
                AddBuff(Actor, Actor, player.Session.ServerTableMetadata.ConstantsTable.shadowWorldBuffMoveProtect, 1, Actor.Field.FieldTick);
            }
        }
    }

    // The prestige abilities the account's prestige level has reached: each is an effect whose level is the
    // ability's step (PrestigeRules.AbilityStep). The client's prestige window shows an ability as locked
    // until the character has the effect at that level.
    public void RefreshPrestigeBuffs() {
        if (Actor is not FieldPlayer player) {
            return;
        }

        int prestigeLevel = player.Session.Exp.PrestigeLevel;
        foreach (PrestigeLevelAbilityMetadata ability in player.Session.TableMetadata.PrestigeLevelAbilityTable.Entries.Values) {
            short step = PrestigeRules.AbilityStep(ability.Id, ability.RequiredLevel, ability.Interval, ability.MaxCount, prestigeLevel);
            if (step > 0 && ability.BuffId > 0 && !HasBuff(ability.BuffId, step)) {
                AddBuff(Actor, Actor, ability.BuffId, step, Actor.Field.FieldTick);
            }
        }
    }

    // The effects of a worn item or gem that its wearer does not hold yet. Every stat refresh applies the worn
    // items' effects again; an effect already held is left alone, so a refresh never adds a stack to it (168 item
    // and gem effects have a stack maximum above 1) nor restarts it.
    public static IEnumerable<ItemMetadataAdditionalEffect> ItemEffectsToAdd(IEnumerable<ItemMetadataAdditionalEffect> effects, Func<int, short, bool> held) {
        return effects.Where(effect => !held(effect.Id, effect.Level));
    }

    public void AddItemBuffs(Item item) {
        foreach (ItemMetadataAdditionalEffect buff in ItemEffectsToAdd(item.Metadata.AdditionalEffects, (id, level) => HasBuff(id, level))) {
            AddBuff(Actor, Actor, buff.Id, buff.Level, Actor.Field.FieldTick);
        }

        if (item.Socket != null) {
            foreach (ItemGemstone? gem in item.Socket.Sockets) {
                if (gem != null && Actor.Field.ItemMetadata.TryGet(gem.ItemId, out ItemMetadata? metadata)) {
                    foreach (ItemMetadataAdditionalEffect buff in ItemEffectsToAdd(metadata.AdditionalEffects, (id, level) => HasBuff(id, level))) {
                        AddBuff(Actor, Actor, buff.Id, buff.Level, Actor.Field.FieldTick);
                    }
                }
            }
        }
    }

    public void RemoveItemBuffs(Item item) {
        List<(int id, int casterId)> buffsToRemove = [];
        foreach (ItemMetadataAdditionalEffect buff in item.Metadata.AdditionalEffects) {
            buffsToRemove.Add((buff.Id, Actor.ObjectId));
        }

        if (item.Socket != null) {
            foreach (ItemGemstone? gem in item.Socket.Sockets) {
                if (gem != null && Actor.Field.ItemMetadata.TryGet(gem.ItemId, out ItemMetadata? metadata)) {
                    foreach (ItemMetadataAdditionalEffect buff in metadata.AdditionalEffects) {
                        buffsToRemove.Add((buff.Id, Actor.ObjectId));
                    }
                }
            }
        }

        Remove(buffsToRemove.ToArray());
    }

    public void SetCacheBuffs(IList<BuffInfo> cacheBuffs, long currentTick) {
        if (Actor is not FieldPlayer player) {
            return;
        }

        foreach (BuffInfo info in cacheBuffs) {
            if (!player.Field.SkillMetadata.TryGetEffect(info.Id, (short) info.Level, out AdditionalEffectMetadata? additionalEffect)) {
                logger.Error("Invalid buff: {SkillId},{Level}", info.Id, info.Level);
                continue;
            }

            if (additionalEffect.Property.UseInGameTime || info.MsRemaining > 0) {
                AddBuff(Actor, Actor, info.Id, (short) info.Level, currentTick, info.Stacks, info.MsRemaining);
            }
        }
    }

    public void Remove(params (int id, int casterId)[] buffIds) {
        foreach ((int id, int casterId) in buffIds) {
            Remove(id, casterId);
        }
    }

    // A buff held once per caster (casterIndividualBuff) is removed only for the caster named, so a removal that
    // means "this buff" (death, leaving the field, a cancel, a broken shield) names the buff's own caster.
    public static bool RemoveMatches(bool casterIndividual, int buffCasterId, int casterId) {
        return !casterIndividual || buffCasterId == casterId;
    }

    public bool Remove(int id, int casterId) {
        //TODO: Check if buff is removable/should be removed
        bool refreshStats = false;
        List<Buff> buffsToRemove = [];
        List<Buff> buffsEnum = EnumerateBuffs();
        foreach (Buff buff in buffsEnum) {
            if (buff.Id != id) {
                continue;
            }
            if (!RemoveMatches(buff.Metadata.Property.CasterIndividualBuff, buff.Caster.ObjectId, casterId)) {
                continue;
            }

            buffsToRemove.Add(buff);
            if (HasStatus(buff.Metadata)) {
                refreshStats = true;
            }
        }

        if (Reflect?.SourceBuffId == id) {
            Reflect = null;
        }

        foreach (Buff buff in buffsToRemove) {
            RemoveInternal(buff);
            Actor.Field.Broadcast(BuffPacket.Remove(buff));
            if (buff.Metadata.Property.RideId > 0) {
                refreshStats = true;
                if (Actor is FieldPlayer player && player.Session.Ride.Ride?.Action is RideOnActionBattle battle && battle.SkillId == buff.Id) {
                    player.Session.Ride.Dismount(RideOffType.AdditionalEffect);
                }
            }
            TriggerEvent(Actor, Actor, Actor, EventConditionType.OnEffectRemoved, buffId: buff.Id);
        }
        if (refreshStats) {
            Actor.Stats.Refresh();
        }

        return true;

        void RemoveInternal(Buff buffToRemove) {
            if (!buffs.TryGetValue(buffToRemove.Id, out List<Buff>? buffList)) {
                logger.Error("Buff {Id} doesn't exist in buffs {Buffs}. Buffs to remove {BuffsToRemove}", buffToRemove.Id, buffs, buffsToRemove);
                return;
            }
            if (buffList.Contains(buffToRemove)) {
                if (!buffList.Remove(buffToRemove)) {
                    logger.Error("Failed to remove buff {Id} from {Object}", buffToRemove.Id, Actor.ObjectId);
                }
                InvalidateSums();
            } else {
                logger.Error("Buff {Id} doesn't exist in buff list {BuffList}. Buffs to remove {BuffsToRemove}", buffToRemove.Id, buffList, buffsToRemove);
            }
        }
    }

    public void OnDeath() {
        foreach (Buff buff in EnumerateBuffs()) {
            if (!buff.Metadata.Property.KeepOnDeath) {
                Remove(buff.Id, buff.Caster.ObjectId);
                continue;
            }
            buff.UpdateEnabled();
        }
    }

    private bool CheckImmunity(int newBuffId, BuffCategory category) {
        foreach (Buff buff in EnumerateBuffs()) {
            if (buff.Metadata.Update.ImmuneIds.Contains(newBuffId) || buff.Metadata.Update.ImmuneCategories.Contains(category)) {
                return false;
            }
        }

        return true;
    }

    public List<Buff> GetSaveCacheBuffs() {
        return EnumerateBuffs()
            .Where(buff => !buff.Metadata.Property.RemoveOnLogout)
            .ToList();
    }

    public void UpdateEnabled() {
        foreach (Buff buff in EnumerateBuffs()) {
            buff.UpdateEnabled();
        }
    }
}
