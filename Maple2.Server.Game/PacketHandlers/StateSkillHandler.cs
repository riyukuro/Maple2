using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

public class StateSkillHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.StateSkill;

    public override void Handle(GameSession session, IByteReader packet) {
        if (session.Field is null) return;
        byte function = packet.ReadByte();
        if (function != 0) {
            Logger.Warning("Unhandled StateSkill function: {Function}", function);
            return;
        }

        long skillCastUid = packet.ReadLong();
        int serverTick = packet.ReadInt();
        int skillId = packet.ReadInt();
        short skillLevel = packet.ReadShort();
        var state = (ActorState) packet.ReadInt();
        int clientTick = packet.ReadInt();
        long itemUid = packet.ReadLong();

        // A state skill not taken is answered with SkillUseFailed 6, so the client leaves the state it entered.
        // The item is the quick slot's (CSkillSystem::FUN_141764e30, skillslot.cpp:2092): one the player holds at a
        // level the item allows; a glide takes only a glider (FUN_14176a8c0, skillslot.cpp:3155: the item's glide id,
        // CItemData::GetGlideItemId, is 0 for an item not of type 5 and has no glide data).
        Item? item = itemUid != 0 ? session.Item.Inventory.Get(itemUid) : null;
        if (itemUid != 0 && !StateItemAllowed(item, state, session.Player.Value.Character.Level)) {
            Refuse(session, skillCastUid, skillId, state, null);
            return; // Invalid item
        }

        if (!session.Field.SkillMetadata.TryGet(skillId, skillLevel, out SkillMetadata? metadata)) {
            Refuse(session, skillCastUid, skillId, state, null);
            return;
        }

        // The client sends a state only for the skill's own state (SkillCastRules.StateRefuses).
        if (SkillCastRules.StateRefuses(state, metadata.Property.MainType, metadata.Property.State, itemUid)) {
            Logger.Debug("State skill refused, skill {SkillId} sent with state {State}", skillId, state);
            Refuse(session, skillCastUid, skillId, state, metadata);
            return;
        }

        // The client's checks of a state skill are those of any skill (MyPC::vfunc_102, CSkillSystem), so the
        // same cast checks run here, without the timing ones: no state-skill cast time is kept.
        var cast = new SkillRecord(metadata, skillCastUid, session.Player) { ItemUid = itemUid };
        if (!SkillCastCheck.Allowed(session, cast, session.Field.FieldTick, null, out SkillCooldown? blocking)
            || !session.Player.SkillCastConsume(cast)) {
            Refuse(session, skillCastUid, skillId, state, metadata, blocking);
            return;
        }

        cast.StateNextTick = SkillCastRules.NextStateTick(session.Field.FieldTick);
        session.Player.ActiveSkills.Add(cast, session.Field.FieldTick, SkillHitRules.CastKeepMs(metadata, session.Field.TableMetadata.MagicPathTable.Entries));
        session.Field.Broadcast(SkillPacket.StateSkill(session.Player, skillId, skillCastUid));
    }

    public const int GliderType = 5;

    public static bool StateItemAllowed(Item? item, ActorState state, short level) {
        if (item == null || item.Metadata.Limit.Level > level) {
            return false;
        }
        return state != ActorState.Glide || item.Metadata.Property.Type == GliderType;
    }

    private static void Refuse(GameSession session, long castUid, int skillId, ActorState state, SkillMetadata? metadata, SkillCooldown? blocking = null) {
        session.Send(SkillUseFailedPacket.StateFail(castUid, skillId, (int) state, metadata?.Data.Condition.CooldownTime ?? 0f,
            blocking ?? session.Config.GetSkillCooldown(skillId), session.Field?.FieldTick ?? 0));
    }
}
