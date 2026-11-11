using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// The field game score board (client FieldGameScoreBoardCli, 0xE1). Command 0 opens a board: its kind, the title
// (a narrow string, looked up as a server string), the current and the most points. Kind 1 is the Shadow
// Expedition boss gauge (cur*100/max %); kinds 2 and 3 are the score dialog that shows only the current points,
// 2 at the top center and 3 at the top right (80 px higher). Command 1 (an int) sets the current points of the
// open board. Command 2 closes the board.
public static class ShadowExpeditionPacket {
    private enum Command : byte {
        Open = 0,
        SetPoints = 1,
        Close = 2,
    }

    public const byte BossGauge = 1;
    public const byte TopCenterScore = 2;
    public const byte TopRightScore = 3;

    public static ByteWriter OpenBossGauge(string title, int points, int maxPoints) {
        return Open(BossGauge, title, points, maxPoints);
    }

    public static ByteWriter Open(byte kind, string title, int points, int maxPoints) {
        var pWriter = Packet.Of(SendOp.ShadowExpedition);
        pWriter.WriteByte((byte) Command.Open);
        pWriter.WriteByte(kind);
        pWriter.WriteString(title);
        pWriter.WriteInt(points);
        pWriter.WriteInt(maxPoints);
        return pWriter;
    }

    public static ByteWriter SetPoints(int points) {
        var pWriter = Packet.Of(SendOp.ShadowExpedition);
        pWriter.WriteByte((byte) Command.SetPoints);
        pWriter.WriteInt(points);
        return pWriter;
    }

    public static ByteWriter Close() {
        var pWriter = Packet.Of(SendOp.ShadowExpedition);
        pWriter.WriteByte((byte) Command.Close);
        return pWriter;
    }
}
