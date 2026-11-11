using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

public static class AutoRevivePacket {
    // Client: CSceneStateGame::OnAutoRevive (scenestategame.cpp:4332): int duration in milliseconds. It opens the
    // auto revive countdown dialog and arms it (UIAutoReviveCountDownDialog, uiautorevivecountdowndialog.cpp:78).
    // The dialog only counts down; the revive itself has to come from the server.
    public static ByteWriter Countdown(int durationMs) {
        var pWriter = Packet.Of(SendOp.AutoRevive);
        pWriter.WriteInt(durationMs);

        return pWriter;
    }
}
