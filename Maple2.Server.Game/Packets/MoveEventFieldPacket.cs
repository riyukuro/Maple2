using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

public static class MoveEventFieldPacket {
    // Client: UIWorldMapDialog::vfunc_79 (uiworldmapdialog.cpp:413): one byte. 0 shows the notice
    // s_err_timeevent_move_field, 1 shows s_err_timeevent_same_continent; any other value is ignored.
    public const byte NO_EVENT = 0;
    public const byte OTHER_CONTINENT = 1;

    public static ByteWriter Refuse(byte reason) {
        var pWriter = Packet.Of(SendOp.MoveEventField);
        pWriter.WriteByte(reason);

        return pWriter;
    }
}
