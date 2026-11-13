using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// CSceneStateGame::OnDpsStat (scenestategame.cpp:4289): the server's part of the DPS meter's recovery block. The
// client reads it only while its meter records, and only for its own player. Skill heals are not sent here: the
// client counts those from SkillDamage Heal (FUN_14171d820, scenestategame.cpp:5685).
public static class DpsStatPacket {
    private enum Command : byte {
        Recover = 0,
        Spend = 1,
        HealEffective = 2,
    }

    // A recovery of type 4, 7 or 10 (health, spirit, stamina). The client counts spirit as skill spirit when natural
    // is false and as natural spirit otherwise, stamina only when natural, and health only when not natural
    // (CDPSMeterSystem::AddStat, dpsmetersystem.cpp:994).
    public static ByteWriter Recover(bool natural, BasicAttribute attribute, int amount) {
        var pWriter = Packet.Of(SendOp.DpsStat);
        pWriter.WriteByte((byte) Command.Recover);
        pWriter.WriteBool(natural);
        pWriter.WriteInt((int) attribute);
        pWriter.WriteInt(amount);

        return pWriter;
    }

    // Spirit and stamina spent on a skill. The client stores the first int in the record but reads it nowhere;
    // chosen here: 0.
    public static ByteWriter Spend(int spirit, int stamina) {
        var pWriter = Packet.Of(SendOp.DpsStat);
        pWriter.WriteByte((byte) Command.Spend);
        pWriter.WriteInt(0);
        pWriter.WriteInt(spirit);
        pWriter.WriteInt(stamina);

        return pWriter;
    }

    // The health a heal actually restored (after the cap): given (false; the meter's heal effective) or received
    // (true; healed effective). The client takes the full amounts from SkillDamage Heal and shows the difference
    // as over-healing.
    public static ByteWriter HealEffective(bool received, int amount) {
        var pWriter = Packet.Of(SendOp.DpsStat);
        pWriter.WriteByte((byte) Command.HealEffective);
        pWriter.WriteBool(received);
        pWriter.WriteInt(amount);

        return pWriter;
    }
}
