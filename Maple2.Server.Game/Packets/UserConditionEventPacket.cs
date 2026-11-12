using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// The system mail notice's reader (UISystemMailNotifyDialog, uisystemmailnotifydialog.cpp:324): a mode byte and an
// id. Mode 0 stores the id as the row last noticed; mode 1 looks the id up among the user condition event rows and
// shows that row's notice.
public static class UserConditionEventPacket {
    private enum Command : byte {
        Load = 0,
        Notice = 1,
    }

    // What KMS2 sends once at every login: no row noticed yet (id 0).
    public static ByteWriter Load(int id = 0) {
        var pWriter = Packet.Of(SendOp.UserConditionEvent);
        pWriter.WriteByte((byte) Command.Load);
        pWriter.WriteInt(id);

        return pWriter;
    }
}
