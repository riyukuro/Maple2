using Grpc.Core;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class CharacterInfoHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.CharacterInfo;

    public override void Handle(GameSession session, IByteReader packet) {
        long characterId = packet.ReadLong();

        if (session.FindSession(characterId, out GameSession? other)) {
            session.Send(PlayerInfoPacket.CanLoad(other) ? PlayerInfoPacket.Load(other) : PlayerInfoPacket.NotFound(characterId));
            return;
        }

        // A player online on another channel: that channel writes the packet. Offline, or no answer: not found (the
        // client shows s_char_info_err_not_found).
        byte[]? remote = null;
        try {
            CharacterInfoResponse response = session.World.CharacterInfo(new CharacterInfoRequest {
                CharacterId = characterId,
            });
            remote = response.Packet.ToByteArray();
        } catch (RpcException ex) {
            Logger.Warning(ex, "CharacterInfo of {CharacterId} from World", characterId);
        }

        if (remote is { Length: > 0 }) {
            session.Send(remote);
            return;
        }
        session.Send(PlayerInfoPacket.NotFound(characterId));
    }
}
