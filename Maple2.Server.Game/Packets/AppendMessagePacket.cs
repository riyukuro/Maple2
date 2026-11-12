using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// The client reads both in UIMessengerBrowser (client/ms2/uimessenger.cpp) and shows the line in the system channel;
// the npc id gives the npc's title and name.
public static class AppendMessagePacket {
    // FUN_141a22ee0: unicode killer name, int npc id; s_hunting_kill_boss.
    public static ByteWriter KillBoss(string killerName, int npcId) {
        var pWriter = Packet.Of(SendOp.AppendMessageKillBoss);
        pWriter.WriteUnicodeString(killerName);
        pWriter.WriteInt(npcId);

        return pWriter;
    }

    // FUN_141a22fc0: int npc id; s_fieldboss_reward_assistbonus_notify.
    public static ByteWriter AssistBonus(int npcId) {
        var pWriter = Packet.Of(SendOp.AppendMessageAssistBonus);
        pWriter.WriteInt(npcId);

        return pWriter;
    }
}
