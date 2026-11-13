using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

// The client's answer to RequestClientTickSync (CNetworkService::OnRequestClientTickSync, written by FUN_1401fa580):
// the server's int back, then the client's elapsed time. The server keeps the client's tick from the heartbeat
// (ResponseHeartbeat, the same pair), so this one is read and dropped.
public class ResponseClientTickSyncHandler : PacketHandler<GameSession> {
    public override RecvOp OpCode => RecvOp.ResponseClientTickSync;

    public override void Handle(GameSession session, IByteReader packet) {
        packet.ReadInt(); // The server's tick, echoed.
        packet.ReadInt(); // The client's elapsed time.
    }
}
