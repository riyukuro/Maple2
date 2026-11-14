using Maple2.Model.Game;
using Maple2.Model.Metadata;
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

    // A promotionContents box (PromotionTable): the row's type, no id, the items given and no notice. The client lists
    // each item in the reward notifier: type 0 the item and its amount, 1 to 3 the npc's portrait with its
    // s_<npc>_promotion_reward text and the item's name.
    public static ByteWriter Reward(PromotionKind kind, ICollection<Item> items) {
        var pWriter = Packet.Of(SendOp.TaskEvent);
        pWriter.WriteByte((byte) kind);
        pWriter.WriteInt(0);
        pWriter.WriteShort(0);
        pWriter.WriteInt(items.Count);
        pWriter.WriteByte(0);
        foreach (Item item in items) {
            pWriter.WriteInt(item.Id);
            pWriter.WriteShort((short) Math.Min(item.Amount, short.MaxValue));
        }

        return pWriter;
    }
}
