using System.Numerics;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;
using Maple2.Tools.Collision;
using Serilog;

namespace Maple2.Server.Game.Model;

public enum SkillSource {
    Region,
    Cube,
}

public class FieldSkill : FieldEntity<SkillMetadata> {
    public IActor Caster { get; init; }
    public SkillSource Source { get; init; } = SkillSource.Region;
    public int Interval { get; }
    public int FireCount { get; private set; }
    public bool Enabled => FireCount > 0 || NextTick <= endTick || Field.FieldTick <= endTick;
    public bool Active { get; private set; } = true;
    public int TriggerId { get; init; }

    public readonly Vector3[] Points;
    public readonly bool UseDirection;
    private readonly long endTick;
    public long NextTick { get; private set; }
    // A region of a skill that fires only when made to (OnlyManualActiveSplash): it never fires on its own, and the
    // caster's attacks that name it (SplashActiveSkillIds) fire it through Invoke.
    public bool ManualOnly { get; }
    private readonly int invokeCoolTick;
    private long nextInvokeTick;
    private readonly ILogger logger = Log.ForContext<FieldSkill>();

    // How many tags each attack of the region's fires has placed, and how many of them each vibrate object has taken
    // (VibrateHandler): a tag sends once per object it meets.
    private readonly Dictionary<(byte MotionPoint, byte AttackPoint), int> tags = new();
    private readonly Dictionary<(string EntityId, byte MotionPoint, byte AttackPoint), int> vibrates = new();

    // The tags one fire of an attack places: one, or for a bouncing attack one per bounce target its SkillDamage
    // Target packet lists, each with its own entered set (at least one, never fewer than the client may send).
    public static int FireTags(bool bounce, int targetRecords) => bounce ? Math.Max(1, targetRecords) : 1;

    // Counts a fire's tags of an attack.
    public void AddTags(byte motionPoint, byte attackPoint, int count) {
        lock (vibrates) {
            tags.TryGetValue((motionPoint, attackPoint), out int placed);
            tags[(motionPoint, attackPoint)] = placed + count;
        }
    }

    // Takes one of the attack's tags for the vibrate object, false when each tag placed has already sent for it.
    public bool TakeVibrate(string entityId, byte motionPoint, byte attackPoint) {
        lock (vibrates) {
            tags.TryGetValue((motionPoint, attackPoint), out int placed);
            vibrates.TryGetValue((entityId, motionPoint, attackPoint), out int taken);
            if (taken >= placed) {
                return false;
            }
            vibrates[(entityId, motionPoint, attackPoint)] = taken + 1;
            return true;
        }
    }

    // The point a cube skill's tile packets give: one block below the cube (SplashTiming.TilePoint).
    public Vector3 CubePosition { get; init; }

    // The point a region's hit on a target at a position comes from and the direction from there to the target, as
    // its packet sends them (the cube's position for a cube skill, else the region's nearest point). The target's
    // damage record takes them (DamageRecord.RegionFire), and the npc's push reads them from there.
    public (Vector3 Position, Vector3 Direction) Fire(Vector3 target) {
        Vector3 point = Source == SkillSource.Cube ? CubePosition : SplashTiming.FirePoint(Points, target, Position);
        return (point, SplashTiming.FireDirection(point, target));
    }

    // The damage record of one of the region's attacks.
    internal DamageRecord Damage(SkillRecord record, SkillMetadataAttack attack) {
        return new DamageRecord(record.Metadata, attack) {
            CasterId = Caster.ObjectId, // the attacking actor; OwnerId stays the region id the client finds the region by
            OwnerId = ObjectId,
            SkillId = Value.Id,
            Level = Value.Level,
            MotionPoint = record.MotionPoint,
            AttackPoint = record.AttackPoint,
            Position = Position,
            Direction = Rotation,
            RegionFire = Fire,
        };
    }

    private void BroadcastDamage(DamageRecord record) {
        if (Source != SkillSource.Cube) {
            Field.Broadcast(SkillDamagePacket.Region(record));
            return;
        }

        foreach (DamageRecordTarget target in record.Targets.Values) {
            Field.Broadcast(SkillDamagePacket.Tile(record.SkillId, record.Level,
                new SkillDamagePacket.RegionTarget(target.ObjectId, target.Position, target.Direction, target.Damage)));
        }
    }

    public FieldSkill(FieldManager field, int objectId, IActor caster,
                      SkillMetadata value, int interval, params Vector3[] points) : base(field, objectId, value) {
        Caster = caster;
        Points = points;
        Interval = interval;
        FireCount = int.MaxValue;
        NextTick = Field.FieldTick + interval;
    }

    public FieldSkill(FieldManager field, int objectId, IActor caster,
                      SkillMetadata value, int interval, int fireCount, params Vector3[] points) : base(field, objectId, value) {
        Caster = caster;
        Points = points;
        Interval = interval;
        FireCount = fireCount;
        NextTick = Field.FieldTick + interval;
    }

    // TODO: check if duration of field skill is correct.
    // Example: Heaven's Wrath says duration is 20s but it's lasting ~22s (Probably the splash.Delay being added to the endTick).
    public FieldSkill(FieldManager field, int objectId, IActor caster,
                      SkillMetadata value, int fireCount, SkillEffectMetadataSplash splash, params Vector3[] points) : base(field, objectId, value) {
        Caster = caster;
        Points = points;
        Interval = splash.Interval;
        FireCount = fireCount;
        UseDirection = splash.UseDirection;

        long baseTick = Field.FieldTick;
        if (splash.ImmediateActive) {
            NextTick = baseTick;
            endTick = baseTick + splash.RemoveDelay + (FireCount - 1) * splash.Interval;
        } else {
            NextTick = baseTick + SplashTiming.FirstFire(splash.ImmediateActive, splash.OnlySensingActive, splash.Delay, splash.Interval);
            endTick = baseTick + splash.Delay + splash.RemoveDelay + FireCount * splash.Interval;
        }
        if (splash.OnlySensingActive) {
            endTick = baseTick + splash.Delay + splash.RemoveDelay;
            Active = false;
        }
        if (value.Data.OnlyManualActiveSplash) {
            SkillMetadataMotionProperty? motion = value.Data.Motions.FirstOrDefault()?.MotionProperty;
            ManualOnly = true;
            Active = true;
            // It fires only through Invoke, so its fire count never runs down: its life alone ends it.
            FireCount = 0;
            NextTick = long.MaxValue;
            endTick = baseTick + (motion?.SplashLifeTick ?? 0);
            invokeCoolTick = motion?.SplashInvokeCoolTick ?? 0;
            nextInvokeTick = baseTick;
        }
    }

    // Fires a region that fires only when made to, once, unless it is gone or its invoke cool time has not passed.
    public bool Invoke() {
        long now = Field.FieldTick;
        if (!ManualOnly || !SplashTiming.CanInvoke(now, endTick, nextInvokeTick)) {
            return false;
        }
        nextInvokeTick = now + invokeCoolTick;
        Fire();
        return true;
    }

    public override void Update(long tickCount) {
        if (!Enabled) {
            Field.RemoveSkill(ObjectId);
            return;
        }

        if (tickCount < NextTick) {
            return;
        }

        // Check Activation for OnlySensingActive
        if (!Active) {
            if (tickCount > endTick) {
                FireCount = 0;
                NextTick = long.MaxValue;
                return;
            }
            foreach (SkillMetadataMotion motion in Value.Data.Motions) {
                foreach (SkillMetadataAttack attack in motion.Attacks) {
                    float skillAngle = UseDirection ? Rotation.Z : 0;
                    Prism[] prisms = Points
                        .Select(point => attack.GetRegionPrism(point, skillAngle))
                        .ToArray();
                    if (Field.GetTargets(Caster, prisms, attack.Range, attack.TargetCount).Any()) {
                        Active = true;
                        goto activated;
                    }
                }
            }

            // Not reached yet: it keeps looking until its life ends (a snare, 93000002, has no interval), and a
            // look that finds no one takes none of its fires.
            NextTick = Field.FieldTick + Interval;
            return;
        }

    activated:
        Fire();
        if (Interval == 0) {
            FireCount = 0;
            NextTick = long.MaxValue;
        } else {
            if (FireCount != int.MaxValue) {
                FireCount--;
            }

            NextTick += Interval;
            // Fires owed from ticks the field did not run (an empty field stops its updates) are dropped, not sent
            // one per frame when it runs again: KMS2 sends a map region's fires at least 500 ms apart. Chosen here:
            // the region keeps its rhythm, and a region of a few fires spends the dropped ones, so it ends when it
            // would have.
            if (NextTick <= tickCount) {
                long missed = (tickCount - NextTick) / Interval + 1;
                NextTick += missed * Interval;
                if (FireCount != int.MaxValue) {
                    FireCount -= (int) Math.Min(missed, FireCount);
                }
            }

            if (FireCount <= 0) {
                NextTick = long.MaxValue;
            }
        }
    }

    private void Fire() {
        // TODO: These are buffs? seems irrelevant to FieldSkill?
        // foreach (SkillEffectMetadata skill in Value.Data.Skills) {
        //     if (skill.Condition != null) {
        //         ConditionSkill(Field.Players.Values, skill);
        //     } else if (skill.Splash != null) {
        //         SplashSkill(skill);
        //     }
        // }

        var record = new SkillRecord(Value, 0, Caster) {
            TargetUid = (long) ObjectId << 32,
            Position = Position,
            Rotation = Rotation,
        };
        for (byte i = 0; record.TrySetMotionPoint(i); i++) {
            for (byte j = 0; record.TrySetAttackPoint(j); j++) {
                SkillMetadataAttack attack = record.Attack;
                record.TargetUid++;
                DamageRecord damage = Damage(record, attack);
                var targetRecords = new List<TargetRecord>();
                if (attack.Arrow.BounceType > 0) {
                    IActor[] targets = [];
                    var bounceTargets = new List<IActor>();
                    Vector3 position = Position;
                    long prevTargetUid = 0;
                    for (int bounce = 0; bounce <= attack.Arrow.BounceCount; bounce++) {
                        Vector3 box = attack.Arrow.Collision + attack.Arrow.CollisionAdd;
                        var circle = new Circle(new Vector2(position.X, position.Y), attack.Arrow.BounceRadius);
                        // var rectangle = new Rectangle(new Vector2(Position.X, Position.Y), box.X, box.Y, UseDirection ? Rotation.Z : 0);
                        var prism = new Prism(circle, position.Z, box.Z);

                        targets = attack.Arrow.BounceOverlap
                            ? Field.GetTargets(Caster, [prism], record.Attack.Range, 1, targets).ToArray()
                            : Field.GetTargets(Caster, [prism], record.Attack.Range, 1, bounceTargets).ToArray();
                        if (targets.Length <= 0) {
                            break;
                        }

                        IActor target = targets[0];
                        record.TargetUid++;
                        targetRecords.Add(new TargetRecord {
                            PrevUid = prevTargetUid,
                            Uid = record.TargetUid,
                            TargetId = target.ObjectId,
                            Index = (byte) targetRecords.Count,
                        });
                        bounceTargets.Add(target);
                        position = target.Position;
                        prevTargetUid = record.TargetUid;
                    }

                    AddTags(i, j, FireTags(bounce: true, targetRecords.Count));
                    Field.Broadcast(SkillDamagePacket.Target(record, targetRecords));
                    BroadcastDamage(damage);
                    for (int t = 0; t < bounceTargets.Count; t++) {
                        IActor target = bounceTargets[t];
                        record.TargetUid = targetRecords[t].Uid;
                        record.Position = target.Position;
                        record.Targets.TryAdd(target.ObjectId, target);

                        // TODO: There should be some delay between bounces.
                        target.TargetAttack(record);
                        Caster.ApplyEffects(attack.SkillsOnDamage, Caster, damage, targets: record.Targets.Values.ToArray());
                        Caster.ApplyEffects(attack.Skills, Caster, Caster, skillId: Value.Id, targets: targets);
                    }
                } else {
                    float skillAngle = UseDirection ? Rotation.Z : 0;
                    Prism[] prisms = Points
                        .Select(point => attack.GetRegionPrism(point, skillAngle))
                        .ToArray();
                    IActor[] targets = Field.GetTargets(Caster, prisms, attack.Range, attack.TargetCount).ToArray();
                    // if (targets.Length > 0) {
                    //     logger.Debug("[{Tick}] {ObjectId}:{AttackPoint} Targeting: {Count}/{Limit} {Type}",
                    //         NextTick, ObjectId, attack.Point, targets.Length, attack.TargetCount, attack.Range.ApplyTarget);
                    //     foreach (IActor target in targets) {
                    //         logger.Debug("- {Id} @ {Position}", target.ObjectId, target.Position);
                    //     }
                    // }
                    // else if (Caster.ObjectId != 0) { // Not Ms2RegionSkill
                    //     logger.Debug("[{Tick}] {ObjectId}:{AttackPoint} has {Count}/{Limit} {Type}",
                    //         NextTick, ObjectId, attack.Point, 0, attack.TargetCount, attack.Range.ApplyTarget);
                    //     logger.Debug("{Range}", attack.Range);
                    //     foreach (Prism prism in prisms) {
                    //         logger.Debug("- {Prism}", prism);
                    //     }
                    // }

                    AddTags(i, j, FireTags(bounce: false, targetRecords.Count));
                    Field.VibrateObjects(damage, Position);
                    foreach (IActor target in targets) {
                        target.ApplyDamage(Caster, damage, attack);
                        // TODO: Set PrevUid?
                        targetRecords.Add(new TargetRecord {
                            Uid = (long) ObjectId << 32 | (uint) targetRecords.Count,
                            TargetId = target.ObjectId,
                            Index = (byte) targetRecords.Count,
                        });
                    }
                    (bool sendTargets, bool sendDamage) = SplashTiming.FirePackets(Source == SkillSource.Cube, targetRecords.Count);
                    if (sendTargets) {
                        Field.Broadcast(SkillDamagePacket.Target(record, targetRecords));
                    }
                    if (sendDamage) {
                        BroadcastDamage(damage);
                    }

                    Caster.ApplyEffects(attack.SkillsOnDamage, Caster, damage, targets: targets);
                    Caster.ApplyEffects(attack.Skills, Caster, Caster, skillId: Value.Id, targets: targets);
                }

                if (attack.Skills.Any(effect => effect.Splash != null)) {
                    Field.AddSkill(record);
                }
            }
        }
    }
}
