using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

public static class SkillUseFailedPacket {
    private enum Command : byte {
        Fail = 5,
        Unknown = 6,
    }

    // Client: CSceneStateGame::OnSkillUseFailed reads byte mode, long cast uid, int skill id, byte error,
    // (mode 6: int state), int cooldown start tick, short cooldown length in 0.1 s, int recharge uses.
    // A length above 0 sets the skill's cooldown from the start tick; length 0 with 0 uses clears it.
    public static ByteWriter Fail(long castUid, int skillId, float cooldownSeconds, SkillCooldown? cooldown, long currentTick) {
        var pWriter = Packet.Of(SendOp.SkillUseFailed);
        pWriter.WriteByte((byte) Command.Fail);
        pWriter.WriteLong(castUid);
        pWriter.WriteInt(skillId);
        pWriter.WriteByte(); // error
        pWriter.WriteCooldown(cooldownSeconds, cooldown, currentTick);

        return pWriter;
    }

    public static void WriteCooldown(this IByteWriter pWriter, float cooldownSeconds, SkillCooldown? cooldown, long currentTick) {
        if (cooldown != null && cooldown.EndTick > currentTick && cooldownSeconds > 0) {
            int tenths = (int) Math.Clamp(cooldownSeconds * 10f, 1f, short.MaxValue);
            pWriter.WriteInt(unchecked((int) (cooldown.EndTick - tenths * 100L)));
            pWriter.WriteShort((short) tenths);
        } else {
            pWriter.WriteInt();
            pWriter.WriteShort();
        }
        pWriter.WriteInt(cooldown?.Charges ?? 0);
    }
}
