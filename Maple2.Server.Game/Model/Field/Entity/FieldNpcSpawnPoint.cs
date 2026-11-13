using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;
using Serilog;

namespace Maple2.Server.Game.Model;

public class FieldSpawnPointNpc : FieldEntity<SpawnPointNPC> {
    private int SpawnId => Value.SpawnPointId;

    private bool active;

    private long checkTick;

    // Named by a combine spawn group of this field (CombineSpawnRules.GroupSpawnIds): the point spawns only when the
    // group picks it (start_combine_spawn, then each group reset), not at field creation or on its own regen.
    // Chosen here: the group's totalCount is how many of its points are up, so a point that filled itself would
    // put up every point of the map at once (82000000: all 1188 at the first field update).
    public bool GroupSpawned { get; }

    public FieldSpawnPointNpc(FieldManager field, int objectId, SpawnPointNPC metadata, bool groupSpawned = false) : base(field, objectId, metadata) {
        GroupSpawned = groupSpawned;
        active = SpawnGroupRules.SelfFills(metadata, groupSpawned);
    }

    public void SpawnOnCreate() {
        if (SpawnGroupRules.SpawnsOnCreate(Value, GroupSpawned)) {
            TriggerSpawn(forceFullSpawn: true);
        }
    }

    public override void Update(long tickCount) {
        if (!active || Value.RegenCheckTime <= 0) {
            return;
        }
        if (checkTick > tickCount) {
            return;
        }

        checkTick = tickCount + (long) TimeSpan.FromSeconds(Value.RegenCheckTime).TotalMilliseconds;
        TriggerSpawn();
    }

    public void TriggerSpawn(bool forceFullSpawn = false) {
        FieldNpc[] npcs = forceFullSpawn ? [] :
            Field.GetActorsBySpawnId(SpawnId).OfType<FieldNpc>().ToArray();

        foreach (SpawnPointNPCListEntry spawn in Value.NpcList) {
            if (!Field.NpcMetadata.TryGet(spawn.NpcId, out NpcMetadata? npcMetadata)) {
                // Log.Logger.Warning("Npc {NpcId} failed to load for map {MapId}", spawn.NpcId, Field.MapId);
                continue;
            }

            int spawnCountNeeded = forceFullSpawn ? spawn.Count : spawn.Count - npcs.Count(x => x.Value.Id == spawn.NpcId);

            for (int i = 0; i < spawnCountNeeded; i++) {
                FieldNpc? npc = Field.SpawnNpc(npcMetadata, Value);
                if (npc == null) {
                    continue;
                }
                npc.SpawnPointId = SpawnId;

                Field.Broadcast(FieldPacket.AddNpc(npc));
                Field.Broadcast(ProxyObjectPacket.AddNpc(npc));
            }
        }
    }
}
