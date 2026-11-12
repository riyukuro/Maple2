using Grpc.Core;
using Maple2.Server.Core.Helpers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Service;

public partial class ChannelService {
    public override Task<HeartbeatResponse> Heartbeat(HeartbeatRequest request, ServerCallContext context) {
        if (request.CharacterId == 0) {
            logger.Warning("Heartbeat from unknown session. No character id.");
            return Task.FromResult(new HeartbeatResponse {
                Success = false,
            });
        }
        if (!server.GetSession(request.CharacterId, out GameSession? session)) {
            logger.Warning("Heartbeat from unknown session: {CharacterId}", request.CharacterId);
            return Task.FromResult(new HeartbeatResponse {
                Success = false,
            });
        }

        // A client that has sent nothing for the limit is gone: the session ends, with a reset so that a client
        // that is still there shows its lost-connection notice, and the World server marks the player offline.
        long now = Environment.TickCount64;
        if (!HeartbeatRules.Ask(session, now)) {
            logger.Information("No packet from {CharacterId} for {Seconds} s, closing the session",
                request.CharacterId, (now - session.LastReceivedMs) / 1000);
            return Task.FromResult(new HeartbeatResponse {
                Success = false,
            });
        }

        return Task.FromResult(new HeartbeatResponse {
            Success = true,
        });
    }
}
