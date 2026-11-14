using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Login.Session;

namespace Maple2.Server.Login.PacketHandlers;

public class ServerEnterHandler : PacketHandler<LoginSession> {
    public override RecvOp OpCode => RecvOp.ServerEnter;

    public ServerEnterHandler() { }

    // The client sends ServerEnter to the login server only on its MoveResult (CNetworkService::OnMoveResult), which
    // ResponseKeyHandler sends after the account's entry was taken (Init): one before that has no account to list.
    // From the server list the client sends none (it logs in anew first).
    public override void Handle(LoginSession session, IByteReader packet) {
        packet.Read<Language>();
        if (session.State != SessionState.Connected) {
            Logger.Debug("Dropped a server enter before the login ({State})", session.State);
            return;
        }
        if (session.LeftCharacterListRefuses("ServerEnter")) {
            return;
        }

        session.ListServers(session.AccountId);
        session.ListCharacters();
        session.SendEvents();
    }
}
