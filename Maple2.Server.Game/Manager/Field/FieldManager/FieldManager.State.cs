using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using DotRecast.Core.Numerics;
using DotRecast.Detour.Crowd;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Field;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.LuaFunctions;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Tools;
using Maple2.Tools.Collision;
using Maple2.Tools.DotRecast;
using Maple2.Tools.Extensions;
using Maple2.Tools.VectorMath;
using Maple2.Server.World.Service;
using Serilog;
using WorldTimeEventRequest = Maple2.Server.World.Service.TimeEventRequest;

namespace Maple2.Server.Game.Manager.Field;

public partial class FieldManager {
    // Actors
    public ConcurrentDictionary<int, FieldPlayer> Players { get; } = [];
    public ConcurrentDictionary<int, FieldNpc> Npcs { get; } = [];
    public ConcurrentDictionary<int, FieldNpc> Mobs { get; } = [];
    public ConcurrentDictionary<int, FieldPet> Pets { get; } = [];
    // Set by the limit_spawn_npc_count trigger action: trigger spawns stop once the field holds this many monsters. 0 = no limit.
    public int TriggerMobLimit { get; set; }
    // The string ids of the monsters that died here (npc_is_dead_by_string_id).
    public NpcStringIdDeaths StringIdDeaths { get; } = new();

    // Entities
    private readonly ConcurrentDictionary<string, FieldBreakable> fieldBreakables = new();
    private readonly ConcurrentDictionary<string, FieldLiftable> fieldLiftables = new();
    private readonly ConcurrentDictionary<string, FieldInteract> fieldInteracts = new();
    private readonly ConcurrentDictionary<string, FieldFunctionInteract> fieldFunctionInteracts = new();
    private readonly ConcurrentDictionary<string, FieldInteract> fieldAdBalloons = new();
    private readonly ConcurrentDictionary<string, FieldInteract> fieldChests = new();
    private readonly ConcurrentDictionary<int, FieldInstrument> fieldInstruments = new();
    private readonly ConcurrentDictionary<int, FieldItem> fieldItems = new();
    private readonly ConcurrentDictionary<int, FieldMobSpawn> fieldMobSpawns = new();
    private readonly ConcurrentDictionary<string, FieldSpawnPointNpc> fieldSpawnPointNpcs = new();
    private readonly ConcurrentDictionary<int, FieldPlayerSpawnPoint> fieldPlayerSpawnPoints = new();
    private readonly ConcurrentDictionary<int, FieldSpawnGroup> fieldSpawnGroups = new();
    private readonly ConcurrentDictionary<int, FieldSkill> fieldSkills = new();
    private readonly ConcurrentDictionary<int, FieldSkill> cubeSkills = new();
    private readonly ConcurrentDictionary<int, FieldPortal> fieldPortals = new();

    private readonly ConcurrentDictionary<int, HongBao> hongBaos = new();
    private readonly ConcurrentDictionary<int, FieldReactor> fieldReactors = new();
    private readonly object reactorAddLock = new(); // the cell and limit tests and the add are one step

    public IEnumerable<FieldSkill> DebugFieldSkills => fieldSkills.Values;
    public IEnumerable<FieldSkill> DebugCubeSkills => cubeSkills.Values;

    private int worldBossObjectId;

    private string? background;
    private readonly ConcurrentDictionary<FieldProperty, IFieldProperty> fieldProperties = new();

    public RoomTimer? RoomTimer { get; private set; }
    // The Dark Stream run of the field, from the script's start_game on.
    public DarkStreamGame? DarkStream { get; set; }
    // The Spring Farm arcade game of the field, from the script's start_game on.
    public SpringFarmGame? SpringFarm { get; set; }
    // The Boom Boom Ocean and Three Two One arcade games of the field, from the script's start_game on.
    public BoomBoomOceanGame? BoomBoomOcean { get; set; }
    public ThreeTwoOneGame? ThreeTwoOne { get; set; }


    #region Helpers
    public ICollection<FieldNpc> EnumerateNpcs() => Npcs.Values.Concat(Mobs.Values).ToList();
    public IReadOnlyDictionary<int, FieldPlayer> GetPlayers() {
        return Players;
    }
    public ICollection<FieldPortal> GetPortals() => fieldPortals.Values;
    #endregion

    #region Spawn
    public FieldPlayer SpawnPlayer(GameSession session, Player player, int portalId = -1, in Vector3 position = default, in Vector3 rotation = default) {
        // TODO: Not sure what the difference is between instance ids.
        player.Character.MapId = MapId;
        player.Character.InstanceMapId = MapId;
        player.Character.RoomId = RoomId;
        player.Character.DungeonId = DungeonId;

        var fieldPlayer = new FieldPlayer(session, player) {
            Position = position,
            Rotation = rotation,
        };
        session.Stats.ResetActor(fieldPlayer);
        session.Buffs.ResetActor(fieldPlayer);
        session.Animation.ResetActor(fieldPlayer);

        // Use Portal if needed.
        if (fieldPlayer.Position == default && Entities.Portals.TryGetValue(portalId, out Portal? portal)) {
            fieldPlayer.Position = portal.Position.Offset(portal.FrontOffset, portal.Rotation);
            if (portal.RandomRadius > 0) {
                fieldPlayer.Position += new Vector3(Random.Shared.Next(-portal.RandomRadius, portal.RandomRadius), Random.Shared.Next(-portal.RandomRadius, portal.RandomRadius), 0);
            }
            fieldPlayer.Rotation = portal.Rotation;
        }

        // Use SpawnPoint if needed.
        if (fieldPlayer.Position == default) {
            if (TryGetPlayerSpawn(-1, out FieldPlayerSpawnPoint? spawn)) {
                fieldPlayer.Position = spawn.Position + new Vector3(0, 0, 25);
                fieldPlayer.Rotation = spawn.Rotation;
            }
        }

        if (FieldEntryRules.SavesReturnMap(FieldInstance.Type, FieldInstance.SaveField, this is DungeonFieldManager, Metadata.Property.EnterReturnId)) {
            player.Character.ReturnMaps.Push(Metadata.Property.EnterReturnId);
        }

        return fieldPlayer;
    }

    public FieldNpc? SpawnNpc(NpcMetadata npc, Vector3 position, Vector3 rotation, bool disableAi = false, FieldMobSpawn? owner = null, SpawnPointNPC? spawnPointNpc = null, string spawnAnimation = "", long maidUid = 0, bool playerSummon = false) {
        // The played client creates no NPC it has no data for (PlayedClientNpcs).
        if (!PlayedClientNpcs.CanSpawn(npc)) {
            logger.Debug("[SpawnNpc] NPC {NpcId} is not in the played client's NPC data, not spawned", npc.Id);
            return null;
        }

        // Apply random offset if SpawnRadius is set
        Vector3 spawnPosition = position;
        if (spawnPointNpc?.SpawnRadius > 0) {
            float angle = Random.Shared.NextSingle() * MathF.PI * 2;
            float distance = Random.Shared.NextSingle() * spawnPointNpc.SpawnRadius;
            Vector3 offset = new Vector3(
                MathF.Cos(angle) * distance,
                MathF.Sin(angle) * distance,
                0
            );
            Vector3 randomizedPosition = position + offset;

            // Validate and snap to nearest valid navmesh position
            if (FindNearestPoly(randomizedPosition, out long nearestRef, out RcVec3f validPosition) && nearestRef != 0) {
                spawnPosition = DotRecastHelper.FromNavMeshSpace(validPosition);
                logger.Debug("[SpawnNpc] Applied spawn radius {Radius} to NPC {NpcId} at SpawnPoint {SpawnPointId}, offset: {Offset}, snapped from {Original} to {Final}",
                    spawnPointNpc.SpawnRadius, npc.Id, spawnPointNpc.SpawnPointId, offset, randomizedPosition, spawnPosition);
            } else {
                logger.Warning("[SpawnNpc] Randomized position {Position} invalid for NPC {NpcId}, using original spawn point {Original}",
                    randomizedPosition, npc.Id, position);
                spawnPosition = position;
            }
        }

        DtCrowdAgent agent = Navigation.AddAgent(npc, spawnPosition);

        AnimationMetadata? animation = NpcMetadata.GetAnimation(npc.Model.Name);
        string aiPath = disableAi ? string.Empty : npc.AiPath;
        var fieldNpc = new FieldNpc(this, NextLocalId(), agent, new Npc(npc, animation), aiPath, patrolDataUUID: spawnPointNpc?.PatrolData, spawnAnimation: spawnAnimation) {
            Owner = owner,
            Position = spawnPosition,
            Rotation = rotation,
            Origin = owner?.Position ?? spawnPosition,
            SpawnPointId = owner?.Value.Id ?? 0,
            MaidUid = maidUid,
            SpawnBuffsPending = npc.Property.Buffs.Length > 0,
        };

        // The room's npcStatFactorByPlayerCount row, taken once at spawn: an npc keeps its stats when players join or
        // leave later. A player's summon is never scaled (pets do not come through here).
        if (!playerSummon) {
            int factorId = NpcStatFactorRules.FactorId((this as DungeonFieldManager)?.DungeonMetadata.PlayerCountFactorId ?? 0, FieldInstance.NpcStatFactorId);
            NpcStatFactorMetadata? factor = NpcStatFactorRules.Find(ServerTableMetadata.NpcStatFactorTable, factorId, npc.Basic.Class,
                NpcStatFactorRules.PlayerCount(Size, Players.Count));
            if (factor != null) {
                fieldNpc.Stats.SetNpcStats(NpcStatFactorRules.Apply(npc.Stat.Stats, factor));
            }
        }

        if (npc.Basic.Friendly > 0) {
            Npcs[fieldNpc.ObjectId] = fieldNpc;
        } else {
            Mobs[fieldNpc.ObjectId] = fieldNpc;
        }

        return fieldNpc;
    }

    // NPCs spawned by a GM (create window 0x24, `npc` command). The field holds GmCreateRules.MaxNpcPerField
    // of them alive at most; returns how many were spawned.
    private readonly HashSet<int> gmNpcs = [];

    public int SpawnGmNpcs(NpcMetadata npc, int requested, Func<int, Vector3> position, Vector3 rotation, bool disableAi = false) {
        lock (gmNpcs) {
            gmNpcs.RemoveWhere(objectId => !Npcs.ContainsKey(objectId) && !Mobs.ContainsKey(objectId));
            int count = GmCreateRules.PlanNpc(requested, gmNpcs.Count);
            int spawned = 0;
            for (int i = 0; i < count; i++) {
                FieldNpc? fieldNpc = SpawnNpc(npc, position(i), rotation, disableAi: disableAi);
                if (fieldNpc == null) {
                    continue;
                }

                gmNpcs.Add(fieldNpc.ObjectId);
                Broadcast(FieldPacket.AddNpc(fieldNpc));
                Broadcast(ProxyObjectPacket.AddNpc(fieldNpc));
                fieldNpc.Update(Environment.TickCount64);
                spawned++;
            }
            return spawned;
        }
    }

    public FieldNpc? SpawnNpc(NpcMetadata npc, SpawnPointNPC spawnPointNpc) {
        return SpawnNpc(npc, spawnPointNpc.Position, spawnPointNpc.Rotation, disableAi: false, owner: null, spawnPointNpc: spawnPointNpc);
    }

    public FieldPet? SpawnPet(Item pet, Vector3 position, Vector3 rotation, FieldMobSpawn? owner = null, FieldPlayer? player = null) {
        if (!NpcMetadata.TryGet(pet.Metadata.Property.PetId, out NpcMetadata? npc)) {
            logger.Error("Failed to get npc metadata for pet id {PetId}", pet.Metadata.Property.PetId);
            return null;
        }

        if (!ItemMetadata.TryGetPet(pet.Metadata.Property.PetId, out PetMetadata? petMetadata)) {
            logger.Error("Failed to get pet metadata for pet id {PetId}", pet.Metadata.Property.PetId);
            return null;
        }

        DtCrowdAgent agent = Navigation.AddAgent(npc, position);

        // We use GlobalId if there is an owner because players can move between maps.
        int objectId = player != null ? NextGlobalId() : NextLocalId();
        AnimationMetadata? animation = NpcMetadata.GetAnimation(npc.Model.Name);

        var fieldPet = new FieldPet(this, objectId, agent, new Npc(npc, animation), pet, petMetadata, Constant.PetFieldAiPath, player) {
            Owner = owner,
            Position = position,
            Rotation = rotation,
            Origin = owner?.Position ?? position,
        };
        Pets[fieldPet.ObjectId] = fieldPet;

        return fieldPet;
    }

    public FieldPortal SpawnPortal(Portal portal, Vector3 position = default, Vector3 rotation = default) {
        var fieldPortal = new FieldPortal(this, NextLocalId(), portal) {
            Position = position != default ? position : portal.Position,
            Rotation = rotation != default ? rotation : portal.Rotation,
        };
        fieldPortals[fieldPortal.ObjectId] = fieldPortal;

        return fieldPortal;
    }

    public FieldPortal SpawnPortal(Portal portal, int roomId, Vector3 position = default, Vector3 rotation = default) {
        var fieldPortal = new FieldPortal(this, NextLocalId(), portal) {
            Position = position != default ? position : portal.Position,
            Rotation = rotation != default ? rotation : portal.Rotation,
            RoomId = roomId,
        };
        fieldPortals[fieldPortal.ObjectId] = fieldPortal;

        return fieldPortal;
    }

    public FieldPortal SpawnPortal(QuestSummonPortal metadata, FieldNpc npc, FieldPlayer owner) {
        var portal = new Portal(NextLocalId(), metadata.MapId, metadata.PortalId, PortalType.Quest, PortalActionType.Interact,
            npc.Position.Offset(Constants.QuestPortalDistanceFromNpc, npc.Rotation), npc.Rotation,
            new Vector3(Constants.QuestPortalDistanceFromNpc, Constants.QuestPortalDimensionY,
            Constants.QuestPortalDimensionZ), Constants.QuestPortalDistanceFromNpc,
            0, true, false, true);
        var fieldPortal = new FieldQuestPortal(owner, this, NextLocalId(), portal) {
            Position = portal.Position,
            Rotation = portal.Rotation,
            EndTick = (FieldTick + (long) TimeSpan.FromSeconds(Constants.QuestPortalKeepTime).TotalMilliseconds).Truncate32(),
            StartTick = FieldTickInt,
            Model = Constants.QuestPortalKeepNif,
        };
        fieldPortals[fieldPortal.ObjectId] = fieldPortal;

        return fieldPortal;
    }

    public FieldPortal? SpawnCubePortal(PlotCube plotCube) {
        int targetMapId = MapId;
        long targetHomeAccountId = 0;
        CubePortalSettings? cubePortalSettings = plotCube.Interact?.PortalSettings;
        if (cubePortalSettings is null) {
            return null;
        }

        if (!string.IsNullOrEmpty(cubePortalSettings.DestinationTarget)) {
            switch (cubePortalSettings.Destination) {
                case CubePortalDestination.PortalInHome:
                    targetMapId = Constant.DefaultHomeMapId;
                    break;
                case CubePortalDestination.SelectedMap:
                    targetMapId = int.Parse(cubePortalSettings.DestinationTarget);
                    break;
                case CubePortalDestination.FriendHome:
                    targetMapId = Constant.DefaultHomeMapId;
                    targetHomeAccountId = long.Parse(cubePortalSettings.DestinationTarget);
                    break;
            }
        }
        var portal = new Portal(NextLocalId(), targetMapId, -1, PortalType.InHome, cubePortalSettings.Method, plotCube.Position, new Vector3(0, 0, plotCube.Rotation), new Vector3(200, 200, 250), 0, 0, Visible: true, MinimapVisible: false, Enable: true);
        FieldPortal fieldPortal = SpawnPortal(portal);
        fieldPortal.HomeId = targetHomeAccountId;
        cubePortalSettings.PortalObjectId = fieldPortal.ObjectId;

        return fieldPortal;
    }

    public FieldPortal? SpawnFieldToHomePortal(PlotCube plotCube, long targetHomeAccountId) {
        if (plotCube.Metadata.Install is { IndoorPortal: false }) {
            return null;
        }

        var transform = new Transform {
            Position = plotCube.Position,
            RotationAnglesDegrees = new Vector3(0, 0, plotCube.Rotation),
        };

        Vector3 newPosition = plotCube.Position;
        newPosition -= transform.FrontAxis * 75;
        newPosition.Z -= 75;

        var portal = new Portal(NextLocalId(), Constant.DefaultHomeMapId, -1, PortalType.FieldToHome, PortalActionType.Interact, newPosition, new Vector3(0, 0, plotCube.Rotation), new Vector3(75, 75, 75), 0, 0, Visible: true, MinimapVisible: false, Enable: true);
        FieldPortal fieldPortal = SpawnPortal(portal);
        fieldPortal.HomeId = targetHomeAccountId;

        return fieldPortal;
    }

    public FieldItem SpawnItem(IActor owner, Item item, short dropKind = ItemDropKind.PlayerDrop, long msDuration = FieldItem.PlayerDropDurationMs) {
        lock (item) {
            using GameStorage.Request db = GameStorage.Context();
            db.SaveItems(0, item);
        }

        var fieldItem = new FieldItem(this, NextLocalId(), item, msDuration) {
            Owner = owner,
            Position = owner.Position,
            Rotation = owner.Rotation,
            Type = DropType.Player,
            DropKind = dropKind,
        };
        fieldItems[fieldItem.ObjectId] = fieldItem;

        return fieldItem;
    }

    public FieldItem SpawnItem(Vector3 position, Vector3 rotation, Item item, IFieldEntity? owner, long characterId = 0, bool fixedPosition = false,
                               short dropKind = ItemDropKind.None, long msDuration = 0) {
        var fieldItem = new FieldItem(this, NextLocalId(), item, msDuration) {
            Owner = owner,
            Position = position,
            Rotation = rotation,
            FixedPosition = fixedPosition,
            ReceiverId = characterId,
            Type = characterId > 0 ? DropType.Default : DropType.Player,
            DropKind = dropKind,
        };
        fieldItems[fieldItem.ObjectId] = fieldItem;

        return fieldItem;
    }

    public void DropItem(IActor owner, Item item) {
        FieldItem fieldItem = SpawnItem(owner, item);
        Broadcast(FieldPacket.DropItem(fieldItem));
    }

    public void DropItem(Vector3 position, Vector3 rotation, Item item, IFieldEntity? owner = null, long characterId = 0, bool fixedPosition = false,
                         short dropKind = ItemDropKind.None, long msDuration = 0) {
        FieldItem fieldItem = SpawnItem(position, rotation, item, owner, characterId, fixedPosition, dropKind, msDuration);
        Broadcast(FieldPacket.DropItem(fieldItem));
    }

    public FieldBreakable? AddBreakable(string entityId, BreakableActor breakable) {
        if (fieldBreakables.ContainsKey(entityId)) {
            return null;
        }

        var fieldBreakable = new FieldBreakable(this, NextLocalId(), entityId, breakable) {
            Position = breakable.Position,
            Rotation = breakable.Rotation,
        };

        fieldBreakables.TryAdd(entityId, fieldBreakable);
        if (breakable.Id != 0) {
            triggerBreakable.TryAdd(breakable.Id, fieldBreakable);
        }
        return fieldBreakable;
    }

    public FieldLiftable? AddLiftable(string entityId, Liftable liftable) {
        if (fieldLiftables.ContainsKey(entityId)) {
            return null;
        }

        var fieldLiftable = new FieldLiftable(this, NextLocalId(), entityId, liftable) {
            Position = liftable.Position,
            Rotation = liftable.Rotation,
        };

        if (!fieldLiftables.TryAdd(entityId, fieldLiftable)) {
            return null;
        }
        return fieldLiftable;
    }

    public FieldInteract? AddInteract(string entityId, InteractObject interact) {
        if (!TableMetadata.InteractObjectTable.Entries.TryGetValue(interact.InteractId, out InteractObjectMetadata? metadata)) {
            return null;
        }
        IInteractObject interactObject = interact switch {
            Ms2InteractMesh mesh => new InteractMeshObject(entityId, mesh),
            Ms2Telescope telescope => new InteractTelescopeObject(entityId, telescope),
            Ms2SimpleUiObject ui => new InteractUiObject(entityId, ui),
            Ms2InteractDisplay display when metadata.Type == InteractType.DisplayImage => new InteractDisplayImage(entityId, display),
            Ms2InteractDisplay poster when metadata.Type == InteractType.GuildPoster => new InteractGuildPosterObject(entityId, poster),
            Ms2InteractActor actor => new InteractGatheringObject(entityId, actor),
            _ => throw new ArgumentException($"Unsupported Type: {metadata.Type}"),
        };

        return AddInteract(interact, interactObject);
    }

    public FieldInteract? AddInteract(InteractObject interactData, IInteractObject interactObject, InteractObjectMetadata? metadata = null) {
        if (metadata == null && !TableMetadata.InteractObjectTable.Entries.TryGetValue(interactData.InteractId, out metadata)) {
            return null;
        }

        var fieldInteract = new FieldInteract(this, NextLocalId(), interactObject.EntityId, metadata, interactObject) {
            Position = interactData.Position,
            Rotation = interactData.Rotation,
        };

        //TODO: Add treasure chests
        switch (interactObject) {
            case InteractBillBoardObject billboard:
                fieldAdBalloons[billboard.EntityId] = fieldInteract;
                break;
            default:
                fieldInteracts[interactObject.EntityId] = fieldInteract;
                break;
        }

        return fieldInteract;
    }

    /// <summary>The GM object create dialog (InteractObject 9, see DebugInteract): the object is added where the GM stands.</summary>
    public FieldInteract? AddDebugInteractObject(int interactId, string model, string asset, string normal, string reactable,
                                                 Vector3 position, Vector3 rotation) {
        if (!TableMetadata.InteractObjectTable.Entries.TryGetValue(interactId, out InteractObjectMetadata? interactObjectMetadata)) {
            return null;
        }

        int objectId = NextLocalId();
        InteractMeshObject interactObject = DebugInteract.Create(objectId, interactId, model, asset, normal, reactable, position, rotation);
        var fieldInteract = new FieldInteract(this, objectId, interactObject.EntityId, interactObjectMetadata, interactObject) {
            Transform = new Transform {
                Position = position,
                RotationAnglesDegrees = rotation,
            },
        };
        fieldChests[interactObject.EntityId] = fieldInteract;
        Broadcast(InteractObjectPacket.Add(fieldInteract.Object));
        return fieldInteract;
    }

    public FieldMobSpawn? AddMobSpawn(MapMetadataSpawn metadata, Ms2RegionSpawn regionSpawn, ICollection<int> npcIds) {
        var spawnNpcs = new WeightedSet<NpcMetadata>();
        foreach (int npcId in npcIds) {
            if (!NpcMetadata.TryGet(npcId, out NpcMetadata? npc)) {
                continue;
            }
            if (npc.Basic.Difficulty < metadata.MinDifficulty || npc.Basic.Difficulty > metadata.MaxDifficulty) {
                continue;
            }

            int spawnWeight = (int) Lua.CalcNpcSpawnWeight(npc.Basic.MainTags.Length, npc.Basic.SubTags.Length, npc.Basic.RareDegree, npc.Basic.Difficulty);
            spawnNpcs.Add(npc, spawnWeight);
        }

        var spawnPets = new WeightedSet<ItemMetadata>();
        foreach ((NpcMetadata npc, int weight) in spawnNpcs) {
            if (!metadata.PetIds.TryGetValue(npc.Id, out int petId)) {
                continue;
            }

            if (!ItemMetadata.TryGetPet(petId, out ItemMetadata? pet)) {
                continue;
            }

            spawnPets.Add(pet, weight);
        }

        if (spawnNpcs.Count <= 0) {
            logger.Warning("No valid Npcs found from: {NpcIds}", string.Join(",", npcIds));
            return null;
        }

        var fieldMobSpawn = new FieldMobSpawn(this, NextLocalId(), metadata, spawnNpcs, spawnPets) {
            Position = regionSpawn.Position,
            Rotation = regionSpawn.UseRotAsSpawnDir ? regionSpawn.Rotation : default,
        };

        fieldMobSpawns[fieldMobSpawn.ObjectId] = fieldMobSpawn;
        return fieldMobSpawn;
    }

    // The spawn points the field's combine spawn groups name (CombineSpawnRules.GroupSpawnIds), read on the first add.
    private HashSet<int>? groupSpawnIds;

    // The combine spawn groups the field's own trigger scripts start (start_combine_spawn), read only for a map the
    // table lists no group for.
    private IEnumerable<int> StartedCombineSpawnGroups() {
        if (ServerTableMetadata.CombineSpawnTable.Groups.ContainsKey(MapId) || TriggerCache == null) {
            yield break;
        }
        foreach (TriggerModel model in Entities.TriggerModels.Values) {
            if (!TriggerCache.TryGet(Metadata.XBlock, model.Name.ToLower(), out Trigger.Helpers.Trigger? trigger)) {
                continue;
            }
            foreach (int groupId in trigger.StartedCombineSpawnGroups()) {
                yield return groupId;
            }
        }
    }

    public FieldSpawnPointNpc AddSpawnPointNpc(SpawnPointNPC metadata) {
        if (groupSpawnIds == null) {
            var npcSpawns = Entities.NpcSpawns.Select(spawn => spawn.SpawnPointId).ToHashSet();
            groupSpawnIds = CombineSpawnRules.GroupSpawnIds(ServerTableMetadata.CombineSpawnTable, MapId, npcSpawns.Contains, StartedCombineSpawnGroups());
        }
        var fieldSpawnPointNpc = new FieldSpawnPointNpc(this, NextLocalId(), metadata, groupSpawnIds.Contains(metadata.SpawnPointId)) {
            Position = metadata.Position,
            Rotation = metadata.Rotation,
        };

        fieldSpawnPointNpcs[metadata.EntityId] = fieldSpawnPointNpc;
        fieldSpawnPointNpc.SpawnOnCreate();
        return fieldSpawnPointNpc;
    }

    // Set once a trigger starts a combine spawn group of another map here (a copy map such as 82000000): that map
    // has no map chests, so this field drops its own and reads no more.
    private bool combineSpawnFromOtherMap;

    public void ToggleCombineSpawn(SpawnGroupMetadata metadata, bool enable) {
        if (enable && metadata.MapId != MapId && !combineSpawnFromOtherMap) {
            combineSpawnFromOtherMap = true;
            StopMapChests();
        }
        if (!fieldSpawnGroups.TryGetValue(metadata.GroupId, out FieldSpawnGroup? fieldSpawnGroup)) {
            fieldSpawnGroup = new FieldSpawnGroup(this, NextLocalId(), metadata);
            fieldSpawnGroups[metadata.GroupId] = fieldSpawnGroup;
        }

        fieldSpawnGroup.ToggleActive(enable);
    }

    #region Map chests
    private static readonly object GoldChestLock = new();
    // Map id to the golden chest interact ids of the map's golden chest trophy, in point order.
    private static IReadOnlyDictionary<int, int[]>? goldChestIds;

    private bool mapChestsRead;
    private long nextMapChestTick;
    private List<int> woodChestPoints = [];
    private Dictionary<int, int> goldChestPoints = new();

    private static IReadOnlyDictionary<int, int[]> GoldChestIds(AchievementMetadataStorage storage) {
        lock (GoldChestLock) {
            if (goldChestIds != null) {
                return goldChestIds;
            }

            var result = new Dictionary<int, int[]>();
            foreach (AchievementMetadata achievement in storage.GetAll().OrderBy(achievement => achievement.Id)) {
                if (!MapChestRules.TryTrophyMap(achievement.Name, out int mapId) || result.ContainsKey(mapId) || achievement.Grades.Count == 0) {
                    continue;
                }
                int[] ids = MapChestRules.TrophyChestIds(achievement.Grades.OrderBy(grade => grade.Key).First().Value.Condition);
                if (ids.Length > 0) {
                    result[mapId] = ids;
                }
            }
            goldChestIds = result;
            return result;
        }
    }

    // The map's chest points. A dungeon, a home or a map whose points belong to combine spawn groups has none.
    private void ReadMapChests() {
        mapChestsRead = true;
        if (this is DungeonFieldManager or HomeFieldManager || AchievementMetadata == null
            || ServerTableMetadata.CombineSpawnTable.Groups.ContainsKey(MapId) || combineSpawnFromOtherMap) {
            return;
        }

        woodChestPoints = Entities.RegionSpawns.Keys.Where(id => MapChestRules.SpawnKind(id) == MapChestRules.Kind.Wood).ToList();
        if (GoldChestIds(AchievementMetadata).TryGetValue(MapId, out int[]? ids)) {
            goldChestPoints = MapChestRules.GoldChests(Entities.RegionSpawns.Keys, ids);
        }
    }

    // Chests are updated here: an opened one is removed once it hides. Every MapChestRules.Cycle the map
    // chests left are removed and a new set is added: every golden chest and some of the wooden ones.
    private void UpdateChests(long tickCount) {
        // Only the map treasure chests: the objects of combine spawn groups and of AI nodes stay as they were.
        foreach (FieldInteract chest in fieldChests.Values) {
            if (chest.Chest == MapChestRules.Kind.None) {
                continue;
            }
            chest.Update(tickCount);
            if (chest.State == InteractState.Hidden && fieldChests.TryRemove(chest.EntityId, out _)) {
                Broadcast(InteractObjectPacket.Remove(chest.EntityId));
            }
        }

        if (!mapChestsRead) {
            ReadMapChests();
        }
        if ((woodChestPoints.Count == 0 && goldChestPoints.Count == 0) || tickCount < nextMapChestTick) {
            return;
        }
        nextMapChestTick = tickCount + (long) MapChestRules.Cycle.TotalMilliseconds;

        foreach (FieldInteract chest in fieldChests.Values) {
            if (chest.Chest != MapChestRules.Kind.None && fieldChests.TryRemove(chest.EntityId, out _)) {
                Broadcast(InteractObjectPacket.Remove(chest.EntityId));
            }
        }
        foreach ((int spawnId, int interactId) in goldChestPoints.OrderBy(entry => entry.Key)) {
            SpawnMapChest(spawnId, interactId, MapChestRules.Kind.Gold);
        }
        foreach (int spawnId in MapChestRules.PickWood(woodChestPoints, Random.Shared)) {
            SpawnMapChest(spawnId, MapChestRules.WoodInteractId, MapChestRules.Kind.Wood);
        }
    }

    private void StopMapChests() {
        woodChestPoints = [];
        goldChestPoints = new Dictionary<int, int>();
        foreach (FieldInteract chest in fieldChests.Values) {
            if (chest.Chest != MapChestRules.Kind.None && fieldChests.TryRemove(chest.EntityId, out _)) {
                Broadcast(InteractObjectPacket.Remove(chest.EntityId));
            }
        }
    }

    private void SpawnMapChest(int spawnId, int interactId, MapChestRules.Kind kind) {
        if (!Entities.RegionSpawns.TryGetValue(spawnId, out Ms2RegionSpawn? spawn) ||
            !TableMetadata.InteractObjectTable.Entries.TryGetValue(interactId, out InteractObjectMetadata? metadata)) {
            return;
        }
        string name = MapChestRules.EntityName(spawn.Position.X, spawn.Position.Y, spawn.Position.Z);
        var interactObject = new InteractMeshObject(name, new Ms2InteractMesh(
            interactId, spawn.Position, spawn.Rotation)) {
            Model = MapChestRules.Model,
            Asset = kind == MapChestRules.Kind.Gold ? MapChestRules.GoldAsset : MapChestRules.WoodAsset,
            NormalState = MapChestRules.NormalState,
            Reactable = MapChestRules.ReactableState,
        };
        var fieldInteract = new FieldInteract(this, NextLocalId(), interactObject.EntityId, metadata, interactObject) {
            Transform = new Transform {
                Position = spawn.Position,
                RotationAnglesDegrees = spawn.Rotation,
            },
            SpawnId = spawn.Id,
            Chest = kind,
            InitialState = InteractState.Reactable,
        };
        fieldChests[interactObject.EntityId] = fieldInteract;
        Broadcast(InteractObjectPacket.Add(fieldInteract.Object));
    }
    #endregion

    public FieldNpc? SpawnWorldBoss(WorldBossMetadata metadata, int eventId) {
        if (metadata.NpcIds.Length == 0 || metadata.SpawnPointIds.Length == 0) {
            logger.Warning("[WorldBoss] Metadata {Id} has no NpcIds or SpawnPointIds", metadata.Id);
            return null;
        }

        if (metadata.Unique && worldBossObjectId != 0) {
            logger.Warning("[WorldBoss] Unique boss {Id} already present in map {MapId}, skipping spawn", metadata.Id, MapId);
            return null;
        }

        // TimeEventData.Xml only uses 1 npc and spawnpointid for each event
        if (!NpcMetadata.TryGet(metadata.NpcIds.First(), out NpcMetadata? npcMetadata)) {
            logger.Warning("[WorldBoss] NPC {NpcId} not found", metadata.NpcIds.First());
            return null;
        }

        if (!Entities.RegionSpawns.TryGetValue(metadata.SpawnPointIds.First(), out Ms2RegionSpawn? regionSpawn)) {
            logger.Warning("[WorldBoss] RegionSpawn {SpawnPointId} not found in map {MapId}", metadata.SpawnPointIds.First(), MapId);
            return null;
        }

        FieldNpc? npc = SpawnNpc(npcMetadata, regionSpawn.Position, regionSpawn.Rotation);
        if (npc == null) {
            return null;
        }

        worldBossObjectId = npc.ObjectId;
        npc.WorldBossDeathCallback = killedNpc => {
            worldBossObjectId = 0;
            // The kill notice (s_hunting_kill_boss) is the boss kill message every field boss death sends
            // (FieldNpc.SendKillNotices); a second notice here showed it twice.
            // Notify World to remove this channel from the boss's alive channel list (for world map accuracy)
            try {
                FieldFactory.World.TimeEvent(new WorldTimeEventRequest {
                    WorldBossKilled = new WorldTimeEventRequest.Types.WorldBossKilled {
                        MetadataId = metadata.Id,
                        EventId = eventId,
                        Channel = GameServer.GetChannel(),
                    },
                });
            } catch (Exception ex) {
                logger.Error(ex, "[WorldBoss] Failed to notify world of boss {MetadataId} kill on channel {Channel}", metadata.Id, GameServer.GetChannel());
            }
        };
        Broadcast(FieldPacket.AddNpc(npc));
        logger.Information("[WorldBoss] Spawned {NpcId} (objectId={ObjectId}) for event {EventId} in map {MapId}", metadata.NpcIds[0], npc.ObjectId, eventId, MapId);
        return npc;
    }

    public FieldNpc? GetWorldBossNpc() {
        if (worldBossObjectId == 0) return null;
        Mobs.TryGetValue(worldBossObjectId, out FieldNpc? npc);
        return npc;
    }

    public void DespawnWorldBoss() {
        if (worldBossObjectId == 0) {
            return;
        }

        int objectId = worldBossObjectId;
        worldBossObjectId = 0;
        RemoveNpc(objectId);
    }

    public void ToggleNpcSpawnPoint(int spawnId) {
        List<FieldSpawnPointNpc> spawns = fieldSpawnPointNpcs.Values.Where(spawn => spawn.Value.SpawnPointId == spawnId).ToList();
        foreach (FieldSpawnPointNpc spawn in spawns) {
            spawn.TriggerSpawn();
        }
    }

    public void SpawnInteractObject(SpawnInteractObjectMetadata metadata) {
        if (!Entities.RegionSpawns.TryGetValue(metadata.RegionSpawnId, out Ms2RegionSpawn? boxSpawn) ||
            !TableMetadata.InteractObjectTable.Entries.TryGetValue(metadata.InteractId, out InteractObjectMetadata? interactObjectMetadata)) {
            return;
        }
        Vector3B position = boxSpawn.Position;

        var interactObject = new InteractMeshObject($"EventCreate_{position.ConvertToInt()}", new Ms2InteractMesh(
            metadata.InteractId, boxSpawn.Position, boxSpawn.Rotation)) {
            Asset = metadata.Asset,
            Model = metadata.Model,
            NormalState = metadata.Normal,
            Reactable = metadata.Reactable,
            Scale = metadata.Scale,
        };

        var fieldInteract = new FieldInteract(this, NextLocalId(), interactObject.EntityId, interactObjectMetadata, interactObject) {
            Transform = new Transform {
                Position = boxSpawn.Position,
                RotationAnglesDegrees = boxSpawn.Rotation,
            },
            SpawnId = boxSpawn.Id,
            // The Add packet says reactable, so it starts so: it is never updated before it is opened.
            InitialState = InteractState.Reactable,
        };
        fieldChests[interactObject.EntityId] = fieldInteract;
        Broadcast(InteractObjectPacket.Add(fieldInteract.Object));
    }

    /// <summary>An AI createInteractObject node: the object is added where the npc stands and removed after lifeTime.</summary>
    public FieldInteract? AddAiInteractObject(AiMetadata.CreateInteractObjectNode node, Vector3 position, Vector3 rotation) {
        if (!TableMetadata.InteractObjectTable.Entries.TryGetValue(node.InteractID, out InteractObjectMetadata? interactObjectMetadata)) {
            logger.Warning("CreateInteractObject: no interact object {Id}", node.InteractID);
            return null;
        }

        int objectId = NextLocalId();
        InteractMeshObject interactObject = AiInteract.Create(objectId, node, position, rotation);
        var fieldInteract = new FieldInteract(this, objectId, interactObject.EntityId, interactObjectMetadata, interactObject) {
            Transform = new Transform {
                Position = position,
                RotationAnglesDegrees = rotation,
            },
            // As a combine spawn group's object: the Add packet says reactable.
            InitialState = InteractState.Reactable,
        };
        fieldChests[interactObject.EntityId] = fieldInteract;
        Broadcast(InteractObjectPacket.Add(fieldInteract.Object));

        TimeSpan lifeTime = AiInteract.LifeTime(node.LifeTime);
        if (lifeTime > TimeSpan.Zero) {
            Scheduler.Schedule(() => {
                if (fieldChests.TryRemove(interactObject.EntityId, out _)) {
                    Broadcast(InteractObjectPacket.Remove(interactObject.EntityId));
                }
            }, lifeTime);
        }
        return fieldInteract;
    }

    public void AddSkill(Ms2TriggerSkill triggerSkill, int interval, in Vector3 position, in Vector3 rotation = default, int triggerId = 0) {
        if (!SkillMetadata.TryGet(triggerSkill.SkillId, triggerSkill.Level, out SkillMetadata? skillMetadata)) {
            logger.Warning("Invalid skill: {Id}", triggerSkill.SkillId);
            return;
        }

        var fieldSkill = new FieldSkill(this, NextLocalId(), FieldActor, skillMetadata, interval, triggerSkill.Count, position) {
            Position = position,
            Rotation = rotation,
            TriggerId = triggerId,
        };

        fieldSkills[fieldSkill.ObjectId] = fieldSkill;
        Broadcast(RegionSkillPacket.Add(fieldSkill));
    }

    public void AddSkill(SkillMetadata metadata, int interval, in Vector3 position, in Vector3 rotation = default, int triggerId = 0) {
        var fieldSkill = new FieldSkill(this, NextLocalId(), FieldActor, metadata, interval, position) {
            Position = position,
            Rotation = rotation,
            TriggerId = triggerId,
        };

        fieldSkills[fieldSkill.ObjectId] = fieldSkill;
        Broadcast(RegionSkillPacket.Add(fieldSkill));
    }

    public void AddSkill(IActor caster, SkillEffectMetadata effect, Vector3[] points, in Vector3 rotation = default) {
        Debug.Assert(effect.Splash != null, "Cannot add non-splash skill to field");

        foreach (SkillEffectMetadata.Skill skill in effect.Skills) {
            short level = SkillCastRules.LinkedLevel(caster, skill);
            if (!SkillMetadata.TryGet(skill.Id, level, out SkillMetadata? metadata)) {
                continue;
            }

            int fireCount = effect.FireCount > 0 ? effect.FireCount : -1;
            var fieldSkill = new FieldSkill(this, NextLocalId(), caster, metadata, fireCount, effect.Splash, points) {
                Position = points[0],
                Rotation = rotation,
            };

            fieldSkills[fieldSkill.ObjectId] = fieldSkill;
            Broadcast(RegionSkillPacket.Add(fieldSkill));
        }
    }

    // The caster's attack that names skills in splashActiveSkillID hit (Actor.TargetAttack): every region of those
    // skills the caster placed that fires only when made to fires once (FieldSkill.Invoke). Returns how many fired.
    public int InvokeSplashes(IActor caster, IReadOnlyCollection<int> skillIds) {
        int fired = 0;
        foreach (FieldSkill fieldSkill in fieldSkills.Values) {
            if (fieldSkill.ManualOnly && fieldSkill.Caster.ObjectId == caster.ObjectId && skillIds.Contains(fieldSkill.Value.Id)
                && fieldSkill.Invoke()) {
                fired++;
            }
        }
        return fired;
    }

    public void AddSkill(SkillRecord record) {
        SkillMetadataAttack attack = record.Attack;
        if (!TableMetadata.MagicPathTable.Entries.TryGetValue(attack.CubeMagicPathId, out IReadOnlyList<MagicPath>? cubeMagicPaths)) {
            logger.Error("No CubeMagicPath found for {CubeMagicPath})", attack.CubeMagicPathId);
            return;
        }

        Vector3[] cubePoints;
        if (attack.CubeMagicPathId != 0) {
            // TODO: If this is a CubeMagicPath, we always align the height. Ideally we can detect the floor instead.
            record.Position = record.Position.AlignHeight();
            cubePoints = new Vector3[cubeMagicPaths.Count];
            for (int i = 0; i < cubeMagicPaths.Count; i++) {
                MagicPath magicPath = cubeMagicPaths[i];
                Vector3 rotation = default;
                if (magicPath.Rotate) {
                    rotation = record.Rotation;
                }

                Vector3 position = record.Position + magicPath.FireOffset.Rotate(rotation);
                cubePoints[i] = magicPath.IgnoreAdjust ? position : position.Align();
            }
        } else {
            cubePoints = [record.Position];
        }

        // Condition-Skills are expected to be handled separately.
        foreach (SkillEffectMetadata effect in attack.Skills.Where(effect => effect.Splash != null)) {
            if (effect.Splash == null) {
                logger.Fatal("Invalid Splash-Skill being handled: {Effect}", effect);
                continue;
            }

            AddSkill(record.Caster, effect, cubePoints, record.Rotation);
        }
    }

    private static bool FightsNpc(FieldNpc attacker, FieldNpc target) {
        NpcMetadataBasic own = attacker.Value.Metadata.Basic;
        NpcMetadataBasic other = target.Value.Metadata.Basic;
        return !target.IsScriptMarker && NpcTargetRules.FightsNpc(own.Friendly, own.AttackGroup, other.Friendly, other.DefenseGroup);
    }

    // The players a skill can reach: every player but a rank duel's onlookers.
    private IEnumerable<FieldPlayer> TargetPlayers => Players.Values.Where(player => !IsOnlooker(player));

    private bool IsOnlooker(FieldPlayer player) => RankDuel?.IsOnlooker(player.Value.Character.Id) == true;

    // The living actors on the center's hostile or friendly side (as its skills pick them, GetTargetPool) whose
    // position lies between minRange and maxRange of its own; the center itself is not counted (chosen here).
    public int CountAround(IActor center, float minRange, float maxRange, bool hostile) {
        Vector3 origin = center.Position;
        var prism = new Prism(new Circle(new Vector2(origin.X, origin.Y), maxRange), origin.Z - maxRange, maxRange * 2);
        return GetTargetPool(center, [prism], hostile ? ApplyTargetType.Hostile : ApplyTargetType.Friendly, int.MaxValue, [center])
            .Count(actor => !actor.IsDead && actor.ObjectId != center.ObjectId &&
                            Vector3.Distance(origin, actor.Position) is var distance && distance >= minRange && distance <= maxRange);
    }

    private IEnumerable<IActor> GetTargetPool(IActor caster, Prism[] prisms, ApplyTargetType targetType, int limit, ICollection<IActor>? ignore) {
        // A rank duel's onlookers take no part: their skills reach no one, and no one's reach them.
        if (caster is FieldPlayer onlooker && IsOnlooker(onlooker)) {
            return [];
        }
        // A friendly npc (e.g. a cannon a player fires by InteractNpc) sides with the players: its hostile
        // targets are the mobs it fights (NpcTargetRules.FightsNpc) and its friendly targets the players.
        if (caster is FieldNpc { Value.Metadata.Basic.Friendly: > 0 } friendlyCaster and not FieldPet) {
            switch (targetType) {
                case ApplyTargetType.Hostile:
                    return prisms.Filter(Mobs.Values.Where(mob => FightsNpc(friendlyCaster, mob)), limit, ignore);
                case ApplyTargetType.Friendly:
                    return prisms.Filter(TargetPlayers, limit, ignore);
            }
        }
        // A player's buffs may widen what the skill takes (forceApplyTarget, as the client's target filter
        // FilterTarget ORs it into the query): bit 2 adds the friendly players to a hostile skill (a boss's charm
        // makes the holder hit its allies), bit 1 adds the mobs to a friendly one.
        int force = caster is FieldPlayer { Buffs: not null } forcePlayer ? forcePlayer.Buffs.ForceApplyTarget() : 0;
        switch (targetType) {
            case ApplyTargetType.Friendly:
                if (caster is FieldNpc) {
                    return prisms.Filter(Mobs.Values, limit, ignore);
                } else if (caster is FieldPlayer friendlyPlayer) {
                    // The client's friendly filter (FilterTarget, basecollision.cpp:784) takes a player only while the
                    // caster's hostility test (PC::vfunc_122) gives 0: on a pk field an enemy gets no heal. Chosen here: in a
                    // field game off a pk field the friendly pool stays every player (the client test there is not the game's).
                    IEnumerable<FieldPlayer> friends = PkField
                        ? TargetPlayers.Where(player => !PlayerHostile(friendlyPlayer, player))
                        : TargetPlayers;
                    if ((force & 1) != 0) {
                        return prisms.Filter(friends.Cast<IActor>().Concat(Mobs.Values), limit, ignore);
                    }
                    return prisms.Filter(friends, limit, ignore);
                }
                Log.Debug("Unhandled ApplyTargetType:{Entity} for {caster.GetType()}", targetType, caster.GetType());
                return [];
            case ApplyTargetType.Hostile:
                if (caster is FieldPet) {
                    return prisms.Filter(TargetPlayers, limit, ignore);
                }
                if (caster is FieldNpc monster) {
                    // The players and the friendly npcs the monster fights (a barrier a script waits on to fall).
                    return prisms.Filter(TargetPlayers.Cast<IActor>().Concat(Npcs.Values.Where(npc => FightsNpc(monster, npc))), limit, ignore);
                } else if (caster is FieldPlayer casterPlayer) {
                    if ((force & 2) != 0) {
                        return prisms.Filter(Mobs.Values.Cast<IActor>().Concat(TargetPlayers), limit, ignore);
                    }
                    // In a rank duel's fight or a PvP zone the other side's players are hostile too; in Hide and Seek
                    // the seekers' skills reach the hiders; on a pk field the players the pk rule makes hostile.
                    if (FieldGameRuns || PkField) {
                        return prisms.Filter(Mobs.Values.Cast<IActor>()
                            .Concat(TargetPlayers.Where(player => PlayerHostile(casterPlayer, player))), limit, ignore);
                    }
                    return prisms.Filter(Mobs.Values, limit, ignore);
                }
                Log.Debug("Unhandled ApplyTargetType:{Entity} for {caster.GetType()}", targetType, caster.GetType());
                return [];
            case ApplyTargetType.HungryMobs:
                return prisms.Filter(Pets.Values.Where(pet => pet.OwnerId == 0), limit, ignore);
            case ApplyTargetType.RegionBuff:
            case ApplyTargetType.RegionBuff2:
                return prisms.Filter(TargetPlayers, limit, ignore);
            default:
                Log.Debug("Unhandled SkillEntity:{Entity}", targetType);
                return [];
        }
    }

    public IEnumerable<IActor> GetTargets(IActor caster, Prism[] prisms, SkillMetadataRange range, int targetCount, ICollection<IActor>? ignore = null, TargetSort? sort = null) {
        if (targetCount <= 0) {
            return [];
        }

        // Caster is always excluded from the pool; re-added explicitly per IncludeCaster semantics
        ICollection<IActor> poolIgnore = ignore != null ? [.. ignore, caster] : [caster];

        switch (range.IncludeCaster) {
            case IncludeCasterType.Priority: {
                    // Caster guaranteed as first target; pool fills remaining slots
                    IActor[] pool = SelectTargets(caster, prisms, range, targetCount - 1, poolIgnore, sort).ToArray();
                    return Enumerable.Repeat<IActor>(caster, 1).Concat(pool);
                }
            case IncludeCasterType.Last: {
                    // Pool fills all slots; caster appended only if fewer than targetCount were found
                    IActor[] pool = SelectTargets(caster, prisms, range, targetCount, poolIgnore, sort).ToArray();
                    return pool.Length < targetCount ? pool.Concat(Enumerable.Repeat<IActor>(caster, 1)) : pool;
                }
            default: // Exclude
                return SelectTargets(caster, prisms, range, targetCount, poolIgnore, sort);
        }
    }

    // The range's pool in its prisms; with a target choice (Select) or a search frame (sort) the whole pool is
    // filtered and put in order before the target count is taken, as the client's search does.
    private IEnumerable<IActor> SelectTargets(IActor caster, Prism[] prisms, SkillMetadataRange range, int limit, ICollection<IActor>? ignore, TargetSort? sort) {
        if (!range.Select.Active() && sort == null && !range.Filters()) {
            return GetTargetPool(caster, prisms, range.ApplyTarget, limit, ignore);
        }
        IEnumerable<IActor> pool = GetTargetPool(caster, prisms, range.ApplyTarget, int.MaxValue, ignore)
            .Where(target => !RangeTargetSelect.Excludes(range, target));
        if (!range.Select.Active() && sort == null) {
            return pool.Take(limit);
        }
        return RangeTargetSelect.Apply(range.Select, caster, pool, range.Type, sort).Take(limit);
    }

    public IEnumerable<IActor> GetTargets(SkillRecord record, ICollection<IActor>? ignore = null) {
        Prism[] prisms = [record.Attack.GetPrism(record.ImpactPosition, record.Rotation.Z)];
        TargetSort sort = record.Attack.GetTargetSort(record.ImpactPosition, record.Rotation.Z);
        return GetTargets(record.Caster, prisms, record.Attack.Range, record.Attack.TargetCount, ignore, sort);
    }

    // An arrow step's hits (StateSkill FUN_1417e9d20, DetectWithMatrixRotation): the range's actors in the query box
    // but the caster, less the ones the step leaves out (hit already, or off the step's own box), then the range's
    // target choice, once, and the step's order, as the client's search applies its exclusion before both.
    public List<IActor> GetStepTargets(IActor caster, Prism query, SkillMetadataRange range, Func<IActor, bool> hits, TargetSort sort) {
        IEnumerable<IActor> pool = GetTargetPool(caster, [query], range.ApplyTarget, int.MaxValue, [caster])
            .Where(target => target.ObjectId != caster.ObjectId && hits(target) && !RangeTargetSelect.Excludes(range, target));
        return RangeTargetSelect.Apply(range.Select, caster, pool, SkillRegion.Box, sort).ToList();
    }

    // The client's bounce search: a cylinder of the arrow's bounce radius, 150 high, around the last hit; nearest first.
    private const float BounceSearchHeight = 150f;

    public IReadOnlyList<IActor> GetBounceTargets(SkillRecord record, IActor from) {
        Vector3 origin = from.Position;
        var prism = new Prism(new Circle(new Vector2(origin.X, origin.Y), record.Attack.Arrow.BounceRadius), origin.Z, BounceSearchHeight);
        return GetTargetPool(record.Caster, [prism], record.Attack.Range.ApplyTarget, int.MaxValue, [record.Caster])
            .Where(target => !RangeTargetSelect.Excludes(record.Attack.Range, target))
            .OrderBy(target => Vector3.DistanceSquared(target.Position, origin))
            .ToList();
    }

    public void RemoveSkill(int objectId) {
        if (fieldSkills.Remove(objectId, out _)) {
            Broadcast(RegionSkillPacket.Remove(objectId));
        }
    }

    public void RemoveSkillByTriggerId(int triggerId) {
        foreach (FieldSkill fieldSkill in fieldSkills.Values.Where(skill => skill.TriggerId == triggerId)) {
            fieldSkills.Remove(fieldSkill.ObjectId, out _);
            Broadcast(RegionSkillPacket.Remove(fieldSkill.ObjectId));
        }
    }

    // Cube skills are server-side only and do not need to be broadcast to clients.
    // They are static map hazards that persist for the lifetime of the field.
    private void AddCubeSkill(SkillMetadata metadata, in Vector3 position, in Vector3 rotation = default) {
        Vector3 adjustedPosition = position;
        adjustedPosition.Z += FieldAccelerationStructure.BLOCK_SIZE;
        var fieldSkill = new FieldSkill(this, NextLocalId(), FieldActor, metadata, (int) Constants.GlobalCubeSkillIntervalTime.TotalMilliseconds, adjustedPosition) {
            Position = adjustedPosition,
            Rotation = rotation,
            Source = SkillSource.Cube,
            CubePosition = SplashTiming.TilePoint(position),
        };
        cubeSkills[fieldSkill.ObjectId] = fieldSkill;
    }
    #endregion

    public void AddFieldProperty(IFieldProperty fieldProperty) {
        fieldProperties[fieldProperty.Type] = fieldProperty;
        Broadcast(FieldPropertyPacket.Add(fieldProperty));
    }

    public void RemoveFieldProperty(FieldProperty fieldProperty) {
        fieldProperties.Remove(fieldProperty, out _);
        Broadcast(FieldPropertyPacket.Remove(fieldProperty));
    }

    public IFieldProperty GetFieldProperty(FieldProperty type) {
        if (fieldProperties.TryGetValue(type, out IFieldProperty? fieldProperty)) {
            return fieldProperty;
        }
        fieldProperty = new FieldPropertySightRange();
        AddFieldProperty(fieldProperty);
        return fieldProperty;
    }

    public void SetBackground(string ddsPath) {
        background = ddsPath;
        Broadcast(FieldPropertyPacket.Background(background));
    }

    private void SetBonusMapPortal(IList<MapMetadata> bonusMaps, Ms2RegionSpawn spawn) {
        // Spawn a hat within a random range of 5 min to 8 hours
        var delay = Random.Shared.Next(1, 97) * TimeSpan.FromMinutes(5);
        MapMetadata bonusMapMetadata = bonusMaps[Random.Shared.Next(bonusMaps.Count)];
        IField? bonusMap = FieldFactory.Create(bonusMapMetadata.Id);
        bonusMap?.Init();
        Console.WriteLine($"Creating bonus map {bonusMapMetadata.Id} at {spawn.Position} in {delay} ms.");
        if (bonusMap == null) {
            return;
        }
        bonusMap.SetRoomTimer(RoomTimerType.Clock, 90000);
        var portal = new Portal(NextLocalId(), bonusMapMetadata.Id, -1, PortalType.Event, PortalActionType.Interact, spawn.Position, spawn.Rotation,
            new Vector3(200, 200, 250), 0, 0, true, false, true);
        FieldPortal fieldPortal = SpawnPortal(portal, bonusMap.RoomId);
        fieldPortal.Model = Metadata.Property.Continent switch {
            Continent.VictoriaIsland => "Eff_event_portal_A01",
            Continent.KarkarIsland => "Eff_kr_sandswirl_01",
            Continent.ShadowWorld => "Eff_uw_potral_A01",
            Continent.Kritias => "Eff_ks_magichole_portal_A01",
            _ => "Eff_event_portal_A01",
        };
        fieldPortal.EndTick = (FieldTick + (long) TimeSpan.FromSeconds(30).TotalMilliseconds).Truncate32();
        Broadcast(PortalPacket.Add(fieldPortal));
        Scheduler.Schedule(() => SetBonusMapPortal(bonusMaps, spawn), delay);
    }

    /// <summary>
    /// An AI createRandomRoom node (the ShadowGate npcs, at their death): an Event portal at the npc for
    /// durationSeconds, to a room of the randomroom row. The room itself is made at the first entry
    /// (UseRandomRoomPortal).
    /// </summary>
    public FieldPortal? SpawnRandomRoomPortal(int randomRoomId, int durationSeconds, Vector3 position, Vector3 rotation) {
        RoomRandomTable table = ServerTableMetadata.RoomRandomTable;
        if (!table.RandomRooms.TryGetValue(randomRoomId, out RandomRoomMetadata? randomRoom)) {
            logger.Warning("createRandomRoom: no randomroom {Id} in room_random.xml", randomRoomId);
            return null;
        }
        if (!RandomRoomRules.Opens(randomRoom.Prob, Random.Shared.Next(RandomRoomRules.ProbScale))) {
            return null;
        }

        int totalWeight = RandomRoomRules.TotalWeight(randomRoom.Refs);
        int? roomId = totalWeight > 0 ? RandomRoomRules.PickRoom(randomRoom.Refs, Random.Shared.Next(totalWeight)) : null;
        if (roomId is null || !table.Rooms.TryGetValue(roomId.Value, out RandomRoomFieldMetadata? room) || room.FieldIds.Length == 0) {
            logger.Warning("createRandomRoom: randomroom {Id} gives no room with a field ({RoomId})", randomRoomId, roomId);
            return null;
        }

        // Every shipped room has one field; with more, one is picked.
        int mapId = room.FieldIds[Random.Shared.Next(room.FieldIds.Length)];
        var portal = new Portal(NextLocalId(), mapId, -1, PortalType.Event, PortalActionType.Interact, position, rotation,
            new Vector3(200, 200, 250), 0, 0, true, false, true);
        FieldPortal fieldPortal = SpawnPortal(portal);
        fieldPortal.Model = room.AssetName;
        fieldPortal.EndTick = (FieldTick + (long) TimeSpan.FromSeconds(Math.Max(durationSeconds, 1)).TotalMilliseconds).Truncate32();
        fieldPortal.RandomRoom = room;
        Broadcast(PortalPacket.Add(fieldPortal));
        return fieldPortal;
    }

    public void SetRoomTimer(RoomTimerType type, int duration) {
        SetRoomTimer(type, duration, expiresRoom: true);
    }

    public void SetRoomTimer(RoomTimerType type, int duration, bool expiresRoom) {
        RoomTimer = new RoomTimer(this, type, duration, expiresRoom);
    }

    public void AddHongBao(FieldPlayer owner, int sourceItemId, int itemId, int totalUser, int durationSec, int itemCount) {
        var hongBao = new HongBao(this, owner, sourceItemId, NextLocalId(), itemId, totalUser, FieldTick, durationSec, itemCount);
        if (!hongBaos.TryAdd(hongBao.ObjectId, hongBao)) {
            logger.Error("Failed to add hongbao {ObjectId}", hongBao.ObjectId);
            return;
        }

        Item? item = hongBao.Claim(owner);
        if (item == null) {
            return;
        }
        if (!owner.Session.Item.Inventory.Add(item, true)) {
            owner.Session.Item.MailItem(item);
        }
        Broadcast(PlayerHostPacket.UseHongBao(hongBao, item.Amount));
    }

    // False when the cell already holds a reactor (the client checks the same by name).
    // ownerLimit: how many reactors of one character may stand in the field at once.
    public bool TryAddReactor(ReactorTable.Entry metadata, Vector3 position, long ownerId, int ownerLimit, [NotNullWhen(true)] out FieldReactor? reactor) {
        (int X, int Y, int Z) cell = ReactorRules.Cell(position);
        string name = ReactorRules.Name(ReactorRules.CellKey(cell));
        Vector3 center = ReactorRules.CellCenter(cell);
        reactor = null;
        lock (reactorAddLock) {
            if (metadata.States.Length == 0 || fieldReactors.Values.Any(other => other.Name == name)) {
                return false;
            }
            if (fieldReactors.Count >= ReactorRules.MaxPerField || fieldReactors.Values.Count(other => other.OwnerId == ownerId) >= ownerLimit) {
                return false;
            }
            if (!CanPlaceReactor(center)) {
                return false;
            }

            reactor = new FieldReactor(this, NextLocalId(), name, metadata, center, default, ownerId);
            if (!fieldReactors.TryAdd(reactor.ObjectId, reactor)) {
                reactor = null;
                return false;
            }
        }

        Broadcast(ReactorPacket.Add(reactor.ObjectId, reactor.Name, metadata.Id, reactor.Position, reactor.Rotation,
            reactor.StateIndex, reactor.StateTime, reactor.Connected));
        return true;
    }

    // The placement tests of the client's CheckReactorPlacement that the server can repeat.
    private bool CanPlaceReactor(Vector3 center) {
        IEnumerable<Vector3> interacts = fieldInteracts.Values.Select(interact => interact.Position)
            .Concat(fieldFunctionInteracts.Values.Select(interact => interact.Position))
            .Concat(fieldAdBalloons.Values.Select(interact => interact.Position))
            .Concat(fieldChests.Values.Select(interact => interact.Position));
        if (!ReactorRules.IsClear(center, interacts)) {
            return false;
        }
        // Friendly NPCs (kind 2) that are alive, by the centre of their own cell.
        IEnumerable<Vector3> npcs = Npcs.Values
            .Where(npc => !npc.IsDead && npc.Value.Metadata.Basic.Friendly == 2)
            .Select(npc => ReactorRules.CellCenter(ReactorRules.Cell(npc.Position)));
        if (!ReactorRules.IsClear(center, npcs)) {
            return false;
        }
        if (fieldPortals.Values.Any(portal => ReactorRules.InBox(center, portal.Position, portal.Value.Dimension))) {
            return false;
        }
        // The client samples the ground under the cell; the server asks for walkable ground there.
        return ValidPosition(center);
    }

    public void RemoveReactor(int objectId) {
        if (fieldReactors.TryRemove(objectId, out _)) {
            Broadcast(ReactorPacket.Remove(objectId));
        }
    }

    public bool TryGetReactor(int objectId, [NotNullWhen(true)] out FieldReactor? reactor) {
        return fieldReactors.TryGetValue(objectId, out reactor);
    }

    public void RemoveHongBao(int objectId) {
        hongBaos.TryRemove(objectId, out _);
    }

    public bool TryGetHongBao(int objectId, [NotNullWhen(true)] out HongBao? hongBao) {
        return hongBaos.TryGetValue(objectId, out hongBao);
    }


    #region Player Managed
    // GuideObject is not added to the field, it will be managed by |GameSession.State|
    public FieldGuideObject SpawnGuideObject(IActor<Player> owner, IGuideObject guideObject, Vector3 position = default) {
        if (position == default) {
            position = owner.Position;
        }
        var fieldGuideObject = new FieldGuideObject(this, NextLocalId(), guideObject) {
            CharacterId = owner.Value.Character.Id,
            Position = position,
            // rotation?
        };

        return fieldGuideObject;
    }

    public FieldInstrument SpawnInstrument(IActor<Player> owner, InstrumentMetadata instrument) {
        var fieldInstrument = new FieldInstrument(this, NextLocalId(), instrument) {
            OwnerId = owner.ObjectId,
            Position = owner.Position + new Vector3(0, 0, 1),
            Rotation = owner.Rotation,
        };

        fieldInstruments[fieldInstrument.ObjectId] = fieldInstrument;

        return fieldInstrument;
    }

    public FieldPortal SpawnEventPortal(FieldPlayer player, int fieldId, int portalDurationTick, string password, int maxCount) {
        var portal = new Portal(NextLocalId(), fieldId, -1, PortalType.Event, PortalActionType.Interact, player.Position, player.Rotation, new Vector3(200, 200, 250), 0, 0, true, false, true);
        FieldPortal fieldPortal = SpawnPortal(portal);
        fieldPortal.Model = "Eff_Com_Portal_E";
        fieldPortal.Password = password;
        fieldPortal.OwnerName = player.Value.Character.Name;
        fieldPortal.EndTick = (int) (Environment.TickCount64 + portalDurationTick);
        fieldPortal.MassiveMaxCount = maxCount;
        Broadcast(PortalPacket.Add(fieldPortal));
        return fieldPortal;
    }

    // plot: the plot the cube is placed in, which owns a mannequin's items; without it no mannequin is set up.
    public FieldFunctionInteract? AddFieldFunctionInteract(PlotCube cube, Plot? plot = null) {
        if (cube.Interact == null) {
            return null;
        }
        var fieldInteract = new FieldFunctionInteract(this, NextLocalId(), cube.Interact, cube.Id) {
            Position = cube.Position,
            Rotation = new Vector3(0, 0, cube.Rotation),
            ItemId = cube.ItemId,
        };

        fieldFunctionInteracts[cube.Interact.Id] = fieldInteract;

        Broadcast(FunctionCubePacket.AddFunctionCube(cube.Interact));

        if (plot != null && FittingDollRules.Allowed(plot.MapId) && cube.Interact.Metadata.FittingDoll is { } dollData) {
            AddFittingDoll(cube, plot, dollData);
        }

        return fieldInteract;
    }

    public bool RemoveFieldFunctionInteract(string entityId) {
        if (!fieldFunctionInteracts.TryRemove(entityId, out FieldFunctionInteract? removed)) {
            return false;
        }
        if (fittingDolls.TryRemove(removed.CubeId, out FieldFittingDoll? doll)) {
            // A put on or take off that holds the lock finishes first; one that waits for it finds the doll gone.
            lock (doll.Lock) {
                doll.Removed = true;
            }
            Broadcast(FittingDollPacket.Remove(doll));
        }
        return true;
    }

    #region Fitting dolls
    private readonly ConcurrentDictionary<long, FieldFittingDoll> fittingDolls = new();

    private void AddFittingDoll(PlotCube cube, Plot plot, FunctionCubeMetadata.FittingDollData data) {
        var doll = new FieldFittingDoll(NextLocalId(), cube, plot, data);
        using (GameStorage.Request db = GameStorage.Context()) {
            foreach (Item item in db.GetItemGroups(cube.Id, ItemGroup.FittingDoll).GetValueOrDefault(ItemGroup.FittingDoll) ?? []) {
                doll.Items[(EquipSlot) item.Slot] = item;
            }
        }
        fittingDolls[cube.Id] = doll;
        Broadcast(FittingDollPacket.Load(doll, doll.Items.Values));
    }

    public FieldFittingDoll? GetFittingDoll(long cubeUid) => fittingDolls.GetValueOrDefault(cubeUid);
    #endregion

    #region Maids
    // A placed maid by cube uid and its npc's object id, 0 until the field thread has spawned the npc.
    private sealed class MaidSlot(Maid maid) {
        public Maid Maid { get; } = maid;
        public int NpcObjectId;
    }
    private readonly ConcurrentDictionary<long, MaidSlot> maids = new();

    // The maid of a maid cube placed on plot: its row, made on the first placement, and its npc on the cube. The
    // slot is held at once; the npc is spawned on the field thread (the crowd is not thread safe). The FieldMaid
    // add goes first: the client looks the npc's maid up by the maid uid of its FieldAddNpc.
    // announce: the maid is new to its owner's list under this uid (moved from its storage item), as a hired one is.
    public void AddMaid(PlotCube cube, Plot plot, bool announce = false) {
        int maidId = MaidRules.MaidId(cube.Metadata, plot.IsPlanner);
        if (maidId == 0 || maids.ContainsKey(cube.Id)) {
            return;
        }
        if (!TableMetadata.MaidTable.Entries.TryGetValue(maidId, out MaidTable.Entry? entry) || !NpcMetadata.TryGet(entry.NpcId, out NpcMetadata? npc)) {
            logger.Warning("No maid data for maid {MaidId} of cube {CubeUid}", maidId, cube.Id);
            return;
        }
        // The client draws a maid as a friendly npc; a hostile one would be a mob that can be hit and killed.
        if (npc.Basic.Friendly <= 0) {
            logger.Warning("Maid npc {NpcId} of maid {MaidId} is not friendly, not placed", npc.Id, maidId);
            return;
        }

        Maid? maid;
        bool hired = false;
        using (GameStorage.Request db = GameStorage.Context()) {
            maid = db.GetMaid(cube.Id);
            if (maid == null) {
                maid = db.CreateMaid(MaidRules.Hire(cube, maidId, plot.OwnerId, DateTime.UtcNow.ToEpochSeconds(), Constants.PeriodOfMaidEmployment));
                hired = maid != null;
            }
        }
        if (maid == null) {
            // Another field may have made the row first (the create then fails on its key): read it again.
            using GameStorage.Request db = GameStorage.Context();
            maid = db.GetMaid(cube.Id);
        }
        if (maid == null) {
            logger.Warning("Failed to load the maid of cube {CubeUid}", cube.Id);
            return;
        }

        var slot = new MaidSlot(maid);
        if (!maids.TryAdd(cube.Id, slot)) {
            return;
        }
        Vector3 position = cube.Position;
        var rotation = new Vector3(0, 0, cube.Rotation);
        Scheduler.Schedule(() => {
            if (!maids.TryGetValue(slot.Maid.Uid, out MaidSlot? current) || current != slot) {
                return; // removed before the field thread got to it
            }
            // No AI: the maid stands on its furnishing.
            FieldNpc? fieldNpc = SpawnNpc(npc, position, rotation, disableAi: true, maidUid: slot.Maid.Uid);
            if (fieldNpc == null) {
                maids.TryRemove(new KeyValuePair<long, MaidSlot>(slot.Maid.Uid, slot));
                return;
            }
            slot.NpcObjectId = fieldNpc.ObjectId;
            Broadcast(FieldMaidPacket.Add(slot.Maid));
            Broadcast(FieldPacket.AddNpc(fieldNpc));
            Broadcast(ProxyObjectPacket.AddNpc(fieldNpc));
            if (hired || announce) {
                SendToMaidOwner(slot.Maid.OwnerId, UserMaidPacket.Add(slot.Maid));
                if (MaidRules.IsCrafting(slot.Maid)) {
                    SendToMaidOwner(slot.Maid.OwnerId, MaidCraftItemPacket.Add(slot.Maid));
                }
            }
        });
    }

    // The npc goes first, then the maid, in one field-thread action: the client keeps no npc that names a gone maid.
    public void RemoveMaid(long cubeUid) {
        if (!maids.TryRemove(cubeUid, out MaidSlot? removed)) {
            return;
        }
        Scheduler.Schedule(() => {
            if (removed.NpcObjectId == 0) {
                return; // never spawned, so never sent
            }
            RemoveNpcNow(removed.NpcObjectId);
            Broadcast(FieldMaidPacket.Remove(cubeUid));
        });
        // The owner's maid windows drop the cube uid (a picked-up maid comes back under its storage item uid).
        SendToMaidOwner(removed.Maid.OwnerId, UserMaidPacket.Remove(cubeUid));
        if (MaidRules.IsCrafting(removed.Maid)) {
            SendToMaidOwner(removed.Maid.OwnerId, MaidCraftItemPacket.Remove(cubeUid));
        }
    }

    // Removes the maids whose cube is no longer on any plot of the field (a plot forfeited or expired, its cubes
    // swapped out by a reload).
    public void SyncMaids() {
        IEnumerable<long> cubeIds = Plots.Values.SelectMany(plot => plot.Cubes.Values).Select(cube => cube.Id);
        foreach (long cubeUid in MaidRules.Orphans(maids.Keys, cubeIds)) {
            RemoveMaid(cubeUid);
        }
    }

    // Removes the maids of plot's cubes (the plot is forfeited while its cubes are still in memory).
    public void RemoveMaids(Plot plot) {
        foreach (PlotCube cube in plot.Cubes.Values) {
            RemoveMaid(cube.Id);
        }
    }

    public Maid? GetMaid(long cubeUid) => maids.TryGetValue(cubeUid, out MaidSlot? slot) ? slot.Maid : null;

    // The object id of a placed maid's npc, 0 while it is not spawned.
    public int GetMaidNpcObjectId(long cubeUid) => maids.TryGetValue(cubeUid, out MaidSlot? slot) ? slot.NpcObjectId : 0;

    // Saves a placed maid after a change on the field thread and sends it again: FieldMaid add to the field (the npc
    // dialog and the name read it), UserMaid update to its owner when the owner is here (the maid windows read it).
    // The caller has changed the maid on the field thread.
    public bool SaveMaid(Maid maid) {
        using (GameStorage.Request db = GameStorage.Context()) {
            if (!db.SaveMaid(maid)) {
                logger.Warning("Failed to save maid {MaidUid}", maid.Uid);
                return false;
            }
        }
        if (GetMaidNpcObjectId(maid.Uid) != 0) {
            Broadcast(FieldMaidPacket.Add(maid));
        }
        SendToMaidOwner(maid.OwnerId, UserMaidPacket.Update(maid));
        return true;
    }

    // Changes the running craft of a placed maid (recipe 0: none) from the one this field's copy holds, as one
    // update that tests the stored craft: another channel may have the same home open with its own copy of the
    // maid. True when the row changed; the copy then holds the new craft. False when it did not (another channel
    // changed the craft first, or the row is gone): the copy then holds the stored craft, read again.
    public bool SetMaidCraft(Maid maid, int recipeId, long startTime, int duration) {
        using GameStorage.Request db = GameStorage.Context();
        if (db.SetMaidCraft(maid.Uid, maid.CraftRecipeId, maid.CraftStartTime, recipeId, startTime, duration)) {
            (maid.CraftRecipeId, maid.CraftStartTime, maid.CraftDuration) = (recipeId, startTime, duration);
            return true;
        }
        (int RecipeId, long StartTime, int Duration)? stored = db.GetMaidCraft(maid.Uid);
        if (stored == null) {
            logger.Warning("Failed to set the craft of maid {MaidUid}: no row", maid.Uid);
        } else {
            (maid.CraftRecipeId, maid.CraftStartTime, maid.CraftDuration) = stored.Value;
        }
        return false;
    }

    // Reads the stored craft into this field's copy of a placed maid: the copy is loaded with the home and does
    // not see a craft another channel started since. False when the row is gone or cannot be read; the copy
    // then stays as it is.
    public bool ReloadMaidCraft(Maid maid) {
        using GameStorage.Request db = GameStorage.Context();
        (int RecipeId, long StartTime, int Duration)? stored = db.GetMaidCraft(maid.Uid);
        if (stored == null) {
            return false;
        }
        (maid.CraftRecipeId, maid.CraftStartTime, maid.CraftDuration) = stored.Value;
        return true;
    }

    private void SendToMaidOwner(long ownerId, ByteWriter packet) {
        foreach (FieldPlayer player in Players.Values) {
            if (player.Session.AccountId == ownerId) {
                player.Session.Send(packet);
            }
        }
    }
    #endregion

    public FieldFunctionInteract? TryGetFieldFunctionInteract(string entityId) {
        return fieldFunctionInteracts.TryGetValue(entityId, out FieldFunctionInteract? fieldFunctionInteract) ? fieldFunctionInteract : null;
    }
    #endregion

    #region Remove
    public virtual bool RemovePlayer(int objectId, [NotNullWhen(true)] out FieldPlayer? fieldPlayer) {
        if (Players.TryRemove(objectId, out fieldPlayer)) {
            CommitPlot(fieldPlayer.Session);
            fieldPlayer.Session.Ride.LeaveField(this);
            foreach (FieldReactor reactor in fieldReactors.Values) {
                reactor.Leave(fieldPlayer.Session.CharacterId);
            }
            // A function cube the leaver holds in use goes back to its default state, as on a dialog close.
            long leaverId = fieldPlayer.Session.CharacterId;
            foreach (FieldFunctionInteract interact in fieldFunctionInteracts.Values) {
                InteractCube cube = interact.InteractCube;
                if (!FunctionCubeCloseRule.Releases(interact.Value.ControlType, cube.State, cube.Metadata.DefaultState, cube.InteractingCharacterId, leaverId)) {
                    continue;
                }
                cube.State = cube.Metadata.DefaultState;
                cube.InteractingCharacterId = 0;
                Broadcast(FunctionCubePacket.UpdateFunctionCube(cube));
                Broadcast(FunctionCubePacket.UseFurniture(leaverId, cube));
            }
            // An RPS invite or game ends with the leaver; the partner gets mode 6 with the leaver as actor, which
            // closes its boxes and takes it out of the game state (statemicrogame.cpp FUN_140b887d0).
            long rpsPartner = Rps.Remove(leaverId);
            if (rpsPartner != 0 && TryGetPlayerById(rpsPartner, out FieldPlayer? rpsPlayer)) {
                rpsPlayer.Session.Send(MicrogamePacket.Failed(leaverId, RpsRules.ReasonFailed));
            }
            // The same for a couple dance invite or dance, with mode 0x14 (microgamemanagercli.cpp).
            long dancePartner = CoupleDance.Remove(leaverId, out int danceEventId);
            if (dancePartner != 0 && TryGetPlayerById(dancePartner, out FieldPlayer? dancePlayer)) {
                dancePlayer.Session.Send(MicrogamePacket.CoupleDanceFailed(danceEventId, leaverId, CoupleDanceRules.ReasonFailed));
            }
            Broadcast(FieldPacket.RemovePlayer(objectId));
            Broadcast(ProxyObjectPacket.RemovePlayer(objectId));
            // A duelist who leaves the rank duel loses it.
            RankDuel?.Leave(fieldPlayer);
            PvpZone?.Leave(fieldPlayer);
            Survival?.Leave(fieldPlayer);
            HideAndSeek?.Leave(fieldPlayer);
            ArcadeLeave(fieldPlayer);
            Arena?.Left(this);
            // Last, and guarded: a failed send to the world must not cut the leave short.
            try {
                fieldPlayer.FlushHealth();
            } catch (Exception ex) {
                logger.Warning(ex, "Held health of {CharacterId} not sent on leave", fieldPlayer.Session.CharacterId);
            }

            return true;
        }

        return false;
    }

    public bool PickupItem(FieldPlayer looter, int objectId, [NotNullWhen(true)] out Item? item) {
        if (!fieldItems.TryRemove(objectId, out FieldItem? fieldItem)) {
            item = null;
            return false;
        }

        item = fieldItem.Value;
        fieldItem.Pickup(looter);
        Broadcast(FieldPacket.RemoveItem(objectId));
        return true;
    }

    public bool RemoveItem(int objectId) {
        if (!fieldItems.TryRemove(objectId, out _)) {
            return false;
        }

        Broadcast(FieldPacket.RemoveItem(objectId));
        return true;
    }

    public bool RemoveLiftable(string entityId) {
        if (!fieldLiftables.TryRemove(entityId, out FieldLiftable? fieldLiftable)) {
            return false;
        }

        if (Plots[0].Cubes.Remove(fieldLiftable.Position)) {
            Broadcast(CubePacket.RemoveCube(fieldLiftable.ObjectId, fieldLiftable.Position));
        }

        Broadcast(LiftablePacket.Remove(entityId));
        return true;
    }

    public bool RemoveInteract(IInteractObject interactObject, TimeSpan removeDelay = default) {
        if (!fieldAdBalloons.ContainsKey(interactObject.EntityId) && !fieldInteracts.ContainsKey(interactObject.EntityId)) {
            return false;
        }

        Scheduler.Schedule(() => {
            switch (interactObject) {
                case InteractBillBoardObject billboard:
                    fieldAdBalloons.TryRemove(billboard.EntityId, out _);
                    break;
                default:
                    fieldInteracts.TryRemove(interactObject.EntityId, out _);
                    fieldAdBalloons.TryRemove(interactObject.EntityId, out _);
                    break;
            }

            Broadcast(InteractObjectPacket.Remove(interactObject.EntityId));
        }, removeDelay);
        return true;
    }

    public bool RemoveNpc(int objectId, TimeSpan removeDelay = default) {
        if (!Mobs.TryGetValue(objectId, out _) && !Npcs.TryGetValue(objectId, out _)) {
            return false;
        }

        Scheduler.Schedule(() => RemoveNpcNow(objectId), removeDelay);
        return true;
    }

    // On the field thread.
    private void RemoveNpcNow(int objectId) {
        if (!Mobs.TryRemove(objectId, out FieldNpc? npc) && !Npcs.TryRemove(objectId, out npc)) {
            // Already removed
            return;
        }
        NpcEffects.TryRemove(objectId, out _);
        npc.RemoveSummons();
        Broadcast(FieldPacket.RemoveNpc(objectId));
        Broadcast(ProxyObjectPacket.RemoveNpc(objectId));
        npc.Dispose();
    }

    public bool RemovePet(int objectId, TimeSpan removeDelay = default) {
        if (!Pets.TryRemove(objectId, out FieldPet? pet)) {
            return false;
        }

        Scheduler.Schedule(() => {
            Broadcast(FieldPacket.RemovePet(objectId));
            Broadcast(ProxyObjectPacket.RemovePet(objectId));
            pet.Dispose();
        }, removeDelay);
        return true;
    }

    public bool RemovePortal(int objectId) {
        if (!fieldPortals.TryRemove(objectId, out FieldPortal? portal)) {
            return false;
        }

        if (portal is FieldQuestPortal questPortal) {
            questPortal.Owner.Session.Send(PortalPacket.Remove(questPortal.Value.Id));
        } else {
            Broadcast(PortalPacket.Remove(portal.Value.Id));
        }
        return true;
    }

    // broadcast: false for an improvisation, whose end (PlayInstrument 2) the handler has told already.
    public bool RemoveInstrument(int objectId, bool broadcast = true) {
        if (!fieldInstruments.TryRemove(objectId, out FieldInstrument? instrument)) {
            return false;
        }

        if (broadcast) {
            Broadcast(InstrumentPacket.StopScore(instrument));
        }
        return true;
    }
    #endregion

    #region Events
    public void OnAddPlayer(FieldPlayer added) {
        added.Session.Ride.EnterField(this);
        // A guild vs game player takes its team's tag before the field sends it (FieldPacket.AddPlayer).
        if (FieldInstance.Type == InstanceType.GuildVsGame) {
            StartGuildVsGame().Arrive(added);
        }
        Players[added.ObjectId] = added;
        // A dungeon room's timer starts with its first player; later players get the running one.
        if (RoomTimer == null && this is DungeonFieldManager dungeonField) {
            dungeonField.StartRoomTimer();
        }
        RoomTimer?.Load(added);
        StartFieldWarCountdown();
        // LOAD:
        foreach (FieldLiftable liftable in fieldLiftables.Values.Where(liftable => liftable.FinishTick > 0)) {
            added.Session.Send(LiftablePacket.Add(liftable));
        }
        added.Session.Send(LiftablePacket.Update(fieldLiftables.Values.Where(liftable => liftable.FinishTick == 0).ToList()));
        added.Session.Send(BreakablePacket.Update(fieldBreakables.Values));
        List<(Vector3B, int)> away = LiftupRules.Away(liftedCubes, Environment.TickCount64);
        if (away.Count > 0) {
            added.Session.Send(CubePacket.LiftedCubes(away));
        }
        // The client shows every vibrate object; the hidden groups are hidden for the newcomer.
        foreach (string vibrateId in Vibrate.Hidden) {
            added.Session.Send(VibratePacket.Hide(vibrateId));
        }
        added.Session.Send(InteractObjectPacket.Load(fieldInteracts.Values));
        foreach (FieldInteract fieldInteract in fieldAdBalloons.Values) {
            added.Session.Send(InteractObjectPacket.Add(fieldInteract.Object));
        }
        foreach (FieldInteract fieldInteract in fieldChests.Values) {
            added.Session.Send(InteractObjectPacket.Add(fieldInteract.Object));
            // The Add packet says reactable; an opened map treasure chest gets its state after it.
            if (MapChestRules.NeedsStateAfterAdd(fieldInteract.Chest, fieldInteract.State)) {
                added.Session.Send(InteractObjectPacket.Update(fieldInteract));
            }
        }
        foreach (FieldInstrument fieldInstrument in fieldInstruments.Values) {
            if (fieldInstrument.Score != null) {
                added.Session.Send(InstrumentPacket.StartScore(fieldInstrument, fieldInstrument.Score));
            }
        }

        added.Session.Send(FunctionCubePacket.SendCubes(fieldFunctionInteracts.Values));
        foreach (FieldFittingDoll doll in fittingDolls.Values) {
            lock (doll.Lock) {
                added.Session.Send(FittingDollPacket.Load(doll, doll.Items.Values));
            }
        }
        // Before the npcs: a maid npc's FieldAddNpc names its maid.
        foreach (MaidSlot slot in maids.Values) {
            if (slot.NpcObjectId != 0) { // a maid not spawned yet is broadcast when it is
                added.Session.Send(FieldMaidPacket.Add(slot.Maid));
            }
        }

        foreach (Plot plot in Plots.Values) {
            foreach (PlotCube plotCube in plot.Cubes.Values) {
                if (plotCube.Interact?.NoticeSettings is not null) {
                    added.Session.Send(HomeActionPacket.SendCubeNoticeSettings(plotCube));
                }
            }
        }

        foreach (FieldPlayer fieldPlayer in Players.Values) {
            added.Session.Send(FieldPacket.AddPlayer(fieldPlayer.Session));
            if (fieldPlayer.Session.GuideObject is { Value: BallGuideObject } ball) {
                // the client creates balls through HomeAction, its GuideObject create has no ball type
                added.Session.Send(HomeActionPacket.AddBall(ball));
            } else if (fieldPlayer.Session.GuideObject != null) {
                added.Session.Send(GuideObjectPacket.Create(fieldPlayer.Session.GuideObject));
                // A player already fishing: the newcomer was not in the field when the state was broadcast.
                if (StateFishingPacket.IsFishing(fieldPlayer.Session.GuideObject.Value.Type) && fieldPlayer != added) {
                    added.Session.Send(StateFishingPacket.State(fieldPlayer.ObjectId, StateFishingPacket.STATE_FISHING));
                }
            }
            switch (fieldPlayer.Session.HeldCube) {
                case PlotCube plotCube:
                    added.Session.Send(SetCraftModePacket.Plot(fieldPlayer.ObjectId, plotCube));
                    break;
                case LiftableCube liftableCube:
                    added.Session.Send(SetCraftModePacket.Liftable(fieldPlayer.ObjectId, liftableCube));
                    break;
            }
        }
        Broadcast(FieldPacket.AddPlayer(added.Session), added.Session);
        added.Flag = PlayerObjectFlag.All;

        Broadcast(ProxyObjectPacket.AddPlayer(added), added.Session);
        foreach (FieldItem fieldItem in fieldItems.Values) {
            added.Session.Send(FieldPacket.DropItem(fieldItem));
        }
        foreach (FieldNpc fieldNpc in Npcs.Values.Concat(Mobs.Values)) {
            added.Session.Send(FieldPacket.AddNpc(fieldNpc));
            if (fieldNpc.IsCorpse) {
                added.Session.Send(NpcControlPacket.Dead(fieldNpc));
            }
        }
        foreach (FieldPet fieldPet in Pets.Values) {
            added.Session.Send(FieldPacket.AddPet(fieldPet));
        }
        foreach (FieldReactor reactor in fieldReactors.Values) {
            added.Session.Send(ReactorPacket.Add(reactor.ObjectId, reactor.Name, reactor.Metadata.Id, reactor.Position, reactor.Rotation,
                reactor.StateIndex, reactor.StateTime, reactor.Connected));
        }
        foreach (FieldPortal fieldPortal in fieldPortals.Values) {
            switch (fieldPortal) {
                case FieldQuestPortal questPortal:
                    if (questPortal.Owner.ObjectId == added.ObjectId) {
                        added.Session.Send(PortalPacket.Add(questPortal));
                    }
                    continue;
                default:
                    added.Session.Send(PortalPacket.Add(fieldPortal));
                    continue;
            }
        }
        // ProxyGameObj
        foreach (FieldPlayer fieldPlayer in Players.Values) {
            added.Session.Send(ProxyObjectPacket.AddPlayer(fieldPlayer));
        }
        foreach (FieldNpc fieldNpc in Npcs.Values.Concat(Mobs.Values)) {
            added.Session.Send(ProxyObjectPacket.AddNpc(fieldNpc));
        }
        foreach (FieldPet fieldPet in Pets.Values) {
            added.Session.Send(ProxyObjectPacket.AddPet(fieldPet));
        }
        foreach (FieldSkill skillSource in fieldSkills.Values) {
            added.Session.Send(RegionSkillPacket.Add(skillSource));
        }

        added.Session.Send(TriggerPacket.Load(TriggerObjects));
        var effects = LiveNpcEffects(NpcEffects, objectId => Npcs.ContainsKey(objectId) || Mobs.ContainsKey(objectId));
        if (effects.Count > 0) {
            added.Session.Send(TriggerPacket.LoadEffects(effects));
        }
        if (ScoreBoard.EnterPacket(ScoreBoardOpen) is { } scoreBoard) {
            added.Session.Send(scoreBoard);
        }

        if (background != null) {
            added.Session.Send(FieldPropertyPacket.Background(background));
        }
        added.Session.Send(FieldPropertyPacket.Load(fieldProperties.Values));

        foreach (TickTimer timer in Timers.Values.Where(timer => timer.Display)) {
            Broadcast(TriggerPacket.TimerDialog(timer));
        }

        RankDuel?.Enter(added);
        Arena?.Enter(added);
        PvpZone?.Enter(added);
        HideAndSeek?.Arrive(added);
    }
    #endregion Events

    // Drops the RPS and couple dance invites and games past their MicrogameLimits; both players get the failed mode
    // with the other as actor, which closes their boxes and ends the game state on the client (as for a leaver).
    private void ExpireMicrogames() {
        foreach (RpsMatchTable.Expired expired in Rps.Expire(FieldTick)) {
            if (TryGetPlayerById(expired.Inviter, out FieldPlayer? inviter)) {
                inviter.Session.Send(MicrogamePacket.Failed(expired.Invitee, RpsRules.ReasonFailed));
            }
            if (TryGetPlayerById(expired.Invitee, out FieldPlayer? invitee)) {
                invitee.Session.Send(MicrogamePacket.Failed(expired.Inviter, RpsRules.ReasonFailed));
            }
        }
        foreach (CoupleDanceMatchTable.Expired expired in CoupleDance.Expire(FieldTick)) {
            if (TryGetPlayerById(expired.Inviter, out FieldPlayer? inviter)) {
                inviter.Session.Send(MicrogamePacket.CoupleDanceFailed(expired.EventId, expired.Invitee, CoupleDanceRules.ReasonFailed));
            }
            if (TryGetPlayerById(expired.Invitee, out FieldPlayer? invitee)) {
                invitee.Session.Send(MicrogamePacket.CoupleDanceFailed(expired.EventId, expired.Inviter, CoupleDanceRules.ReasonFailed));
            }
        }
    }
}
