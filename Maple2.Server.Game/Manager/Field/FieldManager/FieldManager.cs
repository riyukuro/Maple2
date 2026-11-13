using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using DotRecast.Core;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Validators;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Field;
using Maple2.Model.Game.Ugc;
using Maple2.Model.Metadata;
using Maple2.Model.Metadata.FieldEntity;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Network;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.DebugGraphics;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Field;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Tools.DotRecast;
using Maple2.Tools.Extensions;
using Maple2.Tools.Scheduler;
using Serilog;

namespace Maple2.Server.Game.Manager.Field;

// FieldManager is instantiated by Autofac
// ReSharper disable once ClassNeverInstantiated.Global
public partial class FieldManager : IField {
    private static int _globalIdCounter = 10000000;
    private int localIdCounter = 50000000;
    private DateTime? fieldEmptySince;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public GameStorage GameStorage { get; init; } = null!;
    public ItemMetadataStorage ItemMetadata { get; init; } = null!;
    public MapMetadataStorage MapMetadata { get; init; } = null!;
    public MapDataStorage MapData { get; init; } = null!;
    public NpcMetadataStorage NpcMetadata { get; init; } = null!;
    public AiMetadataStorage AiMetadata { get; init; } = null!;
    public SkillMetadataStorage SkillMetadata { get; init; } = null!;
    public TableMetadataStorage TableMetadata { get; init; } = null!;
    public FunctionCubeMetadataStorage FunctionCubeMetadata { get; init; } = null!;
    public ServerTableMetadataStorage ServerTableMetadata { get; init; } = null!;
    public AchievementMetadataStorage AchievementMetadata { get; init; } = null!;
    public RideMetadataStorage RideMetadata { get; init; } = null!;
    public ItemStatsCalculator ItemStatsCalc { get; init; } = null!;
    public TriggerCache TriggerCache { get; init; } = null!;
    public Factory FieldFactory { get; init; } = null!;
    public IGraphicsContext DebugGraphicsContext { get; init; } = null!;
    private ConstantsTable Constants => ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    public readonly MapMetadata Metadata;
    public MapEntityMetadata Entities { get; init; }
    public Navigation Navigation { get; init; }
    public readonly PerformanceStageManager? PerformanceStage;
    public FieldAccelerationStructure? AccelerationStructure { get; private set; }
    // The map's hidden vibrate groups (shown by an AI's showVibrate, hidden again by hideVibrateAll).
    public AiVibrate Vibrate { get; private set; } = new([]);
    // Microgame 0x0114 (rock-paper-scissors): the pending invites and running games of this field.
    public Maple2.Model.Validators.RpsMatchTable Rps { get; } = new();
    // Microgame 0x0114 (couple dance): the pending invites and running dances of this field.
    public Maple2.Model.Validators.CoupleDanceMatchTable CoupleDance { get; } = new();
    private readonly UgcMapMetadata ugcMetadata;

    internal readonly EventQueue Scheduler;
    internal readonly FieldActor FieldActor;
    private readonly CancellationTokenSource cancel;
    private readonly Thread thread;
    private readonly List<(FieldPacketHandler handler, GameSession session, ByteReader reader)> queuedPackets;
    private bool initialized;

    public bool Disposed { get; private set; }

    private readonly ILogger logger = Log.Logger.ForContext<FieldManager>();

    public ItemDropManager ItemDrop { get; }

    public int MapId { get; init; }
    // The rank duel match this field was made for (Factory.CreateRankDuel); null in every other field.
    public RankDuelField? RankDuel { get; private set; }

    public void StartRankDuel(int matchId, long first, long second, Action<int> onEnd, Action<int>? onObservable = null) {
        RankDuel = new RankDuelField(this, matchId, first, second, Environment.TickCount64, onEnd, onObservable);
    }

    // The individual arena match this field was made for (Factory.CreateArena); null in every other field.
    public ArenaMatchField? Arena { get; private set; }

    public void StartArena(int matchId, IndividualArenaTable.Entry arena, IReadOnlyList<long> characters, Action<int> onEnd) {
        Arena = new ArenaMatchField(this, matchId, arena, characters, onEnd);
    }

    // The trigger's PvP zone (set_pvp_zone) while it runs and after (pvp_zone_ended); null before one is set.
    public IPvpZoneField? PvpZone { get; private set; }

    // A PvP zone of the script's type: 1 the Red Arena's face-offs, 2 the free-for-all, 3 Treasure Island's teams, 5
    // 61000035's one punch round and 6 its item round (both free-for-all zones in their own mode). False for a type the
    // server does not build.
    public bool StartPvpZone(int type, int boxId, int prepareSeconds, int matchSeconds, int additionalEffectId, IReadOnlyList<int> boxIds,
                             Func<int, IEnumerable<FieldPlayer>> playersInBox) {
        ArenaMatchField? arena = Arena;
        Action<IReadOnlyList<long>, int> onEnd = (ranking, winners) => arena?.ZoneEnded(ranking, winners);
        long tick = Environment.TickCount64;
        IPvpZoneField? zone = type switch {
            // The Red Arena's box is its stage (65000001: box 102 around portal 1), empty when the zone starts: the
            // participants wait on the entry grate (box 101). They are every player in the field, and each face-off's
            // fighters are moved onto the stage.
            1 => new RedArenaZoneField(this, boxId, prepareSeconds, matchSeconds, additionalEffectId, () => playersInBox(0), onEnd, tick,
                stage: StagePosition(boxId)),
            2 => new PvpZoneField(this, boxId, prepareSeconds, matchSeconds, additionalEffectId, () => playersInBox(boxId), onEnd, tick,
                waiting: WaitingArea(boxId)),
            5 or 6 => new PvpZoneField(this, boxId, prepareSeconds, matchSeconds, additionalEffectId, () => playersInBox(boxId), onEnd, tick,
                waiting: WaitingArea(boxId), mode: type),
            3 => new TeamZoneField(this, boxId, prepareSeconds, matchSeconds, additionalEffectId, boxIds, playersInBox, onEnd, tick),
            _ => null,
        };
        if (zone == null) {
            return false;
        }
        PvpZone = zone;
        return true;
    }

    // A zone box's stage: the map's portal inside the box (its position and rotation), else the box's center; null
    // without the box.
    private (Vector3 Position, Vector3 Rotation)? StagePosition(int boxId) {
        if (TriggerObjects?.Boxes.TryGetValue(boxId, out TriggerBox? box) != true || box == null) {
            return null;
        }
        FieldPortal? portal = fieldPortals?.Values.Where(entry => box.Contains(entry.Value.Position))
            .OrderBy(entry => entry.Value.Id).FirstOrDefault();
        return StageSpotOf(box.Metadata, portal?.Value.Position, portal?.Value.Rotation ?? Vector3.Zero);
    }

    // The stage's spot: the portal's ground position at the box's centre height (65000001's portal 1 lies under the
    // stage floor, 2875 against the floor's 3300, so its own height could put a fighter under the stage; the box's
    // centre, 3350, is just above the floor, which the client falls onto). The box's centre without a portal.
    public static (Vector3 Position, Vector3 Rotation) StageSpotOf(Ms2TriggerBox box, Vector3? portal, Vector3 rotation) {
        return portal is { } position ? (position with { Z = box.Position.Z }, rotation) : (box.Position, Vector3.Zero);
    }

    // A spot on the floor: the nearest navmesh point to the position, else the position itself (a map portal's spot,
    // which a portal move lands on anyway).
    public Vector3 StageSpot(Vector3 position) {
        return Navigation != null && FindNearestPoly(position, out long nearestRef, out RcVec3f point) && nearestRef != 0
            ? DotRecastHelper.FromNavMeshSpace(point)
            : position;
    }

    // A free-for-all zone's waiting area: the smallest trigger box but the zone's own that holds the map's player spawn
    // (65000002: box 102, the platform above the mine, while the zone's box 101 is the whole map), and the anchor of its
    // floor: the map's portal inside the zone's box outside the waiting box (65000002: portal 2 on the mine's floor).
    // Null without both.
    private (TriggerBox Box, Vector3 Floor)? WaitingArea(int zoneBoxId) {
        if (TriggerObjects?.Boxes.TryGetValue(zoneBoxId, out TriggerBox? zone) != true || zone == null || fieldPlayerSpawnPoints == null) {
            return null;
        }
        List<Vector3> spawns = fieldPlayerSpawnPoints.Values.Select(spawn => spawn.Position).ToList();
        TriggerBox? waiting = TriggerObjects.Boxes.Values
            .Where(box => box.Id != zoneBoxId && spawns.Any(spawn => box.Contains(spawn)))
            .OrderBy(box => box.Metadata.Dimensions.X * box.Metadata.Dimensions.Y * box.Metadata.Dimensions.Z)
            .FirstOrDefault();
        FieldPortal? floor = waiting == null ? null : fieldPortals?.Values
            .Where(portal => zone.Contains(portal.Value.Position) && !waiting.Contains(portal.Value.Position))
            .OrderBy(portal => portal.Value.Id).FirstOrDefault();
        return waiting != null && floor != null ? (waiting, floor.Value.Position) : null;
    }

    // A random floor spot within radius of a point (the navmesh), the point itself without one.
    public Vector3 FloorSpotAround(Vector3 point, int radius) {
        if (Navigation == null || !FindNearestPoly(point, out long nearestRef, out RcVec3f start) || nearestRef == 0) {
            return point;
        }
        float distance = radius * DotRecastHelper.MapRotation.GetRightAxis().Length(); // to navmesh space
        DtStatus status = Navigation.Crowd.GetNavMeshQuery().FindRandomPointAroundCircle(nearestRef, start, distance,
            Navigation.Crowd.GetFilter(0), new RcRand(), out _, out RcVec3f randomPoint);
        return status.Failed() ? DotRecastHelper.FromNavMeshSpace(start) : DotRecastHelper.FromNavMeshSpace(randomPoint);
    }

    // The Maple Survival game of a survival map (CreateFieldGame MapleSurvival); null before one is made.
    public SurvivalGameField? Survival { get; private set; }

    public void StartSurvival() {
        Survival = new SurvivalGameField(this, Players.Values);
    }

    // The BonusGame notice wins the bonus_game_reward trigger condition waits for.
    public BonusGameWins BonusGameWins { get; } = new();

    // The Hide and Seek game of a hide-and-seek map (CreateFieldGame HideAndSeek); null before one is made.
    public HideAndSeekField? HideAndSeek { get; private set; }

    public void StartHideAndSeek(int triggerId) {
        // A new game (the script's reset) takes back what the old one gave the players still here.
        HideAndSeek?.Close();
        HideAndSeek = new HideAndSeekField(this, triggerId);
    }

    // The guild vs game of a guild vs game map (CreateFieldGame GuildVsGame, or its first arrival); null before.
    public GuildVsGameField? GuildVsGame { get; private set; }

    public GuildVsGameField StartGuildVsGame() {
        return GuildVsGame ??= new GuildVsGameField(this);
    }

    // Whether a player's hostile skills reach another player here: the rank duel's fight, the PvP zone's or the
    // survival game's, a Hide and Seek seeker's on a hider, or the open PvP of a pk field. On a pk field the client's
    // rule (pc.cpp:3112) also vetoes any of them: no one is hostile to themselves, to a party member, or when either
    // side holds a pvpOnOff buff. Under KR that covers the survival game on the Mushtopia maps.
    public bool PlayerHostile(FieldPlayer caster, FieldPlayer target) {
        bool pk = PkField;
        if (pk && !PkFieldRules.Hostile(caster == target, caster.Session.Party.Id, target.Session.Party.Id,
                caster.Buffs.PvpOnOff, target.Buffs.PvpOnOff)) {
            return false;
        }
        return RankDuel?.Hostile(caster, target) == true || PvpZone?.Hostile(caster, target) == true || Survival?.Hostile(caster, target) == true
               || HideAndSeek?.Hostile(caster, target) == true || (pk && !FieldGameRuns);
    }

    private bool PkField => Metadata.Property?.PkMode == true;

    // A pk field's open PvP past the vetoes above (PkFieldRules: the party ids and the buff sums the buff managers
    // keep, so a hit reads one bool). Under KR the pk fields are 03000059, Mushtopia (82000000, 82000001, 82000006,
    // 82000012) and test maps. Chosen here: while a field game runs (a PvP zone, survival, rank duel or Hide and Seek)
    // only its own rule makes players hostile, so the survival game's fallen players and onlookers stay out of it;
    // the client's rule takes no field game into account.
    private bool FieldGameRuns => PvpZone != null || Survival != null || RankDuel != null || HideAndSeek != null;

    // A player killed another on a pk field outside the field's games (a PvP zone, survival, rank duel or Hide and
    // Seek count their own kills): shadow_world_kill for the killer and shadow_world_die for the victim (trophies
    // 21300001 and 21300002), and the kill log. The client shows no text of its own for it; chosen here: the field
    // gets mode 0 (killer and victim names, the kill log dialog), the killer s_shadowworld_kill_log_kill with the
    // victim's name and the victim s_shadowworld_kill_log_die with the killer's name (mode 1).
    public bool PkKill(FieldPlayer killer, FieldPlayer victim) {
        if (!PkField || killer == victim || FieldGameRuns) {
            return false;
        }
        string killerName = killer.Value.Character.Name;
        string victimName = victim.Value.Character.Name;
        killer.Session.ConditionUpdate(ConditionType.shadow_world_kill);
        victim.Session.ConditionUpdate(ConditionType.shadow_world_die);
        Broadcast(PlayerKillNoticePacket.Kill(killerName, victimName));
        killer.Session.Send(PlayerKillNoticePacket.Notice(new InterfaceText(StringCode.s_shadowworld_kill_log_kill, victimName)));
        victim.Session.Send(PlayerKillNoticePacket.Notice(new InterfaceText(StringCode.s_shadowworld_kill_log_die, killerName)));
        return true;
    }
    public int RoomId { get; init; }
    public int DungeonId { get; init; }
    public int Size { get; init; }
    public FieldType FieldType { get; init; }
    public InstanceFieldMetadata FieldInstance { get; private set; }
    public readonly AiManager Ai;
    public IFieldRenderer? DebugRenderer { get; private set; }

    public FieldManager(MapMetadata metadata, UgcMapMetadata ugcMetadata, MapEntityMetadata entities, NpcMetadataStorage npcMetadata, long ownerId = 0) {
        Metadata = metadata;
        MapId = metadata.Id;
        this.ugcMetadata = ugcMetadata;
        Entities = entities;
        TriggerObjects = new TriggerCollection(entities);

        Scheduler = new EventQueue(logger);
        FieldActor = new FieldActor(this, NextLocalId(), metadata, npcMetadata); // pulls from argument because member NpcMetadata is null here
        cancel = new CancellationTokenSource();
        thread = new Thread(UpdateLoop);
        queuedPackets = new();
        Ai = new AiManager(this);
        RoomId = NextGlobalId();

        FieldInstance = new InstanceFieldMetadata(0, InstanceType.none, 0, false, 0, true, 0, 0, 0, 0);

        ItemDrop = new ItemDropManager(this);

        Navigation = new Navigation(metadata.XBlock);

        if (MapId is Constant.PerformanceMapId) {
            PerformanceStage = new PerformanceStageManager(this);
        }

        FieldType = FieldType.Default;
    }

    // Init is separate from constructor to allow properties to be injected first.
    public virtual void Init() {
        if (initialized) {
            return;
        }

        initialized = true;
        FieldTick = Environment.TickCount64;

        if (MapData.TryGet(Metadata.XBlock, out FieldAccelerationStructure? accelerationStructure)) {
            AccelerationStructure = accelerationStructure;
            Vibrate = new AiVibrate(accelerationStructure.VibrateEntities.ToArray());
        } else {
            logger.Error("Failed to load acceleration structure for map {MapId}", MapId);
        }

        if (ServerTableMetadata.InstanceFieldTable.Entries.TryGetValue(Metadata.Id, out InstanceFieldMetadata? instanceField)) {
            FieldInstance = instanceField;
        }

        if (ugcMetadata.Plots.Count > 0) {
            using GameStorage.Request db = GameStorage.Context();
            foreach (Plot plot in db.LoadPlotsForMap(MapId)) {
                Plots[plot.Number] = plot;

                plot.Cubes.Values
                    .Where(plotCube => plotCube.Interact != null)
                    .ToList()
                    .ForEach(plotCube => AddFieldFunctionInteract(plotCube, plot));

                foreach (PlotCube plotCube in plot.Cubes.Values) {
                    AddMaid(plotCube, plot);
                }

                plot.Cubes.Values
                    .Where(plotCube => plotCube.Metadata.Install is { IndoorPortal: true })
                    .ToList()
                    .ForEach(plotCube => SpawnFieldToHomePortal(plotCube, plot.OwnerId));
            }
        }

        // Create default to place liftable cubes
        if (MapId is not Constant.DefaultHomeMapId) {
            Plots[0] = new Plot(new UgcMapGroup(0,
                0,
                0,
                new UgcMapGroup.Cost(0, 0, 0),
                new UgcMapGroup.Cost(0, 0, 0),
                new UgcMapGroup.Limits(0, 0, 0, 0, 0, 0)));
        }

        foreach ((Guid guid, BreakableActor breakable) in Entities.BreakableActors) {
            AddBreakable(guid.ToString("N"), breakable);
        }
        foreach ((Guid guid, Liftable liftable) in Entities.Liftables) {
            AddLiftable(guid.ToString("N"), liftable);
        }
        foreach ((Guid guid, InteractObject interact) in Entities.Interacts) {
            AddInteract(guid.ToString("N"), interact);
        }

        foreach (SpawnPointNPC spawnPointNpc in Entities.NpcSpawns) {
            AddSpawnPointNpc(spawnPointNpc);
        }

        foreach (SpawnPointPC spawnPoint in Entities.PlayerSpawns) {
            int fieldId = spawnPoint.SpawnPointId;
            if (fieldPlayerSpawnPoints.ContainsKey(fieldId)) {
                // Duplicate spawn point id (ex. unassigned ID), use next local id
                fieldId = NextLocalId();
            }
            fieldPlayerSpawnPoints[fieldId] = new FieldPlayerSpawnPoint(this, NextLocalId(), spawnPoint, fieldId);
        }

        foreach (TriggerModel trigger in Entities.TriggerModels.Values) {
            AddTrigger(trigger);
        }

        foreach (Portal portal in Entities.Portals.Values) {
            SpawnPortal(portal);
        }

        foreach (FieldTrigger trigger in fieldTriggers.Values) {
            trigger.Update(FieldTick);
        }

        IList<MapMetadata> bonusMaps = MapMetadata.GetMapsByType(Metadata.Property.Continent, MapType.PocketRealm);
        foreach (MapMetadataSpawn spawn in Metadata.Spawns) {
            if (!Entities.RegionSpawns.TryGetValue(spawn.Id, out Ms2RegionSpawn? regionSpawn)) {
                continue;
            }

            var npcIds = new HashSet<int>();
            foreach (string tag in spawn.Tags) {
                if (NpcMetadata.TryLookupTag(tag, out IReadOnlyCollection<int>? tagNpcIds)) {
                    foreach (int tagNpcId in tagNpcIds) {
                        npcIds.Add(tagNpcId);
                    }
                }
            }

            if (npcIds.Count > 0 && spawn.Population > 0) {
                AddMobSpawn(spawn, regionSpawn, npcIds);
                continue;
            }

            if (spawn.Tags.Contains("보너스맵")) { // Bonus Map
                // Spawn a hat within a random range of 5 min to 8 hours
                TimeSpan delay = Random.Shared.Next(1, 97) * TimeSpan.FromMinutes(5);
                Scheduler.Schedule(() => SetBonusMapPortal(bonusMaps, regionSpawn), delay);
            }
        }

        // The npcs a running SpawnNpc game event puts at a region spawn of this field. An event that starts or ends
        // while the field runs is not followed: the field only reads them when it is made.
        foreach (SpawnNpcEvent eventNpc in EventNpcRules.ForField(ServerTableMetadata.GameEventTable.Entries.Values, MapId, DateTime.Now)) {
            if (!Entities.RegionSpawns.TryGetValue(eventNpc.SpawnPointId, out Ms2RegionSpawn? npcSpawn) ||
                !NpcMetadata.TryGet(eventNpc.NpcId, out NpcMetadata? npcMetadata)) {
                logger.Debug("[SpawnNpc] event npc {NpcId} at spawn {SpawnPointId} of {MapId} not found", eventNpc.NpcId, eventNpc.SpawnPointId, MapId);
                continue;
            }

            FieldNpc? fieldNpc = SpawnNpc(npcMetadata, npcSpawn.Position, npcSpawn.Rotation);
            if (fieldNpc != null) {
                fieldNpc.EventShopId = eventNpc.ShopId;
            }
        }

        foreach (Ms2RegionSkill regionSkill in Entities.RegionSkills) {
            if (!SkillMetadata.TryGet(regionSkill.SkillId, regionSkill.Level, out SkillMetadata? skill)) {
                continue;
            }

            AddSkill(skill, regionSkill.Interval, regionSkill.Position, regionSkill.Rotation);
        }

        foreach (Ms2CubeSkill cubeSkill in Entities.CubeSkills) {
            if (!SkillMetadata.TryGet(cubeSkill.SkillId, cubeSkill.Level, out SkillMetadata? skill)) {
                continue;
            }

            AddCubeSkill(skill, cubeSkill.Position, cubeSkill.Rotation);
        }

        foreach (BannerTable.Entry entry in TableMetadata.BannerTable.Entries) {
            if (entry.MapId != MapId) {
                continue;
            }

            using GameStorage.Request db = GameStorage.Context();

            FieldUgcBanner ugcBanner = new(this, entry.Id, MapId, db.FindBannerSlotsByBannerId(entry.Id));
            Banners[entry.Id] = ugcBanner;

            DateTimeOffset dateTimeOffset = DateTimeOffset.UtcNow;

            BannerSlot? slot = ugcBanner.Slots.FirstOrDefault(x => x.ActivateTime.Day == dateTimeOffset.Day && x.ActivateTime.Hour == dateTimeOffset.Hour);

            if (slot is null || slot.Expired || slot.Active) {
                continue;
            }

            slot.Active = true;
        }

        Scheduler.Start();
        thread.Start();
        DebugRenderer = DebugGraphicsContext.FieldAdded(this);
    }

    /// <summary>
    /// Generates an ObjectId unique across all map instances.
    /// </summary>
    /// <returns>Returns a globally unique ObjectId</returns>
    public static int NextGlobalId() => Interlocked.Increment(ref _globalIdCounter);

    /// <summary>
    /// Generates an ObjectId unique to this specific map instance.
    /// </summary>
    /// <returns>Returns a local ObjectId</returns>
    private int NextLocalId() => Interlocked.Increment(ref localIdCounter);

    // Use this to keep systems in sync. Do not use Environment.TickCount directly
    public long FieldTick { get; private set; }

    /// <summary>
    /// FieldTick truncated to 32-bit int. Use this when sending tick values to the client or when
    /// int tick values are required (e.g., TimeSync packets, performance stage).
    /// This properly handles overflow to match Environment.TickCount behavior.
    /// </summary>
    public int FieldTickInt => FieldTick.Truncate32();

    public void QueuePacket(FieldPacketHandler handler, GameSession session, ByteReader reader) {
        lock (queuedPackets) {
            queuedPackets.Add((handler, session, reader));
        }
    }

    private void ProcessPackets() {
        lock (queuedPackets) {
            foreach ((FieldPacketHandler handler, GameSession session, ByteReader reader) packet in queuedPackets) {
                try {
                    if (packet.session.State is SessionState.Disconnected) {
                        continue;
                    }
                    // The copy's buffer is rented and can be longer than the packet: the handler's reader ends at the packet.
                    int length = packet.reader is FieldPacketReader copy ? copy.PacketLength - copy.Position : packet.reader.Available;
                    packet.handler.Handle(packet.session, new BoundedPacketReader(packet.reader, length));
                } catch (PacketOverreadException ex) {
                    if (packet.session.DropLog.ShouldLog(DateTime.UtcNow, out int drops)) {
                        logger.Warning("Dropped packet {OpCode} in {Handler}: read of {Wanted} bytes with {Left} left ({Session}, account {AccountId}, {Drops} dropped since the last line)",
                            $"0x{(ushort) packet.handler.OpCode:X4}", packet.handler.GetType().Name, ex.Wanted, ex.Left, packet.session, packet.session.AccountId, drops);
                    }
                } catch (Exception ex) {
                    if (ex is IndexOutOfRangeException) {
                        logger.Error("Error processing packet {OpCode} for account ID {AccountId}. Packet: {Packet}", packet.handler.OpCode, packet.session.AccountId, packet.ToString());
                    } else {
                        logger.Error(ex, "Error processing handler {Handler} for account ID {AccountId}. Packet: {Packet}", packet.handler.OpCode, packet.session.AccountId, packet.ToString());
                    }
                } finally {
                    ArrayPool<byte>.Shared.Return(packet.reader.Buffer);
                }
            }

            queuedPackets.Clear();
        }
    }

    private void UpdateLoop() {
        while (!cancel.IsCancellationRequested) {
            if (!(DebugRenderer?.IsActive ?? false)) {
                Update();
            }

            // Environment.TickCount has ~16ms precision so sleep until next update
            Thread.Sleep(15);
        }
    }

    public void Update() {
        ProcessPackets();

        if (Players.IsEmpty) {
            return;
        }

        Scheduler.InvokeAll();

        FieldTick = Environment.TickCount64;
        foreach (FieldTrigger trigger in fieldTriggers.Values) trigger.Update(FieldTick);

        foreach (FieldPlayer player in Players.Values) player.Update(FieldTick);
        foreach (FieldNpc npc in Npcs.Values) npc.Update(FieldTick);
        foreach (FieldNpc mob in Mobs.Values) mob.Update(FieldTick);
        foreach (FieldPet pet in Pets.Values) pet.Update(FieldTick);
        foreach (FieldBreakable breakable in fieldBreakables.Values) breakable.Update(FieldTick);
        foreach (FieldLiftable liftable in fieldLiftables.Values) liftable.Update(FieldTick);
        foreach (FieldInteract interact in fieldInteracts.Values) interact.Update(FieldTick);
        foreach (FieldFunctionInteract interact in fieldFunctionInteracts.Values) interact.Update(FieldTick);
        foreach (FieldInteract interact in fieldAdBalloons.Values) interact.Update(FieldTick);
        UpdateChests(FieldTick);
        foreach (HongBao hongBao in hongBaos.Values) hongBao.Update(FieldTick);
        foreach (FieldItem item in fieldItems.Values) item.Update(FieldTick);
        foreach (FieldMobSpawn mobSpawn in fieldMobSpawns.Values) mobSpawn.Update(FieldTick);
        foreach (FieldSpawnPointNpc spawnPointNpc in fieldSpawnPointNpcs.Values) spawnPointNpc.Update(FieldTick);
        foreach (FieldSkill skill in fieldSkills.Values) skill.Update(FieldTick);
        foreach (FieldSkill skill in cubeSkills.Values) skill.Update(FieldTick);
        foreach (FieldPortal portal in fieldPortals.Values) portal.Update(FieldTick);
        foreach (FieldReactor reactor in fieldReactors.Values) reactor.Update(FieldTick);
        UpdateBanners();
        ExpireMicrogames();

        RoomTimer?.Update(FieldTick);
        PerformanceStage?.Update();
        RankDuel?.Update(FieldTick);
        PvpZone?.Update(FieldTick);
        Survival?.Update(FieldTick);
        HideAndSeek?.Update(FieldTick);
    }

    public void EnsurePlayerPosition(FieldPlayer player) {
        if (Entities.BoundingBox.Contains(player.Position, 0.001f)) {
            return;
        }

        if (Maple2.Model.Validators.FallDamageRules.OutOfBoundsHurts(Metadata.Limit.FallDamage)) {
            player.FallDamage(Constants.FallBoundingAddedDistance);
        }
        player.MoveToPosition(player.LastGroundPosition.Align() + new Vector3(0, 0, 150f), default);
    }

    public bool ValidPosition(Vector3 position) {
        return FindNearestPoly(position, out long nearestRef, out _) && nearestRef != 0;
    }

    public bool FindNearestPoly(Vector3 point, out long nearestRef, out RcVec3f position) {
        if (point.X is float.NaN or float.PositiveInfinity or float.NegativeInfinity || point.Y is float.NaN or float.PositiveInfinity or float.NegativeInfinity || point.Z is float.NaN or float.PositiveInfinity or float.NegativeInfinity) {
            nearestRef = 0;
            position = default;
            logger.Error("Invalid point {Point} in field {MapId}", point, MapId);
            return false;
        }
        RcVec3f pointToNavMesh = DotRecastHelper.ToNavMeshSpace(point);
        return FindNearestPoly(pointToNavMesh, out nearestRef, out position);
    }

    public bool FindNearestPoly(RcVec3f point, out long nearestRef, out RcVec3f position, [CallerMemberName] string caller = "") {
        if (point.X is float.NaN or float.PositiveInfinity or float.NegativeInfinity || point.Y is float.NaN or float.PositiveInfinity or float.NegativeInfinity || point.Z is float.NaN or float.PositiveInfinity or float.NegativeInfinity) {
            nearestRef = 0;
            position = default;
            logger.Error("Invalid point {Point} in field {MapId} called by {Caller}", point, MapId, caller);
            return false;
        }

        DtStatus status = Navigation.Crowd.GetNavMeshQuery().FindNearestPoly(point, AgentNavigation.NearestPolyExtents, Navigation.Crowd.GetFilter(0), out nearestRef, out position, out _);
        if (status.Failed()) {
            logger.Warning("Failed to find nearest poly from position {Source} in field {MapId}", point, MapId);
            return false;
        }

        return true;
    }

    public bool TryGetPlayerById(long characterId, [NotNullWhen(true)] out FieldPlayer? player) {
        foreach (FieldPlayer fieldPlayer in Players.Values) {
            if (fieldPlayer.Value.Character.Id == characterId) {
                player = fieldPlayer;
                return true;
            }
        }

        player = null;
        return false;
    }

    public bool TryGetActor(int objectId, [NotNullWhen(true)] out IActor? actor) {
        if (Players.TryGetValue(objectId, out FieldPlayer? player)) {
            actor = player;
            return true;
        }

        if (Npcs.TryGetValue(objectId, out FieldNpc? npc)) {
            actor = npc;
            return true;
        }

        if (Mobs.TryGetValue(objectId, out FieldNpc? mob)) {
            actor = mob;
            return true;
        }

        if (Pets.TryGetValue(objectId, out FieldPet? pet)) {
            actor = pet;
            return true;
        }

        actor = null;

        return false;
    }

    public IEnumerable<IActor> GetActorsBySpawnId(int spawnId) {
        foreach (FieldNpc npc in Npcs.Values) {
            if (npc.SpawnPointId == spawnId) {
                yield return npc;
            }
        }

        foreach (FieldNpc mob in Mobs.Values) {
            if (mob.SpawnPointId == spawnId) {
                yield return mob;
            }
        }

        foreach (FieldPet pet in Pets.Values) {
            if (pet.SpawnPointId == spawnId) {
                yield return pet;
            }
        }
    }

    public bool TryGetPlayer(int objectId, [NotNullWhen(true)] out FieldPlayer? player) {
        return Players.TryGetValue(objectId, out player);
    }

    public bool TryGetPlayer(string name, [NotNullWhen(true)] out FieldPlayer? player) {
        player = Players.Values.FirstOrDefault(p => p.Value.Character.Name == name);
        return player != null;
    }

    public bool TryGetPortal(int portalId, [NotNullWhen(true)] out FieldPortal? portal) {
        portal = fieldPortals.Values.FirstOrDefault(p => p.Value.Id == portalId);
        return portal != null;
    }

    public bool TryGetPlayerSpawn(int id, [NotNullWhen(true)] out FieldPlayerSpawnPoint? playerSpawnPoint) {
        // Get random spawn point if id is -1
        if (id < 0) {
            List<FieldPlayerSpawnPoint> enabledSpawns = fieldPlayerSpawnPoints.Values.Where(spawn => spawn.Enable).ToList();
            playerSpawnPoint = enabledSpawns.Count > 0 ? enabledSpawns[Random.Shared.Next(enabledSpawns.Count)] : null;
            return playerSpawnPoint != null;
        }
        return fieldPlayerSpawnPoints.TryGetValue(id, out playerSpawnPoint);
    }

    public List<FieldPlayerSpawnPoint> GetPlayerSpawns() {
        return fieldPlayerSpawnPoints.Values.ToList();
    }

    public bool TryGetItem(int objectId, [NotNullWhen(true)] out FieldItem? fieldItem) {
        return fieldItems.TryGetValue(objectId, out fieldItem);
    }

    public bool TryGetBreakable(string entityId, [NotNullWhen(true)] out FieldBreakable? fieldBreakable) {
        return fieldBreakables.TryGetValue(entityId, out fieldBreakable);
    }

    public bool TryGetBreakable(int triggerId, [NotNullWhen(true)] out FieldBreakable? fieldBreakable) {
        return triggerBreakable.TryGetValue(triggerId, out fieldBreakable);
    }

    public bool TryGetLiftable(string entityId, [NotNullWhen(true)] out FieldLiftable? fieldLiftable) {
        return fieldLiftables.TryGetValue(entityId, out fieldLiftable);
    }

    public ICollection<FieldInteract> EnumerateInteract() => fieldInteracts.Values;
    public ICollection<FieldLiftable> EnumerateLiftables() => fieldLiftables.Values;
    public FieldInteract? FindBillboard(long boardUid) {
        return fieldAdBalloons.Values.FirstOrDefault(interact => interact.Object is InteractBillBoardObject billboard && billboard.BoardUid == boardUid);
    }
    public bool TryGetInteract(string entityId, [NotNullWhen(true)] out FieldInteract? fieldInteract) {
        return fieldInteracts.TryGetValue(entityId, out fieldInteract) || fieldAdBalloons.TryGetValue(entityId, out fieldInteract) || fieldChests.TryGetValue(entityId, out fieldInteract);
    }

    public IEnumerable<FieldInteract> GetInteractObjectsBySpawnId(int spawnId) {
        return fieldInteracts.Values.Where(interact => interact.SpawnId == spawnId)
            .Concat(fieldAdBalloons.Values.Where(interact => interact.SpawnId == spawnId))
            .Concat(fieldChests.Values.Where(interact => interact.SpawnId == spawnId));
    }

    public bool UsePortal(GameSession session, int portalId, string password) {
        if (!TryGetPortal(portalId, out FieldPortal? fieldPortal)) {
            return false;
        }

        if (!fieldPortal.Enabled) {
            session.Send(NoticePacket.MessageBox(new InterfaceText($"Cannot use disabled portal: {portalId}")));
            return false;
        }

        if (!string.IsNullOrEmpty(fieldPortal.Password) && password != fieldPortal.Password) {
            session.Send(NoticePacket.Message(StringCode.s_home_password_mismatch, NoticePacket.Flags.Alert));
            return false;
        }

        if (fieldPortal.RandomRoom is { } randomRoom) {
            return UseRoomPortal(session, fieldPortal, randomRoom.MaxUserCount, randomRoom.AutoClosing, randomRoom.DurationTick);
        }
        if (fieldPortal.MassiveMaxCount is { } massiveMaxCount) {
            // A full OpenMassive portal takes nobody more, so it closes; its room has no time limit and goes
            // when it is empty.
            return UseRoomPortal(session, fieldPortal, massiveMaxCount, true, 0);
        }

        // MoveByPortal (same map)
        Portal srcPortal = fieldPortal;
        switch (srcPortal.Type) {
            case PortalType.InHome:
                Plot? fieldPlot = session.Housing.GetFieldPlot();
                PlotCube? cubePortal = fieldPlot?.Cubes.Values.FirstOrDefault(x => x.Interact?.PortalSettings is not null && x.Interact.PortalSettings.PortalObjectId == fieldPortal.ObjectId);
                if (cubePortal is null) {
                    return false;
                }

                switch (cubePortal.Interact!.PortalSettings!.Destination) {
                    case CubePortalDestination.PortalInHome:
                        PlotCube? destinationCube = fieldPlot?.Cubes.Values.FirstOrDefault(x => x.Interact?.PortalSettings is not null && x.Interact.PortalSettings.PortalName == cubePortal.Interact.PortalSettings.DestinationTarget);
                        if (destinationCube is null) {
                            return false;
                        }

                        session.Send(PortalPacket.MoveByPortal(session.Player, destinationCube.Position, default));
                        session.Player.PortalInvincible();
                        return true;
                    case CubePortalDestination.SelectedMap:
                        session.Migrate(srcPortal.TargetMapId);
                        return true;
                    case CubePortalDestination.FriendHome:
                        Home? home = GetHome(session, fieldPortal);
                        if (home is null) {
                            session.Send(FieldEnterPacket.Error(MigrationError.s_move_err_no_server));
                            return false;
                        }

                        // The same check as a visit-home request: block list, private home, password.
                        if (!HomeEntry.TryEnter(session, home, password ?? string.Empty)) {
                            return false;
                        }

                        session.Migrate(home.Indoor.MapId, home.Indoor.OwnerId);
                        return true;
                }
                return false;
            case PortalType.LeaveDungeon:
                //TODO: Migrate back to original channel
                session.Send(session.PrepareFieldByPortal(session.ReturnMapId())
                    ? FieldEnterPacket.Request(session.Player)
                    : FieldEnterPacket.Error(MigrationError.s_move_err_default));
                return true;
            case PortalType.DungeonReturnToLobby:
                if (this is not DungeonFieldManager dungeonField || dungeonField.Lobby is null) {
                    logger.Warning("DungeonReturnToLobby portal used in non-dungeon map {MapId}", MapId);
                    return false;
                }
                session.Send(session.PrepareFieldByPortal(dungeonField.Lobby.MapId, portalId: 2, roomId: dungeonField.Lobby.RoomId)
                    ? FieldEnterPacket.Request(session.Player)
                    : FieldEnterPacket.Error(MigrationError.s_move_err_default));
                return true;
            case PortalType.Quest:
                session.Field?.RemovePortal(fieldPortal.ObjectId);
                break;
            case PortalType.FieldToHome:
                // The door of an outdoor plot: the same check as a visit-home request.
                Home? plotHome = GetHome(session, fieldPortal);
                if (plotHome is null) {
                    session.Send(EnterUgcMapPacket.CannotVisitThatCharacterHome());
                    return false;
                }
                if (!HomeEntry.TryEnter(session, plotHome, password ?? string.Empty)) {
                    return false;
                }

                session.Migrate(Constant.DefaultHomeMapId, fieldPortal.HomeId);
                break;
        }

        if (srcPortal.TargetMapId == MapId) {
            if (TryGetPortal(srcPortal.TargetPortalId, out FieldPortal? dstPortal)) {
                Vector3 position = dstPortal.Position;
                Vector3 rotation = dstPortal.Rotation;

                if (dstPortal.Value.ActionType is PortalActionType.Touch) {
                    // add one blocks or use offset in the direction of front axis
                    float offset = Constant.BlockSize;
                    if (dstPortal.Value.FrontOffset != 0) {
                        offset = dstPortal.Value.FrontOffset;
                    }

                    Vector3 forward = dstPortal.Transform.FrontAxis;

                    position += forward * offset;
                }
                session.Send(PortalPacket.MoveByPortal(session.Player, position, rotation));
                session.Player.PortalInvincible();
            }

            return true;
        }

        if (srcPortal.TargetMapId == 0) {
            session.ReturnFieldByPortal();
            return true;
        }

        // The "map" condition counts a portal that was taken, not one whose entry was refused. A portal to a
        // room (a bonus map) leads into that room, not into the map's shared field.
        bool entered = session.PrepareFieldByPortal(srcPortal.TargetMapId, portalId: srcPortal.TargetPortalId, roomId: fieldPortal.RoomId);
        if (entered) {
            session.ConditionUpdate(ConditionType.map, codeLong: srcPortal.TargetMapId);
        }

        session.Send(entered
            ? FieldEnterPacket.Request(session.Player)
            : FieldEnterPacket.Error(MigrationError.s_move_err_default));
        return true;
    }

    // The portal's own room is made at the first entry, with durationTick on the clock when above 0; it takes
    // maxUserCount players (0 = no limit), and with autoClosing the portal goes when it is full. An ended room
    // closes its portal.
    private bool UseRoomPortal(GameSession session, FieldPortal fieldPortal, int maxUserCount, bool autoClosing, int durationTick) {
        FieldManager? roomField;
        bool close;
        lock (fieldPortal) {
            if (RandomRoomRules.Full(fieldPortal.RoomEntered, maxUserCount)) {
                session.Send(FieldEnterPacket.Error(MigrationError.s_move_err_member_limit));
                return false;
            }
            if (fieldPortal.RoomField is { Disposed: true }) {
                RemovePortal(fieldPortal.ObjectId);
                session.Send(FieldEnterPacket.Error(MigrationError.s_move_err_dungeon_not_exist));
                return false;
            }
            if (fieldPortal.RoomField == null) {
                FieldManager? created = FieldFactory.Create(fieldPortal.Value.TargetMapId);
                if (created == null) {
                    session.Send(FieldEnterPacket.Error(MigrationError.s_move_err_default));
                    return false;
                }
                created.Init();
                if (durationTick > 0) {
                    created.SetRoomTimer(RoomTimerType.Clock, durationTick);
                }
                fieldPortal.RoomField = created;
            }

            roomField = fieldPortal.RoomField;
            fieldPortal.RoomEntered++;
            close = autoClosing && RandomRoomRules.Full(fieldPortal.RoomEntered, maxUserCount);
        }

        if (close) {
            RemovePortal(fieldPortal.ObjectId);
        }

        bool entered = session.PrepareFieldByPortal(roomField.MapId, roomId: roomField.RoomId);
        if (entered) {
            session.ConditionUpdate(ConditionType.map, codeLong: roomField.MapId);
        } else {
            lock (fieldPortal) {
                fieldPortal.RoomEntered--;
            }
        }
        session.Send(entered
            ? FieldEnterPacket.Request(session.Player)
            : FieldEnterPacket.Error(MigrationError.s_move_err_default));
        return entered;
    }

    // A lifted weapon cube is away until its respawn time: nobody lifts it again before, and a player who enters
    // meanwhile is told (ResponseCube 27).
    private readonly ConcurrentDictionary<Vector3B, long> liftedCubes = new();

    public bool LiftupCube(in Vector3B coordinates, [NotNullWhen(true)] out LiftupWeapon? liftupWeapon, out int respawnTick) {
        respawnTick = 0;
        long now = Environment.TickCount64;
        if (!Entities.ObjectWeapons.TryGetValue(coordinates, out ObjectWeapon? objectWeapon)
            || (liftedCubes.TryGetValue(coordinates, out long until) && LiftupRules.IsAway(until, now))) {
            liftupWeapon = null;
            return false;
        }

        int itemId = objectWeapon.ItemIds[Environment.TickCount % objectWeapon.ItemIds.Length];
        if (!ItemMetadata.TryGet(itemId, out ItemMetadata? item) || !(item.Skill?.WeaponId > 0)) {
            liftupWeapon = null;
            return false;
        }

        liftupWeapon = new LiftupWeapon(objectWeapon, itemId, item.Skill.WeaponId, item.Skill.WeaponLevel);
        liftedCubes[coordinates] = now + objectWeapon.RespawnTick;
        respawnTick = LiftupRules.ClientTick(now + objectWeapon.RespawnTick);

        if (objectWeapon.SpawnNpcId != 0 && Random.Shared.NextSingle() < objectWeapon.SpawnNpcRate) {
            if (NpcMetadata.TryGet(objectWeapon.SpawnNpcId, out NpcMetadata? metadata)) {
                FieldNpc? fieldNpc = SpawnNpc(metadata, objectWeapon.Position, objectWeapon.Rotation);
                if (fieldNpc != null) {
                    Broadcast(FieldPacket.AddNpc(fieldNpc));
                    Broadcast(ProxyObjectPacket.AddNpc(fieldNpc));
                }
            }
        }

        return true;
    }

    // An object's npc spawn on its use (InteractRules.PickSpawn): Count npcs of the picked entry, each at a random
    // spot within its Radius put on the navmesh, or at the object itself with no radius (the survival maps' boat
    // docks stand on water, where the nearest navmesh point is ashore), removed after LifeTime.
    public void SpawnInteractNpcs(FieldInteract interact) {
        InteractObjectMetadataSpawn[] spawns = interact.Value.Spawn;
        if (spawns.Length == 0) {
            return;
        }
        int pick = InteractRules.PickSpawn(spawns.Select(spawn => spawn.Probability).ToArray(), Random.Shared.Next(InteractRules.SpawnRollRange));
        if (pick < 0 || !NpcMetadata.TryGet(spawns[pick].Id, out NpcMetadata? metadata)) {
            return;
        }

        InteractObjectMetadataSpawn picked = spawns[pick];
        for (int i = 0; i < picked.Count; i++) {
            Vector3 position = interact.Position;
            if (picked.Radius > 0) {
                position = StageSpot(AiSummon.Position(interact.Position, Vector3.Zero, Vector3.Zero, new Vector3(picked.Radius, picked.Radius, 0), Random.Shared));
            }
            FieldNpc? fieldNpc = SpawnNpc(metadata, position, interact.Rotation);
            if (fieldNpc == null) {
                continue;
            }
            Broadcast(FieldPacket.AddNpc(fieldNpc));
            Broadcast(ProxyObjectPacket.AddNpc(fieldNpc));
            fieldNpc.Update(Environment.TickCount64);

            TimeSpan lifeTime = AiSummon.LifeTime(picked.LifeTime);
            if (lifeTime > TimeSpan.Zero) {
                RemoveNpc(fieldNpc.ObjectId, lifeTime);
            }
        }
    }

    // moves: the players to send (patrol_condition_user); null sends every player.
    public void MovePlayerAlongPath(string pathName, Func<FieldPlayer, bool>? moves = null) {
        MS2PatrolData? patrolData = Entities.Patrols.FirstOrDefault(p => p.Name == pathName);
        if (patrolData is null) {
            return;
        }

        foreach (FieldPlayer player in Players.Values) {
            if (moves != null && !moves(player)) {
                continue;
            }
            if (!NpcMetadata.TryGet(Constant.DummyNpc(player.Value.Character.Gender), out NpcMetadata? npcMetadata)) {
                continue;
            }

            FieldNpc? dummyNpc = SpawnNpc(npcMetadata, player.Position, player.Rotation);
            if (dummyNpc is null) {
                continue;
            }
            Broadcast(FieldPacket.AddNpc(dummyNpc));
            Broadcast(ProxyObjectPacket.AddNpc(dummyNpc));

            dummyNpc.SetPatrolData(patrolData);
            dummyNpc.MovementState.CleanupPatrolData(player);
            player.Session.Send(FollowNpcPacket.FollowNpc(dummyNpc.ObjectId));
        }
    }

    public void VibrateObjects(DamageRecord record, Vector3 position) {
        if (record.AttackMetadata.BrokenOffence == 0) {
            // No items are going to vibrate/break.
            return;
        }

        float rangeDistance = record.AttackMetadata.Range.Distance;
        if (AccelerationStructure is null) {
            return;
        }

        List<FieldVibrateEntity> vibrateObjects = AccelerationStructure.QueryVibrateObjectsCenterList(position, 2 * new Vector3(rangeDistance, rangeDistance, rangeDistance));
        foreach (FieldVibrateEntity vibrate in vibrateObjects) {
            if (vibrate.BreakDefense < record.AttackMetadata.BrokenOffence) {
                // TODO Keep a record of when the vibrate object was broken. Don't send if it's currently broken and respawning.
            }
            Broadcast(VibratePacket.Attack(vibrate.Id.Id, record));
        }
    }

    private static Home? GetHome(GameSession session, FieldPortal fieldPortal) {
        using GameStorage.Request db = session.GameStorage.Context();
        Home? home = db.GetHome(fieldPortal.HomeId);
        return home;
    }

    #region DebugUtils
    public void BroadcastAiMessage(ByteWriter packet) {
        foreach ((int objectId, FieldPlayer player) in Players) {
            if (player.DebugAi) {
                player.Session.Send(packet);
            }
        }
    }

    public void BroadcastAiType(GameSession requester) {
        foreach ((int objectId, FieldNpc npc) in Npcs) {
            npc.SendDebugAiInfo(requester);
        }

        foreach ((int objectId, FieldPet npc) in Pets) {
            npc.SendDebugAiInfo(requester);
        }
    }
    #endregion

    public void Broadcast(ByteWriter packet, GameSession? sender = null) {
        if (!initialized) {
            return;
        }

        foreach (FieldPlayer fieldPlayer in Players.Values) {
            if (fieldPlayer.Session == sender) continue;
            fieldPlayer.Session.Send(packet);
        }
    }

    public void BroadcastNpcControl(FieldNpc npc) {
        if (!initialized) {
            return;
        }

        foreach (FieldPlayer fieldPlayer in Players.Values) {
            NpcScriptManager? npcScript = fieldPlayer.Session.NpcScript;
            if (npcScript is not null && npcScript.Npc is not null && npcScript.Npc.Value.Id == npc.Value.Id && npc.Value.Metadata.LookAtTarget.UseTalkMotion) {
                fieldPlayer.Session.Send(NpcControlPacket.Talk(npc));
                continue;
            }

            fieldPlayer.Session.Send(NpcControlPacket.Control(npc));
        }
    }

    public void Dispose() {
        if (Disposed) {
            return;
        }
        // An arena match whose field goes before its zone ended: the World lets its characters go.
        Arena?.Close();
        // A guild vs game match whose field goes before its game ended (no one came, or all left): the World frees
        // both guilds now, not at the match's lifetime.
        GuildVsGame?.End();

        logger.Debug("Disposing FieldManager {MapId}", MapId);

        DebugGraphicsContext.FieldRemoved(this);
        try {
            cancel.Cancel();
        } catch (Exception e) {
            logger.Error(e, "Failed to cancel FieldManager thread, cancel.Cancel() threw an exception. Disposed: {Disposed}", Disposed);
        }
        cancel.Dispose();
        thread.Join();
        Navigation.Dispose();

        Disposed = true;
        logger.Debug("Disposed FieldManager {MapId}", MapId);
    }
}
