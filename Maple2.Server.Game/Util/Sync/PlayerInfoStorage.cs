using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Core.Sync;
using Serilog;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Game.Util.Sync;

public class PlayerInfoStorage {
    private readonly WorldClient world;
    // TODO: Just using dictionary for now, might need eviction at some point (LRUCache)
    private readonly ConcurrentDictionary<long, PlayerInfo> cache;
    private readonly ConcurrentDictionary<long, IDictionary<int, PlayerInfoListener>> listeners;

    private readonly ILogger logger = Log.ForContext<PlayerInfoStorage>();

    // private readonly ConcurrentDictionary<Key, IDictionary<PlayerInfoUpdateEvent.Type, PlayerSubscriber>> subscribers;

    public PlayerInfoStorage(WorldClient world) {
        this.world = world;

        cache = new ConcurrentDictionary<long, PlayerInfo>();
        listeners = new ConcurrentDictionary<long, IDictionary<int, PlayerInfoListener>>();
    }

    public bool GetOrFetch(long characterId, [NotNullWhen(true)] out PlayerInfo? info,
                           [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) {
        if (cache.TryGetValue(characterId, out info)) {
            return true;
        }
        // A name lookup that found no character gives id 0 (a whisper to an unknown name): that is a normal not-found,
        // not a request the World would refuse. The caller is logged so a lookup that should never see id 0 can be found.
        if (characterId <= 0) {
            logger.Debug("No PlayerInfo for character id {CharacterId} (from {CallerFile}:{CallerLine})",
                characterId, Path.GetFileName(callerFile), callerLine);
            return false;
        }

        // Fetch PlayerInfo from World server.
        try {
            PlayerInfoResponse response = world.PlayerInfo(new PlayerInfoRequest {
                CharacterId = characterId,
            });
            var characterInfo = new CharacterInfo(response.AccountId, response.CharacterId, response.Name, response.Motto, response.Picture, (Gender) response.Gender, (Job) response.Job, (short) response.Level) {
                GearScore = response.GearScore,
                CurrentHp = response.Health.CurrentHp,
                TotalHp = response.Health.TotalHp,
                MapId = response.MapId,
                RoomId = response.RoomId,
                DungeonId = response.DungeonId,
                Channel = (short) response.Channel,
                UpdateTime = response.UpdateTime,
                GuildName = response.GuildName,
                GuildId = response.GuildId,
                MentorRole = (MentorRole) response.MentorRole,
                DeathState = (DeathState) response.DeathState,
            };
            var trophy = new AchievementInfo {
                Adventure = response.Trophy.Adventure,
                Combat = response.Trophy.Combat,
                Lifestyle = response.Trophy.Lifestyle,
            };

            info = new PlayerInfo(characterInfo, response.Home.Name, trophy, response.Clubs.Select(club => club.Id).ToList()) {
                PlotMapId = response.Home.MapId,
                PlotNumber = response.Home.PlotNumber,
                ApartmentNumber = response.Home.ApartmentNumber,
                PlotExpiryTime = response.Home.ExpiryTime.Seconds,
                PremiumTime = response.PremiumTime,
                LastOnlineTime = response.LastOnlineTime,
                AccountAdminPermissions = (AdminPermissions) response.AdminPermissions,
                DungeonEnterLimits = response.DungeonEnterLimits.ToDictionary(limit => limit.DungeonId, limit => (DungeonEnterLimit) limit.Limit),
            };

            cache[characterId] = info;
            return true;
        } catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound) {
            return false;
        } catch (RpcException ex) {
            logger.Error("Failed to fetch PlayerInfo for {CharacterId} (from {CallerFile}:{CallerLine}): {Status}",
                characterId, Path.GetFileName(callerFile), callerLine, ex.Status);
            return false;
        }
    }

    public void Listen(long characterId, PlayerInfoListener listener) {
        if (!listeners.ContainsKey(characterId)) {
            listeners[characterId] = new ConcurrentDictionary<int, PlayerInfoListener>();
        }

        listeners[characterId][listener.GetHashCode()] = listener;
    }

    public void SendUpdate(PlayerUpdateRequest request) {
        try {
            //PlayerInfoCache
            world.UpdatePlayer(request);
        } catch (RpcException) { /* ignored */ }
    }

    public bool ReceiveUpdate(PlayerUpdateRequest request) {
        PlayerInfoUpdateEvent @event = cache.TryGetValue(request.CharacterId, out PlayerInfo? info)
            ? new PlayerInfoUpdateEvent(info, request)
            : new PlayerInfoUpdateEvent(request);

        if (@event.Type == UpdateField.None) {
            return false; // No fields changed
        }

        if (!GetOrFetch(@event.Request.CharacterId, out info)) {
            return false; // Character does not exist
        }

        info.Update(@event);
        return Notify(info, @event.Type);
    }

    private bool Notify(PlayerInfo info, UpdateField type) {
        if (!listeners.TryGetValue(info.CharacterId, out IDictionary<int, PlayerInfoListener>? dict)) {
            return false;
        }

        int called = 0;
        foreach ((int id, PlayerInfoListener listener) in dict) {
            if ((listener.Type & type) == default) {
                continue;
            }

            // One listener that fails (a session that is gone) must not keep the others from their update.
            bool completed;
            try {
                completed = listener.Callback(type, info);
            } catch (Exception ex) {
                logger.Error(ex, "PlayerInfo listener for {CharacterId} failed on {Type}", info.CharacterId, type);
                continue;
            }
            called++;
            if (completed) {
                dict.Remove(id, out _);
            }
        }
        if (type.HasFlag(UpdateField.Map)) {
            logger.Debug("PlayerInfo {CharacterId} map {MapId} ({Type}): {Count} listeners", info.CharacterId, info.MapId, type, called);
        }
        return true;
    }
}
