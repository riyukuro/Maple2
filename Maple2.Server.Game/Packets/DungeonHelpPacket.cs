using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// The recruiting notice (UIMessengerBrowser::FUN_141a23060, uimessenger.cpp:3003), shown in the chat with a join link
// (Party 39 or 43). The client drops a notice from a blocked character and starts its own 10 s deadline when the
// notice is for its party (CPartyManager::SetPartyDeadline).
public static class DungeonHelpPacket {
    // Mode 0 (FUN_141a230e0): the helper notice; the last byte is the party's member count.
    public static ByteWriter Helper(int partyId, string leaderName, long leaderAccountId, long leaderId, int roomId, int members) {
        var pWriter = Packet.Of(SendOp.DungeonHelp);
        pWriter.WriteByte((byte) DungeonRecruitKind.Helper);
        pWriter.WriteInt(partyId);
        pWriter.WriteUnicodeString(leaderName);
        pWriter.WriteLong(leaderAccountId);
        pWriter.WriteLong(leaderId);
        pWriter.WriteInt(roomId);
        pWriter.WriteByte(ToByte(members));

        return pWriter;
    }

    // Mode 1 (FUN_141a233d0): the member notice, with the places it asks for.
    public static ByteWriter Member(int needHelpers, int needMembers, int partyId, string leaderName, long leaderAccountId, long leaderId, int roomId, int members) {
        var pWriter = Packet.Of(SendOp.DungeonHelp);
        pWriter.WriteByte((byte) DungeonRecruitKind.Member);
        pWriter.WriteByte(ToByte(needHelpers));
        pWriter.WriteByte(ToByte(needMembers));
        pWriter.WriteInt(partyId);
        pWriter.WriteUnicodeString(leaderName);
        pWriter.WriteLong(leaderAccountId);
        pWriter.WriteLong(leaderId);
        pWriter.WriteInt(roomId);
        pWriter.WriteByte(ToByte(members));

        return pWriter;
    }

    private static byte ToByte(int value) => (byte) Math.Clamp(value, byte.MinValue, byte.MaxValue);
}
