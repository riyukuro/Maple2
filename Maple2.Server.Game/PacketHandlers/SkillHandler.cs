using System.Numerics;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager.Config;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Enum;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Tools.Collision;

namespace Maple2.Server.Game.PacketHandlers;

public class SkillHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.Skill;

    private enum Command : byte {
        Use = 0,
        Attack = 1,
        Sync = 2,
        TickSync = 3,
        Cancel = 4,
    }

    private enum SubCommand : byte {
        Point = 0,
        Target = 1,
        CubeMagicPath = 2,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required SkillMetadataStorage SkillMetadata { private get; init; }
    public required TableMetadataStorage TableMetadata { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Use:
                HandleUse(session, packet);
                return;
            case Command.Attack:
                var subcommand = packet.Read<SubCommand>();
                switch (subcommand) {
                    case SubCommand.Point:
                        HandlePoint(session, packet);
                        return;
                    case SubCommand.Target:
                        HandleTarget(session, packet);
                        return;
                    case SubCommand.CubeMagicPath:
                        HandleCubeMagicPath(session, packet);
                        return;
                }
                return;
            case Command.Sync:
                HandleSync(session, packet);
                return;
            case Command.TickSync:
                HandleTickSync(session, packet);
                return;
            case Command.Cancel:
                HandleCancel(session, packet);
                return;
        }
    }

    private void HandleUse(GameSession session, IByteReader packet) {
        if (session.Field is null) return;
        long skillUid = packet.ReadLong();
        int serverTick = packet.ReadInt();
        int skillId = packet.ReadInt();
        short level = packet.ReadShort();

        if (session.HeldLiftup != null) {
            if (session.HeldLiftup.SkillId == skillId && session.HeldLiftup.Level == level) {
                session.HeldLiftup = null;
            } else {
                // Cannot use other skills while holding LiftupWeapon.
                return;
            }
        }
        byte motionPoint = packet.ReadByte();
        var position = packet.Read<Vector3>();
        var direction = packet.Read<Vector3>();
        var rotation = packet.Read<Vector3>();
        float rotate2Z = packet.ReadFloat(); // Rotation2Z

        SkillRecord? record = session.Player.CastSkill(skillId, level, skillUid, serverTick, position, direction, rotation, rotate2Z, motionPoint);
        if (record == null) {
            return;
        }

        SkillMetadataMotionProperty motion = record.Metadata.Data.Motions.First().MotionProperty;
        session.Animation.TryPlaySequence(motion.SequenceName, motion.SequenceSpeed, AnimationType.Skill, record.Metadata);

        SkillUseTail tail = SkillUseTail.Read(packet);
        record.Unknown = tail.Unknown; // UnkBool
        record.ItemUid = tail.ItemUid;
        record.IsHold = tail.IsHold;
        if (record.IsHold) {
            record.HoldInt = tail.HoldInt;
            record.HoldString = tail.HoldString;

            if (session.Player.DebugSkills) {
                session.Send(NoticePacket.Message($"Skill.Use: {skillId}, {skillUid}; IsHold: true; HoldInt: {record.HoldInt}; HoldString: {record.HoldString}; UnkBool: {record.Unknown}"));
            }
        } else if (session.Player.DebugSkills) {
            session.Send(NoticePacket.Message($"Skill.Use: {skillId}, {skillUid}; IsHold: false; UnkBool: {record.Unknown}"));
        }

        long startTick = session.Field.FieldTick;
        // A cast position away from the player's own is not taken: the cast stands where the server has the player.
        if (SkillHitRules.CastTooFar(record.Position, session.Player.Position, session.Player.SinceStateSync)) {
            Logger.Debug("Cast position {Cast} away from the player at {Player}: {Record}", record.Position, session.Player.Position, record);
            record.Position = session.Player.Position;
        }
        record.CastPosition = record.Position;
        record.Hits.Open(startTick);
        SkillCooldown? blocking = null;
        if (!CastAllowed(session, record, startTick, serverTick, out blocking) || !session.Player.SkillCastConsume(record)) {
            // Nothing was paid. The client ends the cast and takes the cooldown from this answer
            // (CSceneStateGame::OnSkillUseFailed, mode 5).
            session.Send(SkillUseFailedPacket.Fail(record.CastUid, record.SkillId, record.Metadata.Data.Condition.CooldownTime,
                blocking ?? session.Config.GetSkillCooldown(record.SkillId), startTick));
            return;
        }

        session.Player.InBattle = record.Metadata.State.InBattle;
        session.Player.ActiveSkills.Add(record);
        session.Field.Broadcast(SkillPacket.Use(record));
        session.Send(StatsPacket.Init(session.Player));
        session.Field.Broadcast(StatsPacket.Update(session.Player), session);

        session.Player.ApplyEffects(record.Metadata.Data.Skills, session.Player, session.Player, EventConditionType.Activate, skillId: record.SkillId, targets: [session.Player]);
        session.Buffs.TriggerEvent(session.Player, session.Player, session.Player, EventConditionType.OnSkillCasted, skillId: record.SkillId);

        session.ConditionUpdate(ConditionType.skill, codeLong: skillId, targetLong: session.Field.MapId);
        session.Config.SaveSkillCooldown(record.Metadata, startTick);
        session.Config.SaveCastTime(record.Metadata, serverTick, startTick);
        session.Config.SaveCastMotion(record.Motion.MotionProperty, record.SkillId, serverTick, startTick);
    }

    // Checked before any cost is paid. A dead player casts only a skill that allows it. A skill of a job's tree
    // must be in the caster's own tree at that level or above. An item skill must also pass its own condition and
    // be off cooldown, its own and its cooldown group's; a job skill must not come too early after its last cast.
    // No cast comes before the motion of the last cast lets the next one in (SkillCastRules.MotionRefuses).
    private bool CastAllowed(GameSession session, SkillRecord record, long tick, int castTick, out SkillCooldown? blocking) {
        blocking = null;
        BeginCondition condition = record.Metadata.Data.Condition;
        if (SkillCastRules.RefusedDead(session.Player.IsDead, condition.AllowDead)) {
            return false;
        }
        if (session.Config.MotionTooEarly(record.Metadata, castTick, tick)) {
            Logger.Debug("Cast refused, skill {SkillId} comes before the last cast's motion lets it in", record.SkillId);
            return false;
        }
        SkillInfo skillInfo = session.Config.Skill.SkillInfo;
        bool inAnyTree = skillInfo.InAnyTree(record.SkillId);
        if (SkillCastRules.TreeRefuses(inAnyTree, skillInfo.TreeLevel(record.SkillId), record.Level)) {
            Logger.Debug("Cast refused, skill {SkillId} level {Level} is not in the tree of {Job} at that level", record.SkillId, record.Level, skillInfo.Job);
            return false;
        }
        if (!record.Metadata.Data.Consume.UseItem) {
            if (SkillCastRules.IsJobSkill(record.SkillId, inAnyTree) && session.Config.CastTooEarly(record.Metadata, castTick, tick)) {
                Logger.Debug("Cast refused, skill {SkillId} is on cooldown", record.SkillId);
                return false;
            }
            return true;
        }
        if (!condition.Check(session.Player, session.Player, session.Player)) {
            return false;
        }
        blocking = session.Config.BlockingCooldown(record.Metadata, tick);
        return blocking == null;
    }

    private void HandlePoint(GameSession session, IByteReader packet) {
        if (session.Field == null) {
            return;
        }

        long skillUid = packet.ReadLong();
        SkillRecord? record = session.Player.ActiveSkills.Get(skillUid);
        if (record == null) {
            Logger.Warning("Invalid Attack-Point Skill {SkillUid}", skillUid);
            return;
        }

        byte attackPoint = packet.ReadByte();
        if (!record.TrySetAttackPoint(attackPoint)) {
            Logger.Error("Invalid AttackPoint({AttackPoint}) for {Record}", attackPoint, record);
            return;
        }

        var position = packet.Read<Vector3>();
        var direction = packet.Read<Vector3>();

        if (session.Player.DebugSkills) {
            session.Send(NoticePacket.Message($"Skill.Attack.Point: {skillUid}; AttackPoint: {attackPoint}"));
        }

        byte count = packet.ReadByte();
        // Note: counts up for skills that are held down (reused skillUid)
        int iterations = packet.ReadInt();

        // An attack point packet outside what a cast sends (SkillHitRules) is dropped: nothing is declared or relayed.
        if (SkillHitRules.PointMoved(position, record.CastPosition)
            || !record.Hits.TakePoint(attackPoint, iterations, record.Metadata.Property.ContinueSkill, count, session.Field.FieldTick,
                SkillHitRules.MaxAttacks(record.Attack.TargetCount, record.Attack.Arrow.NonTarget == SkillTargetType.Owner),
                session.Config.HeldInterval(record.Motion.MotionProperty))) {
            Logger.Debug("Attack point dropped: AttackPoint={AttackPoint} iteration={Iteration} count={Count} at {Position}: {Record}",
                attackPoint, iterations, count, position, record);
            return;
        }

        record.Position = position;
        record.Direction = direction;

        for (byte i = 0; i < count; i++) {
            var targets = new List<TargetRecord>();
            var targetRecord = new TargetRecord {
                Uid = packet.ReadLong(),
                TargetId = packet.ReadInt(),
                Unknown = packet.ReadByte(),
            };
            targets.Add(targetRecord);
            record.Hits.Declare(targetRecord.Uid, attackPoint);

            // While more targets in packet.
            while (packet.ReadBool()) {
                targetRecord = new TargetRecord {
                    PrevUid = targetRecord.Uid,
                    Uid = packet.ReadLong(),
                    TargetId = packet.ReadInt(),
                    Unknown = packet.ReadByte(),
                    Index = packet.ReadByte(),
                };
                targets.Add(targetRecord);
                record.Hits.Declare(targetRecord.Uid, attackPoint);
            }

            session.Player.InBattle = true;

            // When the client reports no valid target (TargetId=0), the NPC may have died since the last
            // SubCommand.Target. Do server-side detection to find hittable corpses in attack range.
            if (targets.All(t => t.TargetId == 0) && session.Field != null) {
                float angle = MathF.Atan2(record.Direction.Y, record.Direction.X) * (180f / MathF.PI);
                Prism attackPrism = record.Attack.Range.GetPrism(record.Position, angle);
                foreach (FieldNpc npc in attackPrism.Filter(session.Field.Mobs.Values.Where(n => n.IsCorpse), record.Attack.TargetCount)) {
                    record.Targets.TryAdd(npc.ObjectId, npc);
                }

                // Fallback: if no corpse found in prism, pick the nearest corpse within range (non-directional).
                if (record.Targets.IsEmpty) {
                    float maxDist = record.Attack.Range.Distance + record.Attack.Range.RangeAdd.Y;
                    float maxHeight = record.Attack.Range.Height + record.Attack.Range.RangeAdd.Z;
                    FieldNpc? nearestCorpse = null;
                    float nearestDist = float.MaxValue;
                    foreach (FieldNpc npc in session.Field.Mobs.Values) {
                        if (!npc.IsCorpse) continue;
                        float dist = Vector2.Distance(new Vector2(record.Position.X, record.Position.Y),
                            new Vector2(npc.Position.X, npc.Position.Y));
                        float heightDelta = MathF.Abs(npc.Position.Z - record.Position.Z);
                        if (dist <= maxDist && heightDelta <= maxHeight && dist < nearestDist) {
                            nearestDist = dist;
                            nearestCorpse = npc;
                        }
                    }

                    if (nearestCorpse != null) {
                        record.Targets.TryAdd(nearestCorpse.ObjectId, nearestCorpse);
                    }
                }
                if (!record.Targets.IsEmpty) {
                    session.Player.TargetAttack(record);
                    record.Targets.Clear();
                    session.Buffs.TriggerEvent(session.Player, session.Player, session.Player, EventConditionType.OnSkillCastEnd, skillId: record.SkillId);
                    continue;
                }
            }

            session.Field?.Broadcast(SkillDamagePacket.Target(record, targets), session);

            // Unsure if this is correct.
            session.Buffs.TriggerEvent(session.Player, session.Player, session.Player, EventConditionType.OnSkillCastEnd, skillId: record.SkillId);
        }
    }

    private void HandleTarget(GameSession session, IByteReader packet) {
        if (session.Field == null) {
            return;
        }

        long skillUid = packet.ReadLong();
        SkillRecord? record = session.Player.ActiveSkills.Get(skillUid);
        if (record == null) {
            Logger.Warning("Invalid Attack-Target Skill {SkillUid}", skillUid);
            return;
        }

        long targetUid = packet.ReadLong();
        var impactPosition = packet.Read<Vector3>();
        packet.Read<Vector3>(); // ImpactPosition2
        var direction = packet.Read<Vector3>();
        byte attackPoint = packet.ReadByte();
        if (!record.TrySetAttackPoint(attackPoint)) {
            Logger.Error("Invalid AttackPoint({AttackPoint}) for {Record}", attackPoint, record);
            return;
        }

        // A target packet pays damage only for an attack its cast declared for this attack point, once (a bouncing
        // arrow once per target it can reach), in time and with the impact near the cast (SkillHitRules). Anything
        // else is dropped without damage.
        if (record.Hits.TooLate(session.Field.FieldTick)
            || SkillHitRules.ImpactTooFar(impactPosition, record.CastPosition, record.Attack.Range.Distance)
            || !record.Hits.TakeTarget(targetUid, attackPoint, record.Attack.Arrow.BounceCount)) {
            Logger.Debug("Target packet dropped: attack {AttackUid} AttackPoint={AttackPoint} impact {Impact}: {Record}",
                targetUid, attackPoint, impactPosition, record);
            return;
        }

        record.TargetUid = targetUid;
        record.ImpactPosition = impactPosition;
        record.Direction = direction;

        byte count = packet.ReadByte();
        // The client sends every hit of a bounce chain in one packet: TargetCount first hits, each with up to
        // BounceCount bounces.
        int maxCount = record.Attack.TargetCount * (1 + Math.Max(0, record.Attack.Arrow.BounceCount));
        if (count > maxCount) {
            Logger.Warning("SkillId={SkillId} AttackPoint={AttackPoint} sent {Count} targets but TargetCount={TargetCount} BounceCount={BounceCount} — clamping.",
                record.SkillId, attackPoint, count, record.Attack.TargetCount, record.Attack.Arrow.BounceCount);
            count = (byte) Math.Min(maxCount, byte.MaxValue);
        }

        int iterations = packet.ReadInt();
        if (iterations != 0) {
            Logger.Information("Unhandled skill-Target iterations: ({Value}): {Record}", iterations, record);
        }

        if (session.Player.DebugSkills) {
            session.Send(NoticePacket.Message($"Skill.Attack.Damage: {skillUid}; AttackPoint: {attackPoint}"));
        }

        // Although the client feeds us this information and is right, we cannot rely on it and must validate it
        // we should keep it just to ensure what the server gathers as proper targets is the same as the client
        /*for (byte i = 0; i < count; i++) {
            int targetId = packet.ReadInt();
            if (record.Targets.ContainsKey(targetId)) {
                continue;
            }
            packet.ReadByte();

            switch (record.Attack.Range.ApplyTarget) {
                case ApplyTargetType.Hostile:
                    if (session.Field.Mobs.TryGetValue(targetId, out FieldNpc? npc)) {
                        record.Targets.TryAdd(npc.ObjectId, npc);
                    }
                    continue;
                case ApplyTargetType.Friendly:
                    if (session.Field.TryGetPlayer(targetId, out FieldPlayer? player)) {
                        record.Targets.TryAdd(player.ObjectId, player);
                    }
                    continue;
                case ApplyTargetType.HungryMobs:
                    if (session.Field.Pets.TryGetValue(targetId, out FieldPet? pet)) {
                        record.Targets.TryAdd(pet.ObjectId, pet);
                    }
                    continue;
                case ApplyTargetType.RegionBuff:
                case ApplyTargetType.RegionBuff2:
                default:
                    Logger.Debug("Unhandled Target-SkillEntity:{Entity}", record.Attack.Range.ApplyTarget);
                    continue;
            }
        }*/

        IEnumerable<IActor> targets = session.Field.GetTargets(record);
        SkillMetadataArrow arrow = record.Attack.Arrow;
        bool bounces = arrow.BounceCount > 0 && BounceChain.PicksAround(arrow.BounceType);
        foreach (IActor target in targets) {
            if (!bounces) {
                record.Targets.TryAdd(target.ObjectId, target);
                continue;
            }
            // One damage record per object: a chain that comes back to an object it already hit adds no hit.
            foreach (IActor hit in BounceChain.Build(target, arrow.BounceCount, arrow.BounceType, arrow.BounceOverlap,
                         last => session.Field.GetBounceTargets(record, last))) {
                record.Targets.TryAdd(hit.ObjectId, hit);
            }
        }
        session.Player.TargetAttack(record);
    }

    private void HandleCubeMagicPath(GameSession session, IByteReader packet) {
        long skillUid = packet.ReadLong();
        SkillRecord? record = session.Player.ActiveSkills.Get(skillUid);
        if (record == null) {
            Logger.Warning("Invalid Attack-Splash Skill {SkillUid}", skillUid);
            return;
        }

        byte attackPoint = packet.ReadByte();
        if (!record.TrySetAttackPoint(attackPoint)) {
            Logger.Error("Invalid AttackPoint({AttackPoint}) for {Record}", attackPoint, record);
            return;
        }

        int unknown1 = packet.ReadInt(); // Unknown(0)
        if (unknown1 != 0) {
            Logger.Error("Unhandled skill-MagicPath value1({Value}): {Record}", unknown1, record);
        }

        int attackIndex = packet.ReadInt(); // Unknown(0)
        if (attackIndex != 0) {
            Logger.Error("Unhandled skill-MagicPath attackIndex ({Value}): {Record}", attackIndex, record);
        }

        if (session.Player.DebugSkills) {
            session.Send(NoticePacket.Message($"Skill.Attack.Region: {skillUid}; AttackPoint: {attackPoint}; UnkInt: {unknown1}; UnkInt: {attackIndex}"));
        }

        record.Position = packet.Read<Vector3>();
        record.Rotation = packet.Read<Vector3>();

        session.Field?.AddSkill(record);
    }

    private void HandleSync(GameSession session, IByteReader packet) {
        long skillUid = packet.ReadLong();
        SkillRecord? record = session.Player.ActiveSkills.Get(skillUid);
        if (record == null) {
            Logger.Warning("Invalid Sync Skill {SkillUid}", skillUid);
            return;
        }
        int skillId = packet.ReadInt();
        short skillLevel = packet.ReadShort();
        byte motionPoint = packet.ReadByte();

        if (record.Metadata.Data.Motions.Length <= motionPoint) {
            Logger.Warning("Invalid motion point {MotionPoint} for {SkillRecord}", motionPoint, record);
            session.Send(SkillUseFailedPacket.Fail(record.CastUid, record.SkillId, record.Metadata.Data.Condition.CooldownTime,
                session.Config.GetSkillCooldown(record.SkillId), session.Field?.FieldTick ?? 0));
            return;
        }

        if (session.Player.Animation.PlayingSequence is null) {
            Logger.Warning("Last motion already expired on skill {SkillUid}", skillUid);
        }

        record.TrySetMotionPoint(motionPoint);

        var position = packet.Read<Vector3>();
        var direction = packet.Read<Vector3>(); // either velocity or direction
        var rotation = packet.Read<Vector3>();
        var input = packet.Read<Vector3>(); // x and y match with wasd/arrow input, not normalized
        bool isCharge = packet.ReadBool();
        bool isRelease = packet.ReadBool();
        int unk3 = packet.ReadInt();

        session.Field?.Broadcast(SkillPacket.Sync(record, position, direction, rotation, input, isCharge, isRelease, unk3), session);

        if (session.Player.DebugSkills) {
            session.Send(NoticePacket.Message($"Skill.Sync: {skillId},{skillUid}; AttackPoint: {motionPoint}; IsCharge: {isCharge}; IsReleased: {isRelease}; UnkInt: {unk3}; Direction: {direction}"));
        }

        SkillMetadataMotionProperty motion = record.Metadata.Data.Motions[motionPoint].MotionProperty;

        session.Animation.TryPlaySequence(motion.SequenceName, motion.SequenceSpeed, AnimationType.Skill, record.Metadata);
    }

    private void HandleTickSync(GameSession session, IByteReader packet) {
        long skillUid = packet.ReadLong();
        SkillRecord? record = session.Player.ActiveSkills.Get(skillUid);
        if (record == null) {
            Logger.Warning("Invalid TickSync Skill {SkillUid}", skillUid);
            return;
        }

        record.ServerTick = packet.ReadInt();

        if (session.Player.DebugSkills) {
            session.Send(NoticePacket.Message($"Skill.SyncTick: {skillUid}"));
        }

        // Only a skill that continues while held pays again at a sync, and only its spirit and stamina.
        if (SkillCastRules.SyncPays(record.Metadata.Property.ContinueSkill) && !session.Player.SkillSyncConsume(record)) {
            session.Send(SkillUseFailedPacket.Fail(record.CastUid, record.SkillId, record.Metadata.Data.Condition.CooldownTime,
                session.Config.GetSkillCooldown(record.SkillId), session.Field?.FieldTick ?? 0));
            return;
        }

        string skillSequence = record.Motion.MotionProperty.SequenceName;
        string playingSequence = session.Player.Animation.PlayingSequence?.Name ?? "";

        if (skillSequence != playingSequence) {
            Logger.Warning($"Motion point on skill cast {skillUid} '{skillSequence}' doesn't match playing sequence '{playingSequence}'", skillUid);
            return;
        }

        session.Player.Animation.SetLoopSequence(true, true);
    }

    private void HandleCancel(GameSession session, IByteReader packet) {
        long skillUid = packet.ReadLong();
        SkillRecord? record = session.Player.ActiveSkills.Get(skillUid);
        if (record == null) {
            Logger.Warning("Invalid Cancel Skill {SkillUid}", skillUid);
            return;
        }

        session.Player.InBattle = true;
        session.Field?.Broadcast(SkillPacket.Cancel(record));

        if (session.Player.DebugSkills) {
            session.Send(NoticePacket.Message($"Skill.Cancel: {skillUid}"));
        }

        session.Animation.CancelSequence();
    }
}
