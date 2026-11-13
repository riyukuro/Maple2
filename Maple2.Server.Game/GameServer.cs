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
using Maple2.Server.Game.Packets;
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
    // The events and the sessions that loaded them; set in the constructor (a test's server makes it on first use).
    private ChannelGameEvents<GameSession>? gameEvents;
    private readonly Timer? eventEdgeTimer;

    private readonly ItemMetadataStorage itemMetadataStorage;

    private static short _channel = 0;

    // The shutdown time a GM announced (Unix seconds, 0 for none), sent to the players who enter this channel later.
    public long ShutdownTime { get; set; }
    // The notice banner of the GM notice config (NoticeRules), sent at each session's first field entry while on.
    public bool NoticeBanner { get; set; }
    // The term tags a GM started or ended on this channel (made on first use: a test's server is uninitialized).
    private TermTagState? termTags;
    public TermTagState TermTags => LazyInitializer.EnsureInitialized(ref termTags);

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

        premiumMarketCache = PremiumMarket(serverTableMetadataStorage.MeretMarketTable,
            itemId => itemMetadataStorage.TryGet(itemId, out ItemMetadata? itemMetadata) ? itemMetadata : null);

        debugGraphicsContext.Initialize();
        gameEvents = new ChannelGameEvents<GameSession>(eventCache, DateTime.Now);
        eventEdgeTimer = new Timer(_ => {
            SendEventEdges(DateTime.Now);
            SendTermEdges(serverTableMetadataStorage.TermTagTable, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private ChannelGameEvents<GameSession> GameEvents => gameEvents ??= new ChannelGameEvents<GameSession>(eventCache, DateTime.Now);

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
        GameEvents.Drop(session);
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

    public DungeonFieldManager? CreateDungeon(DungeonRoomMetadata metadata, long requesterId, int size, int partyId,
                                              bool randomMatch = false) {
        return fieldFactory.CreateDungeon(metadata, requesterId, size, partyId, randomMatch);
    }

    public FieldManager? CreateRankDuel(int matchId, long first, long second) {
        return fieldFactory.CreateRankDuel(matchId, first, second);
    }

    public FieldManager? CreateGuildVsGame(int matchId, long blueGuildId, long redGuildId) {
        return fieldFactory.CreateGuildVsGame(matchId, blueGuildId, redGuildId);
    }

    public FieldManager? CreateGuildPvp(int matchId, long guildA, long guildB) {
        return fieldFactory.CreateGuildPvp(matchId, guildA, guildB);
    }

    // Whether the rank duel field of this room still runs on this channel. Never creates a field.
    public bool HasPvpMatch(int roomId) {
        return fieldFactory.HasPvpMatch(roomId);
    }

    public FieldManager? CreateArena(int matchId, int arenaType, IReadOnlyList<long> characters, IReadOnlyDictionary<long, long>? units = null) {
        return fieldFactory.CreateArena(matchId, arenaType, characters, units);
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
        return GameEvents.Active(DateTime.Now).Where(gameEvent => gameEvent.Metadata.Type == type).ToList();
    }

    public GameEvent? FindEvent(int eventId) {
        return GameEvents.Find(eventId, DateTime.Now);
    }

    public void AddEvent(GameEvent gameEvent) {
        List<GameSession>? receivers = GameEvents.Add(gameEvent, DateTime.Now);
        if (receivers == null) {
            return;
        }

        foreach (GameSession session in receivers) {
            if (!session.SendsEvent(gameEvent)) {
                continue;
            }
            session.Send(GameEventPacket.Add(gameEvent));
        }
    }

    public void RemoveEvent(int eventId) {
        if (GameEvents.Remove(eventId) is not var (gameEvent, receivers)) {
            return;
        }
        if (gameEvent.Metadata.Data is DungeonOpenPeriod) {
            lock (endedOpenPeriods) {
                endedOpenPeriods.Add(gameEvent);
            }
        }

        foreach (GameSession session in receivers) {
            session.Send(GameEventPacket.Remove(gameEvent.Id));
        }
    }

    // The session's events at its entry (GameEventPacket.Load), sent so that no edge comes between (see
    // ChannelGameEvents.Load). By start, then id: where the client keeps one value per key (a beauty shop's sale
    // rate), the last of the list stays, and EventSaleRules.BeautyShopRate picks the same one.
    public void LoadEvents(GameSession session) {
        GameEvents.Load(session, DateTime.Now, active => session.Send(GameEventPacket.Load(active.Where(session.SendsEvent)
            .OrderBy(gameEvent => gameEvent.StartTime).ThenBy(gameEvent => gameEvent.Id).ToArray())));
    }

    // The day-of-week and time-of-day edges of the events (ChannelGameEvents.Tick), to the sessions that loaded them.
    // A term starts or ends in the online clients when its start or end passes (TermTag mode 1 or 2, as the
    // "termtag" command sends): a table term, or a GM-started one at its end. The players who enter later get it in
    // their load. Ends go first, as the command's restart sends them.
    public void SendTermEdges(TermTagTable table, long now) {
        (List<TermTagTable.Entry> started, List<TermTagTable.Entry> ended) = TermTags.Edges(table, now);
        if (ended.Count > 0) {
            Broadcast(TermTagPacket.End(ended));
        }
        if (started.Count > 0) {
            Broadcast(TermTagPacket.Start(started));
        }
    }

    public void SendEventEdges(DateTime now) {
        ChannelGameEvents<GameSession>.Edges edges;
        try {
            edges = GameEvents.Tick(now);
        } catch (Exception ex) {
            Logger.Error(ex, "Failed to find the game event edges");
            return;
        }
        if (!edges.Any) {
            return;
        }
        foreach (GameEvent gameEvent in edges.Started) {
            Logger.Information("Game event {Id} ({Type}) comes on", gameEvent.Id, gameEvent.Metadata.Type);
        }
        foreach (GameEvent gameEvent in edges.Stopped) {
            Logger.Information("Game event {Id} ({Type}) goes off", gameEvent.Id, gameEvent.Metadata.Type);
        }
        foreach (GameSession session in edges.Receivers) {
            GameEvent[] started = edges.Started.Where(session.SendsEvent).ToArray();
            int[] stopped = edges.Stopped.Where(session.SendsEvent).Select(gameEvent => gameEvent.Id).ToArray();
            if (stopped.Length > 0) {
                session.Send(GameEventPacket.Remove(stopped));
            }
            if (started.Length > 0) {
                session.Send(GameEventPacket.Add(started));
            }
        }
    }

    // Every DungeonOpenPeriod event this server has held, the ended ones first: a client that was in the game while
    // one ran still goes by it (DungeonRoomRules.OpenPeriodTags).
    public List<DungeonRoomRules.OpenPeriod> GetOpenPeriods() {
        List<GameEvent> events;
        lock (endedOpenPeriods) {
            events = endedOpenPeriods.ToList();
        }
        events.AddRange(GameEvents.All());
        var periods = new List<DungeonRoomRules.OpenPeriod>();
        foreach (GameEvent gameEvent in events) {
            if (gameEvent.Metadata.Data is DungeonOpenPeriod period) {
                periods.Add(new DungeonRoomRules.OpenPeriod(gameEvent.Id, period.Tag, gameEvent.Metadata.StartTime, gameEvent.Metadata.EndTime));
            }
        }
        return periods;
    }

    public IEnumerable<GameEvent> GetEvents() => GameEvents.Active(DateTime.Now);

    public IList<SystemBanner> GetSystemBanners() => bannerCache;

    // The Meret Market custom shop (shop_merat_custom.xml): the parents first, then each additionalQuantity child (the
    // bigger packs) under its parent, so a child is kept whatever order the table's rows come back in.
    public static ConcurrentDictionary<int, PremiumMarketItem> PremiumMarket(MeretMarketTable table, Func<int, ItemMetadata?> items) {
        var market = new ConcurrentDictionary<int, PremiumMarketItem>();
        foreach ((int id, MeretMarketItemMetadata metadata) in table.Entries) {
            if (metadata.ParentId == 0 && items(metadata.ItemId) is { } itemMetadata) {
                market.TryAdd(id, new PremiumMarketItem(metadata, itemMetadata));
            }
        }
        foreach (MeretMarketItemMetadata metadata in table.Entries.Values) {
            if (metadata.ParentId != 0 && market.TryGetValue(metadata.ParentId, out PremiumMarketItem? parent) &&
                items(parent.Metadata.ItemId) is { } itemMetadata) {
                parent.AdditionalQuantities.Add(new PremiumMarketItem(metadata, itemMetadata));
            }
        }
        return market;
    }

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

    public void DailyReset() => TakeResets();

    public void WeeklyReset() => TakeResets();

    public void MonthlyReset() {
        TakeResets();
        ReloadArchitectScores();
    }

    // The World's message after a reset: each session takes the resets stored since its state was read, once (a
    // session's save may have taken them already). One session that fails does not stop the others'.
    private void TakeResets() {
        GameStorage.ResetStamps stamps;
        using (GameStorage.Request db = gameStorage.Context()) {
            stamps = db.GetResetStamps();
        }
        foreach (GameSession session in sessions.Values) {
            try {
                session.TakeResets(stamps);
            } catch (Exception ex) {
                Logger.Error(ex, "Reset not taken by {CharacterId}", session.CharacterId);
            }
        }
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
        eventEdgeTimer?.Dispose();

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
