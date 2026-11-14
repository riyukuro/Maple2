using System.Numerics;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
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
using Maple2.Tools.Extensions;

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
        session.Config.SaveCastTime(record.Metadata, record.Motion.MotionProperty, serverTick, startTick);
        session.Config.SaveCastMotion(record.Motion.MotionProperty, record.SkillId, serverTick, startTick);
    }

    // Checked before any cost is paid. A dead player casts only a skill that allows it, and no one casts a skill
    // that a buff held disables or that names other fields, or a skill that takes the caster off the ride while on it. A skill of a job's tree
    // must be in the caster's own tree at that level or above; a skill of no tree must be one some client path gives a player. An item skill must also pass its own condition and
    // be off cooldown, its own and its cooldown group's; a job skill must not come too early after its last cast and must find
    // its hand weapons held and its spirit threshold met.
    // No cast comes before the motion of the last cast lets the next one in. The two timing checks
    // (ConfigManager.CastTimingRefusal) refuse only when CastCheckSetting says so, else they log.
    private bool CastAllowed(GameSession session, SkillRecord record, long tick, int castTick, out SkillCooldown? blocking) {
        blocking = null;
        BeginCondition condition = record.Metadata.Data.Condition;
        if (SkillCastRules.RefusedDead(session.Player.IsDead, condition.AllowDead)) {
            return false;
        }
        SkillRecord? held = session.Player.ActiveSkills.Get(record.CastUid);
        bool laterMotion = SkillCastRules.LaterMotion(record.MotionPoint, record.SkillId, held?.SkillId, held?.MotionPoint ?? 0);
        if (SkillCastRules.TypeDisabled(session.Player.Buffs.SkillUseDisableMask(), SkillCastRules.FieldTypeMask(session.Field.Metadata.Limit.DisableSkills),
                record.Metadata.Property.MainType, laterMotion)) {
            Logger.Debug("Cast refused, skill {SkillId} of main type {MainType} is disabled by a buff held or the field", record.SkillId, record.Metadata.Property.MainType);
            return false;
        }
        // The same holds for a buff that disables the skill itself (answer 0x1f): checked on the first motion only.
        if (!laterMotion && session.Player.Buffs.SkillUseDisabled(record.SkillId)) {
            Logger.Debug("Cast refused, a buff held disables skill {SkillId}", record.SkillId);
            return false;
        }
        if (SkillCastRules.MapRefuses(condition.Maps, session.Field.MapId)) {
            Logger.Debug("Cast refused, skill {SkillId} is not for field {MapId}", record.SkillId, session.Field.MapId);
            return false;
        }
        if (SkillCastRules.RideRefuses(record.Metadata.Property.UnrideOnUse, record.Metadata.Property.MainType,
                session.Ride.Ride is { Action: RideOnActionDefault } ride && ride.OwnerId == session.Player.ObjectId)) {
            Logger.Debug("Cast refused, skill {SkillId} is not cast from a ride", record.SkillId);
            return false;
        }
        SkillInfo skillInfo = session.Config.Skill.SkillInfo;
        bool inAnyTree = skillInfo.InAnyTree(record.SkillId);
        if (SkillCastRules.TreeRefuses(inAnyTree, skillInfo.TreeLevel(record.SkillId), record.Level)) {
            Logger.Debug("Cast refused, skill {SkillId} level {Level} is not in the tree of {Job} at that level", record.SkillId, record.Level, skillInfo.Job);
            return false;
        }
        if (SkillCastRules.SourceRefuses(inAnyTree, record.Metadata.Property.NoPlayerSource)) {
            Logger.Debug("Cast refused, no client path gives skill {SkillId} to a player", record.SkillId);
            return false;
        }
        bool useItem = record.Metadata.Data.Consume.UseItem;
        string? early = session.Config.CastTimingRefusal(record.Metadata, !useItem && SkillCastRules.IsJobSkill(record.SkillId, inAnyTree), castTick, tick, out bool clockTurnedFast);
        if (clockTurnedFast) {
            Logger.Information("Fast clock: the cast ticks of {Player} ({CharacterId}) run ahead of the server's time or jump back", session.PlayerName, session.CharacterId);
        }
        if (early != null) {
            if (CastCheckSetting.Enforce) {
                Logger.Debug("Cast refused, skill {SkillId}, {Reason}", record.SkillId, early);
                return false;
            }
            if (session.Config.CastLog.Count(tick) is > 0 and int unlogged) {
                Logger.Information("Cast not refused (log only), skill {SkillId} of {Player}, {Reason} ({Count} since the last line)", record.SkillId, session.PlayerName, early, unlogged);
            }
        }
        if (!useItem) {
            return HandsAndSpiritAllow(session, record);
        }
        if (!condition.Check(session.Player, session.Player, session.Player)) {
            return false;
        }
        blocking = session.Config.BlockingCooldown(record.Metadata, tick);
        return blocking == null;
    }

    // A job skill's weapon condition and spirit threshold (an item skill's own condition checks both).
    private bool HandsAndSpiritAllow(GameSession session, SkillRecord record) {
        BeginCondition condition = record.Metadata.Data.Condition;
        if (condition.Weapon is { Length: > 0 } weapons) {
            Item? right = session.Item.Equips.Get(EquipSlot.RH);
            Item? left = session.Item.Equips.Get(EquipSlot.LH);
            (int heldRight, int heldLeft) = SkillCastRules.HandTypes(right?.Type.Type ?? 0, right?.Uid ?? 0, right == null || PcItemUse.CanUseItem(session, right),
                left?.Type.Type ?? 0, left?.Uid ?? 0, left == null || PcItemUse.CanUseItem(session, left));
            WeaponActionTable.Action? held = session.TableMetadata.WeaponActionTable.Held(heldRight, heldLeft);
            if (SkillCastRules.WeaponRefuses(weapons.Select(weapon => ((int) weapon.LeftHand.Type, (int) weapon.RightHand.Type)).ToArray(), held)) {
                Logger.Debug("Cast refused, skill {SkillId} needs another weapon than {Left}/{Right} (action {Action})", record.SkillId,
                    held?.LeftHand ?? heldLeft, held?.RightHand ?? heldRight, held == null ? "none" : "held");
                return false;
            }
        }
        if (condition.Stat.TryGetValue(BasicAttribute.Spirit, out long threshold)) {
            FieldPlayer player = session.Player;
            (int invokeValue, float invokeRate) = player.Buffs.GetInvokeValues(InvokeEffectType.ReduceSpiritCost, record.SkillId, record.Metadata.Property.SkillGroup);
            int reduced = SkillCastRules.SpiritCost(threshold, invokeValue, invokeRate, player.Buffs.ConsumeSpRate);
            if (SkillCastRules.SpiritThresholdRefuses(reduced, player.Stats.Values[BasicAttribute.Spirit].Current)) {
                Logger.Debug("Cast refused, skill {SkillId} needs {Spirit} spirit", record.SkillId, reduced);
                return false;
            }
        }
        return true;
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
        if (SkillCastRules.CancelledRefuses(record.CancelTick, session.Field.FieldTick)) {
            Logger.Debug("Attack point dropped, cast {SkillUid} of skill {SkillId} was cancelled {Ms} ms ago", skillUid, record.SkillId, session.Field.FieldTick - record.CancelTick);
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
        bool taken = !SkillHitRules.PointMoved(position, record.CastPosition)
            && record.Hits.TakePoint(attackPoint, iterations, record.Metadata.Property.ContinueSkill, count, session.Field.FieldTick,
                SkillHitRules.MaxAttacks(record.Attack.TargetCount, record.Attack.Arrow.NonTarget == SkillTargetType.Owner),
                session.Config.HeldInterval(record.Motion.MotionProperty, record.CastUid), session.Config.Lag, CastCheckSetting.Enforce);
        if (!taken) {
            Logger.Debug("Attack point dropped: AttackPoint={AttackPoint} iteration={Iteration} count={Count} at {Position}: {Record}",
                attackPoint, iterations, count, position, record);
            return;
        }
        if (record.Hits.Unenforced is { } unenforced && session.Config.PointLog.Count(session.Field.FieldTick) is > 0 and int unlogged) {
            Logger.Information("Attack point not dropped (log only), {Reason}: AttackPoint={AttackPoint} iteration={Iteration} count={Count} of {Player} ({Unlogged} since the last line): {Record}",
                unenforced, attackPoint, iterations, count, session.PlayerName, unlogged, record);
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
            record.Hits.Declare(targetRecord.Uid, attackPoint, targetRecord.TargetId);

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
                record.Hits.Declare(targetRecord.Uid, attackPoint, targetRecord.TargetId);
            }

            session.Player.InBattle = true;

            // When the client reports no valid target (TargetId=0), the NPC may have died since the last
            // SubCommand.Target. Do server-side detection to find hittable corpses in attack range.
            if (targets.All(t => t.TargetId == 0) && session.Field != null) {
                // The facing of the cast's direction (0 faces -y, as Angle2D).
                float angle = Vector3.Zero.Angle2D(record.Direction);
                float reach = SkillUtils.KeyMoveReach(record.Motion.MotionProperty, record.Attack.Arrow, record.Rotate2Z, record.CastDirection);
                Prism attackPrism = record.Attack.GetPrism(record.Position, angle, reach: reach);
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
        if (SkillCastRules.CancelledRefuses(record.CancelTick, session.Field.FieldTick)) {
            Logger.Debug("Attack target dropped, cast {SkillUid} of skill {SkillId} was cancelled {Ms} ms ago", skillUid, record.SkillId, session.Field.FieldTick - record.CancelTick);
            return;
        }

        long targetUid = packet.ReadLong();
        var impactPosition = packet.Read<Vector3>();
        var impactPosition2 = packet.Read<Vector3>();
        var direction = packet.Read<Vector3>();
        byte attackPoint = packet.ReadByte();
        if (!record.TrySetAttackPoint(attackPoint)) {
            Logger.Error("Invalid AttackPoint({AttackPoint}) for {Record}", attackPoint, record);
            return;
        }
        byte count = packet.ReadByte();
        packet.ReadInt(); // the cast's TickSync count so far (CSkillRecord::m_nSendCount), not used here

        // A target packet pays damage only for an attack its cast declared for this attack point, once (a bouncing
        // arrow once per target it can reach, again on a boomerang's way back and after each piercing interval), in
        // time and with the impact near the cast (SkillHitRules). Anything else is dropped without damage.
        session.Field.TableMetadata.MagicPathTable.Entries.TryGetValue(record.Attack.MagicPathId, out IReadOnlyList<MagicPath>? paths);
        if (record.Hits.TooLate(session.Field.FieldTick)
            || SkillHitRules.ImpactRefused(impactPosition, record.CastPosition, session.Player.Position, record.Attack, paths)
            || !record.Hits.TakeTarget(targetUid, attackPoint, record.Attack.Arrow.BounceCount, SkillHitRules.TargetRounds(record.Attack, paths), count)) {
            Logger.Debug("Target packet dropped: attack {AttackUid} AttackPoint={AttackPoint} impact {Impact}: {Record}",
                targetUid, attackPoint, impactPosition, record);
            return;
        }

        record.TargetUid = targetUid;
        record.ImpactPosition = impactPosition;
        record.Direction = direction;

        if (session.Player.DebugSkills) {
            session.Send(NoticePacket.Message($"Skill.Attack.Damage: {skillUid}; AttackPoint: {attackPoint}"));
        }

        // The objects the client hit, each with the number its arrow hit before (CMagic, magicdata.cpp). Each one
        // pays only inside what the client's writer sends for the attack (SkillHitBudget.TakeHit) and when the field
        // has it as an actor the range takes; any other is skipped, the rest of the packet still pays.
        Span<int> ids = stackalloc int[count];
        Span<byte> ordinals = stackalloc byte[count];
        for (int i = 0; i < count; i++) {
            ids[i] = packet.ReadInt();
            ordinals[i] = packet.ReadByte();
        }
        IActor?[] found = new IActor?[count];
        session.Field.FindTargets(record, ids, found);
        SkillHitShape shape = SkillHitRules.Shape(record.Attack, paths);

        // Where the client's writer searched: a region (FUN_141464d00) at the owner's position turned to its facing,
        // which the server checks against its own player position and the cast's or synced facing; an arrow
        // (FUN_141465b30) on its step from the last point to the current one, from a point its paths reach. An object
        // off the area, or any object of a packet whose area is off, is skipped (SkillHitRules.TargetMargin and the
        // others). The step's length and the arrow's speed between two packets are logged only: a frame hitch on the
        // client and a packet the link held up make both, and Reach already holds the points.
        HitRefusal packetRefusal = HitRefusal.None;
        Prism area;
        if (shape.Arrow) {
            float velocity = SkillHitRules.Velocity(paths);
            packetRefusal = SkillHitRules.ArrowRefusal(impactPosition2, record.CastPosition, session.Player.Position, record.Attack.Arrow, paths);
            bool longStep = SkillHitRules.StepTooLong(impactPosition, impactPosition2, velocity);
            bool fast = !record.Hits.TakeStep(targetUid, impactPosition2, session.Field.FieldTick, velocity);
            if ((longStep || fast) && session.Config.StepLog.Count(session.Field.FieldTick) is > 0 and int unloggedSteps) {
                Logger.Information("Arrow step past its path's speed (logged only): step {Step} from {Last} to {Current}, long {Long}, fast since the packet before {Fast}, {Unlogged} since the last line, attack {AttackUid} of {Player}: {Record}",
                    Vector3.Distance(impactPosition, impactPosition2), impactPosition, impactPosition2, longStep, fast, unloggedSteps, targetUid, session.PlayerName, record);
            }
            area = SkillHitRules.ArrowArea(record.Attack.Arrow, impactPosition, impactPosition2, direction);
        } else {
            if (!SkillHitRules.FacingNear(direction.Z, record.Rotation.Z, session.Player.Rotation.Z)) {
                packetRefusal = HitRefusal.Facing;
            }
            if (!SkillHitRules.RegionOwnerTrusted(impactPosition2, session.Player.Position)
                && session.Config.RegionLog.Count(session.Field.FieldTick) is > 0 and int unloggedRegions) {
                Logger.Information("Region owner position {Owner} is {Distance} from the server's {Position}, the area stands at the server's: {Unlogged} since the last line, attack {AttackUid} of {Player}: {Record}",
                    impactPosition2, Vector3.Distance(impactPosition2, session.Player.Position), session.Player.Position, unloggedRegions, targetUid, session.PlayerName, record);
            }
            float reach = SkillUtils.KeyMoveReach(record.Motion.MotionProperty, record.Attack.Arrow, record.Rotate2Z, record.CastDirection);
            area = SkillHitRules.RegionArea(record.Attack, SkillHitRules.RegionCentre(impactPosition2, session.Player.Position), direction.Z, reach);
        }

        int refused = 0;
        HitRefusal reason = HitRefusal.None;
        for (int i = 0; i < count; i++) {
            // TakeHit first, so the attack counts each hit as the client did.
            HitRefusal refusal = record.Hits.TakeHit(targetUid, ids[i], ordinals[i], shape);
            if (refusal == HitRefusal.None) {
                if (found[i] is not { } actor) {
                    refusal = HitRefusal.NoActor;
                } else if (actor == record.Caster) {
                    // The caster the range includes is hit wherever the packet says it searched.
                } else if (packetRefusal != HitRefusal.None) {
                    refusal = packetRefusal;
                } else if (!area.Intersects(actor.Shape)) {
                    refusal = HitRefusal.Outside;
                }
            }
            if (refusal != HitRefusal.None) {
                refused++;
                reason = refusal;
                continue;
            }
            record.Targets.TryAdd(ids[i], found[i]!);
        }
        if (refused > 0 && session.Config.HitLog.Count(session.Field.FieldTick, refused) is > 0 and int unlogged) {
            Logger.Information("Target packet hits skipped: {Refused} of {Count} ({Reason} last), {Unlogged} since the last line, attack {AttackUid} of {Player}: {Record}",
                refused, count, reason, unlogged, targetUid, session.PlayerName, record);
        }

        if (session.Field.RankDuel != null) {
            LogRankDuelTarget(session, record, record.Targets.Values.ToList());
        }
        session.Player.TargetAttack(record);
    }

    // A rank duelist's hit (Attack.Target) that the server finds no player for: where the impact, the caster and the
    // other players were, and whether the field holds them hostile (the duel's fight).
    private void LogRankDuelTarget(GameSession session, SkillRecord record, IReadOnlyCollection<IActor> targets) {
        if (session.Field == null || targets.Any(target => target is FieldPlayer)) {
            return;
        }
        string others = string.Join("; ", session.Field.Players.Values.Where(player => player != session.Player)
            .Select(player => $"{player.Value.Character.Name} at {player.Position} hostile {session.Field.PlayerHostile(session.Player, player)}"));
        Logger.Information("Rank duel hit by {Caster}, skill {SkillId}, attack point {AttackPoint}, apply target {ApplyTarget}: no player in range; impact {Impact}, caster at {Position}; {Others}",
            session.PlayerName, record.SkillId, record.AttackPoint, record.Attack.Range.ApplyTarget, record.ImpactPosition, session.Player.Position, others);
    }

    // The rest of Skill 1 2, which the client sends when an arrow of the cast ends where it places a region
    // (CMagic::FUN_14145f570 and FUN_141466da0, magicdata.cpp): the cast's TickSync count so far
    // (CSkillRecord::m_nSendCount, counted by SendSkill), the arrow's index among those its attack fired
    // (StateSkill::PickAttackTargets numbers them from 0; a cast of several arrows sends one per arrow), the end
    // position and the direction.
    public readonly record struct RegionPath(int SendCount, int Arrow, Vector3 Position, Vector3 Rotation);

    public static RegionPath ReadRegionPath(IByteReader packet) {
        int sendCount = packet.ReadInt();
        int arrow = packet.ReadInt();
        var position = packet.Read<Vector3>();
        var rotation = packet.Read<Vector3>();
        return new RegionPath(sendCount, arrow, position, rotation);
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

        RegionPath path = ReadRegionPath(packet);
        if (session.Player.DebugSkills) {
            session.Send(NoticePacket.Message($"Skill.Attack.Region: {skillUid}; AttackPoint: {attackPoint}; SendCount: {path.SendCount}; Arrow: {path.Arrow}"));
        }

        record.Position = path.Position;
        record.Rotation = path.Rotation;

        // Hide and Seek: where a seek shot lands decides whether it missed (HideAndSeekField.Landed).
        if (record.SkillId == Manager.Field.HideAndSeekField.SeekSkillId) {
            session.Field?.HideAndSeek?.Landed(session.Player, record.Position);
        }
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
        record.Hits.Sync();

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
        if (record.CancelTick == 0 && session.Field != null) {
            record.CancelTick = session.Field.FieldTick;
        }
        session.Field?.Broadcast(SkillPacket.Cancel(record), SkillCastRules.CancelGoesToCaster(endedByServer: false) ? null : session);

        if (session.Player.DebugSkills) {
            session.Send(NoticePacket.Message($"Skill.Cancel: {skillUid}"));
        }
        session.Config.ClearCastMotion(record.SkillId);

        session.Animation.CancelSequence();
    }
}
