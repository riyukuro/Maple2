using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

// Reconnect 0x05: after a lost link the client connects to the game server again and, in place of ResponseKey,
// sends the account id, the character id, the attempt, the packet sequence, the machine id and the context the
// server gave it (CNetworkService::OnRequestKey, CReconnectionManagerCli::SendReconnectRequest). It does so only
// when the server turned reconnection on (Gvg group 43, option 0 = 1) and sent a context of mode 1; this server
// sends neither, so the client does not reconnect. Nothing in the request proves the account, and the lost session
// is not kept to resume, so every request is refused: the client quits to the login scene.
public class ReconnectHandler : PacketHandler<GameSession> {
    public override RecvOp OpCode => RecvOp.Reconnect;

    public override void Handle(GameSession session, IByteReader packet) {
        ReconnectRequest request = ReconnectRequest.Read(packet);
        Logger.Information("Refused reconnect for account {AccountId}, character {CharacterId}", request.AccountId, request.CharacterId);
        session.Send(ReconnectPacket.Refuse());
    }
}
