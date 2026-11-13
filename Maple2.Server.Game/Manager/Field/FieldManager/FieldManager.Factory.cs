using System.Collections.Concurrent;
using System.Diagnostics;
using Autofac;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;
using Serilog;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Game.Manager.Field;

public partial class FieldManager {
    public sealed class Factory : IDisposable {
        #region Autofac Autowired
        // ReSharper disable MemberCanBePrivate.Global
        public GameStorage GameStorage { get; init; } = null!;
        public required WorldClient World { get; init; }
        public required MapMetadataStorage MapMetadata { private get; init; }
        public required MapEntityStorage MapEntities { private get; init; }
        public required MapDataStorage MapData { private get; init; }
        public required ServerTableMetadataStorage ServerTableMetadata { private get; init; }
        public TableMetadataStorage? TableMetadata { private get; init; }
        public required NpcMetadataStorage NpcMetadata { get; init; } = null!;
        // ReSharper restore All
        #endregion

        private readonly ILogger logger = Log.Logger.ForContext<Factory>();

        private readonly IComponentContext context;
        //private readonly Fields fields;

        private readonly CancellationTokenSource cancel;
        private readonly Thread thread;

        private readonly ConcurrentDictionary<int, SemaphoreSlim> mapLocks;

        private readonly ConcurrentDictionary<int, ConcurrentDictionary<int, FieldManager>> fields = []; // Key MapId, RoomId
        private readonly ConcurrentDictionary<long, HomeFieldManager> homes = []; // Key: OwnerId
        private readonly ConcurrentDictionary<(int MapId, long OwnerId), int> ownerRooms = []; // Value: RoomId
        private readonly ConcurrentDictionary<int, DungeonFieldManager> dungeons = []; // Key: RoomId
        private readonly ConcurrentDictionary<int, DungeonFieldManager> subDungeonFields = []; // Key: RoomId. This is for rooms created by the dungeon lobby.

        public Factory(IComponentContext context) {
            this.context = context;
            mapLocks = new ConcurrentDictionary<int, SemaphoreSlim>();

            cancel = new CancellationTokenSource();
            thread = new Thread(DisposeLoop);
            thread.Start();
        }

        /// <summary>
        /// Get player home map field or any player owned map. If not found, create a new field.
        /// </summary>
        public FieldManager? Get(int mapId = 0, long ownerId = 0, int roomId = 0) {
            SemaphoreSlim mapLock = GetMapLock(mapId);
            mapLock.Wait();
            FieldManager? field;
            try {
                field = GetInternal(mapId, ownerId, roomId);
            } finally {
                mapLock.Release();
            }
            return field;
        }

        /// <summary>
        /// The running home field of this owner on this channel, if there is one. Never creates a field.
        /// </summary>
        public HomeFieldManager? FindHome(long ownerId) {
            return homes.TryGetValue(ownerId, out HomeFieldManager? home) && !home.Disposed ? home : null;
        }

        // The owners of the homes loaded on this channel.
        public List<long> HomeOwners() {
            return homes.Values.Where(home => !home.Disposed).Select(home => home.OwnerId).ToList();
        }

        /// <summary>
        /// Create a new Field instance for the given mapId.
        /// If ownerId is provided, it will be a house field. (Player or Guild house)
        /// If no ownerId is provided, it belongs to the "server".
        /// roomId can be used to create a new instance of the map.
        /// </summary>
        public FieldManager? Create(int mapId, long ownerId = 0, int roomId = 0, InstanceFieldMetadata? instanceFieldMetadata = null) {
            var sw = new Stopwatch();
            sw.Start();
            if (!MapMetadata.TryGet(mapId, out MapMetadata? metadata)) {
                logger.Error("Loading invalid Map:{MapId}", mapId);
                return null;
            }

            // Without the navmesh file the field's constructor throws in the middle of a move. Refuse here so
            // the caller answers the move with its error and the player stays where they are.
            if (!Navigation.Exists(metadata.XBlock)) {
                logger.Error("Map:{MapId} has no navmesh file ({XBlock}), field not created", mapId, metadata.XBlock);
                return null;
            }

            if (!MapMetadata.TryGetUgc(mapId, out UgcMapMetadata? ugcMetadata)) {
                ugcMetadata = new UgcMapMetadata(mapId, new Dictionary<int, UgcMapGroup>());
            }

            MapEntityMetadata? entities = MapEntities.Get(metadata.XBlock);
            if (entities == null) {
                throw new InvalidOperationException($"Failed to load entities for map: {mapId}");
            }

            switch (instanceFieldMetadata?.Type) {
                case InstanceType.ugcMap:
                    HomeFieldManager homeField = CreateHome(ownerId, metadata, ugcMetadata, entities, NpcMetadata, roomId);
                    logger.Debug("Home Field:{MapId} OwnerId:{OwnerId} Room:{RoomId} initialized in {Time}ms", mapId, ownerId, roomId, sw.ElapsedMilliseconds);

                    if (roomId < 0) { // Decor Planner or Blueprint Designer
                        AddField(homeField);
                    } else {
                        homes.TryAdd(ownerId, homeField);
                    }
                    return homeField;
                    /*case InstanceType.DungeonLobby:
                        DungeonFieldManager? dungeonField = CreateDungeon(ServerTableMetadata.InstanceFieldTable.DungeonRooms[roomId], ownerId);
                        logger.Debug("Dungeon Field:{MapId} OwnerId:{OwnerId} Room:{RoomId} initialized in {Time}ms", mapId, ownerId, roomId, sw.ElapsedMilliseconds);
                        return dungeonField;*/
            }

            var field = new FieldManager(metadata, ugcMetadata, entities, NpcMetadata);
            context.InjectProperties(field);
            field.Init();

            // Add to fields
            AddField(field);

            logger.Debug("Field:{MapId} OwnerId:{OwnerId} Room:{RoomId} initialized in {Time}ms", mapId, ownerId, field.RoomId, sw.ElapsedMilliseconds);
            return field;

            void AddField(FieldManager field) {
                fields.AddOrUpdate(
                    mapId,
                    _ => new ConcurrentDictionary<int, FieldManager>([new KeyValuePair<int, FieldManager>(field.RoomId, field)]),
                    (_, existingOwnerFields) => {
                        existingOwnerFields.AddOrUpdate(field.RoomId, field, (_, __) => field);
                        return existingOwnerFields;
                    });
            }
        }

        private HomeFieldManager CreateHome(long ownerId, MapMetadata mapMetadata, UgcMapMetadata ugcMetadata, MapEntityMetadata entities, NpcMetadataStorage npcMetadata, int roomId) {
            using GameStorage.Request db = GameStorage.Context();
            Home? home = db.GetHome(ownerId);
            if (home == null) {
                logger.Error("Loading invalid Home:{OwnerId}", ownerId);
                home = new Home();
            }

            var field = new HomeFieldManager(home, mapMetadata, ugcMetadata, entities, npcMetadata, roomId);
            context.InjectProperties(field);
            field.Init();

            return field;
        }

        public DungeonFieldManager? CreateDungeon(DungeonRoomMetadata dungeonMetadata, long ownerId, int size = 1, int partyId = 0,
                                                  bool randomMatch = false) {
            var sw = new Stopwatch();
            sw.Start();

            if (!MapMetadata.TryGet(dungeonMetadata.LobbyFieldId, out MapMetadata? mapMetadata)) {
                logger.Error("Loading invalid Lobby Field Map:{MapId}", dungeonMetadata.LobbyFieldId);
                return null;
            }

            if (!MapMetadata.TryGetUgc(dungeonMetadata.LobbyFieldId, out UgcMapMetadata? ugcMetadata)) {
                ugcMetadata = new UgcMapMetadata(dungeonMetadata.LobbyFieldId, new Dictionary<int, UgcMapGroup>());
            }

            // Every field of the dungeon needs its map data, navmesh file and map entities. Check them all before any
            // field is made, so a missing one refuses the whole dungeon instead of throwing after the lobby exists
            // or making the lobby without that field.
            (int unmadeMapId, FieldEntryRules.DungeonFieldProblem problem) = FieldEntryRules.UnmadeDungeonField(
                dungeonMetadata.LobbyFieldId, dungeonMetadata.FieldIds,
                fieldId => MapMetadata.TryGet(fieldId, out MapMetadata? checkMetadata) ? checkMetadata.XBlock : null,
                Navigation.Exists, xblock => MapEntities.Get(xblock) != null);
            if (problem != FieldEntryRules.DungeonFieldProblem.None) {
                logger.Error("Dungeon:{DungeonId} Map:{MapId} cannot be made ({Problem}), dungeon not created", dungeonMetadata.Id, unmadeMapId, problem);
                return null;
            }

            MapEntityMetadata? entities = MapEntities.Get(mapMetadata.XBlock);
            if (entities == null) {
                throw new InvalidOperationException($"Failed to load entities for map: {dungeonMetadata.LobbyFieldId}");
            }

            var dungeonRoomRecord = new DungeonRoomRecord(dungeonMetadata) {
                RandomMatch = randomMatch,
            };
            var lobbyField = new DungeonFieldManager(dungeonMetadata, mapMetadata, ugcMetadata, entities, NpcMetadata, ownerId, size, partyId) {
                DungeonRoomRecord = dungeonRoomRecord,
                FieldType = FieldType.Default,
            };
            context.InjectProperties(lobbyField);
            lobbyField.Init();

            foreach (int fieldId in dungeonMetadata.FieldIds) {
                if (!MapMetadata.TryGet(fieldId, out mapMetadata)) {
                    logger.Error("Loading invalid Dungeon Field Map:{MapId}", fieldId);
                    continue;
                }

                if (!MapMetadata.TryGetUgc(fieldId, out ugcMetadata)) {
                    ugcMetadata = new UgcMapMetadata(fieldId, new Dictionary<int, UgcMapGroup>());
                }

                entities = MapEntities.Get(mapMetadata.XBlock);
                if (entities == null) {
                    throw new InvalidOperationException($"Failed to load entities for map: {fieldId}");
                }
                var field = new DungeonFieldManager(dungeonMetadata, mapMetadata, ugcMetadata, entities, NpcMetadata, ownerId, size, partyId) {
                    Lobby = lobbyField,
                    DungeonRoomRecord = dungeonRoomRecord,
                    FieldType = FieldType.Dungeon,
                };
                context.InjectProperties(field);
                field.Init();

                lobbyField.RoomFields.TryAdd(fieldId, field);
                subDungeonFields.TryAdd(field.RoomId, field);
            }

            dungeons.TryAdd(lobbyField.RoomId, lobbyField);
            logger.Debug("Dungeon Lobby Field:{MapId} OwnerId:{OwnerId} Room:{RoomId} initialized in {Time}ms", dungeonMetadata.LobbyFieldId, ownerId, lobbyField.RoomId, sw.ElapsedMilliseconds);

            return lobbyField;
        }

        /// <summary>
        /// Whether the dungeon room still runs. Never creates a field.
        /// </summary>
        /// <summary>
        /// A field for a rank duel match, on one of the rank duel maps (RankDuelField.Maps) the server can load. The
        /// map is picked at random. The field tells the world when the match ended.
        /// </summary>
        public FieldManager? CreateRankDuel(int matchId, long first, long second) {
            int[] maps = RankDuelField.Maps
                .Where(mapId => MapMetadata.TryGet(mapId, out MapMetadata? map) && !map.Property.NotInPlayedClient && Navigation.Exists(map.XBlock))
                .ToArray();
            if (maps.Length == 0) {
                logger.Error("No rank duel map can be loaded");
                return null;
            }

            int mapId = maps[Random.Shared.Next(maps.Length)];
            SemaphoreSlim mapLock = GetMapLock(mapId);
            mapLock.Wait();
            try {
                FieldManager? field = Create(mapId);
                field?.StartRankDuel(matchId, first, second, id => World.Pvp(new Maple2.Server.World.Service.PvpRequest {
                    End = new Maple2.Server.World.Service.PvpRequest.Types.End { MatchId = id },
                }, deadline: DateTime.UtcNow.AddSeconds(10)), id => World.Pvp(new Maple2.Server.World.Service.PvpRequest {
                    Observable = new Maple2.Server.World.Service.PvpRequest.Types.Observable { MatchId = id },
                }, deadline: DateTime.UtcNow.AddSeconds(10)));
                return field;
            } finally {
                mapLock.Release();
            }
        }

        // Whether the PvP match field of this room (a rank duel that still takes its duelists, an individual arena, or
        // a guild vs game match whose game has not ended) still runs on this channel. Never creates a field.
        public bool HasPvpMatch(int roomId) {
            return fields.Values.Any(mapFields => mapFields.TryGetValue(roomId, out FieldManager? field) && !field.Disposed
                                                  && (field.RankDuel is { Runs: true } || field.Arena != null
                                                      || field.GuildVsGame is { MatchId: > 0, State.Ended: false }));
        }

        // A field for a guild vs game match (World GuildVsGameLookup): a new room of the guild vs game map (the
        // instance field of type GuildVsGame, 66200001), its teams the two guilds. The field tells the World when the
        // game ended.
        public FieldManager? CreateGuildVsGame(int matchId, long blueGuildId, long redGuildId) {
            int mapId = ServerTableMetadata.InstanceFieldTable.Entries.Values
                .Where(entry => entry.Type == InstanceType.GuildVsGame).Select(entry => entry.MapId).Order().FirstOrDefault();
            if (mapId == 0) {
                logger.Error("No guild vs game map in the instance field table");
                return null;
            }
            SemaphoreSlim mapLock = GetMapLock(mapId);
            mapLock.Wait();
            try {
                FieldManager? field = Create(mapId);
                field?.StartGuildVsGame().SetMatch(matchId, blueGuildId, redGuildId, id => Task.Run(() => {
                    try {
                        World.Guild(new Maple2.Server.World.Service.GuildRequest {
                            VsGameEnd = new Maple2.Server.World.Service.GuildRequest.Types.VsGameEnd { MatchId = id },
                        }, deadline: DateTime.UtcNow.AddSeconds(10));
                    } catch (Exception ex) {
                        logger.Warning(ex, "Guild vs game match {MatchId}: the end was not sent", id);
                    }
                }));
                return field;
            } finally {
                mapLock.Release();
            }
        }

        /// <summary>
        /// A field for an individual arena match: a new room of the arena's fieldID (individualarena.xml). The map's
        /// own script runs the round; the field tells the world when its PvP zone ended.
        /// </summary>
        public FieldManager? CreateArena(int matchId, int arenaType, IReadOnlyList<long> characters, IReadOnlyDictionary<long, long>? units = null) {
            IndividualArenaTable.Entry? arena = arenaType == MapleSurvivalRules.Arena ? MapleSurvivalRules.Entry : null;
            if (arena == null && (TableMetadata == null || !TableMetadata.IndividualArenaTable.Entries.TryGetValue(arenaType, out arena))) {
                logger.Error("Arena {Arena} is not in the arena table", arenaType);
                return null;
            }
            SemaphoreSlim mapLock = GetMapLock(arena.FieldId);
            mapLock.Wait();
            try {
                FieldManager? field = Create(arena.FieldId);
                field?.StartArena(matchId, arena, characters, id => World.Pvp(new Maple2.Server.World.Service.PvpRequest {
                    End = new Maple2.Server.World.Service.PvpRequest.Types.End { MatchId = id, Arena = arenaType },
                }, deadline: DateTime.UtcNow.AddSeconds(10)));
                if (field?.Arena != null && units != null) {
                    field.Arena.Units = units;
                }
                return field;
            } finally {
                mapLock.Release();
            }
        }

        public bool HasDungeon(int roomId) {
            return dungeons.TryGetValue(roomId, out DungeonFieldManager? dungeon) && !dungeon.Disposed;
        }

        /// <summary>
        /// The lobby or room field of mapId in a running room the character entered (FieldEntryRules.RejoinedField).
        /// Never creates a field.
        /// </summary>
        public DungeonFieldManager? FindDungeonField(long characterId, int mapId) {
            return FieldEntryRules.RejoinedField(dungeons.Values.Where(lobby => !lobby.Disposed)
                .Select(lobby => new FieldEntryRules.RunningRoom<DungeonFieldManager>(lobby.RoomId,
                    lobby.DungeonRoomRecord.UserResults.ContainsKey(characterId), lobby.MapId, lobby, lobby.RoomFields)), mapId);
        }

        public MigrationError DestroyDungeon(int roomId) {
            if (!dungeons.TryGetValue(roomId, out DungeonFieldManager? dungeon)) {
                return MigrationError.s_move_err_dungeon_not_exist;
            }

            return DisposeDungeon(dungeon);
        }

        /// <summary>
        /// Disposes the field managers that have no players and have been empty for more than 10 minutes.
        /// </summary>
        private void DisposeLoop() {
            while (!cancel.IsCancellationRequested) {
                foreach ((int mapId, ConcurrentDictionary<int, FieldManager> fieldList) in fields) {
                    foreach ((int roomId, FieldManager field) in fieldList) {
                        if (!field.Players.IsEmpty) {
                            logger.Verbose("Field {MapId} {RoomId} has players", field.MapId, field.RoomId);
                            field.fieldEmptySince = null;
                            continue;
                        }

                        if (field.fieldAdBalloons.Values
                            .Any(fieldInteract => fieldInteract.Object is InteractBillBoardObject billboard && DateTime.Now.ToEpochSeconds() < billboard.ExpirationTime)) {
                            logger.Verbose("Field {MapId} {RoomId} has ad balloons", field.MapId, field.RoomId);
                            field.fieldEmptySince = null;
                            continue;
                        }

                        Model.RoomTimer? roomTimer = field.RoomTimer;
                        // A timer that does not close the room (a dungeon without isExpireTimeOut, or a stopped
                        // time) leaves the room to the empty-field rule.
                        if (roomTimer is null || !roomTimer.ExpiresRoom || roomTimer.Clock.Stopped) {
                            if (field.fieldEmptySince is null) {
                                logger.Verbose("Field {MapId} {RoomId} is empty, starting timer", field.MapId, field.RoomId);
                                field.fieldEmptySince = DateTime.UtcNow;
                            } else if (DateTime.UtcNow - field.fieldEmptySince > Constant.FieldDisposeEmptyTime) {
                                logger.Debug("Field {MapId} {RoomId} has been empty for more than {Time}, disposing", field.MapId, field.RoomId, Constant.FieldDisposeEmptyTime);
                                field.Dispose();
                            }
                            continue;
                        }

                        if (roomTimer.Expired(field.FieldTick)) {
                            logger.Debug("Field {MapId} {RoomId} room timer expired, disposing", field.MapId, field.RoomId);
                            field.Dispose();
                        } else {
                            logger.Verbose("Field {MapId} {RoomId} room timer has not expired", field.MapId, field.RoomId);
                        }
                    }
                }

                // remove fields disposed
                foreach (int mapId in fields.Keys) {
                    foreach ((int roomId, FieldManager fieldManager) in fields[mapId]) {
                        if (fieldManager.Disposed) {
                            bool success = fields[mapId].TryRemove(roomId, out _);
                            foreach (((int MapId, long OwnerId) key, int ownerRoomId) in ownerRooms) {
                                if (key.MapId == mapId && ownerRoomId == roomId) {
                                    // Only the pair seen: GetOwnerRoom may have stored a new room for the key since.
                                    ownerRooms.TryRemove(new KeyValuePair<(int MapId, long OwnerId), int>(key, ownerRoomId));
                                }
                            }
                            logger.Debug("Field {MapId}, RoomId {RoomId}, removed: {Success}", mapId, roomId, success);
                        }
                    }
                }

                foreach (HomeFieldManager homeField in homes.Values) {
                    if (!homeField.Players.IsEmpty) {
                        logger.Verbose("Home Field {MapId} Owner: {OwnerId} has players", homeField.MapId, homeField.OwnerId);
                        homeField.fieldEmptySince = null;
                        continue;
                    }

                    if (homeField.fieldEmptySince is null) {
                        logger.Verbose("Home Field {MapId} Owner: {OwnerId} is empty, starting timer", homeField.MapId, homeField.OwnerId);
                        homeField.fieldEmptySince = DateTime.UtcNow;
                    } else if (DateTime.UtcNow - homeField.fieldEmptySince > Constant.FieldDisposeEmptyTime) {
                        logger.Debug("Field {MapId} {OwnerId} has been empty for more than {Time}, disposing", homeField.MapId, homeField.OwnerId, Constant.FieldDisposeEmptyTime);
                        homeField.Dispose();
                    }
                }

                foreach (long ownerId in homes.Keys) {
                    if (homes[ownerId].Disposed) {
                        bool success = homes.TryRemove(ownerId, out _);
                        logger.Debug("Home {OwnerId} removed: {Success}", ownerId, success);
                    }
                }

                foreach (DungeonFieldManager dungeonField in dungeons.Values) {
                    // Calculate total players across lobby + sub fields
                    int totalPlayers = dungeonField.Players.Count + dungeonField.RoomFields.Values.Sum(r => r.Players.Count);
                    if (totalPlayers > 0) {
                        // Active dungeon, reset empty timer
                        dungeonField.fieldEmptySince = null;
                        logger.Verbose("Dungeon Lobby {LobbyMapId} Room:{RoomId} has players (Total={Total})", dungeonField.MapId, dungeonField.RoomId, totalPlayers);
                        continue;
                    }

                    // Empty dungeon lobby (and its rooms)
                    if (dungeonField.fieldEmptySince is null) {
                        dungeonField.fieldEmptySince = DateTime.UtcNow;
                        logger.Verbose("Dungeon Lobby {LobbyMapId} Room:{RoomId} empty, starting grace timer {Grace}", dungeonField.MapId, dungeonField.RoomId, Constant.DungeonDisposeEmptyTime);
                        continue;
                    }

                    TimeSpan emptyFor = DateTime.UtcNow - dungeonField.fieldEmptySince.Value;
                    if (emptyFor < Constant.DungeonDisposeEmptyTime) {
                        logger.Verbose("Dungeon Lobby {LobbyMapId} Room:{RoomId} empty for {Elapsed}/{Grace}, waiting", dungeonField.MapId, dungeonField.RoomId, emptyFor, Constant.DungeonDisposeEmptyTime);
                        continue;
                    }

                    // Grace period passed -> dispose
                    MigrationError result = DisposeDungeon(dungeonField);
                    logger.Debug("Dungeon Grace Dispose attempt LobbyMap:{LobbyMapId} Room:{RoomId} Result:{Result}", dungeonField.MapId, dungeonField.RoomId, result);
                }

                logger.Verbose("Field dispose loop sleeping for {Interval}ms", Constant.FieldDisposeLoopInterval);
                try {
                    Task.Delay(Constant.FieldDisposeLoopInterval, cancel.Token).Wait(cancel.Token);
                } catch {
                    /* Do nothing */
                }
            }
        }

        private MigrationError DisposeDungeon(DungeonFieldManager dungeon) {
            int playerCount = dungeon.Players.Values.Count + dungeon.RoomFields.Values.Sum(x => x.Players.Values.Count);
            if (playerCount > 0) {
                return MigrationError.s_move_err_InsideDungeonUser;
            }

            dungeon.Dispose();

            List<int> roomIds = dungeon.RoomFields.Values.Select(x => x.RoomId).ToList();
            foreach (DungeonFieldManager roomField in dungeon.RoomFields.Values) {
                roomField.Dispose();
                subDungeonFields.TryRemove(roomField.RoomId, out _);
            }
            dungeons.TryRemove(dungeon.RoomId, out _);
            logger.Debug("Dungeon disposed room Ids: {lobbyRoomId}, {roomIds}", dungeon.RoomId, string.Join(",", roomIds));
            return MigrationError.ok;
        }

        public void Dispose() {
            foreach (ConcurrentDictionary<int, FieldManager> fields in fields.Values) {
                foreach (FieldManager field in fields.Values) {
                    field.Dispose();
                }
            }

            foreach (DungeonFieldManager dungeon in dungeons.Values) {
                dungeon.Dispose();
            }

            foreach (DungeonFieldManager subDungeon in subDungeonFields.Values) {
                subDungeon.Dispose();
            }

            foreach (HomeFieldManager home in homes.Values) {
                home.Dispose();
            }

            foreach (KeyValuePair<int, SemaphoreSlim> locks in mapLocks) {
                locks.Value.Dispose();
            }

            fields.Clear();
            homes.Clear();
            dungeons.Clear();
            subDungeonFields.Clear();
            cancel.Cancel();
            thread.Join();
        }

        private SemaphoreSlim GetMapLock(int mapId) {
            return mapLocks.GetOrAdd(mapId, _ => new SemaphoreSlim(1, 1));
        }

        private FieldManager? GetInternal(int mapId, long ownerId = 0, int roomId = 0) {
            if (roomId != 0) { // Specified room
                FieldManager? foundField = FindRoom();
                if (foundField != null) {
                    return foundField;
                }
                // A rank duel room that is gone (disposed since the arrival checked it) is not made again as a
                // plain field: the arrival fails and takes its fallback field.
                if (!FieldEntryRules.MakesMissingRoom(mapId, PvpMatchMaps.All(TableMetadata))) {
                    return null;
                }
            }

            if (ServerTableMetadata.InstanceFieldTable.Entries.TryGetValue(mapId, out InstanceFieldMetadata? metadata)) {
                switch (metadata.Type) {
                    case InstanceType.ugcMap: // this is both homes and UGD. Currently just doing homes.
                        if (roomId != 0) { // User wants to go into decorplanner or blueprint designer
                            return Create(mapId, ownerId: ownerId, roomId: roomId, instanceFieldMetadata: metadata);
                        }
                        if (!homes.TryGetValue(ownerId, out HomeFieldManager? homeField)) {
                            return Create(mapId, ownerId: ownerId, roomId: roomId, instanceFieldMetadata: metadata);
                        }
                        return homeField;
                    case InstanceType.DungeonLobby:
                        return Create(mapId, ownerId: ownerId, roomId: roomId); // TODO: this should never happen.
                    case InstanceType.channelScale:
                        if (fields.TryGetValue(mapId, out ConcurrentDictionary<int, FieldManager>? scalingFields) && !scalingFields.IsEmpty) {
                            return scalingFields.Values.FirstOrDefault(f => !f.Disposed);
                        }
                        break;
                    default:
                        if (FieldEntryRules.HasOwnerRoom(metadata.Type, ownerId, roomId)) {
                            return GetOwnerRoom();
                        }
                        if (FieldEntryRules.NewFieldPerEntry(metadata.Type, ownerId, roomId)) {
                            return Create(mapId);
                        }
                        break;
                }
            }

            if (!fields.TryGetValue(mapId, out ConcurrentDictionary<int, FieldManager>? mapFields)) {
                return Create(mapId, ownerId: ownerId, roomId: roomId);
            }

            if (roomId == 0 && !mapFields.IsEmpty) {
                // A rank duel's field is entered only by its match (with its room id).
                FieldManager? firstField = mapFields.Values.FirstOrDefault(mapField => mapField.RankDuel is null && mapField.Arena is null);
                if (firstField != null) {
                    return firstField;
                }
            }

            return mapFields.TryGetValue(roomId, out FieldManager? field) ? field :
                Create(mapId, ownerId: ownerId, roomId: roomId);

            // One room of the map per owner (a guild): the owner's room while it runs, else a new one.
            // Called under the map's lock.
            FieldManager? GetOwnerRoom() {
                if (ownerRooms.TryGetValue((mapId, ownerId), out int ownerRoomId)
                    && fields.TryGetValue(mapId, out ConcurrentDictionary<int, FieldManager>? ownerFields)
                    && ownerFields.TryGetValue(ownerRoomId, out FieldManager? ownerField) && !ownerField.Disposed) {
                    return ownerField;
                }

                FieldManager? created = Create(mapId);
                if (created != null) {
                    ownerRooms[(mapId, ownerId)] = created.RoomId;
                }
                return created;
            }

            FieldManager? FindRoom() {
                if (fields.TryGetValue(mapId, out ConcurrentDictionary<int, FieldManager>? roomFields) && roomFields.TryGetValue(roomId, out FieldManager? field)) {
                    return field;
                }

                if (subDungeonFields.TryGetValue(roomId, out DungeonFieldManager? subDungeonField)) {
                    return subDungeonField;
                }

                return dungeons.TryGetValue(roomId, out DungeonFieldManager? dungeonField) ? dungeonField : null;
            }
        }
    }
}
