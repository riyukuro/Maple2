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
            // A session whose client is gone and that still writes its final save: the World keeps the player online
            // until the save ends (the logout's offline update), else a login could load before it.
            if (server.GetSessionOrClosing(request.CharacterId, out _)) {
                return Task.FromResult(new HeartbeatResponse {
                    Success = true,
                });
            }
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
