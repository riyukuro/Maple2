using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// TeamPvp 0x9A: the guild championship's round (the guild team dialog, UIPvPGuildTeamDialog).
public static class TeamPvpPacket {
    private enum Command : byte {
        Round = (byte) 's',
        End = (byte) 't',
    }

    // 's': the round, its end (the server tick; 0 shows no time), the round's fighters still in it, and each guild's
    // round wins.
    public static ByteWriter Round(int round, int endTick, IReadOnlyCollection<long> fighters, long guildA, int scoreA, long guildB, int scoreB) {
        var pWriter = Packet.Of(SendOp.TeamPvp);
        pWriter.WriteByte((byte) Command.Round);
        pWriter.WriteInt(round);
        pWriter.WriteInt(endTick);
        pWriter.WriteInt();
        pWriter.WriteInt(fighters.Count);
        foreach (long characterId in fighters) {
            pWriter.WriteLong(characterId);
        }
        pWriter.WriteLong(guildA);
        pWriter.WriteInt(scoreA);
        pWriter.WriteLong(guildB);
        pWriter.WriteInt(scoreB);
        return pWriter;
    }

    // 't': the match ended, the winner guild (0: a draw).
    public static ByteWriter End(long winnerGuildId) {
        var pWriter = Packet.Of(SendOp.TeamPvp);
        pWriter.WriteByte((byte) Command.End);
        pWriter.WriteLong(winnerGuildId);
        return pWriter;
    }
}
