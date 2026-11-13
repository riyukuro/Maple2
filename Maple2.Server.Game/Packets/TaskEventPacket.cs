using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// 0xF9, read by CSceneStateGame (scenestategame.cpp:9280): byte type (1 Goldus, 2 Pesca, 3 Stefan, 4 the snowman),
// int id, short rank, int count, byte notice, then count rows of int item id and short amount for the reward
// notifier. A notice of 1 shows the type's text: for the snowman s_snowman_{thema}_promotion_reward.
public static class TaskEventPacket {
    private const byte SnowmanType = 4;

    // A snowflake: no item, no id, only the notice.
    public static ByteWriter Snowflake() {
        var pWriter = Packet.Of(SendOp.TaskEvent);
        pWriter.WriteByte(SnowmanType);
        pWriter.WriteInt(0);
        pWriter.WriteShort(0);
        pWriter.WriteInt(0);
        pWriter.WriteByte(1);

        return pWriter;
    }
}
