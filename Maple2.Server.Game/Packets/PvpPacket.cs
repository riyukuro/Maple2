using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// Pvp 0xB6, read by CSceneStateGame::OnPvp (@0x141732350) and CPvpRankDuelComponent::OnMessage. Only the
// answers that take the client out of a queue are written: the server runs no arena and no rank duel.
public static class PvpPacket {
    private enum Command : byte {
        ArenaUnregistered = 10,
        RankDuelUnregistered = 15,
        RankDuelMatch = 16,
    }

    // The arena state goes back to 0; the client shows its "unregistered" notice only when it was queued.
    public static ByteWriter ArenaUnregistered() {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) Command.ArenaUnregistered);
        return pWriter;
    }

    // The rank duel state goes back to 0 and the register button comes back.
    public static ByteWriter RankDuelUnregistered() {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) Command.RankDuelUnregistered);
        return pWriter;
    }

    // False: the state goes back to 0 with the client's s_rank_duel_matching_fail box.
    public static ByteWriter RankDuelMatchFailed() {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) Command.RankDuelMatch);
        pWriter.WriteBool(false);
        return pWriter;
    }
}
