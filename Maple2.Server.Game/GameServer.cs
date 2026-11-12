using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Autofac;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.PacketLib.Tools;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Core.Network;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Session;
using Maple2.Server.Core.Helpers;
using Maple2.Server.Game.Util;
using Maple2.Server.Game.DebugGraphics;

namespace Maple2.Server.Game;

public class GameServer : Server<GameSession> {
    private readonly object mutex = new();
    private readonly FieldManager.Factory fieldFactory;
    private readonly HashSet<GameSession> connectingSessions;
    private readonly ConcurrentDictionary<long, GameSession> sessions;
    private readonly List<GameEvent> endedOpenPeriods = [];
    private readonly ImmutableList<SystemBanner> bannerCache;
    private readonly ConcurrentDictionary<int, PremiumMarketItem> premiumMarketCache;
    private readonly GameStorage gameStorage;
    private readonly IGraphicsContext debugGraphicsContext;

    private readonly ItemMetadataStorage itemMetadataStorage;

    private static short _channel = 0;

    public GameServer(FieldManager.Factory fieldFactory, PacketRouter<GameSession> router, IComponentContext context, GameStorage gameStorage, ItemMetadataStorage itemMetadataStorage, ServerTableMetadataStorage serverTableMetadataStorage, IGraphicsContext debugGraphicsContext, int port, int channel)
            : base((ushort) port, router, context, serverTableMetadataStorage) {
        _channel = (short) channel;
        this.fieldFactory = fieldFactory;
        connectingSessions = [];
        sessions = new ConcurrentDictionary<long, GameSession>();
        this.gameStorage = gameStorage;
        this.debugGraphicsContext = debugGraphicsContext;
        this.itemMetadataStorage = itemMetadataStorage;

        using GameStorage.Request db = gameStorage.Context();
        bannerCache = db.GetBanners().ToImmutableList();

        premiumMarketCache = new ConcurrentDictionary<int, PremiumMarketItem>();
        foreach ((int id, MeretMarketItemMetadata marketItemMetadata) in serverTableMetadataStorage.MeretMarketTable.Entries) {
            if (marketItemMetadata.ParentId != 0) {
                if (premiumMarketCache.TryGetValue(marketItemMetadata.ParentId, out PremiumMarketItem? parentItem) &&
                    itemMetadataStorage.TryGet(parentItem.Metadata.ItemId, out ItemMetadata? subItemMetadata)) {
                    parentItem.AdditionalQuantities.Add(new PremiumMarketItem(marketItemMetadata, subItemMetadata));
                }
                continue;
            }

            if (!itemMetadataStorage.TryGet(marketItemMetadata.ItemId, out ItemMetadata? itemMetadata)) {
                continue;
            }
            premiumMarketCache.TryAdd(id, new PremiumMarketItem(marketItemMetadata, itemMetadata));
        }

        debugGraphicsContext.Initialize();
    }

    public override void OnConnected(GameSession session) {
        lock (mutex) {
            connectingSessions.Remove(session);
        }
        sessions[session.CharacterId] = session;
    }

    public override void OnDisconnected(GameSession session) {
        lock (mutex) {
            connectingSessions.Remove(session);
        }
        // Only remove if this is still the registered session (reference equality).
        // During same-channel migration, Disconnect() drains the send queue (up to 2s)
        // before Dispose runs. In that window the client reconnects and the new session's
        // OnConnected replaces this dict entry. Without this check, the old session's
        // OnDisconnected would remove the new session, leaving the player unregistered
        // and causing all subsequent heartbeats to fail.
        // For cross-channel migration this is a non-issue since each server has its own dict.
        sessions.TryRemove(KeyValuePair.Create(session.CharacterId, session));
    }

    public bool GetSession(long characterId, [NotNullWhen(true)] out GameSession? session) {
        return sessions.TryGetValue(characterId, out session);
    }

    public GameSession? GetSessionByAccountId(long accountId) {
        return sessions.Values.FirstOrDefault(session => session.AccountId == accountId);
    }

    // Reports every session to a World server that was restarted: the player is online on this channel again
    // and gets heartbeats again. A party the World server no longer has is ended for its member here.
    // False when the World server stopped answering on the way.
    public bool AnnounceSessions(Maple2.Server.World.Service.World.WorldClient world) {
        foreach (GameSession session in sessions.Values) {
            try {
                if (!session.AnnounceToWorld(world)) {
                    continue;
                }
            } catch (Grpc.Core.RpcException ex) {
                Logger.Warning("Could not report {CharacterId} to the World server: {Status}", session.CharacterId, ex.StatusCode);
                // A World server that is gone ends the report, to be tried again. A report the World server
                // refuses for this one session is skipped, so it does not hold up the sessions after it.
                if (ChannelRegisterRules.WorldGone(ex.StatusCode)) {
                    return false;
                }
            }
        }
        return true;
    }

    public List<GameSession> GetSessions() {
        return sessions.Values.ToList();
    }

    protected override void AddSession(GameSession session) {
        lock (mutex) {
            connectingSessions.Add(session);
        }

        Logger.Information("Game client connecting: {Session}", session);
        session.Start();
    }

    public FieldManager? GetField(int mapId, int roomId = 0) {
        return fieldFactory.Get(mapId, roomId: roomId);
    }

    // The loaded home of an account on this channel, if any.
    public HomeFieldManager? FindHome(long ownerId) {
        return fieldFactory.FindHome(ownerId);
    }

    public DungeonFieldManager? CreateDungeon(DungeonRoomMetadata metadata, long requesterId, int size, int partyId) {
        return fieldFactory.CreateDungeon(metadata, requesterId, size, partyId);
    }

    public FieldManager? CreateRankDuel(int matchId, long first, long second) {
        return fieldFactory.CreateRankDuel(matchId, first, second);
    }

    // Whether the rank duel field of this room still runs on this channel. Never creates a field.
    public bool HasPvpMatch(int roomId) {
        return fieldFactory.HasPvpMatch(roomId);
    }

    public FieldManager? CreateArena(int matchId, int arenaType, IReadOnlyList<long> characters) {
        return fieldFactory.CreateArena(matchId, arenaType, characters);
    }

    // Whether the dungeon room still runs on this channel. Never creates a field.
    public bool HasDungeon(int roomId) {
        return fieldFactory.HasDungeon(roomId);
    }

    public DungeonFieldManager? FindDungeonField(long characterId, int mapId) {
        return fieldFactory.FindDungeonField(characterId, mapId);
    }

    public MigrationError DestroyDungeon(int roomId) {
        return fieldFactory.DestroyDungeon(roomId);
    }

    public IList<GameEvent> FindEvent(GameEventType type) {
        return eventCache.Values.Where(gameEvent => gameEvent.Metadata.Type == type && gameEvent.IsActive()).ToList();
    }

    public GameEvent? FindEvent(int eventId) {
        return eventCache.TryGetValue(eventId, out GameEvent? gameEvent) && gameEvent.IsActive() ? gameEvent : null;
    }

    public void AddEvent(GameEvent gameEvent) {
        if (!eventCache.TryAdd(gameEvent.Id, gameEvent)) {
            return;
        }

        foreach (GameSession session in sessions.Values) {
            if (!session.SendsEvent(gameEvent)) {
                continue;
            }
            session.Send(GameEventPacket.Add(gameEvent));
        }
    }

    public void RemoveEvent(int eventId) {
        if (!eventCache.Remove(eventId, out GameEvent? gameEvent)) {
            return;
        }
        if (gameEvent.Metadata.Data is DungeonOpenPeriod) {
            lock (endedOpenPeriods) {
                endedOpenPeriods.Add(gameEvent);
            }
        }

        foreach (GameSession session in sessions.Values) {
            session.Send(GameEventPacket.Remove(gameEvent.Id));
        }
    }

    // Every DungeonOpenPeriod event this server has held, the ended ones first: a client that was in the game while
    // one ran still goes by it (DungeonRoomRules.OpenPeriodTags).
    public List<DungeonRoomRules.OpenPeriod> GetOpenPeriods() {
        List<GameEvent> events;
        lock (endedOpenPeriods) {
            events = endedOpenPeriods.ToList();
        }
        events.AddRange(eventCache.Values);
        var periods = new List<DungeonRoomRules.OpenPeriod>();
        foreach (GameEvent gameEvent in events) {
            if (gameEvent.Metadata.Data is DungeonOpenPeriod period) {
                periods.Add(new DungeonRoomRules.OpenPeriod(gameEvent.Id, period.Tag, gameEvent.Metadata.StartTime, gameEvent.Metadata.EndTime));
            }
        }
        return periods;
    }

    public IEnumerable<GameEvent> GetEvents() => eventCache.Values.Where(gameEvent => gameEvent.IsActive());

    public IList<SystemBanner> GetSystemBanners() => bannerCache;

    public ICollection<PremiumMarketItem> GetPremiumMarketItems(params int[] tabIds) {
        if (tabIds.Length == 0) {
            return premiumMarketCache.Values;
        }

        return premiumMarketCache.Values.Where(item => tabIds.Contains(item.TabId)).ToList();
    }

    public PremiumMarketItem? GetPremiumMarketItem(int id, int subId) {
        if (subId == 0) {
            return premiumMarketCache.GetValueOrDefault(id);
        }

        return premiumMarketCache.TryGetValue(id, out PremiumMarketItem? item) ? item.AdditionalQuantities.FirstOrDefault(subItem => subItem.Id == subId) : null;
    }

    public void DailyReset() {
        foreach (GameSession session in sessions.Values) {
            session.DailyReset();
        }
    }

    public void WeeklyReset() {
        foreach (GameSession session in sessions.Values) {
            session.WeeklyReset();
        }
    }

    public void MonthlyReset() {
        foreach (GameSession session in sessions.Values) {
            session.MonthlyReset();
        }
        ReloadArchitectScores();
    }

    // The month's architect score of a home counts this month's commendations only, so the loaded copies (the home
    // fields' and the online owners') are read again when a month begins.
    private void ReloadArchitectScores() {
        HashSet<long> owners = [.. fieldFactory.HomeOwners(), .. sessions.Values.Select(session => session.AccountId)];
        (long begin, long end) = HomeCommendRules.MonthAtReset(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), TimeZoneInfo.Local);
        using GameStorage.Request db = gameStorage.Context();
        foreach (long ownerId in owners) {
            (int current, int total) = db.GetHomeCommendScore(ownerId, begin, end);
            HomeArchitectScore.Apply(fieldFactory.FindHome(ownerId), GetSessionByAccountId(ownerId), current, total);
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken) {
        debugGraphicsContext.CleanUp();

        lock (mutex) {
            foreach (GameSession session in connectingSessions) {
                session.Send(NoticePacket.Disconnect(new InterfaceText("GameServer Maintenance")));
                session.Dispose();
            }
        }
        foreach (GameSession session in sessions.Values) {
            session.Send(NoticePacket.Disconnect(new InterfaceText("GameServer Maintenance")));
            session.Dispose();
        }
        fieldFactory.Dispose();

        return base.StopAsync(cancellationToken);
    }

    public void Broadcast(ByteWriter packet) {
        foreach (GameSession session in sessions.Values) {
            session.Send(packet);
        }
    }

    public static short GetChannel() {
        return _channel;
    }
}
