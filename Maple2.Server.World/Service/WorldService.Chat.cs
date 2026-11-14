using Grpc.Core;
using Maple2.Model.Game;
using Maple2.Model.Game.Club;
using Maple2.Model.Game.Party;
using Maple2.Server.World.Containers;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;

namespace Maple2.Server.World.Service;

public partial class WorldService {
    public override Task<ChatResponse> Chat(ChatRequest request, ServerCallContext context) {
        switch (request.ChatCase) {
            case ChatRequest.ChatOneofCase.Whisper:
                return WhisperChat(request);
            case ChatRequest.ChatOneofCase.Party:
                return PartyChat(request);
            case ChatRequest.ChatOneofCase.Guild:
                return GuildChat(request);
            case ChatRequest.ChatOneofCase.World:
                return WorldChat(request);
            case ChatRequest.ChatOneofCase.Super:
                return SuperChat(request);
            case ChatRequest.ChatOneofCase.Club:
                return ClubChat(request);
            case ChatRequest.ChatOneofCase.Wedding:
                // The wedding megaphone reaches every channel, as super chat does.
                return SuperChat(request);
            case ChatRequest.ChatOneofCase.SystemNotice:
                return SystemNotice(request);
            default:
                throw new RpcException(
                    new Status(StatusCode.InvalidArgument, $"Invalid chat type: {request.ChatCase}"));
        }
    }

    // The line goes to the recipient's channel. NotFound (offline, gone from that channel, or on a channel with no
    // client) goes back to the sender's channel, which answers s_err_cannot_find_user.
    private Task<ChatResponse> WhisperChat(ChatRequest request) {
        if (!playerLookup.TryGet(request.Whisper.RecipientId, out PlayerInfo? info) || !info.Online) {
            throw new RpcException(new Status(StatusCode.NotFound,
                $"Unable to whisper: {request.Whisper.RecipientName}"));
        }

        int channel = info.Channel;
        if (!channelClients.TryGetClient(channel, out ChannelClient? channelClient)) {
            // The channel is down or not registered yet: the sender is told the player is not found, as for offline.
            logger.Error("No registry for channel: {Channel}", channel);
            throw new RpcException(new Status(StatusCode.NotFound,
                $"Unable to whisper: {request.Whisper.RecipientName} on channel: {channel}"));
        }

        var channelChat = new ChatRequest {
            AccountId = request.AccountId,
            CharacterId = request.CharacterId,
            Name = request.Name,
            Message = request.Message,
            ItemUids = { request.ItemUids },
            Gm = request.Gm,
            Channel = request.Channel,
            Picture = request.Picture,
            Whisper = new ChatRequest.Types.Whisper {
                RecipientId = request.Whisper.RecipientId,
                RecipientName = request.Whisper.RecipientName,
                Reject = request.Whisper.Reject,
            },
        };

        try {
            return Task.FromResult(channelClient.Chat(channelChat));
        } catch (RpcException ex) when (ex.StatusCode is StatusCode.NotFound) {
            throw new RpcException(new Status(StatusCode.NotFound, $"Unable to whisper: {request.Whisper.RecipientName}"));
        }
    }

    private Task<ChatResponse> PartyChat(ChatRequest request) {
        if (!partyLookup.TryGet(request.Party.PartyId, out PartyManager? info)) {
            logger.Information("Party {PartyPartyId} not found...", request.Party.PartyId);
            return Task.FromResult(new ChatResponse());
        }

        foreach (IGrouping<short, PartyMember> group in info.Party.Members.Values.GroupBy(member => member.Info.Channel)) {
            if (!channelClients.TryGetClient(group.Key, out ChannelClient? client)) {
                continue;
            }

            request.Party.MemberIds.Clear();
            request.Party.MemberIds.AddRange(group.Select(member => member.Info.CharacterId));

            try {
                client.Chat(request);
            } catch { /* ignored */ }
        }

        return Task.FromResult(new ChatResponse());
    }

    private Task<ChatResponse> GuildChat(ChatRequest request) {
        if (!guildLookup.TryGet(request.Guild.GuildId, out GuildManager? info)) {
            logger.Information("Guild {GuildGuildId} not found...", request.Guild.GuildId);
            return Task.FromResult(new ChatResponse());
        }

        foreach (IGrouping<short, GuildMember> group in info.Guild.Members.Values.GroupBy(member => member.Info.Channel)) {
            if (!channelClients.TryGetClient(group.Key, out ChannelClient? client)) {
                continue;
            }

            request.Guild.MemberIds.Clear();
            request.Guild.MemberIds.AddRange(group.Select(member => member.Info.CharacterId));
            try {
                client.Chat(request);
            } catch { /* ignored */ }
        }

        return Task.FromResult(new ChatResponse());
    }

    private Task<ChatResponse> WorldChat(ChatRequest request) {
        BroadcastChat(request);
        return Task.FromResult(new ChatResponse());
    }

    // How long a broadcast line waits for one channel.
    internal static readonly TimeSpan ChannelChatDeadline = TimeSpan.FromSeconds(2);

    // A line for every channel: one channel that is down or hangs misses it, the rest still get it, and the line
    // counts as sent (its cost is not given back).
    private void BroadcastChat(ChatRequest request) {
        foreach ((int channel, ChannelClient client) in channelClients) {
            try {
                client.Chat(request, deadline: DateTime.UtcNow + ChannelChatDeadline);
            } catch (RpcException ex) {
                logger.Error(ex, "Chat {Type} not delivered to channel {Channel}", request.ChatCase, channel);
            }
        }
    }

    private Task<ChatResponse> SuperChat(ChatRequest request) {
        BroadcastChat(request);
        return Task.FromResult(new ChatResponse());
    }

    private Task<ChatResponse> ClubChat(ChatRequest request) {
        if (!clubLookup.TryGet(request.Club.ClubId, out ClubManager? info)) {
            logger.Information("Club {ClubClubId} not found...", request.Club.ClubId);
            return Task.FromResult(new ChatResponse());
        }

        foreach (IGrouping<short, ClubMember> group in info.Club.Members.Values.GroupBy(member => member.Info.Channel)) {
            if (!channelClients.TryGetClient(group.Key, out ChannelClient? client)) {
                continue;
            }

            request.Club.MemberIds.Clear();
            request.Club.MemberIds.AddRange(group.Select(member => member.Info.CharacterId));

            try {
                client.Chat(request);
            } catch { /* ignored */ }
        }

        return Task.FromResult(new ChatResponse());
    }

    private Task<ChatResponse> SystemNotice(ChatRequest request) {
        BroadcastChat(request);
        return Task.FromResult(new ChatResponse());
    }
}
