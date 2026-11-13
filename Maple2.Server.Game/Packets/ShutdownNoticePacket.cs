using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// The KR platform's shutdown and maintenance notices (CClientPlatformNexonKorea::vfunc_26, the 0xAF case of the
// client's packet switch, networkservice.cpp:695). Mode 0 kind 1 and mode 1 kind 1 close the socket, show the message
// in a box and send RequestQuit (2, 2), which the server takes as a quit from the game.
public static class ShutdownNoticePacket {
    private enum Mode : byte {
        Shutdown = 0,
        Maintenance = 1,
    }

    // Mode 0 kind 0: the shutdown time in Unix seconds. Once a second the client posts notice 0x9a5 with that time's
    // hour when 30, 15 and 5 minutes are left (vfunc_6); 0 or a past time posts nothing. Each of the three notices
    // shows once per client run, so a time sent again after one was shown does not show it again.
    public static ByteWriter Time(long unixSeconds) {
        var pWriter = Packet.Of(SendOp.ShutdownNotice);
        pWriter.WriteByte((byte) Mode.Shutdown);
        pWriter.WriteByte(0);
        pWriter.WriteLong(unixSeconds);

        return pWriter;
    }

    // Mode 0 kind 1: a message box titled with common string 0x219, and the client quits.
    public static ByteWriter Quit(string message) {
        var pWriter = Packet.Of(SendOp.ShutdownNotice);
        pWriter.WriteByte((byte) Mode.Shutdown);
        pWriter.WriteByte(1);
        pWriter.WriteUnicodeString(message);

        return pWriter;
    }

    // Mode 1 kind 1: a message box titled with common string 0x8c6, and the client quits.
    public static ByteWriter Maintenance(string message) {
        var pWriter = Packet.Of(SendOp.ShutdownNotice);
        pWriter.WriteByte((byte) Mode.Maintenance);
        pWriter.WriteByte(1);
        pWriter.WriteUnicodeString(message);

        return pWriter;
    }

    // Mode 1, any kind but 1: a system notice (ShowSystemNotice(message, 3, 1)). Kind 0 is chosen here.
    public static ByteWriter Notice(string message) {
        var pWriter = Packet.Of(SendOp.ShutdownNotice);
        pWriter.WriteByte((byte) Mode.Maintenance);
        pWriter.WriteByte(0);
        pWriter.WriteUnicodeString(message);

        return pWriter;
    }
}
