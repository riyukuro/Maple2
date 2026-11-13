using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using Grpc.Core;
using Grpc.Health.V1;
using Grpc.Net.Client;
using Maple2.Database.Extensions;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Helpers;
using Maple2.Server.Core.Sync;
using Serilog;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;

namespace Maple2.Server.World.Containers;

public class ChannelClientLookup : IEnumerable<(int, ChannelClient)> {
#if DEBUG
    private static readonly TimeSpan MonitorInterval = TimeSpan.FromSeconds(1);
#else
    private static readonly TimeSpan MonitorInterval = TimeSpan.FromSeconds(5);
#endif

    private WorldServer worldServer = null!;
    private PlayerInfoLookup playerInfoLookup = null!;

    private enum ChannelStatus {
        Active,
        Inactive,
        Pending,
    }

    public void InjectDependencies(WorldServer worldSv, PlayerInfoLookup playerInfo) {
        worldServer = worldSv;
        playerInfoLookup = playerInfo;
    }

    private readonly ConcurrentDictionary<int, Channel> channels = [];

    private readonly ILogger logger = Log.ForContext<ChannelClientLookup>();

    public int Count => channels.Values.Count(ch => ch is { InstancedContent: false, Status: ChannelStatus.Active });

    public IEnumerable<int> Keys {
        get {
            foreach (Channel channel in channels.Values.Where(ch => ch is { InstancedContent: false, Status: ChannelStatus.Active })) {
                yield return channel.Id;
            }
        }
    }

    // A Game server that already runs names its own channel id. A channel this World server has for that same
    // Game server is left as it is (known); an id that is free is registered for it, which is what a World server
    // sees after its restart; an id that another Game server holds is refused (channel -1).
    public (ushort gamePort, int grpcPort, int channel, bool known) Register(string gameIp, string grpcGameIp, bool instancedContent, int requested, string instance) {
        lock (registerLock) {
            if (channels.TryGetValue(requested, out Channel? existing)) {
                if (!ChannelRegisterRules.SameServer(existing.Instance, instance)) {
                    return (0, 0, -1, false);
                }
                // A channel that was marked inactive lost its players here: it is reported again as well.
                if (existing.Status == ChannelStatus.Inactive) {
                    existing.Pending();
                    return (existing.GamePort, existing.GrpcPort, existing.Id, false);
                }
                return (existing.GamePort, existing.GrpcPort, existing.Id, true);
            }

            (ushort gamePort, int grpcPort, int channel) = AddChannel(gameIp, grpcGameIp, instancedContent, instance, requested);
            return (gamePort, grpcPort, channel, false);
        }
    }

    private readonly object registerLock = new();

    public (ushort gamePort, int grpcPort, int channel) FindOrCreateChannelByIp(string gameIp, string grpcGameIp, bool instancedContent, string instance = "") {
        // An inactive channel on the same address is given to the new Game server. Looked up and taken under the
        // lock: two Game servers that start together do not both get it.
        lock (registerLock) {
            Channel? activeChannel = channels.Values.FirstOrDefault(channel =>
                channel.Endpoint.Address.ToString() == gameIp &&
                channel.Status is ChannelStatus.Inactive &&
                channel.InstancedContent == instancedContent);

            if (activeChannel is not null) {
                activeChannel.Pending();
                activeChannel.Instance = instance;
                return (activeChannel.GamePort, activeChannel.GrpcPort, activeChannel.Id);
            }

            return AddChannel(gameIp, grpcGameIp, instancedContent, instance, null);
        }
    }

    /// Gets the ID of the first active, non-instanced content channel.
    /// <returns>
    /// The channel ID if an active, non-instanced content channel is found; otherwise returns -1.
    /// </returns>
    public int FirstChannel() {
        foreach (Channel channel in channels.Values.Where(ch => ch.Status is ChannelStatus.Active && !ch.InstancedContent)) {
            return channel.Id;
        }

        return -1;
    }

    public bool TryGetInstancedChannelId(out int channelId) {
        foreach (Channel channel in channels.Values.Where(ch => ch.Status is ChannelStatus.Active && ch.InstancedContent)) {
            channelId = channel.Id;
            return true;
        }

        channelId = -1;
        return false;
    }

    public bool ValidChannel(int channel) {
        return channel >= 0 && channels.ContainsKey(channel);
    }

    public bool TryGetClient(int channel, [NotNullWhen(true)] out ChannelClient? client) {
        if (!ValidChannel(channel)) {
            client = null;
            return false;
        }

        client = channels[channel].Client;
        return true;
    }

    public bool TryGetActiveEndpoint(int channelId, [NotNullWhen(true)] out IPEndPoint? endpoint) {
        if (!ValidChannel(channelId) || !channels.TryGetValue(channelId, out Channel? channel) || channel.Status is ChannelStatus.Inactive) {
            endpoint = null;
            return false;
        }

        endpoint = channel.Endpoint;
        return true;
    }

    private (ushort gamePort, int grpcPort, int channel) AddChannel(string gameIp, string grpcGameIp, bool instancedContent, string instance, int? requested) {
        int channelId = ChannelRegisterRules.Pick(channels.Keys.ToHashSet(), instancedContent, requested);
        if (channelId < 0) {
            logger.Error("No channel id for {GameIp} (instanced content: {Instanced}, asked for: {Requested})", gameIp, instancedContent, requested);
            return (0, 0, -1);
        }

        int newGamePort = Target.BaseGamePort + channelId;
        int newGrpcChannelPort = Target.BaseGrpcChannelPort + channelId;

        IPAddress ipAddress = IPAddress.Parse(gameIp);
        IPEndPoint gameEndpoint = new IPEndPoint(ipAddress, newGamePort);

        Uri grpcUri;

        bool isDocker = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";
        if (isDocker) {
            // When running in Docker, use the provided grpcGameIp as hostnames
            grpcUri = new Uri($"http://{grpcGameIp}:{newGrpcChannelPort}");
        } else {
            // Outside of Docker, parse the IP addresses normally
            IPAddress grpcIpAddress = IPAddress.Parse(grpcGameIp);
            grpcUri = new Uri($"http://{grpcIpAddress}:{newGrpcChannelPort}");
        }

        GrpcChannel grpcChannel = GrpcChannel.ForAddress(grpcUri);
        var client = new ChannelClient(grpcChannel);
        var healthClient = new Health.HealthClient(grpcChannel);
        var activeChannel = new Channel(ChannelStatus.Pending, channelId, instancedContent, gameEndpoint, client, healthClient, (ushort) newGamePort, newGrpcChannelPort) {
            Instance = instance,
        };
        if (!channels.TryAdd(channelId, activeChannel)) {
            logger.Error("Failed to add channel {Channel}", channelId);
            return (0, 0, -1);
        }

        var cancel = new CancellationTokenSource();
        Task.Factory.StartNew(() => MonitorChannel(activeChannel, cancel), cancellationToken: cancel.Token);

        return ((ushort) newGamePort, newGrpcChannelPort, channelId);
    }

    private async Task MonitorChannel(Channel channel, CancellationTokenSource cancellationTokenSource) {
        CancellationToken cancellationToken = cancellationTokenSource.Token;
        logger.Information("Begin monitoring game channel: {Channel} for {EndPoint}", channel.Id, channel.Endpoint);

        do {
            try {
                HealthCheckResponse response = await channel.Health.CheckAsync(new HealthCheckRequest(), deadline: DateTime.UtcNow.AddSeconds(5),
                    cancellationToken: cancellationToken);
                switch (response.Status) {
                    case HealthCheckResponse.Types.ServingStatus.Serving:
                        if (SetStatusIf(channel, ch => ch.Status is ChannelStatus.Inactive or ChannelStatus.Pending, ChannelStatus.Active)) {
                            logger.Information("Channel {Channel} has become active", channel.Id);
                            Active(channel);
                        }
                        break;
                    default:
                        if (SetStatusIf(channel, ch => ch.Status is ChannelStatus.Active || ch.Status is ChannelStatus.Pending && ch.StartOver, ChannelStatus.Inactive)) {
                            Inactive(channel);
                            logger.Information("Channel {Channel} has become inactive due to {Status}", channel.Id, response.Status);
#if !DEBUG
                            await cancellationTokenSource.CancelAsync();
#endif
                        }
                        break;
                }
            } catch (RpcException ex) {
                if (ex.Status.StatusCode != StatusCode.Unavailable) {
                    logger.Warning("{Error} monitoring channel {Channel}", ex.Message, channel.Id);
                }
                // A Game server that is still starting does not answer yet: its channel stays pending (its id is
                // not given to another Game server) until the start grace is over.
                bool wasActive = false;
                lock (registerLock) {
                    if (channel.Status is ChannelStatus.Active) {
                        wasActive = true;
                        channel.Status = ChannelStatus.Inactive;
                    } else if (channel.Status is not ChannelStatus.Pending || channel.StartOver) {
                        channel.Status = ChannelStatus.Inactive;
                    }
                }
                if (wasActive) {
                    logger.Information("Channel {Channel} has become inactive", channel.Id);
                    Inactive(channel);

#if !DEBUG
                    await cancellationTokenSource.CancelAsync();
#endif
                }
            }

            try {
                await Task.Delay(MonitorInterval, cancellationToken);
            } catch (OperationCanceledException) {
                // Handle cancellation gracefully
                break;
            }
        } while (!cancellationToken.IsCancellationRequested);
        logger.Warning("End monitoring game channel: {Channel} for {EndPoint}", channel.Id, channel.Endpoint);
        channels.TryRemove(channel.Id, out _);
    }

    public IEnumerator<(int, ChannelClient)> GetEnumerator() {
        foreach (Channel channel in channels.Values.Where(ch => ch.Status is ChannelStatus.Active)) {
            yield return (channel.Id, channel.Client);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() {
        return GetEnumerator();
    }

    // A channel that became inactive (its game server is gone).
    public event Action<int>? ChannelInactive;

    // The monitor changes a channel's status under the registration lock, as the two takers (Register and
    // FindOrCreateChannelByIp) do: a channel taken a moment before is not set back by a check made before it was taken.
    private bool SetStatusIf(Channel channel, Func<Channel, bool> when, ChannelStatus status) {
        lock (registerLock) {
            if (!when(channel)) {
                return false;
            }
            channel.Status = status;
            return true;
        }
    }

    // The status is already set (SetStatusIf); these tell the rest of the World server.
    private void Inactive(Channel channel) {
        try {
            ChannelInactive?.Invoke(channel.Id);
        } catch (Exception ex) {
            logger.Warning(ex, "Channel {Channel} inactive handler failed", channel.Id);
        }
        UpdateAllPlayersToOffline(channel.Id);
        UpdateChannels(channel.Id);
    }

    private void Active(Channel channel) {
        UpdateChannels(channel.Id);
        // Load custom string boards
        foreach ((int id, string message) in worldServer.GetCustomStringBoards()) {
            channel.Client.Admin(new AdminRequest {
                AddStringBoard = new AdminRequest.Types.AddStringBoard {
                    Id = id,
                    Message = message,
                },
            });
        }
        // The notice banner, when on (a new channel starts with it off). A failed replay leaves the channel up.
        if (worldServer.NoticeConfig().banner) {
            try {
                channel.Client.Admin(new AdminRequest {
                    Notice = new AdminRequest.Types.Notice {
                        Kind = NoticeRules.Banner,
                        Action = NoticeRules.On,
                    },
                });
            } catch (RpcException ex) {
                logger.Warning(ex, "Channel {Channel}: notice banner replay failed", channel.Id);
            }
        }
    }

    private void UpdateAllPlayersToOffline(int channelId) {
        foreach (PlayerInfo playerInfo in playerInfoLookup.GetPlayersOnChannel(channelId)) {
            // Compared and written in one step: a player who logged in on another channel since the list was
            // read stays online there, and the other channels are not told otherwise.
            if (playerInfo.ApplyIfOn(channelId, new PlayerUpdateRequest {
                    AccountId = playerInfo.AccountId,
                    CharacterId = playerInfo.CharacterId,
                    LastOnlineTime = DateTime.UtcNow.ToEpochSeconds(),
                    Channel = -1,
                }) == null) {
                continue;
            }

            foreach ((int id, ChannelClient channelClient) in this) {
                if (id == channelId) {
                    continue;
                }
                channelClient.UpdatePlayer(new PlayerUpdateRequest {
                    AccountId = playerInfo.AccountId,
                    CharacterId = playerInfo.CharacterId,
                    LastOnlineTime = DateTime.UtcNow.ToEpochSeconds(),
                    Channel = -1,
                    Async = true,
                });
            }
        }
    }

    private void UpdateChannels(int exclueChannelBroadcast = -1) {
        foreach ((int id, ChannelClient channelClient) in this) {
            if (id == exclueChannelBroadcast) {
                continue;
            }

            List<int> channelList = channels.Values.Where(ch => ch.Status is ChannelStatus.Active && !ch.InstancedContent).Select(ch => ch.Id).ToList();
            channelClient.UpdateChannels(new Maple2.Server.Channel.Service.ChannelsUpdateRequest {
                Channels = {
                    channelList,
                },
            });
        }
    }

    private class Channel {
        public ChannelStatus Status { get; set; }
        // When the channel became pending (Environment.TickCount64).
        private long pendingSince = Environment.TickCount64;
        // Whether a pending channel's start grace is over (ChannelRegisterRules.StartGraceMs).
        public bool StartOver => ChannelRegisterRules.UnansweredCheckEndsChannel(false, Environment.TickCount64 - pendingSince);

        public void Pending() {
            Status = ChannelStatus.Pending;
            pendingSince = Environment.TickCount64;
        }
        // Names the Game server process that holds this channel.
        public string Instance { get; set; } = string.Empty;

        public readonly int Id;
        public readonly bool InstancedContent;

        public readonly IPEndPoint Endpoint;
        public readonly ushort GamePort;
        public readonly int GrpcPort;

        public readonly ChannelClient Client;
        public readonly Health.HealthClient Health;

        public Channel(ChannelStatus status, int id, bool instancedContent, IPEndPoint endpoint, ChannelClient client, Health.HealthClient health, ushort gamePort, int grpcPort) {
            Status = status;
            Id = id;
            InstancedContent = instancedContent;
            Endpoint = endpoint;
            Client = client;
            Health = health;
            GamePort = gamePort;
            GrpcPort = grpcPort;
        }
    }
}
