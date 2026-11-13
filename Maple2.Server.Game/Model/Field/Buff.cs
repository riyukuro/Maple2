using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;
using Maple2.Tools;
using Maple2.Tools.Extensions;
using Serilog;

namespace Maple2.Server.Game.Model;

public class Buff : IUpdatable, IByteSerializable {
    private FieldManager Field => Owner.Field;
    public readonly AdditionalEffectMetadata Metadata;
    public readonly int ObjectId;
    public long CastUid { get; set; }

    public IActor Caster { get; private set; }
    public IActor Owner { get; private set; }

    public int Id => Metadata.Id;
    public short Level => Metadata.Level;

    public long StartTick { get; private set; }
    public long EndTick { get; private set; }
    public long IntervalTick { get; private set; }
    public long NextProcTick { get; protected set; }
    public int ProcCount { get; private set; }
    public int Stacks { get; private set; }
    public long ShieldHealth { get; set; }

    public bool Enabled { get; private set; }

    private bool activated;
    private readonly bool canExpire;
    private bool canProc;

    private readonly ILogger logger = Log.ForContext<Buff>();

    public Buff(AdditionalEffectMetadata metadata, int objectId, IActor caster, IActor owner, long startTick, long endTick, int stacks) {
        Metadata = metadata;
        ObjectId = objectId;

        Caster = caster;
        Owner = owner;

        StartTick = startTick;
        EndTick = endTick;

        // Buffs with IntervalTick=0 will just proc a single time
        IntervalTick = metadata.Property.IntervalTick > 0 ? metadata.Property.IntervalTick : metadata.Property.DurationTick + 1000;

        Stack(stacks, true);
        NextProcTick = FirstProcTick(metadata.Property, startTick, IntervalTick);
        UpdateEnabled(false);
        canProc = metadata.Property.KeepCondition != BuffKeepCondition.UnlimitedDuration;
        canExpire = metadata.Property.KeepCondition != BuffKeepCondition.UnlimitedDuration && EndTick >= startTick;
    }

    // dotCondition 0 procs as soon as the delay is over (dots, recovery and splash land on the add), 1 waits one
    // interval first. In KMS2 the first dot of every dotCondition-0 buff comes with its add, the first dot of
    // a dotCondition-1 buff one interval later.
    public static long FirstProcTick(AdditionalEffectMetadataProperty property, long startTick, long intervalTick) {
        long first = startTick + property.DelayTick;
        return property.DotCondition == BuffDotCondition.Default ? first : first + intervalTick;
    }

    public void ResetActor(IActor actor) {
        if (actor.ObjectId == Caster.ObjectId) {
            Caster = actor;
        }
        if (actor.ObjectId == Owner.ObjectId) {
            Owner = actor;
        }
    }

    // The stack count a buff is added with, and the count one more application adds: at least 1, at most the
    // buff's maxBuffCount. Every KMS2 add carries a count of 1 (the client starts at 1 too and multiplies the buff's
    // status values by it, AdditionalCliList additionalcli_2.cpp:1110), and each further application of a buff with
    // a higher maximum adds 1 until the maximum. A buff without a maximum keeps 0.
    public static int AddStacks(int requested, int maxCount) {
        return maxCount <= 0 ? 0 : Math.Clamp(requested, 1, maxCount);
    }

    // What a buff's status, resistance and invoke values are multiplied by: its stack count, with no floor. The
    // client sums a buff's status values, consume rates and resistance rates times the count (AdditionalCliList
    // additionalcli_2.cpp:1110) and its invoke values and rates as well (additionalcli_2.cpp:938), so a buff
    // without a stack maximum (count 0) adds none of them. The 16 such effects have no such value.
    public static int StackFactor(int stacks) => Math.Max(0, stacks);

    public static long ByStacks(long value, int stacks) => value * StackFactor(stacks);

    // A heal of an effect of sub-type Consumable is an item's (itemRecoveryHPRate applies): in the KR data 146 of the
    // 150 health heals the consumable skills (skill type 3, potions and food) cast are of it, and no job skill heal is.
    public static bool IsItemHeal(BuffSubType subType) => subType.HasFlag(BuffSubType.Consumable);
    public static float ByStacks(float rate, int stacks) => rate * StackFactor(stacks);

    // What a dot or recovery amount is multiplied by: the stack count, 1 for a buff without one. KMS2 dot damage
    // follows the count: a dot of 0.8 % of the target's health hits for 3 times that at 3 stacks. The client does
    // not compute these amounts; counting a buff without a stack maximum once is chosen here (the 16 such effects
    // are recoveries).
    public static int AmountFactor(int stacks) => Math.Max(1, stacks);
    // A dot or recovery amount (an int in its packet) at a stack count.
    public static int AmountByStacks(long amount, int stacks) {
        return (int) Math.Clamp(amount * AmountFactor(stacks), int.MinValue, int.MaxValue);
    }

    // A debuff put on a player lasts shorter by the player's gear options, in KMS2 packets: a status debuff
    // (sub-type 2) by attribute 36 (debuff duration reduction), a motion debuff (sub-type 8) with stun 0 or 1 by
    // attribute 34 (stun duration reduction). The totals are per mille and the result is rounded down
    // (1500 ms at 5 is sent as 1492, 9999000 ms at 28 as 9719028). Motion debuffs with stun 2 or 4 keep their
    // duration, and so do debuffs on npcs. A debuff with ignoreReduceTimeCondition (the client loads it with the
    // effect level's basic properties, AdditionalLevelData +ignoreReduceTimeCondition, and reads it nowhere else)
    // keeps its duration too: mostly boss mechanics (50000203, 10 s, status) that gear must not shorten.
    public static int ReducedDuration(AdditionalEffectMetadataProperty property, int durationMs, long reduceStun, long reduceDebuff) {
        if (durationMs <= 0 || property.Type != BuffType.Debuff || property.IgnoreReduceTime) {
            return durationMs;
        }
        long reduce = property.SubType switch {
            BuffSubType.Status => reduceDebuff,
            BuffSubType.Motion when property.Stun <= 1 => reduceStun,
            _ => 0,
        };
        reduce = Math.Clamp(reduce, 0, 1000);
        return (int) (durationMs * (1000 - reduce) / 1000);
    }

    // Applying a buff its owner already has restarts it when the reset condition is 0 or 2 and it has a duration:
    // KMS2 then sends the update with a new start and a new end. Reset condition 1 keeps its ticks, 3 replaces the
    // buff, and a buff without duration keeps its start.
    public static bool RestartsOnReapply(AdditionalEffectMetadataProperty property, long durationTick) {
        return durationTick > 0 && property.ResetCondition is BuffResetCondition.ResetEndTick or BuffResetCondition.Reset2;
    }

    // The restart: new start and end, and the procs run from the beginning, so a dotCondition-0 buff procs again
    // at once (after its delay) and a dotCondition-1 buff one interval later, each with its full count. In KMS2
    // a 1.1 s aura heal re-applied every second heals on every application.
    public bool Refresh(long startTick, long endTick) {
        if (startTick == StartTick && endTick == EndTick) {
            return false;
        }
        StartTick = startTick;
        EndTick = endTick;
        ProcCount = 0;
        NextProcTick = FirstProcTick(Metadata.Property, startTick, IntervalTick);
        canProc = Metadata.Property.KeepCondition != BuffKeepCondition.UnlimitedDuration;
        return true;
    }

    public bool UpdateEndTime(long endTick) {
        if (endTick == EndTick) {
            return false;
        }
        EndTick = endTick;
        return true;
    }

    public bool Stack(int amount = 1, bool silent = false) {
        if (amount == 0) {
            return false;
        }
        if (amount > 0 && Stacks >= Metadata.Property.MaxCount) {
            return false;
        }
        int currentStacks = Stacks;
        Stacks = Math.Clamp(Stacks + amount, 0, Metadata.Property.MaxCount);
        if (Stacks == currentStacks) {
            return false;
        }

        if (silent) {
            return true;
        }
        // What is kept by count follows first, so a skill started by reaching the maximum sees the new totals.
        if (Stacks > 0) {
            Owner.Buffs.StacksChanged(this);
        }
        if (Stacks >= Metadata.Property.MaxCount) {
            Owner.Buffs.TriggerEvent(Owner, Owner, Owner, EventConditionType.OnBuffStacksReached, buffId: Id);
        }

        return true;
    }

    public void RemoveStack(int amount = 1) {
        int before = Stacks;
        Stacks = Math.Max(0, Stacks - amount);
        if (Stacks == 0) {
            Owner.Buffs.Remove(Id, Caster.ObjectId);
        } else if (Stacks != before) {
            Owner.Buffs.StacksChanged(this);
        }
    }

    public virtual void Update(long tickCount) {
        if (!activated) {
            if (Metadata.Update.Cancel != null) {
                foreach (int id in Metadata.Update.Cancel.Ids) {
                    if (Metadata.Update.Cancel.CheckSameCaster && Owner.ObjectId != Caster.ObjectId) {
                        continue;
                    }

                    Owner.Buffs.Remove(id, Caster.ObjectId);
                }
            }

            activated = true;
        }

        if (Metadata.Property.ClearOnDistanceFromCaster > 0) {
            // Cancel buff if caster and owner are in different rooms or too far away
            if (Caster.Field.RoomId != Owner.Field.RoomId ||
                Vector3.Distance(Caster.Position, Owner.Position) > Metadata.Property.ClearOnDistanceFromCaster) {
                Owner.Buffs.Remove(Id, Caster.ObjectId);
                logger.Information("Buff {Id} removed due to distance from caster", Id);
                return;
            }
        }

        // A disabled buff has no proc due, so its timer ends it as well.
        if (canExpire && (!canProc || !Enabled) && tickCount > EndTick) {
            Owner.Buffs.Remove(Id, Caster.ObjectId);
            return;
        }

        bool wasEnabled = Enabled;
        if (!UpdateEnabled()) {
            return;
        }
        if (!wasEnabled) {
            NextProcTick = ResumedProcTick(NextProcTick, Metadata.Property.IntervalTick > 0 ? IntervalTick : 0, tickCount);
            // Its schedule's next proc past its end is not had: the timer ends it, as Proc does for the last slot.
            if (!ResumedCanProc(NextProcTick, EndTick)) {
                canProc = false;
            }
        }

        if (!canProc || tickCount < NextProcTick) {
            return;
        }

        Proc();
    }

    // notifyField is false only in the constructor, before the buff is held: then neither the field nor the
    // holder's sums and stats are told.
    public bool UpdateEnabled(bool notifyField = true) {
        bool enabled = Metadata.Condition.Check(Caster, Owner, Owner);
        if (Enabled != enabled) {
            Enabled = enabled;
            if (notifyField) {
                Field.Broadcast(BuffPacket.Update(this));
                Owner.Buffs.EnabledChanged(this);
            }
        }

        return enabled;
    }

    // A buff that comes back on does not pay the procs it missed while it was off (chosen here): its next proc is the
    // first one of its schedule at or after now. A buff of one proc (no interval) still has it when it comes on.
    internal static long ResumedProcTick(long nextProcTick, long intervalTick, long tickCount) {
        if (intervalTick <= 0 || nextProcTick >= tickCount) {
            return nextProcTick;
        }
        return nextProcTick + (tickCount - nextProcTick + intervalTick - 1) / intervalTick * intervalTick;
    }

    internal static bool ResumedCanProc(long nextProcTick, long endTick) => nextProcTick <= endTick;

    private void Proc() {
        ProcCount++;

        ApplyRecovery();
        ApplyDotDamage();
        ApplyDotBuff();
        ApplyCancel();
        ModifyDuration();
        Owner.ApplyEffects(Metadata.TickSkills, Caster, Owner, EventConditionType.Tick, buffId: Id, targets: [Owner]);
        ApplySplash();

        NextProcTick += IntervalTick;
        if (NextProcTick > EndTick || ProcCount >= ProcLimit(Metadata.Property, EndTick - StartTick, IntervalTick)) {
            canProc = false;
        }
    }

    // A dotCondition-0 buff procs duration / interval times, cut to a whole number and at least once: in KMS2 a
    // 100 ms buff with a 100 ms interval heals once, an 8 s dot with a 1 s interval hits 8 times (at 0 to 7 s) and a
    // 25 s one with a 1.5 s interval 16 times. Other buffs have no count limit; their end tick stops them.
    public static int ProcLimit(AdditionalEffectMetadataProperty property, long durationTick, long intervalTick) {
        if (property.DotCondition != BuffDotCondition.Default) {
            return int.MaxValue;
        }
        if (intervalTick <= 0) {
            return 1;
        }
        return (int) Math.Clamp(durationTick / intervalTick, 1, int.MaxValue);
    }

    private void ApplySplash() {
        foreach (SkillEffectMetadata effect in Metadata.Skills) {
            if (effect.Splash != null) {
                Caster.Field.AddSkill(Caster, effect, [Owner.Position], Owner.Rotation);
            }
        }
    }

    private void ApplyRecovery() {
        if (Metadata.Recovery == null) {
            return;
        }

        // The caster's healing invokes on this effect (type 58) raise its heal (HealDamageRecord.HealingRate).
        (_, float healingRate) = Caster.Buffs.GetEffectInvokeValues(InvokeEffectType.IncreaseHealing, Id, Metadata.Property.InvokeGroupId);
        // The holder's recovery rate for a potion's heal (sub-type Consumable) or any other heal.
        float recoveryRate = Owner.Buffs.RecoveryRate(IsItemHeal(Metadata.Property.SubType));
        var record = new HealDamageRecord(Caster, Owner, ObjectId, Metadata.Recovery, Stacks, healingRate, recoveryRate);
        var updated = new List<BasicAttribute>(3);
        if (record.HpAmount != 0) {
            Stat health = Owner.Stats.Values[BasicAttribute.Health];
            long before = health.Current;
            health.Add(record.HpAmount);
            updated.Add(BasicAttribute.Health);
            SendDpsHealEffective(health.Current - before);
        }
        if (record.SpAmount != 0) {
            Owner.Stats.Values[BasicAttribute.Spirit].Add(record.SpAmount);
            updated.Add(BasicAttribute.Spirit);
        }
        if (record.EpAmount != 0) {
            Owner.Stats.Values[BasicAttribute.Stamina].Add(record.EpAmount);
            updated.Add(BasicAttribute.Stamina);
        }

        if (updated.Count > 0) {
            foreach (ByteWriter packet in StatsPacket.Updates(Owner, updated)) {
                Field.Broadcast(packet);
            }
        }
        Field.Broadcast(SkillDamagePacket.Heal(record));
    }

    // The DPS meters' effective heal: the health this heal actually restored, to the caster (given) and the holder
    // (received), each only while its own meter is open. The full amounts come with SkillDamage Heal.
    private void SendDpsHealEffective(long restored) {
        if (restored <= 0) {
            return;
        }
        if (Caster is FieldPlayer { Session.DpsMeterOpen: true } caster) {
            caster.Session.Send(DpsStatPacket.HealEffective(false, (int) restored));
        }
        if (Owner is FieldPlayer { Session.DpsMeterOpen: true } owner) {
            owner.Session.Send(DpsStatPacket.HealEffective(true, (int) restored));
        }
    }

    private void ApplyDotDamage() {
        if (Metadata.Dot.Damage == null) {
            return;
        }

        // The caster's dot damage invokes on this effect (type 5) raise its rate (DotDamageRecord.DotRate).
        (_, float invokeRate) = Caster.Buffs.GetEffectInvokeValues(InvokeEffectType.IncreaseDotDamage, Id, Metadata.Property.InvokeGroupId);
        var record = new DotDamageRecord(Caster, Owner, Metadata.Dot.Damage, Stacks, invokeRate, Id) {
            BuffObjectId = ObjectId,
        };
        var targetUpdated = new List<BasicAttribute>(3);
        long healthBefore = Owner.Stats.Values[BasicAttribute.Health].Current;
        if (record.HpAmount != 0) {
            Owner.Stats.Values[BasicAttribute.Health].Add(NpcTargetRules.HealthChange(Owner.Stats.Values[BasicAttribute.Health].Current, record.HpAmount, Owner is FieldNpc { NoDie: true } || Owner.Buffs.DeathResistance));
            targetUpdated.Add(BasicAttribute.Health);
            if (record.HpAmount < 0 && Owner is FieldPlayer player) {
                player.DelayHpRegen();
            }
        }
        if (record.SpAmount != 0) {
            Owner.Stats.Values[BasicAttribute.Spirit].Add(record.SpAmount);
            targetUpdated.Add(BasicAttribute.Spirit);
        }
        if (record.EpAmount != 0) {
            Owner.Stats.Values[BasicAttribute.Stamina].Add(record.EpAmount);
            targetUpdated.Add(BasicAttribute.Stamina);
        }

        if (targetUpdated.Count <= 0 && !record.Invincible) {
            return;
        }

        if (targetUpdated.Count > 0) {
            foreach (ByteWriter packet in StatsPacket.Updates(Owner, targetUpdated)) {
                Field.Broadcast(packet);
            }
        }
        Field.Broadcast(SkillDamagePacket.DotDamage(record));
        // A tick of another player's dot that takes the last health on a pk field is that player's kill.
        if (Owner is FieldPlayer victim) {
            victim.PkKilledBy(Caster, healthBefore);
        }
        if (record.RecoverHp != 0) {
            Caster.Stats.Values[BasicAttribute.Health].Add(record.RecoverHp);
            Field.Broadcast(StatsPacket.Update(Caster, BasicAttribute.Health));
        }
    }

    private void ApplyDotBuff() {
        if (Metadata.Dot.Buff == null) {
            return;
        }

        AdditionalEffectMetadataDot.DotBuff dotBuff = Metadata.Dot.Buff;
        if (dotBuff.Target == DotTargetType.Caster) {
            Caster.Buffs.AddBuff(Owner, Caster, dotBuff.Id, dotBuff.Level, Field.FieldTick);
        } else {
            Owner.Buffs.AddBuff(Caster, Owner, dotBuff.Id, dotBuff.Level, Field.FieldTick);
        }
    }

    private void ApplyCancel() {
        if (Metadata.Update.Cancel == null) {
            return;
        }

        if (Metadata.Update.Cancel.Ids.Length > 0) {
            foreach (int id in Metadata.Update.Cancel.Ids) {
                List<Buff> buffs = Owner.Buffs.EnumerateBuffs(id);
                foreach (Buff buff in buffs) {
                    if (!Metadata.Update.Cancel.CheckSameCaster || buff.Caster.ObjectId == Caster.ObjectId) {
                        Owner.Buffs.Remove(id, Caster.ObjectId);
                    }
                }
            }

            List<Buff> buffsToRemove = Owner.Buffs.EnumerateBuffs()
                .Where(buff =>
                    Metadata.Update.Cancel.Categories.Contains(buff.Metadata.Property.Category)
                    && (!Metadata.Update.Cancel.CheckSameCaster || buff.Caster.ObjectId == Caster.ObjectId)
                ).ToList();

            buffsToRemove.ForEach(buff => Owner.Buffs.Remove(buff.Id, Caster.ObjectId));
        }
    }

    public void ModifyDuration() {
        foreach (AdditionalEffectMetadataUpdate.ModifyDuration modifyDuration in Metadata.Update.Duration) {
            List<Buff> buffs = Owner.Buffs.EnumerateBuffs(modifyDuration.Id);
            if (buffs.Count == 0) {
                continue;
            }
            foreach (Buff buff in buffs) {
                buff.EndTick += (long) modifyDuration.Value;
                if (modifyDuration.Rate > 0) {
                    long remainingDuration = (long) (modifyDuration.Rate * (buff.EndTick - Environment.TickCount64));
                    buff.EndTick += (modifyDuration.Rate >= 1) ? remainingDuration : -remainingDuration;
                }

                // restart proc if possible
                if (NextProcTick < EndTick) {
                    canProc = true;
                }
                Field.Broadcast(BuffPacket.Update(buff));
            }
        }
    }

    public void UpdateEndTime(int modifyValue) {
        EndTick += modifyValue;
        Field.Broadcast(BuffPacket.Update(this));
    }

    public void WriteTo(IByteWriter writer) {
        WriteAdditionalEffect(writer);
        WriteShieldHealth(writer);
    }

    public void WriteAdditionalEffect(IByteWriter writer) {
        writer.WriteInt(StartTick.Truncate32());
        writer.WriteInt(EndTick.Truncate32());
        writer.WriteInt(Id);
        writer.WriteShort(Level);
        writer.WriteInt(Stacks);
        writer.WriteBool(Enabled);
    }

    public void WriteShieldHealth(IByteWriter writer) {
        writer.WriteLong(ShieldHealth);
    }

    public override string ToString() {
        return $"Buff(Id={Id}, Level={Level}, Caster={Caster.ObjectId}, Owner={Owner.ObjectId}, StartTick={StartTick}, EndTick={EndTick}, Stacks={Stacks}, Enabled={Enabled})";
    }
}
