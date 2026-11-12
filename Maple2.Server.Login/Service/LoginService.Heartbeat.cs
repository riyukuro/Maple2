using System;
using System.Threading.Tasks;
using Grpc.Core;
using Maple2.Server.Core.Helpers;
using Maple2.Server.Login.Session;

namespace Maple2.Server.Login.Service;

public partial class LoginService {
    public override Task<HeartbeatResponse> Heartbeat(HeartbeatRequest request, ServerCallContext context) {
        long now = Environment.TickCount64;
        foreach (LoginSession loginSession in loginServer.GetSessions()) {
            // The same limit as on a channel: a login session whose client has sent nothing for it is cut.
            if (!HeartbeatRules.Ask(loginSession, now)) {
                logger.Information("No packet from login session of account {AccountId} for {Seconds} s, closing it",
                    loginSession.AccountId, (now - loginSession.LastReceivedMs) / 1000);
            }
        }
        return Task.FromResult(new HeartbeatResponse());
    }
}
