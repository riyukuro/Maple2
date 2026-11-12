using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Grpc.Core;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Core.Sync;
using Serilog;

namespace Maple2.Server.World.Containers;

public class PlayerInfoLookup : IPlayerInfoProvider, IDisposable {
    private readonly TimeSpan syncInterval = TimeSpan.FromSeconds(2);

    private readonly GameStorage gameStorage;
    private readonly ChannelClientLookup channels;
    private readonly CancellationTokenSource tokenSource;

    // TODO: Just using dictionary for now, might need eviction at some point (LRUCache)
    private readonly ConcurrentDictionary<long, PlayerInfo> cache;
    private readonly ConcurrentQueue<PlayerUpdateRequest> events;

    private readonly ILogger logger = Log.ForContext<PlayerInfoLookup>();

    public PlayerInfoLookup(GameStorage gameStorage, ChannelClientLookup channels) {
        this.gameStorage = gameStorage;
        this.channels = channels;
        tokenSource = new CancellationTokenSource();

        cache = new ConcurrentDictionary<long, PlayerInfo>();
        events = new ConcurrentQueue<PlayerUpdateRequest>();

        Task.Factory.StartNew(() => {
            try {
                while (!tokenSource.Token.IsCancellationRequested) {
                    Thread.Sleep(syncInterval);
                    try {
                        Sync();
                    } catch (Exception ex) when (ex is not ObjectDisposedException) {
                        // One failed step (a database error on a record load) must not end the loop: every later
                        // queued update (health, map, death) would stay queued for good.
                        logger.Error(ex, "Player info sync step failed");
                    }
                }
            } catch (ObjectDisposedException) {
                // Expected during shutdown, safe to ignore
            }
        }, tokenSource.Token);
    }

    public void Dispose() {
        tokenSource.Cancel();
        tokenSource.Dispose();
    }

    public bool TryGet(long characterId, [NotNullWhen(true)] out PlayerInfo? info) {
        if (cache.TryGetValue(characterId, out info)) {
            return true;
        }

        // If data is not cached, fetch from database.
        using GameStorage.Request db = gameStorage.Context();
        info = db.GetPlayerInfo(characterId);
        return info != null && cache.TryAdd(characterId, info);
    }

    // Try to get a cached player by account id and that's online, since we need a ChannelClient to notify.
    // If the player is not cached just return false since it was never online.
    // This is a very specific use case, and should be used with caution, mainly used for account wide mail notifications.
    public bool TryGetByAccountId(long accountId, [NotNullWhen(true)] out PlayerInfo? info) {
        info = cache.Values.FirstOrDefault(player => player.AccountId == accountId && player.Online);
        return info is not null;
    }

    public bool Update(PlayerUpdateRequest request) {
        bool result = UpdateOne(request);
        if (request.HasAdminPermissions) {
            // The permissions belong to the account: its other cached characters take the same value.
            foreach (long characterId in PlayerInfoSiblings.Of(cache.Values, request.CharacterId, (AdminPermissions) request.AdminPermissions)) {
                UpdateOne(new PlayerUpdateRequest {
                    AccountId = request.AccountId,
                    CharacterId = characterId,
                    AdminPermissions = request.AdminPermissions,
                    Async = request.Async,
                });
            }
        }
        return result;
    }

    private bool UpdateOne(PlayerUpdateRequest request) {
        // TODO: This support for both sync and async can cause ordering issues where older updates overwrite newer ones.
        if (request.Async) {
            events.Enqueue(request);
            return true;
        }

        return Update(request, out PlayerInfo? info, out UpdateField type) && Notify(info, type);
    }

    private void Sync() {
        if (events.IsEmpty) {
            return;
        }

        var updated = new Dictionary<long, Notification>();
        while (events.TryDequeue(out PlayerUpdateRequest? request)) {
            if (!Update(request, out PlayerInfo? info, out UpdateField type)) {
                continue;
            }

            if (!updated.ContainsKey(request.CharacterId)) {
                updated[request.CharacterId] = new Notification(info);
            }
            updated[request.CharacterId].Type |= type;
        }

        foreach ((long _, Notification notification) in updated) {
            Notify(notification.Info, notification.Type);
        }
    }

    // The request is compared with the record at the time it is applied (see ApplyChanged). False: no entry to
    // update, or no field changed.
    private bool Update(PlayerUpdateRequest request, [NotNullWhen(true)] out PlayerInfo? info, out UpdateField type) {
        type = UpdateField.None;
        if (!TryGet(request.CharacterId, out info)) {
            info = null;
            return false;
        }

        PlayerInfoUpdateEvent? @event = info.ApplyChanged(request);
        type = @event?.Type ?? UpdateField.None;
        return @event != null;
    }

    // Applies the request at once, and only while the record still names `expectedChannel`: the compare and the
    // write are one step, so a login on another channel is never overwritten.
    public bool UpdateIfOn(int expectedChannel, PlayerUpdateRequest request) {
        if (!cache.TryGetValue(request.CharacterId, out PlayerInfo? info)) {
            return false;
        }

        PlayerInfoUpdateEvent? @event = info.ApplyIfOn(expectedChannel, request);
        return @event != null && Notify(info, @event.Type);
    }

    private bool Notify(PlayerInfo info, UpdateField type) {
        var request = new PlayerUpdateRequest {
            AccountId = info.AccountId,
            CharacterId = info.CharacterId,
        };
        request.SetFields(type, info);

        // Forward all updates to channels for caching.
        Parallel.ForEach(channels, entry => {
            try {
                entry.Item2.UpdatePlayer(request);
            } catch (RpcException ex) {
                logger.Warning("[{Error}] Failed to notify channel {Channel} with events: {CharacterId}|{Type}", ex.StatusCode, entry.Item1, info.CharacterId, type);
            }
        });

        return true;
    }

    private record Notification(PlayerInfo Info) {
        public UpdateField Type;
    }

    public PlayerInfo? GetPlayerInfo(long id) {
        TryGet(id, out PlayerInfo? info);
        return info;
    }

    public PlayerInfo[] GetOnlinePlayerInfos() {
        return cache.Values
            .Where(player => player.Online)
            .ToArray();
    }

    public PlayerInfo[] GetPlayersOnChannel(int channelId) {
        return cache.Values
            .Where(player => player.Channel == channelId)
            .ToArray();
    }
}
