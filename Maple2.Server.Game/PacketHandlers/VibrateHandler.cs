using System.Numerics;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
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
        // The client's tag packet (WorldService::FUN_141bc3560, worldservice_1.cpp:9220): an object it hits is shown
        // and has collision, the tag is the cast's own skill, and each tag sends once per object (its entered set).
        // The tick and position after these fields are the player's, not the attack's: the record keeps its own.
        FieldVibrateEntity? vibrate = session.Field?.AccelerationStructure?.GetVibrateEntity(entityId);
        VibrateRules.Refusal refusal = VibrateRules.Check(vibrate != null, session.Field?.Vibrate.IsHidden(entityId) ?? false,
            skillId == record.SkillId && level == record.Level,
            () => record.Vibrates.Take(attackPoint, entityId, record.Hits.PointIterations(attackPoint)));
        if (refusal != VibrateRules.Refusal.None) {
            Logger.Debug("Vibrate {EntityId} refused ({Refusal}): skill {SkillId} level {Level} point {AttackPoint}: {Record}",
                entityId, refusal, skillId, level, attackPoint, record);
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
