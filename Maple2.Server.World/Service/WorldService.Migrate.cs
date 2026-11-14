using System.Net;
using System.Security.Cryptography;
using Grpc.Core;
using Maple2.Model.Error;
using Maple2.Server.Core.Constants;
using Microsoft.Extensions.Caching.Memory;

namespace Maple2.Server.World.Service;

public partial class WorldService {
    private readonly record struct TokenEntry(Server Server, long AccountId, long CharacterId, Guid MachineId, int Channel, int MapId, int PortalId, int RoomId, long OwnerId, MigrationType Type, long PetUid = 0);

    // Duration for which a token remains valid.
    private static readonly TimeSpan AuthExpiry = WorldServer.MigrationLifetime;

    private readonly IMemoryCache tokenCache;

    // The account's newest token, kept in the token cache next to the tokens. An account has one client, so only its
    // newest move is live: a new token ends the earlier one. Else a token left unused (the client was closed on the
    // way, or another login of the account picked a character before it arrived) still let a second game session of
    // the account in next to the first one.
    private readonly record struct AccountTokenKey(long AccountId);
    private static readonly object TokenIssue = new();

    private void Issue(ulong token, TokenEntry entry) {
        lock (TokenIssue) {
            if (tokenCache.TryGetValue(new AccountTokenKey(entry.AccountId), out ulong earlier)) {
                tokenCache.Remove(earlier);
            }
            tokenCache.Set(token, entry, AuthExpiry);
            tokenCache.Set(new AccountTokenKey(entry.AccountId), token, AuthExpiry);
        }
    }

    public override Task<MigrateOutResponse> MigrateOut(MigrateOutRequest request, ServerCallContext context) {
        ulong token = UniqueToken();

        switch (request.Server) {
            case Server.Login:
                var loginEntry = new TokenEntry(request.Server, request.AccountId, request.CharacterId, new Guid(request.MachineId), 0, 0, 0, 0, 0, MigrationType.Normal);
                Issue(token, loginEntry);
                return Task.FromResult(new MigrateOutResponse {
                    IpAddress = Target.LoginIp.ToString(),
                    Port = Target.LoginPort,
                    Token = token,
                });
            case Server.Game:
                if (channelClients.Count == 0) {
                    throw new RpcException(new Status(StatusCode.Unavailable, $"No available game channels"));
                }

                // Try to use requested channel or instanced channel
                if (request.InstancedContent && channelClients.TryGetInstancedChannelId(out int channel)) {
                    if (!channelClients.TryGetActiveEndpoint(channel, out _)) {
                        throw new RpcException(new Status(StatusCode.Unavailable, "No available instanced game channel"));
                    }
                } else {
                    channel = PickGameChannel(request.HasChannel ? request.Channel : null, request.ExactChannel,
                        id => channelClients.TryGetActiveEndpoint(id, out _), channelClients.HasListedChannel, channelClients.FirstChannel);
                    if (channel == -1) {
                        throw new RpcException(new Status(StatusCode.Unavailable, request.ExactChannel && request.HasChannel
                            ? $"Channel {request.Channel} is not available" : "No available game channels"));
                    }
                }

                if (!channelClients.TryGetActiveEndpoint(channel, out IPEndPoint? endpoint)) {
                    throw new RpcException(new Status(StatusCode.Unavailable, $"Channel {channel} not found"));
                }

                // A player who is online leaves the channel of the record: the heartbeat of that channel
                // must not mark them offline while the token is valid. A move inside the channel is not noted.
                if (playerLookup.TryGet(request.CharacterId, out Maple2.Model.Game.PlayerInfo? leaving) && leaving.Online) {
                    worldServer.Migrations.Begin(request.CharacterId, leaving.Channel, channel, DateTime.UtcNow);
                }

                var gameEntry = new TokenEntry(request.Server, request.AccountId, request.CharacterId, new Guid(request.MachineId), channel, request.MapId, request.PortalId, request.RoomId, request.OwnerId, request.Type, request.PetUid);
                Issue(token, gameEntry);
                return Task.FromResult(new MigrateOutResponse {
                    IpAddress = endpoint.Address.ToString(),
                    Port = endpoint.Port,
                    Token = token,
                    Channel = channel,
                });
            default:
                throw new RpcException(new Status(StatusCode.InvalidArgument, $"Invalid server: {request.Server}"));
        }
    }

    // The channel of a move to a game server, -1 for none: the requested one when it is active; else the first
    // active one, unless the player picked (exact) a channel the World has that is not active: that one is refused.
    // A picked channel the World does not have goes to the first one: the client sends its saved last channel,
    // clamped to the channel count and not to a listed id (UIServerListDialog), so it can be a gap or a gone channel.
    public static int PickGameChannel(int? requested, bool exact, Func<int, bool> isActive, Func<int, bool> exists, Func<int> firstChannel) {
        if (requested is int wanted) {
            if (isActive(wanted)) {
                return wanted;
            }
            if (exact && exists(wanted)) {
                return -1;
            }
        }
        return firstChannel();
    }

    public override Task<MigrateInResponse> MigrateIn(MigrateInRequest request, ServerCallContext context) {
        if (!tokenCache.TryGetValue(request.Token, out TokenEntry data)) {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid token"));
        }

        if (data.AccountId != request.AccountId) {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Invalid token for account"));
        }
        if (data.MachineId != new Guid(request.MachineId)) {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Mismatched machineId for account"));
        }

        lock (TokenIssue) {
            tokenCache.Remove(request.Token);
            if (tokenCache.TryGetValue(new AccountTokenKey(data.AccountId), out ulong newest) && newest == request.Token) {
                tokenCache.Remove(new AccountTokenKey(data.AccountId));
            }
        }
        return Task.FromResult(new MigrateInResponse {
            CharacterId = data.CharacterId,
            Channel = data.Channel,
            MapId = data.MapId,
            PortalId = data.PortalId,
            OwnerId = data.OwnerId,
            RoomId = data.RoomId,
            Type = data.Type,
            PetUid = data.PetUid,
        });
    }

    // Generates a 64-bit token that does not exist in cache.
    private ulong UniqueToken() {
        ulong token;
        do {
            token = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8));
        } while (tokenCache.TryGetValue(token, out _));

        return token;
    }

    public override Task<FieldResponse> Field(FieldRequest request, ServerCallContext context) {
        switch (request.FieldCase) {
            case FieldRequest.FieldOneofCase.CreateDungeon:
                return Task.FromResult(CreateDungeon(request));
            case FieldRequest.FieldOneofCase.DestroyDungeon:
            case FieldRequest.FieldOneofCase.CheckDungeon:
                return Task.FromResult(ForwardToFirstChannel(request));
            default:
                return Task.FromResult(new FieldResponse { Error = (int) MigrationError.ok });
        }
    }

    private FieldResponse CreateDungeon(FieldRequest request) {
        if (!channelClients.TryGetClient(channelClients.FirstChannel(), out Channel.Service.Channel.ChannelClient? client)) {
            return new FieldResponse {
                Error = (int) MigrationError.s_move_err_no_server,
            };
        }

        return client.Field(request);
    }

    // Dungeon rooms live on the first channel, the one CreateDungeon is sent to; a request to close a room or
    // to ask whether it still runs goes there unchanged.
    private FieldResponse ForwardToFirstChannel(FieldRequest request) {
        if (!channelClients.TryGetClient(channelClients.FirstChannel(), out Channel.Service.Channel.ChannelClient? client)) {
            return new FieldResponse {
                Error = (int) MigrationError.s_move_err_no_server,
            };
        }

        return client.Field(request);
    }
}
