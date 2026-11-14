using Grpc.Core;
using Maple2.Server.Channel.Service;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Service;

public partial class ChannelService {
    public override Task<MailNotificationResponse> MailNotification(MailNotificationRequest request, ServerCallContext context) {
        if (!server.GetSession(request.CharacterId, out GameSession? session)) {
            throw new RpcException(new Status(StatusCode.NotFound, $"Unable to find: {request.CharacterId}"));
        }

        session.Mail.Notify(true);
        if (request.Gift is { } gift) {
            session.Send(MailPacket.Gift(gift.Sender, gift.ItemId, gift.Rarity, gift.Amount, gift.UgcUrl, gift.UgcTitle));
        }

        return Task.FromResult(new MailNotificationResponse {
            Delivered = true,
        });
    }

    public override Task<PlayerUpdateResponse> UpdatePlayer(PlayerUpdateRequest request, ServerCallContext context) {
        playerInfos.ReceiveUpdate(request);
        return Task.FromResult(new PlayerUpdateResponse());
    }

    public override Task<ChannelsUpdateResponse> UpdateChannels(ChannelsUpdateRequest request, ServerCallContext context) {
        List<short> channels = request.Channels.Select(channel => (short) channel).ToList();
        server.Broadcast(ChannelPacket.Update(channels));
        return Task.FromResult(new ChannelsUpdateResponse());
    }

    // The longest a kick waits for the session's end: the send queue's drain (2 s), the account lock's tries (2.5 s)
    // and the last save, which may wait for a reset being stored (GameSession.ResetWait, 60 s).
    internal static TimeSpan CloseWait { get; set; } = TimeSpan.FromSeconds(75); // tests shorten it

    public override Task<DisconnectResponse> Disconnect(DisconnectRequest request, ServerCallContext context) {
        if (request is { CharacterId: <= 0 }) {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "CharacterId not specified"));
        }

        if (!server.GetSessionOrClosing(request.CharacterId, out GameSession? session)) {
            logger.Error("Unable to find session for character: {CharacterId}", request.CharacterId);
            return Task.FromResult(new DisconnectResponse {
                Success = false,
            });
        }

        // Asked for by the World server (the account logs in elsewhere, or a GM ends the session): the client was
        // told nothing, so it gets the reset and shows its notice.
        session.Cut();
        // Answered once the session's Dispose has ended, its last save included: the caller goes on to let the
        // account in again, and a load before that save let it write nothing. Cut runs that Dispose here, unless
        // the session was already closing on another thread (the client closed it, the heartbeat ended it), whose
        // Cut returns at once.
        if (!session.WaitDisposed(CloseWait)) {
            logger.Warning("Disconnect: the session of character {CharacterId} did not end in {Wait}", request.CharacterId, CloseWait);
        }
        return Task.FromResult(new DisconnectResponse {
            Success = true,
        });
    }
}
