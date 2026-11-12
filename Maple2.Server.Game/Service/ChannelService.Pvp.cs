using Grpc.Core;
using Maple2.Server.Channel.Service;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Service;

public partial class ChannelService {
    // The rank duel matchmaker's word to a character here: a match was found (the match-found box, whose
    // button sends the join), or the search or the match ended without a duel (the matching failed box).
    public override Task<PvpNoticeResponse> Pvp(PvpNoticeRequest request, ServerCallContext context) {
        if (request.ObserveOffer) {
            foreach (GameSession player in server.GetSessions()) {
                if (PvpHandler.TakesObserveOffer(player)) {
                    player.Send(PvpPacket.RankDuelObserveOffer());
                }
            }
            return Task.FromResult(new PvpNoticeResponse());
        }
        if (server.GetSession(request.CharacterId, out GameSession? session)) {
            if (request.Arena != 0) {
                session.Send(request.Matched ? PvpPacket.ArenaMatchFound(request.Arena) : PvpPacket.ArenaUnregistered());
            } else {
                session.Send(request.Matched ? PvpPacket.RankDuelMatchFound() : PvpPacket.RankDuelMatchFailed());
            }
        }
        return Task.FromResult(new PvpNoticeResponse());
    }
}
