using System.Collections.Concurrent;
using Grpc.Core;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Channel.Service;
using Maple2.Server.Core.Helpers;
using Maple2.Server.World.Containers;
using Maple2.Server.Core.Sync;
using Maple2.Tools.Extensions;
using Maple2.Tools.Scheduler;
using Serilog;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;
using LoginClient = Maple2.Server.Login.Service.Login.LoginClient;


namespace Maple2.Server.World;

public class WorldServer {
    #region Autofac Autowired
    private readonly GameStorage gameStorage;
    private readonly ChannelClientLookup channelClients;
    private readonly ServerTableMetadataStorage serverTableMetadata;
    private readonly ItemMetadataStorage itemMetadata;
    private readonly QuestMetadataStorage questMetadata;
    private readonly GlobalPortalLookup globalPortalLookup;
    private readonly WorldBossLookup worldBossLookup;
    private readonly FieldWarLookup fieldWarLookup;
    private readonly PlayerInfoLookup playerInfoLookup;
    private readonly GuildLookup guildLookup;
    private readonly GuildVsGameLookup guildVsGameLookup;
    private ConstantsTable Constants => serverTableMetadata.ConstantsTable;
    #endregion

    private readonly Thread thread;
    private readonly Thread heartbeatThread;
    private readonly EventQueue scheduler;
    private readonly CancellationTokenSource tokenSource = new();
    private readonly ConcurrentDictionary<int, string> memoryStringBoards;
    private static int _globalIdCounter;

    private readonly ILogger logger = Log.ForContext<WorldServer>();

    private readonly LoginClient login;

    public WorldServer(GameStorage gameStorage, ChannelClientLookup channelClients, ServerTableMetadataStorage serverTableMetadata, GlobalPortalLookup globalPortalLookup, WorldBossLookup worldBossLookup, FieldWarLookup fieldWarLookup, PlayerInfoLookup playerInfoLookup, GuildLookup guildLookup, GuildVsGameLookup guildVsGameLookup, LoginClient login, ItemMetadataStorage itemMetadata, QuestMetadataStorage questMetadata) {
        this.gameStorage = gameStorage;
        this.channelClients = channelClients;
        this.serverTableMetadata = serverTableMetadata;
        this.globalPortalLookup = globalPortalLookup;
        this.worldBossLookup = worldBossLookup;
        this.fieldWarLookup = fieldWarLookup;
        this.playerInfoLookup = playerInfoLookup;
        this.guildLookup = guildLookup;
        this.guildVsGameLookup = guildVsGameLookup;
        this.login = login;
        this.itemMetadata = itemMetadata;
        this.questMetadata = questMetadata;
        scheduler = new EventQueue(logger);
        scheduler.Start();
        memoryStringBoards = [];

        // World initialization: set all characters offline and cleanup unowned items
        using GameStorage.Request db = gameStorage.Context();
        db.SetAllCharacterToOffline();
        db.DeleteUnownedItems();
        // Mannequin items on cubes that no longer exist are logged; mailing the bound ones back is opt-in.
        db.ReturnOrphanedDollItems(mail: Environment.GetEnvironmentVariable("MAIL_ORPHANED_DOLL_ITEMS") == "1");

        StartDailyReset();
        StartWeeklyReset();
        StartMonthlyReset();
        StartWorldEvents();
        StartWorldBossEvents();
        ScheduleGameEvents();
        FieldPlotExpiryCheck();
        thread = new Thread(Loop);
        thread.Start();

        heartbeatThread = new Thread(Heartbeat);
        heartbeatThread.Start();
    }

    private void Heartbeat() {
        while (!tokenSource.Token.IsCancellationRequested) {
            try {
                Task.Delay(TimeSpan.FromMilliseconds(HeartbeatRules.IntervalMs), tokenSource.Token).Wait(tokenSource.Token);

                login.Heartbeat(new HeartbeatRequest(), cancellationToken: tokenSource.Token);

                foreach (PlayerInfo playerInfo in playerInfoLookup.GetOnlinePlayerInfos()) {
                    if (playerInfo.CharacterId == 0) {
                        logger.Information("Player {CharacterId} is online without a character id, setting to offline", playerInfo.CharacterId);
                        // No character id to tell the channels about: only the record changes, under its lock
                        // and only while it still names the channel it was read with.
                        playerInfo.ApplyIfOn(playerInfo.Channel, new PlayerUpdateRequest {
                            AccountId = playerInfo.AccountId,
                            CharacterId = playerInfo.CharacterId,
                            LastOnlineTime = DateTime.UtcNow.ToEpochSeconds(),
                            Channel = -1,
                        });
                        continue;
                    }
                    int askedChannel = playerInfo.Channel;
                    if (!channelClients.TryGetClient(askedChannel, out ChannelClient? channel)) {
                        // Player is online and without a channel, set them to offline.
                        logger.Information("Player {CharacterId} is online without a channel, setting to offline", playerInfo.CharacterId);
                        SetOfflineIfOn(playerInfo, askedChannel);
                        continue;
                    }

                    try {
                        HeartbeatResponse? response = channel.Heartbeat(new HeartbeatRequest {
                            CharacterId = playerInfo.CharacterId,
                        }, cancellationToken: tokenSource.Token);
                        if (response is { Success: false }) {
                            // A player between two channels has no session on the old one, so the answer does
                            // not count while the player is on the way out of that channel. Otherwise only
                            // the answer of the channel the player is still recorded on counts. The record
                            // is compared and changed in one step and at once: a queued change, or one made
                            // after a separate check, could land on top of the login on the new channel and
                            // leave the player recorded as offline there.
                            if (MarksOffline(playerInfo, askedChannel)) {
                                SetOfflineIfOn(playerInfo, askedChannel);
                            }
                            continue;
                        }
                        playerInfo.RetryHeartbeat = 3;
                    } catch (RpcException) {
                        if (playerInfo.RetryHeartbeat >= 0) {
                            playerInfo.RetryHeartbeat--;
                            continue;
                        }
                        if (MarksOffline(playerInfo, askedChannel)) {
                            SetOfflineIfOn(playerInfo, askedChannel);
                        }
                    }

                }
            } catch (OperationCanceledException) {
                break;
            } catch (Exception ex) {
                logger.Warning(ex, "Heartbeat loop error");
            }
        }
    }

    // The channel changes under way, noted when the World server hands out the migration token.
    public MigrationTracker Migrations { get; } = new(MigrationLifetime);
    public static readonly TimeSpan MigrationLifetime = TimeSpan.FromSeconds(30);

    private bool MarksOffline(PlayerInfo playerInfo, int askedChannel) {
        return HeartbeatRules.MarksOffline(askedChannel, playerInfo.Channel,
            Migrations.IsLeaving(playerInfo.CharacterId, askedChannel, DateTime.UtcNow));
    }

    // Sets the record offline at once, and only while it still names `channel`: a login on another channel in
    // the meantime is kept.
    public void SetOfflineIfOn(PlayerInfo playerInfo, int channel) {
        if (playerInfoLookup.UpdateIfOn(channel, new PlayerUpdateRequest {
            AccountId = playerInfo.AccountId,
            CharacterId = playerInfo.CharacterId,
            LastOnlineTime = DateTime.UtcNow.ToEpochSeconds(),
            Channel = -1,
        })) {
            Migrations.End(playerInfo.CharacterId);
        }
    }

    private void Loop() {
        while (!tokenSource.Token.IsCancellationRequested) {
            try {
                scheduler.InvokeAll();
            } catch (Exception e) {
                logger.Error(e, "Error in world server loop");
            }
            try {
                Task.Delay(TimeSpan.FromMinutes(1), tokenSource.Token).Wait();
            } catch { /* do nothing */
            }
        }
    }

    public void Stop() {
        tokenSource.Cancel();
        thread.Join();
        heartbeatThread.Join();
        scheduler.Stop();
    }

    #region Daily Reset
    private void StartDailyReset() {
        // Daily reset
        using GameStorage.Request db = gameStorage.Context();
        DateTime lastReset = db.GetLastDailyReset();

        // Get last midnight.
        DateTime now = DateTime.Now;
        var lastMidnight = new DateTime(now.Year, now.Month, now.Day);
        if (lastReset < lastMidnight) {
            db.DailyReset();
        }

        DateTime nextMidnight = lastMidnight.AddDays(1);
        TimeSpan timeUntilMidnight = nextMidnight - now;
        scheduler.Schedule(ScheduleDailyReset, timeUntilMidnight);
    }

    private void ScheduleDailyReset() {
        DailyReset();
        // Schedule it to repeat every once a day.
        scheduler.ScheduleRepeated(DailyReset, TimeSpan.FromDays(1), strict: true);
    }

    private void DailyReset() {
        using GameStorage.Request db = gameStorage.Context();
        db.DailyReset();
        foreach ((int channelId, ChannelClient channelClient) in channelClients) {
            channelClient.GameReset(new GameResetRequest {
                Daily = new GameResetRequest.Types.Daily(),
            });
        }
    }
    #endregion

    #region Weekly Reset
    private void StartWeeklyReset() {
        using GameStorage.Request db = gameStorage.Context();
        DateTime lastReset = db.GetLastWeeklyReset();

        DateTime now = DateTime.Now;
        int daysSinceReset = ((int) now.DayOfWeek - (int) Constant.ResetDay + 7) % 7;
        DateTime lastResetMidnight = now.Date.AddDays(-daysSinceReset);

        if (lastReset < lastResetMidnight) {
            db.WeeklyReset();
        }

        DateTime nextReset = now.NextDayOfWeek(Constant.ResetDay).Date;
        TimeSpan timeUntilReset = nextReset - now;
        scheduler.Schedule(ScheduleWeeklyReset, timeUntilReset);
    }

    private void ScheduleWeeklyReset() {
        WeeklyReset();
        scheduler.ScheduleRepeated(WeeklyReset, TimeSpan.FromDays(7), strict: true);
    }

    private void WeeklyReset() {
        // The guilds held in memory first: a save of one after the database reset would write the old weekly
        // contribution back.
        guildLookup.WeeklyReset();
        using GameStorage.Request db = gameStorage.Context();
        db.WeeklyReset();
        foreach ((int channelId, ChannelClient channelClient) in channelClients) {
            channelClient.GameReset(new GameResetRequest {
                Weekly = new GameResetRequest.Types.Weekly(),
            });
        }
        // Guild 61 cleared the client's vs game ticket (see GuildPacket.WeeklyContributionReset).
        guildVsGameLookup.ResendTickets();
    }
    #endregion

    #region Monthly Reset
    private void StartMonthlyReset() {
        using GameStorage.Request db = gameStorage.Context();
        DateTime lastReset = db.GetLastMonthlyReset();

        DateTime now = DateTime.Now;
        DateTime firstOfMonth = new DateTime(now.Year, now.Month, 1);

        if (lastReset < firstOfMonth) {
            db.MonthlyReset();
        }

        DateTime nextMonth = firstOfMonth.AddMonths(1);
        TimeSpan timeUntilReset = nextMonth - now;
        scheduler.Schedule(ScheduleMonthlyReset, timeUntilReset);
    }

    private void ScheduleMonthlyReset() {
        MonthlyReset();
        DateTime now = DateTime.Now;
        DateTime nextMonth = new DateTime(now.Year, now.Month, 1).AddMonths(1);
        scheduler.Schedule(ScheduleMonthlyReset, nextMonth - now);
    }

    private void MonthlyReset() {
        using GameStorage.Request db = gameStorage.Context();
        db.MonthlyReset();
        foreach ((int channelId, ChannelClient channelClient) in channelClients) {
            channelClient.GameReset(new GameResetRequest {
                Monthly = new GameResetRequest.Types.Monthly(),
            });
        }
    }
    #endregion

    private void StartWorldEvents() {
        // Global Portal
        IReadOnlyDictionary<int, GlobalPortalMetadata> globalEvents = serverTableMetadata.TimeEventTable.GlobalPortal;
        foreach ((int eventId, GlobalPortalMetadata eventData) in globalEvents) {
            if (eventData.EndTime < DateTime.Now) {
                continue;
            }

            // There is no cycle time, so we skip it.
            if (eventData.CycleTime == TimeSpan.Zero) {
                continue;
            }
            // A row whose StartTime is still ahead waits for it (it was never scheduled).
            if (TimeEventRules.FirstRun(eventData.StartTime, eventData.CycleTime, eventData.EndTime, DateTime.Now) is not { } startTime) {
                continue;
            }
            scheduler.Schedule(() => GlobalPortal(eventData, startTime), startTime - DateTime.Now);
        }

        // Field War
        foreach ((int _, FieldWarEventMetadata eventData) in serverTableMetadata.TimeEventTable.FieldWarEvent ?? new Dictionary<int, FieldWarEventMetadata>()) {
            if (eventData.EndTime < DateTime.Now || eventData.CycleTime == TimeSpan.Zero) {
                continue;
            }
            if (TimeEventRules.FirstRun(eventData.StartTime, eventData.CycleTime, eventData.EndTime, DateTime.Now) is not { } startTime) {
                continue;
            }
            scheduler.Schedule(() => FieldWar(eventData, startTime), startTime - DateTime.Now);
        }
    }

    // A run of a field war event: one war of its group (by pickWeight) opens for the event's lifeTime.
    private void FieldWar(FieldWarEventMetadata data, DateTime startTime) {
        IReadOnlyCollection<FieldWarMetadata> wars = (serverTableMetadata.TimeEventTable.FieldWar ?? new Dictionary<int, FieldWarMetadata>()).Values.ToList();
        int total = FieldWarRules.TotalWeight(wars, data.GroupId);
        if (total > 0 && TimeEventRules.Runs(data.Probability, Random.Shared.Next(100))
            && FieldWarRules.Pick(wars, data.GroupId, Random.Shared.Next(total)) is { } war) {
            long uid = fieldWarLookup.Open(war);
            logger.Information("Field war {FieldWarId} (map {MapId}) open for {LifeTime}", war.Id, war.MapId, data.LifeTime);
            scheduler.Schedule(() => fieldWarLookup.Close(uid), data.LifeTime);
        }

        DateTime nextRunTime = startTime + data.CycleTime;
        if (data.RandomTime > TimeSpan.Zero) {
            nextRunTime += TimeSpan.FromMilliseconds(Random.Shared.Next((int) data.RandomTime.TotalMilliseconds));
        }
        if (data.EndTime < nextRunTime) {
            return;
        }
        scheduler.Schedule(() => FieldWar(data, nextRunTime), nextRunTime - DateTime.Now);
    }

    private void GlobalPortal(GlobalPortalMetadata data, DateTime startTime) {
        // check probability
        bool run = TimeEventRules.Runs(data.Probability, Random.Shared.Next(100));

        if (run) {
            DateTime now = DateTime.Now;
            globalPortalLookup.Create(data, new DateTimeOffset(now).ToUnixTimeMilliseconds() + (long) data.LifeTime.TotalMilliseconds, out int eventId);
            if (!globalPortalLookup.TryGet(out GlobalPortalManager? manager)) {
                logger.Error("Failed to create global portal");
                return;
            }

            manager.CreateFields();

            Task.Factory.StartNew(() => {
                Thread.Sleep(data.LifeTime);
                if (globalPortalLookup.TryGet(out GlobalPortalManager? globalPortalManager) && globalPortalManager.Portal.Id == eventId) {
                    globalPortalLookup.Dispose();
                }
            });
        }

        DateTime nextRunTime = startTime + data.CycleTime;
        if (data.RandomTime > TimeSpan.Zero) {
            nextRunTime += TimeSpan.FromMilliseconds(Random.Shared.Next((int) data.RandomTime.TotalMilliseconds));
        }

        if (data.EndTime < nextRunTime) {
            return;
        }

        scheduler.Schedule(() => GlobalPortal(data, nextRunTime), nextRunTime - DateTime.Now);
    }

    private void StartWorldBossEvents() {
        int scheduled = 0;
        foreach ((int _, WorldBossMetadata boss) in serverTableMetadata.TimeEventTable.WorldBoss) {
            if (boss.EndTime < DateTime.Now) {
                continue;
            }
            if (boss.CycleTime == TimeSpan.Zero) {
                continue;
            }

            if (TimeEventRules.FirstRun(boss.StartTime, boss.CycleTime, boss.EndTime, DateTime.Now) is not { } startTime) {
                continue;
            }

            TimeSpan delay = startTime - DateTime.Now;
            scheduler.Schedule(() => SpawnWorldBoss(boss, startTime), delay);
            scheduled++;
        }
        logger.Information("WorldBoss scheduler started — {Count} bosses scheduled", scheduled);
    }

    private void SpawnWorldBoss(WorldBossMetadata metadata, DateTime spawnTime) {
        bool shouldSpawn = TimeEventRules.Runs(metadata.Probability, Random.Shared.Next(100));

        // Compute next spawn time before Create() so it can be broadcast with the announce
        DateTime nextSpawn = spawnTime + metadata.CycleTime;
        if (metadata.RandomTime > TimeSpan.Zero) {
            nextSpawn += TimeSpan.FromMilliseconds(Random.Shared.Next((int) metadata.RandomTime.TotalMilliseconds));
        }
        long nextSpawnTimestamp = nextSpawn <= metadata.EndTime ? new DateTimeOffset(nextSpawn).ToUnixTimeSeconds() : 0;

        if (!shouldSpawn) {
            logger.Debug("WorldBoss {Id} roll failed (probability {Probability}%) — skipping spawn, next at {NextSpawn:HH:mm:ss} UTC",
                metadata.Id, metadata.Probability, nextSpawn.ToUniversalTime());
        } else if (worldBossLookup.TryGet(metadata.Id, out _)) {
            logger.Debug("WorldBoss {Id} still active from previous cycle — skipping spawn", metadata.Id);
        } else {
            long spawnTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long endTick = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (long) metadata.LifeTime.TotalMilliseconds;
            if (worldBossLookup.Create(metadata, endTick, nextSpawnTimestamp, out int eventId)) {
                if (worldBossLookup.TryGet(metadata.Id, out WorldBossManager? manager)) {
                    manager.Boss.SpawnTimestamp = spawnTimestamp;
                    manager.Announce();
                }
                logger.Information("WorldBoss {Id} (NpcId: {NpcId}) spawned — eventId: {EventId}, alive for {LifeTime:mm\\:ss}, next spawn at {NextSpawn:HH:mm:ss} UTC",
                    metadata.Id, metadata.NpcIds.Length > 0 ? metadata.NpcIds[0] : 0, eventId, metadata.LifeTime, nextSpawn.ToUniversalTime());

                _ = MonitorWorldBossLifetimeAsync(metadata.Id, metadata.LifeTime, tokenSource.Token);
            }
        }

        if (nextSpawn > metadata.EndTime) {
            logger.Information("WorldBoss {Id} will not respawn — next spawn {NextSpawn} is past EndTime {EndTime}", metadata.Id, nextSpawn, metadata.EndTime);
            return;
        }

        scheduler.Schedule(() => SpawnWorldBoss(metadata, nextSpawn), nextSpawn - DateTime.Now);
    }

    // The warning when the LifeTime has passed, the boss leaves TimeEventRules.BossLeaveDelay later.
    private async Task MonitorWorldBossLifetimeAsync(int metadataId, TimeSpan lifeTime, CancellationToken token) {
        try {
            await Task.Delay(lifeTime, token);
            if (worldBossLookup.TryGet(metadataId, out WorldBossManager? warnManager)) {
                warnManager.WarnChannels();
            }
            await Task.Delay(TimeEventRules.BossLeaveDelay, token);
            worldBossLookup.Dispose(metadataId);
            logger.Information("WorldBoss {Id} lifetime expired — despawning", metadataId);
        } catch (OperationCanceledException) {
            // Server shutting down — do not despawn or log as error
        } catch (Exception ex) {
            logger.Error(ex, "Error monitoring field boss lifetime for metadata {MetadataId}", metadataId);
        }
    }

    // The events whose user values last until their end (GameEventManager.FollowEventEnd): one that is live again
    // after the end kept for it starts a new run without the old values and stamp quests.
    private void BeginEventRuns(IEnumerable<GameEvent> events) {
        long now = DateTimeOffset.Now.ToUnixTimeSeconds();
        events = events.ToList();
        using GameStorage.Request db = gameStorage.Context();
        foreach (GameEvent gameEvent in events) {
            if (gameEvent.Metadata.Type is not (GameEventType.StampEvent or GameEventType.LongTermAttendGift or GameEventType.CollectItemGroup)) {
                continue;
            }
            int[] quests = [];
            if (gameEvent.Metadata.Data is StampEvent stampEvent) {
                IEnumerable<StampEvent> otherCards = events.Where(other => other.Id != gameEvent.Id).Select(other => other.Metadata.Data).OfType<StampEvent>();
                quests = StampEventRules.RunQuests(stampEvent, otherCards,
                    questId => questMetadata.TryGet(questId, out QuestMetadata? quest) && quest.EventMissionType == QuestEventMissionType.stamp);
            }
            try {
                if (db.BeginEventRun(gameEvent.Id, gameEvent.EndTime, now, quests)) {
                    logger.Information("Game event {Id} runs again: its user values and stamp quests were dropped", gameEvent.Id);
                }
            } catch (Exception ex) {
                logger.Error(ex, "Failed to begin the run of game event {Id}", gameEvent.Id);
            }
        }
    }

    private void ScheduleGameEvents() {
        IEnumerable<GameEvent> events = serverTableMetadata.GetGameEvents().ToList();
        BeginEventRuns(events);
        // Add Events
        // Get only events that havent been started. Started events already get loaded on game/login servers on start up
        foreach (GameEvent data in events.Where(gameEvent => gameEvent.StartTime > DateTimeOffset.Now.ToUnixTimeSeconds())) {
            scheduler.Schedule(() => AddGameEvent(data.Id), TimeSpan.FromSeconds(data.StartTime - DateTimeOffset.Now.ToUnixTimeSeconds()));
        }

        // Remove Events
        foreach (GameEvent data in events.Where(gameEvent => gameEvent.EndTime > DateTimeOffset.Now.ToUnixTimeSeconds())) {
            scheduler.Schedule(() => RemoveGameEvent(data.Id), TimeSpan.FromSeconds(data.EndTime - DateTimeOffset.Now.ToUnixTimeSeconds()));
        }
    }

    private static readonly TimeSpan PlotRetryDelay = TimeSpan.FromMinutes(1);

    public void FieldPlotExpiryCheck() {
        using GameStorage.Request db = gameStorage.Context();
        // Get all plots that have expired but are not yet pending
        List<PlotInfo> expiredPlots = db.GetPlotsToExpire();
        bool retry = false;
        if (expiredPlots.Count > 0) {
            foreach (PlotInfo plot in expiredPlots) {
                bool forfeit = false;
                try {
                    if (plot.OwnerId > 0 && plot.ExpiryTime < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) {
                        if (!SetPlotAsPending(db, plot)) {
                            retry = true;
                            continue; // Nothing was written: the channels keep the plot as it is
                        }
                        forfeit = true;
                        // mark as open when 3 days has passed since the expiry time
                    } else if (plot.OwnerId == 0 && plot.ExpiryTime + Constants.UgcHomeSaleWaitingTime.TotalSeconds < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) {
                        logger.Information("Marking plot {PlotId} as open (no owner)", plot.Id);
                        db.SetPlotOpen(plot.Id); // Mark as open
                    } else {
                        continue; // Still valid, skip
                    }
                } catch (Exception e) {
                    logger.Error(e, "Error processing plot {PlotId} for expiry check", plot.Id);
                    continue; // Skip this plot if there's an error
                }

                // Notify channels about the expired plots
                foreach ((int _, ChannelClient channelClient) in channelClients) {
                    logger.Information("Notifying channel about expired plot {PlotId}", plot.Id);
                    channelClient.UpdateFieldPlot(new FieldPlotRequest {
                        MapId = plot.MapId,
                        PlotNumber = plot.Number,
                        UpdatePlot = new FieldPlotRequest.Types.UpdatePlot() {
                            AccountId = plot.OwnerId,
                            Forfeit = forfeit,
                        },
                    });
                }
            }
        }

        // Schedule next check for the next soonest expiry
        PlotInfo? nextPlot = db.GetSoonestPlotFromExpire(Constants.UgcHomeSaleWaitingTime);
        TimeSpan delay;
        if (nextPlot is not null) {
            DateTimeOffset nextExpiry = DateTimeOffset.FromUnixTimeSeconds(nextPlot.ExpiryTime);
            delay = nextExpiry - DateTimeOffset.UtcNow;
            if (delay < TimeSpan.Zero) {
                delay = TimeSpan.Zero;
            }
        } else {
            delay = TimeSpan.FromDays(1); // Default to 1 day if no plots are found
        }
        if (retry && delay < PlotRetryDelay) {
            delay = PlotRetryDelay; // A plot not taken back is still the soonest: do not check again at once
        }
        scheduler.Schedule(FieldPlotExpiryCheck, delay);
    }

    // Marks a plot as pending, removes its cubes, and adds them to the owner's furnishing storage, all in one write
    // (GameStorage.Request.ExpirePlot). False when nothing was written: the plot was extended or given up since it was
    // listed, a cube went meanwhile, or the write failed; a later check takes it then.
    private bool SetPlotAsPending(GameStorage.Request db, PlotInfo plot) {
        logger.Information("Marking plot {PlotId} as pending (owner: {OwnerId})", plot.Id, plot.OwnerId);
        if (db.ExpirePlot(plot, itemId => CreateItem(itemId))) {
            return true;
        }
        logger.Warning("Plot {PlotId} of owner {OwnerId} was not taken back", plot.Id, plot.OwnerId);
        return false;

        Item? CreateItem(int itemId, int rarity = -1, int amount = 1) {
            if (!itemMetadata.TryGet(itemId, out ItemMetadata? metadata) || !PlayedClientItems.CanCreate(metadata)) {
                return null;
            }

            if (rarity <= 0) {
                if (metadata.Option != null && metadata.Option.ConstantId is < 6 and > 0) {
                    rarity = metadata.Option.ConstantId;
                } else {
                    rarity = 1;
                }
            }

            return new Item(metadata, rarity, amount);
        }
    }

    private void AddGameEvent(int eventId) {
        foreach ((int channelId, ChannelClient channelClient) in channelClients) {
            channelClient.GameEvent(new GameEventRequest {
                Add = new GameEventRequest.Types.Add {
                    EventId = eventId,
                },
            });
        }
    }

    private void RemoveGameEvent(int eventId) {
        foreach ((int channelId, ChannelClient channelClient) in channelClients) {
            channelClient.GameEvent(new GameEventRequest {
                Remove = new GameEventRequest.Types.Remove {
                    EventId = eventId,
                },
            });
        }
    }

    private static int NextGlobalId() => Interlocked.Increment(ref _globalIdCounter);

    public int AddCustomStringBoard(string message) {
        if (string.IsNullOrEmpty(message)) {
            return -1;
        }

        int id = NextGlobalId();
        memoryStringBoards.TryAdd(id, message);
        return id;
    }

    public bool RemoveCustomStringBoard(int id) {
        return memoryStringBoards.TryRemove(id, out _);
    }

    public IReadOnlyDictionary<int, string> GetCustomStringBoards() {
        return memoryStringBoards;
    }
}
