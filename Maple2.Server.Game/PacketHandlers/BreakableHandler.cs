using System.Collections.Concurrent;
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
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

public class BreakableHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.Breakable;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required SkillMetadataStorage SkillMetadata { private get; init; }
    // ReSharper restore All
    #endregion

    // The skill levels missing from the skill data that a region hit has named, logged once each.
    private static readonly ConcurrentDictionary<(int, short), byte> UnknownLevels = new();

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
        // A region skill's hit (SkillDamage Target with the server flag, scenestategame.cpp:5567) makes its tags with
        // uid 0 and the region's skill, owned by the local player: the player's region of that skill is in the field.
        // The cast is looked up among the player's recent casts, not the three pending ones: the client keeps a cast's
        // areas after the player cast again, so a magic still on its way names a cast the pending queue has dropped. A
        // cast is kept while its magic may still land (SkillQueue, SkillHitRules.CastKeepMs).
        SkillRecord? record = session.Field == null ? null : session.Player.ActiveSkills.GetRecent(skillUid, session.Field.FieldTick);
        bool fromRegion = skillUid == 0 && session.Field?.HasFieldSkill(session.Player, skillId, level) == true;
        SkillMetadata? skill = fromRegion
            ? SkillMetadata.TryGet(skillId, level, out SkillMetadata? regionSkill) ? regionSkill : null
            : record?.Metadata;
        bool fromCast = fromRegion || BreakableRules.FromCast(record != null, record?.SkillId == skillId && record.Level == level,
            record?.Hits.PointAttacks(attackPoint) ?? 0);
        bool? breaks = fromCast ? BreakableRules.AttackBreaks(skill, motionPoint, attackPoint) : false;
        if (breaks == null && UnknownLevels.TryAdd((skillId, level), 0)) {
            Logger.Warning("Breakable {EntityId}: skill {SkillId} level {Level} is not in the skill data, taken",
                entityId, skillId, level);
        }
        if (breaks == false) {
            Logger.Information("Breakable {EntityId} refused: cast {SkillUid} skill {SkillId} level {Level} point {MotionPoint},{AttackPoint}",
                entityId, skillUid, skillId, level, motionPoint, attackPoint);
            return;
        }

        if (session.Field?.TryGetBreakable(entityId, out FieldBreakable? breakable) == true) {
            // Without this every repeat of the packet dropped the box again and counted for the condition.
            if (!BreakableRules.CanBreak(breakable.State)) {
                return;
            }
            // The tag's area met the breakable's box, so the breakable lies within the attack's reach of the cast
            // (SkillHitRules.BreakableTooFar). Before, any breakable of the map broke for any cast whose attack point
            // breaks. A region's hit (its own position, placed by the server's region checks) and a skill level the
            // server's data lacks are not checked.
            if (breaks == true && !fromRegion && record != null && skill != null) {
                SkillMetadataAttack attack = skill.Data.Motions[motionPoint].Attacks[attackPoint];
                IReadOnlyDictionary<long, IReadOnlyList<MagicPath>> paths = session.Field.TableMetadata.MagicPathTable.Entries;
                paths.TryGetValue(attack.MagicPathId, out IReadOnlyList<MagicPath>? magicPaths);
                paths.TryGetValue(attack.CubeMagicPathId, out IReadOnlyList<MagicPath>? cubePaths);
                float boxReach = SkillHitRules.BreakableReach(breakable.Value.MoveDistance, breakable.Value.ShapeDimensions);
                if (SkillHitRules.BreakableTooFar(breakable.Position, record.CastPosition, session.Player.Position, attack, magicPaths, cubePaths, boxReach)) {
                    Logger.Information("Breakable {EntityId} refused: at {Position}, past the reach of cast {SkillUid} skill {SkillId} point {MotionPoint},{AttackPoint} from {Cast} (player at {Player})",
                        entityId, breakable.Position, skillUid, skillId, motionPoint, attackPoint, record.CastPosition, session.Player.Position);
                    return;
                }
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
