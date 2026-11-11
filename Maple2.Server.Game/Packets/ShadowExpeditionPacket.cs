using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// The field game score board (client FieldGameScoreBoardCli, 0xE1). Command 0 opens a board: its kind (1 is the
// Shadow Expedition boss gauge), the title (a narrow string, looked up as a server string), the current and the
// most points. Command 1 (an int, the current points) is not sent: the
// server has no source for the points. Command 2 closes the board. The gauge shows cur*100/max %.
public static class ShadowExpeditionPacket {
    private enum Command : byte {
        Open = 0,
        Close = 2,
    }

    public const byte BossGauge = 1;

    public static ByteWriter OpenBossGauge(string title, int points, int maxPoints) {
        var pWriter = Packet.Of(SendOp.ShadowExpedition);
        pWriter.WriteByte((byte) Command.Open);
        pWriter.WriteByte(BossGauge);
        pWriter.WriteString(title);
        pWriter.WriteInt(points);
        pWriter.WriteInt(maxPoints);
        return pWriter;
    }

    public static ByteWriter Close() {
        var pWriter = Packet.Of(SendOp.ShadowExpedition);
        pWriter.WriteByte((byte) Command.Close);
        return pWriter;
    }
}
