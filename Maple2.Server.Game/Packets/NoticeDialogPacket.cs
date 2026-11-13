using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// The notice banner (scenestategame.cpp, the 0xCF case, to UINoticeDialog::OnNoticeDialog): a byte mode. Mode 0 shows
// the notice dialog for NoticeDialogOpenSeconds when it is open and not closed; the client reads no other mode.
public static class NoticeDialogPacket {
    public static ByteWriter Show() {
        var pWriter = Packet.Of(SendOp.NoticeDialog);
        pWriter.WriteByte(0);
        return pWriter;
    }
}
