using Google.Protobuf;
using Grpc.Core;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Service;

public partial class ChannelService {
    // The profile of a player of this channel for a viewer on another one: the CharacterInfo packet, or nothing when
    // the player is not in the game here.
    public override Task<CharacterInfoResponse> CharacterInfo(CharacterInfoRequest request, ServerCallContext context) {
        if (!server.GetSession(request.CharacterId, out GameSession? session) || !PlayerInfoPacket.CanLoad(session)) {
            return Task.FromResult(new CharacterInfoResponse());
        }

        ByteWriter packet = PlayerInfoPacket.Load(session);
        return Task.FromResult(new CharacterInfoResponse {
            Packet = ByteString.CopyFrom(packet.Buffer, 0, packet.Length),
        });
    }
}
