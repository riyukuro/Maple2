using System.Numerics;
using DotRecast.Core.Numerics;
using Maple2.Model.Game;
using Maple2.Model.Game.Field;
using Maple2.Model.Metadata;
using Maple2.Model.Metadata.FieldEntity;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;
using Maple2.Tools;
using Maple2.Tools.DotRecast;
using Serilog;

namespace Maple2.Server.Game.Model;

/// <summary>
/// SpawnPoint on field with random NPCs.
/// </summary>
public class FieldMobSpawn : FieldEntity<MapMetadataSpawn> {
    private const int SPAWN_DISTANCE = 250;
    private const int PET_SPAWN_RATE_TOTAL = 10000;

    private readonly WeightedSet<NpcMetadata> npcs;
    private readonly WeightedSet<ItemMetadata> pets;
    private readonly List<int> spawnedMobs;
    private readonly List<int> spawnedPets;
    private readonly List<Vector3> validSpawns;
    private readonly RespawnQueue respawns = new();
    private bool spawned;
    private int spawnId;

    public FieldMobSpawn(FieldManager field, int objectId, MapMetadataSpawn metadata, WeightedSet<NpcMetadata> npcs, WeightedSet<ItemMetadata> pets) : base(field, objectId, metadata) {
        this.npcs = npcs;
        this.pets = pets;
        spawnedMobs = new List<int>(metadata.Population);
        spawnedPets = new List<int>(metadata.PetPopulation);
        spawnId = metadata.Id;
        if (Value.Cooldown <= 0) {
            Log.Logger.Information("No respawn for mapId:{MapId} spawnId:{SpawnId}", Field.MapId, Value.Id);
        }

        validSpawns = new();
    }

    // A mob of this spawn died: it alone comes back at its death + deadTime + Cooldown.
    public void Despawn(int objectId, float deadTime) {
        // Pets do not impact spawn cycle.
        spawnedPets.Remove(objectId);

        if (!spawnedMobs.Remove(objectId)) {
            return;
        }

#if DEBUG
        // 2x faster respawns.
        const int spawnRate = 500;
#else
        const int spawnRate = 1000;
#endif

        respawns.Died(Environment.TickCount64, deadTime, Value.Cooldown, spawnRate);
    }

    private List<Vector3> GetRandomSpawns(int count) {
        return SpawnPicker.Pick(validSpawns, count, Random.Shared, SpreadAround);
    }

    // A navmesh point within SPAWN_DISTANCE of tile, or the tile itself when none is found there.
    private Vector3 SpreadAround(Vector3 tile) {
        float angle = Random.Shared.NextSingle() * MathF.PI * 2;
        float distance = Random.Shared.NextSingle() * SPAWN_DISTANCE;
        Vector3 candidate = tile + new Vector3(MathF.Cos(angle) * distance, MathF.Sin(angle) * distance, 0);
        if (Field.FindNearestPoly(candidate, out long nearestRef, out RcVec3f position) && nearestRef != 0) {
            return DotRecastHelper.FromNavMeshSpace(position);
        }
        return tile;
    }

    private void InitializeSpawns() {
        if (validSpawns.Count > 0) {
            return;
        }

        if (!Field.MapMetadata.TryGet(Field.MapId, out MapMetadata? map)) {
            Log.Logger.Error("Failed to get map xblock name for map {MapId}", Field.MapId);

            validSpawns.Add(Position);

            return;
        }

        if (!Field.MapData.TryGet(map.XBlock, out FieldAccelerationStructure? mapData)) {
            Log.Logger.Error("Failed to get map xblock name for map {MapId}", Field.MapId);

            validSpawns.Add(Position);

            return;
        }

        foreach (FieldEntity entity in mapData.QuerySpawnsList(Position, SPAWN_DISTANCE)) {
            validSpawns.Add(entity.Position);
        }

        if (validSpawns.Count == 0) {
            Log.Logger.Error("Failed to find spawns for spawn {SpawnId} in map {MapId}", spawnId, Field.MapId);

            validSpawns.Add(Position);
        }
    }

    public override void Update(long tickCount) {
        int spawnMobCount;
        if (!spawned) {
            spawnMobCount = Value.Population - spawnedMobs.Count; // the whole group on field entry
        } else {
            spawnMobCount = respawns.Take(tickCount, Value.Population - spawnedMobs.Count);
        }
        if (spawnMobCount <= 0) {
            return;
        }

        spawned = true;
        InitializeSpawns();

        int target = spawnedMobs.Count + spawnMobCount;
        // One pet roll each time the group is full again (once per refill, as before the per-mob timers).
        bool doSpawnPet = target >= Value.Population && Random.Shared.Next(PET_SPAWN_RATE_TOTAL) < Value.PetSpawnRate;

        if (doSpawnPet) {
            ++spawnMobCount;
        }

        List<Vector3> pickedSpawns = GetRandomSpawns(spawnMobCount);
        int spawnIndex = 0;
        for (int i = spawnedMobs.Count; i < target; i++) {
            FieldNpc? fieldNpc = Field.SpawnNpc(npcs.Get(), pickedSpawns[spawnIndex++], Rotation, owner: this);
            if (fieldNpc == null) {
                // The place is tried again one Cooldown later (another npc of the set may be picked then).
                respawns.Died(tickCount, 0, Value.Cooldown);
                continue;
            }

            spawnedMobs.Add(fieldNpc.ObjectId);

            Field.Broadcast(FieldPacket.AddNpc(fieldNpc));
            Field.Broadcast(ProxyObjectPacket.AddNpc(fieldNpc));
        }

        if (Value.PetSpawnRate <= 0 || pets.Count <= 0 || spawnedPets.Count >= Value.PetPopulation) {
            return;
        }

        if (doSpawnPet) {
            // Any stats are computed after pet is captured since that's when rarity is determined.
            var pet = new Item(pets.Get());
            FieldPet? fieldPet = Field.SpawnPet(pet, pickedSpawns.Last(), Rotation, owner: this);
            if (fieldPet == null) {
                return;
            }

            spawnedPets.Add(fieldPet.ObjectId);

            Field.Broadcast(FieldPacket.AddPet(fieldPet));
            Field.Broadcast(ProxyObjectPacket.AddPet(fieldPet));
        }
    }

    private Vector3 GetRandomSpawn() {
        int spawnX = Random.Shared.Next((int) Position.X - SPAWN_DISTANCE, (int) Position.X + SPAWN_DISTANCE);
        int spawnY = Random.Shared.Next((int) Position.Y - SPAWN_DISTANCE, (int) Position.Y + SPAWN_DISTANCE);
        return new Vector3(spawnX, spawnY, Position.Z);
    }
}
