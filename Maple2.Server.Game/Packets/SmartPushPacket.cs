using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

public static class SmartPushPacket {
    private enum Command : byte {
        Activate = 1,
    }

    // CSceneStateGame::OnSmartPush: bool 1, then the push id and a value for that push's vfunc_4. An effect push
    // shows "s_smartpush_active" with its effect's name and ignores the value; auto fishing then starts the auto
    // mode when fishing (CSmartPushAutoFishing::vfunc_4). Before, the currency went in as an int where the push id
    // is read: a meret buy (2) named push 2, Safe Riding.
    public static ByteWriter ActivateEffect(int smartPushId, int value) {
        var pWriter = Packet.Of(SendOp.SmartPush);
        pWriter.WriteBool(true);
        pWriter.WriteInt(smartPushId);
        pWriter.WriteInt(value);

        return pWriter;
    }

    public static ByteWriter ActivateGather(int id, int amount) {
        var pWriter = Packet.Of(SendOp.SmartPush);
        pWriter.Write<Command>(Command.Activate);
        pWriter.WriteInt(id);
        pWriter.WriteInt(amount);

        return pWriter;
    }
}
