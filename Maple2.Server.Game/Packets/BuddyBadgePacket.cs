using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

public static class BuddyBadgePacket {
    private enum Command : byte {
        Start = 0,
        Stop = 1,
        ShowToNewcomer = 2,
        PartnerName = 3,
    }

    public static ByteWriter Start(long characterId) {
        var pWriter = Packet.Of(SendOp.BuddyBadge);
        pWriter.Write<Command>(Command.Start);
        pWriter.WriteLong(characterId);

        return pWriter;
    }

    // Client: CNetworkService::OnBuddyBadge, kind 2 turns the effect of that player on, as kind 0 does.
    public static ByteWriter ShowToNewcomer(long characterId) {
        var pWriter = Packet.Of(SendOp.BuddyBadge);
        pWriter.WriteByte((byte) Command.ShowToNewcomer);
        pWriter.WriteLong(characterId);

        return pWriter;
    }

    // Client: kind 3 reads the player, the uid of that player's buddy badge and the partner name to show on it.
    public static ByteWriter PartnerName(long characterId, long badgeUid, string partnerName) {
        var pWriter = Packet.Of(SendOp.BuddyBadge);
        pWriter.WriteByte((byte) Command.PartnerName);
        pWriter.WriteLong(characterId);
        pWriter.WriteLong(badgeUid);
        pWriter.WriteUnicodeString(partnerName);

        return pWriter;
    }

    public static ByteWriter Stop(long characterId) {
        var pWriter = Packet.Of(SendOp.BuddyBadge);
        pWriter.Write<Command>(Command.Stop);
        pWriter.WriteLong(characterId);

        return pWriter;
    }
}
