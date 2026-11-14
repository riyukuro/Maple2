using System.Numerics;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Model.Metadata.FieldEntity;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
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

        SkillRecord? record = session.Player.ActiveSkills.Get(skillUid);
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
        FieldVibrateEntity? vibrate = session.Field?.AccelerationStructure?.GetVibrateEntity(entityId);
        VibrateRules.Refusal refusal = VibrateRules.Check(vibrate != null, session.Field?.Vibrate.IsHidden(entityId) ?? false,
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
        IReadOnlyDictionary<long, IReadOnlyList<MagicPath>> paths = session.Field!.TableMetadata.MagicPathTable.Entries;
        paths.TryGetValue(attack.MagicPathId, out IReadOnlyList<MagicPath>? magicPaths);
        paths.TryGetValue(attack.CubeMagicPathId, out IReadOnlyList<MagicPath>? cubePaths);
        float boxReach = SkillHitRules.BreakableReach(0, vibrate!.ShapeDimensions);
        if (SkillHitRules.BreakableTooFar(vibrate.Position, record.CastPosition, session.Player.Position, attack, magicPaths, cubePaths, boxReach)) {
            Logger.Debug("Vibrate {EntityId} refused: at {Position}, past the reach of cast {SkillUid} skill {SkillId} point {MotionPoint},{AttackPoint} from {Cast} (player at {Player})",
                entityId, vibrate.Position, skillUid, skillId, motionPoint, attackPoint, record.CastPosition, session.Player.Position);
            return;
        }

        DamageRecord damage = new(record.Metadata, record.Attack) {
            CasterId = session.Player.ObjectId,
            TargetUid = record.TargetUid,
            OwnerId = session.Player.ObjectId,
            SkillId = record.SkillId,
            Level = record.Level,
            MotionPoint = record.MotionPoint,
            AttackPoint = record.AttackPoint,
            Position = record.Position,
            Direction = record.Direction,
        };

        if (vibrate!.BreakDefense < record.Attack.BrokenOffence) {
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
