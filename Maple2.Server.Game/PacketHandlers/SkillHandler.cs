using System.Numerics;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Metadata.FieldEntity;
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
        if (!SkillCastCheck.Allowed(session, record, startTick, serverTick, out blocking) || !session.Player.SkillCastConsume(record)) {
            // Nothing was paid. The client ends the cast and takes the cooldown from this answer
            // (CSceneStateGame::OnSkillUseFailed, mode 5).
            session.Send(SkillUseFailedPacket.Fail(record.CastUid, record.SkillId, record.Metadata.Data.Condition.CooldownTime,
                blocking ?? session.Config.GetSkillCooldown(record.SkillId), startTick));
            return;
        }

        // A held object weapon is let go when a skill that releases it starts, once the cast is taken (StateSkill::vfunc_4, stateskill.cpp:481;
        // releaseObjectWeapon is true unless the skill's data says 0), whichever skill it is.
        if (session.HeldLiftup != null && record.Metadata.Property.ReleaseObjectWeapon) {
            session.HeldLiftup = null;
        }
        session.Player.InBattle = record.Metadata.State.InBattle;
        if (record.Metadata.State.InBattle) {
            session.Player.BattleCastShowsStance();
        }
        session.Player.ActiveSkills.Add(record, startTick, SkillHitRules.CastKeepMs(record.Metadata, session.Field.TableMetadata.MagicPathTable.Entries));
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

        List<List<TargetRecord>>? tags = ReadPointTags(packet, count, record.Attack.Arrow.BounceCount);
        if (tags == null) {
            Logger.Debug("Attack point dropped: more linked tags than the arrow bounces ({Bounce}): {Record}", record.Attack.Arrow.BounceCount, record);
            return;
        }
        // Only a point that is taken moves the cast (a dropped one leaves its position and direction).
        record.Position = position;
        record.Direction = direction;
        DeclarePointTags(record.Hits, attackPoint, iterations, tags);
        foreach (List<TargetRecord> targets in tags) {
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

    // The attack point packet's tags after its count: each a first tag (uid, target id, its index m_n84,
    // stateskill.cpp:1381) and while a bool is set a linked one (uid, target id, m_n80, m_n84, :1390-1391).
    // A tag's links are its arrow's bounces: the client adds at most one per call, from the attack's bounceCount down
    // to 1, and none when it is 0 (StateSkill::FUN_1417f3e80, stateskill.cpp:2615-2622, 2690-2784). More than maxLinks on one tag gives null and
    // the point is dropped; before, any number was read, declared and relayed to the whole field.
    public static List<List<TargetRecord>>? ReadPointTags(IByteReader packet, byte count, int maxLinks) {
        var tags = new List<List<TargetRecord>>(count);
        for (byte i = 0; i < count; i++) {
            var targets = new List<TargetRecord>();
            var targetRecord = new TargetRecord {
                Uid = packet.ReadLong(),
                TargetId = packet.ReadInt(),
                Unknown = packet.ReadByte(),
            };
            targets.Add(targetRecord);

            // While more targets in packet.
            while (packet.ReadBool()) {
                if (targets.Count > maxLinks) {
                    return null;
                }
                targetRecord = new TargetRecord {
                    PrevUid = targetRecord.Uid,
                    Uid = packet.ReadLong(),
                    TargetId = packet.ReadInt(),
                    Unknown = packet.ReadByte(),
                    Index = packet.ReadByte(),
                };
                targets.Add(targetRecord);
            }
            tags.Add(targets);
        }
        return tags;
    }

    // Declares each tag's attack and arrow: the arrow index is m_n84, the first tag's Unknown and a linked tag's
    // Index (a linked bounce carries 0, stateskill.cpp:2777-2781), so a region path finds its arrow's key.
    public static void DeclarePointTags(SkillHitBudget hits, byte attackPoint, int iterations, IEnumerable<List<TargetRecord>> tags) {
        foreach (List<TargetRecord> targets in tags) {
            for (int i = 0; i < targets.Count; i++) {
                TargetRecord target = targets[i];
                hits.Declare(target.Uid, attackPoint, target.TargetId);
                hits.DeclareArrow(attackPoint, iterations, i == 0 ? target.Unknown : target.Index, target.Uid);
            }
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
        Vector3? sightOwner = null;
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
            // The client stops an arrow with rayPhysxTest short of a wall (SkillHitRules.StepBlocker); a step through one
            // is logged only, its hits pay.
            if (record.Attack.Arrow.RayPhysXTest
                && SkillHitRules.StepBlocker(impactPosition, impactPosition2, SkillHitRules.StepBoxes(session.Field.AccelerationStructure, impactPosition, impactPosition2)) is { } wall
                && session.Config.SightLog.Count(session.Field.FieldTick) is > 0 and int unloggedWalls) {
                Logger.Information("Arrow step through a box collider the client's ray stops at (logged only): from {Last} to {Current}, {Kind} {Box} {Min}-{Max}, {Unlogged} since the last line, attack {AttackUid} of {Player}: {Record}",
                    impactPosition, impactPosition2, wall.IsWhiteBox ? "whitebox" : "cube", wall.Id, wall.Bounds.Min, wall.Bounds.Max, unloggedWalls, targetUid, session.PlayerName, record);
            }
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
            Vector3 centre = SkillHitRules.RegionCentre(impactPosition2, session.Player.Position);
            area = SkillHitRules.RegionArea(record.Attack, centre, direction.Z, reach);
            // The client's ray starts where the region stands, the owner's position (SkillHitRules.SightTest).
            if (record.Attack.Arrow.RayPhysXTest) {
                sightOwner = centre;
            }
        }

        int refused = 0;
        HitRefusal reason = HitRefusal.None;
        List<IActor>? sightHits = sightOwner == null ? null : [];
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
            if (sightHits != null && found[i] != record.Caster) {
                sightHits.Add(found[i]!);
            }
            record.Targets.TryAdd(ids[i], found[i]!);
        }
        if (sightOwner is { } owner && sightHits is { Count: > 0 }) {
            LogSight(session, owner, sightHits, count, targetUid, record);
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
        if (session.Field == null) {
            return;
        }

        long skillUid = packet.ReadLong();
        SkillRecord? record = session.Player.ActiveSkills.Get(skillUid);
        if (record == null) {
            Logger.Warning("Invalid Attack-Splash Skill {SkillUid}", skillUid);
            return;
        }
        if (SkillCastRules.CancelledRefuses(record.CancelTick, session.Field.FieldTick)) {
            Logger.Debug("Region path dropped, cast {SkillUid} of skill {SkillId} was cancelled {Ms} ms ago", skillUid, record.SkillId, session.Field.FieldTick - record.CancelTick);
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

        // Only an arrow an attack point packet of the cast declared sends one, once at its end and once per piercing
        // interval, in the hit window, from a point its paths reach (SkillHitBudget.TakeRegionPath and
        // SkillHitRules.ReachTooFar, as a target packet's). Anything else places nothing.
        session.Field.TableMetadata.MagicPathTable.Entries.TryGetValue(record.Attack.MagicPathId, out IReadOnlyList<MagicPath>? paths);
        bool far = SkillHitRules.ReachTooFar(path.Position, record.CastPosition, record.Attack.Arrow, paths, session.Player.Position);
        if (far || !record.Hits.TakeRegionPath(attackPoint, path.SendCount, path.Arrow, SkillHitRules.RegionPathRounds(record.Attack, paths), session.Field.FieldTick,
                SkillHitRules.RegionPathHitCap(record.Attack))) {
            if (session.Config.HitLog.Count(session.Field.FieldTick) is > 0 and int unlogged) {
                Logger.Information("Region path dropped: AttackPoint={AttackPoint} send count {SendCount} arrow {Arrow} at {Position}, past the paths' reach {Far}, {Unlogged} since the last line, {Player}: {Record}",
                    attackPoint, path.SendCount, path.Arrow, path.Position, far, unlogged, session.PlayerName, record);
            }
            return;
        }

        record.Position = path.Position;
        record.Rotation = path.Rotation;

        // Hide and Seek: where a seek shot lands decides whether it missed (HideAndSeekField.Landed).
        if (record.SkillId == Manager.Field.HideAndSeekField.SeekSkillId) {
            session.Field.HideAndSeek?.Landed(session.Player, record.Position);
        }
        session.Field.AddSkill(record);
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

    // The client drops a region hit its line of sight blocks (SkillHitRules.SightTest); the server only logs it, the
    // hit pays.
    private void LogSight(GameSession session, Vector3 owner, List<IActor> hits, int count, long targetUid, SkillRecord record) {
        static float Radius(IActor actor) => actor.Shape.Polygon is Circle circle ? circle.Radius : 0;
        List<FieldBoxColliderEntity> boxes = SkillHitRules.SightBoxes(session.Field.AccelerationStructure, owner, hits.Select(hit => (hit.Position, Radius(hit))));
        int behind = 0, overhead = 0;
        FieldBoxColliderEntity? blocker = null;
        foreach (IActor hit in hits) {
            switch (SkillHitRules.SightTest(owner, hit.Position, Radius(hit), boxes, out FieldBoxColliderEntity? box)) {
                case SkillHitRules.SightDrop.Blocked:
                    behind++;
                    blocker = box;
                    break;
                case SkillHitRules.SightDrop.Overhead:
                    overhead++;
                    break;
            }
        }
        if (behind + overhead > 0 && session.Config.SightLog.Count(session.Field.FieldTick, behind + overhead) is > 0 and int unlogged) {
            Logger.Information("Region hits the client's line of sight drops (logged only): {Behind} past a box collider, {Overhead} straight above or below the owner, of {Count}, last past {Kind} {Box} {Min}-{Max}, owner {Owner}, {Unlogged} since the last line, attack {AttackUid} of {Player}: {Record}",
                behind, overhead, count, blocker == null ? "none" : blocker.IsWhiteBox ? "whitebox" : "cube", blocker?.Id, blocker?.Bounds.Min, blocker?.Bounds.Max, owner, unlogged, targetUid, session.PlayerName, record);
        }
    }

    private void HandleTickSync(GameSession session, IByteReader packet) {
        long skillUid = packet.ReadLong();
        SkillRecord? record = session.Player.ActiveSkills.Get(skillUid);
        if (record == null) {
            Logger.Warning("Invalid TickSync Skill {SkillUid}", skillUid);
            return;
        }

        record.ServerTick = packet.ReadInt();
        // A sync forgets what the cast's point groups hit only as often as the client sends one: at each loop end of
        // a looping motion (SkillHitBudget.Sync). A sync is refused only when the motion's sequence is known to have
        // no loop and the skill does not continue while held. A sync refused here is still taken for the rest.
        // When the loop is not known (a prefixed sequence the client may play instead loops for its own length), the
        // syncs are not spaced.
        long intervalMs = session.Config.HeldInterval(record.Motion.MotionProperty, record.CastUid);
        bool loopUnknown = session.Config.LoopUnknown(record.Motion.MotionProperty);
        if (loopUnknown) {
            intervalMs = 0;
        }
        long tick = session.Field?.FieldTick ?? 0;
        // The repeat also waits for the skill's cooldown or a recharge use (ConfigManager.SyncTimingRefusal, which
        // draws once on the lag budget for that and the loop's time together); one sent before keeps the hits too,
        // and an accepted one of a recharge skill takes a use.
        bool loops = SkillCastRules.SyncLoops(intervalMs, record.Metadata.Property.ContinueSkill, loopUnknown);
        string? early = loops ? session.Config.SyncTimingRefusal(record.Metadata,
            !record.Metadata.Data.Consume.UseItem && SkillCastRules.IsJobSkill(record.SkillId, session.Config.Skill.SkillInfo.InAnyTree(record.SkillId)),
            record.ServerTick, tick, record.Hits.SyncEarlyBy(tick, intervalMs)) : null;
        if (early != null) {
            Logger.Debug("Tick sync keeps the point group hits, the repeat comes before the client sends it, on the cooldown, a recharge use or the loop's end ({Reason}): {Record}", early, record);
        } else if (!record.Hits.Sync(tick, loops, intervalMs, earlyDrawn: true)) {
            Logger.Debug("Tick sync keeps the point group hits (no loop): interval {Interval} ms: {Record}", intervalMs, record);
        } else {
            session.Config.TakeSyncRechargeUse(record.Metadata, record.ServerTick, tick);
        }

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
