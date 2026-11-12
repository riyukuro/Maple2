using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class ResponseHeartbeatHandler : PacketHandler<GameSession> {
    public override RecvOp OpCode => RecvOp.ResponseHeartbeat;

    public override void Handle(GameSession session, IByteReader packet) {
        int serverTick = packet.ReadInt();
        int clientTick = packet.ReadInt();

        // A late answer does not end the session: the client was held up (a stall of the link or of the client)
        // and is still there. A client that is gone is found by its silence (ChannelService.Heartbeat).
        if (serverTick == 0 || clientTick == 0) {
            return;
        }

        if (session is { ClientTick: 0, ServerTick: 0 }) {
            session.ClientTick = clientTick;
            session.ServerTick = serverTick;
            return;
        }

        int serverDelta = serverTick - session.ServerTick;
        int clientDelta = clientTick - session.ClientTick;

        session.ClientTick = clientTick;
        session.ServerTick = serverTick;
    }
}
