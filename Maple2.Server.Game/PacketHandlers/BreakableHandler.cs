using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class BreakableHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.Breakable;

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

        // The client's tag packet (WorldService::FUN_141bc3560, worldservice_1.cpp:9302): a tag is made only by the
        // player's own cast, with the cast's id (StateSkill, stateskill.cpp:2607), when its attack point fires. So
        // the cast is one of the player's, the skill is its own, and its attack point's packet declared a tag.
        SkillRecord? record = session.Player.ActiveSkills.Get(skillUid);
        if (!BreakableRules.FromCast(record != null, record?.SkillId == skillId && record.Level == level,
                record?.Hits.PointAttacks(attackPoint) ?? 0)) {
            Logger.Debug("Breakable {EntityId} refused: cast {SkillUid} skill {SkillId} level {Level} point {MotionPoint},{AttackPoint}",
                entityId, skillUid, skillId, level, motionPoint, attackPoint);
            return;
        }

        if (session.Field?.TryGetBreakable(entityId, out FieldBreakable? breakable) == true) {
            // Without this every repeat of the packet dropped the box again and counted for the condition.
            if (!BreakableRules.CanBreak(breakable.State)) {
                return;
            }
            if (breakable.Value.GlobalDropBoxId != 0) {
                ICollection<Item> items = session.Field.ItemDrop.GetGlobalDropItems(breakable.Value.GlobalDropBoxId, session.Field.Metadata.Drop.Level, session);
                foreach (Item item in items) {
                    session.Field.DropItem(breakable.Position, breakable.Rotation, item, owner: breakable, characterId: session.CharacterId);
                }
            }
            session.ConditionUpdate(ConditionType.breakable_object);
            session.Field.Survival?.Breakable(session.Player);
            breakable.UpdateState(BreakableState.Break);
        } else {
            Console.WriteLine(entityId + " does not exist...");
        }
    }
}
