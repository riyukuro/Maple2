using System.Numerics;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Model.Metadata.FieldEntity;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

public class VibrateHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.Vibrate;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required SkillMetadataStorage SkillMetadata { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        string entityId = packet.ReadString();
        long skillUid = packet.ReadLong();
        int skillId = packet.ReadInt();
        short level = packet.ReadShort();
        byte motionPoint = packet.ReadByte();
        byte attackPoint = packet.ReadByte();

        if (session.Field == null) {
            return;
        }
        // A region skill's hit makes its tags with uid 0 and the region's skill (SkillDamage Target with the server
        // flag, scenestategame.cpp:5567), the same tag set as a cast's (as for Breakable).
        if (skillUid == 0) {
            HandleRegion(session, session.Field, entityId, skillId, level, motionPoint, attackPoint);
            return;
        }

        // The cast is looked up among the player's recent casts, not the three pending ones: the client keeps a cast's
        // areas after the player cast again, so a magic still on its way tags with a cast the pending queue has dropped
        // (as for Breakable; SkillQueue, SkillHitRules.CastKeepMs).
        SkillRecord? record = session.Player.ActiveSkills.GetRecent(skillUid, session.Field.FieldTick);
        if (record == null) {
            Logger.Warning("Invalid Skill {SkillUid}", skillUid);
            return;
        }
        // The tag is the attack point's own: the cast's skill data has its motion and attack point. Without this the
        // reach check below had no attack to measure and any object of the map took the packet.
        SkillMetadataAttack? attack = record.Metadata.Data.Motions.ElementAtOrDefault(motionPoint)?.Attacks.ElementAtOrDefault(attackPoint);
        if (attack == null) {
            Logger.Debug("Vibrate {EntityId} refused: cast {SkillUid} skill {SkillId} has no point {MotionPoint},{AttackPoint}",
                entityId, skillUid, skillId, motionPoint, attackPoint);
            return;
        }
        // The client's tag packet (WorldService::FUN_141bc3560, worldservice_1.cpp:9220): an object it hits is shown
        // and has collision, the tag is the cast's own skill, and each tag sends once per object (its entered set).
        // The tick and position after these fields are the player's, not the attack's: the record keeps its own.
        FieldVibrateEntity? vibrate = session.Field.AccelerationStructure?.GetVibrateEntity(entityId);
        VibrateRules.Refusal refusal = VibrateRules.Check(vibrate != null, session.Field.Vibrate.IsHidden(entityId),
            skillId == record.SkillId && level == record.Level,
            () => record.Vibrates.Take(attackPoint, entityId, record.Hits.PointAttacks(attackPoint)));
        if (refusal != VibrateRules.Refusal.None) {
            Logger.Debug("Vibrate {EntityId} refused ({Refusal}): skill {SkillId} level {Level} point {AttackPoint}: {Record}",
                entityId, refusal, skillId, level, attackPoint, record);
            return;
        }

        // The tag's area met the object's box (ShapeDimensions, worldservice_1.cpp:6830-6864), so the object lies
        // within the attack's reach of the cast or the player (SkillHitRules.BreakableTooFar, the same test as a
        // breakable's). Before, any vibrate object of the map took the packet.
        if (TooFar(session.Field, vibrate!, attack, record.CastPosition, session.Player.Position)) {
            Logger.Debug("Vibrate {EntityId} refused: at {Position}, past the reach of cast {SkillUid} skill {SkillId} point {MotionPoint},{AttackPoint} from {Cast} (player at {Player})",
                entityId, vibrate.Position, skillUid, skillId, motionPoint, attackPoint, record.CastPosition, session.Player.Position);
            return;
        }

        Take(session, vibrate, entityId, new DamageRecord(record.Metadata, record.Attack) {
            CasterId = session.Player.ObjectId,
            TargetUid = record.TargetUid,
            OwnerId = session.Player.ObjectId,
            SkillId = record.SkillId,
            Level = record.Level,
            MotionPoint = record.MotionPoint,
            AttackPoint = record.AttackPoint,
            Position = record.Position,
            Direction = record.Direction,
        });
    }

    // A region's tag: one of the player's regions of the skill and level, in the field or removed within the hit
    // window (FieldManager.FieldSkillsOf), whose attack point reaches the object from one of its points, and each tag
    // the region's fires placed for the attack (one per fire, one per bounce target for a bounce) sends once per
    // object (FieldSkill.TakeVibrate).
    private void HandleRegion(GameSession session, FieldManager field, string entityId, int skillId, short level, byte motionPoint, byte attackPoint) {
        FieldVibrateEntity? vibrate = field.AccelerationStructure?.GetVibrateEntity(entityId);
        if (vibrate == null || field.Vibrate.IsHidden(entityId)) {
            Logger.Debug("Vibrate {EntityId} refused: no shown object for region skill {SkillId} level {Level}", entityId, skillId, level);
            return;
        }
        foreach (FieldSkill region in field.FieldSkillsOf(session.Player, skillId, level)) {
            if (region.Value.Data.Motions.ElementAtOrDefault(motionPoint)?.Attacks.ElementAtOrDefault(attackPoint) is not { } attack) {
                continue;
            }
            Vector3[] points = region.Points.Length > 0 ? region.Points : [region.Position];
            if (points.All(point => TooFar(field, vibrate, attack, point, point)) || !region.TakeVibrate(entityId, motionPoint, attackPoint)) {
                continue;
            }
            Take(session, vibrate, entityId, new DamageRecord(region.Value, attack) {
                CasterId = session.Player.ObjectId,
                OwnerId = region.ObjectId,
                SkillId = skillId,
                Level = level,
                MotionPoint = motionPoint,
                AttackPoint = attackPoint,
                Position = region.Position,
                Direction = region.Rotation,
            });
            return;
        }
        Logger.Debug("Vibrate {EntityId} refused: no region of skill {SkillId} level {Level} point {MotionPoint},{AttackPoint} reaches it with a fire left",
            entityId, skillId, level, motionPoint, attackPoint);
    }

    private static bool TooFar(FieldManager field, FieldVibrateEntity vibrate, SkillMetadataAttack attack, Vector3 from, Vector3 player) {
        IReadOnlyDictionary<long, IReadOnlyList<MagicPath>> paths = field.TableMetadata.MagicPathTable.Entries;
        paths.TryGetValue(attack.MagicPathId, out IReadOnlyList<MagicPath>? magicPaths);
        paths.TryGetValue(attack.CubeMagicPathId, out IReadOnlyList<MagicPath>? cubePaths);
        float boxReach = SkillHitRules.BreakableReach(0, vibrate.ShapeDimensions);
        return SkillHitRules.BreakableTooFar(vibrate.Position, from, player, attack, magicPaths, cubePaths, boxReach);
    }

    private static void Take(GameSession session, FieldVibrateEntity vibrate, string entityId, DamageRecord damage) {
        if (vibrate.BreakDefense < damage.AttackMetadata.BrokenOffence) {
            //TODO: Keep a record of when the vibrate was broken.
        }

        if (!string.IsNullOrEmpty(vibrate.MaterialTag)) {
            // Each hit counts with the object's material (70400059 "Hit a tree 20 times": code "wood").
            session.ConditionUpdate(ConditionType.vibrate_object, codeString: vibrate.MaterialTag);
        }

        // Seen by the others in the field too.
        session.Field?.Broadcast(VibratePacket.Attack(entityId, damage));
    }
}
