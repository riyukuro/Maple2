using System.Numerics;
using Maple2.Model.Common;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Packets;

public static class SkillPacket {
    public static ByteWriter Use(SkillRecord skill) {
        var pWriter = Packet.Of(SendOp.SkillUse);
        pWriter.WriteLong(skill.CastUid);
        pWriter.WriteInt(skill.ServerTick);
        pWriter.WriteInt(skill.Caster.ObjectId);
        pWriter.WriteInt(skill.SkillId);
        pWriter.WriteShort(skill.Level);
        pWriter.WriteByte(skill.MotionPoint);
        pWriter.Write<Vector3S>(skill.Position);
        pWriter.Write<Vector3>(skill.Direction);
        pWriter.Write<Vector3>(skill.Rotation);
        pWriter.WriteShort((short) (skill.Rotate2Z * 10));
        pWriter.WriteBool(skill.Unknown);
        pWriter.WriteBool(skill.IsHold);
        if (skill.IsHold) {
            pWriter.WriteInt(skill.HoldInt);
            pWriter.WriteUnicodeString(skill.HoldString);
        }

        return pWriter;
    }

    // Client: CSceneStateGame::OnSkillSync. A cast the client already shows takes the rotation, the input vector and
    // the two flags from here; a cast it does not show yet is started from the position and direction.
    public static ByteWriter Sync(SkillRecord skill, in Vector3 position, in Vector3 direction, in Vector3 rotation, in Vector3 input, bool isCharge, bool isRelease, int speedBits) {
        var pWriter = Packet.Of(SendOp.SkillSync);
        pWriter.WriteLong(skill.CastUid);
        pWriter.WriteInt(skill.Caster.ObjectId);
        pWriter.WriteInt(skill.SkillId);
        pWriter.WriteShort(skill.Level);
        pWriter.WriteByte(skill.MotionPoint);
        pWriter.Write<Vector3>(position);
        pWriter.Write<Vector3>(direction);
        pWriter.Write<Vector3>(rotation);
        pWriter.Write<Vector3>(input);
        pWriter.WriteBool(isCharge);
        pWriter.WriteBool(isRelease);
        pWriter.WriteInt(speedBits); // the client reads a float; the 4 bytes are relayed as the caster sent them

        return pWriter;
    }

    public static ByteWriter Cancel(SkillRecord skill) => Cancel(skill.CastUid, skill.Caster.ObjectId);

    public static ByteWriter Cancel(long castUid, int casterId) {
        var pWriter = Packet.Of(SendOp.SkillCancel);
        pWriter.WriteLong(castUid);
        pWriter.WriteInt(casterId);

        return pWriter;
    }

    // Mode 0 of SkillUseFailed; SkillUseFailedPacket.Fail is mode 5 with the same fields.
    public static ByteWriter Fail(long castUid, int skillId, float cooldownSeconds, SkillCooldown? cooldown, long currentTick) {
        var pWriter = Packet.Of(SendOp.SkillUseFailed);
        pWriter.WriteByte();
        pWriter.WriteLong(castUid);
        pWriter.WriteInt(skillId);
        pWriter.WriteByte(); // error
        pWriter.WriteCooldown(cooldownSeconds, cooldown, currentTick);

        return pWriter;
    }

    // Client: CSceneStateGame::OnStateSkill mode 6 reads int object id, long cast uid, int skill id, int state.
    public static ByteWriter StateSkill(FieldPlayer player, int skillId, long skillCastUid) {
        var pWriter = Packet.Of(SendOp.StateSkill);
        pWriter.WriteByte(0x06);
        pWriter.WriteInt(player.ObjectId);
        pWriter.WriteLong(skillCastUid);
        pWriter.WriteInt(skillId);
        pWriter.WriteInt((int) player.State);

        return pWriter;
    }

    public static ByteWriter Cooldown(params SkillCooldown[] cooldowns) {
        var pWriter = Packet.Of(SendOp.SkillCooldown);
        pWriter.WriteByte((byte) cooldowns.Length);
        foreach (SkillCooldown cooldown in cooldowns) {
            pWriter.WriteClass<SkillCooldown>(cooldown);
        }

        return pWriter;
    }

    public static ByteWriter ResetCooldown() {
        var pWriter = Packet.Of(SendOp.SkillResetCooldown);

        return pWriter;
    }

    public static ByteWriter InBattle(FieldPlayer player) {
        var pWriter = Packet.Of(SendOp.UserBattle);
        pWriter.WriteInt(player.ObjectId);
        pWriter.WriteBool(player.InBattle);

        return pWriter;
    }
}
