using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Util;
using Maple2.Tools;
using Serilog;

namespace Maple2.Server.Game.Model;

public class FieldSpawnGroup : FieldEntity<SpawnGroupMetadata> {

    private readonly WeightedSet<SpawnNpcMetadata> npcs;
    private readonly WeightedSet<SpawnInteractObjectMetadata> interactObjects;
    private readonly List<int> spawnIds = [];
    private long resetTick;

    private bool active;

    public FieldSpawnGroup(FieldManager field, int objectId, SpawnGroupMetadata metadata) : base(field, objectId, metadata) {
        resetTick = metadata.ResetTick + field.FieldTick;
        npcs = new WeightedSet<SpawnNpcMetadata>();
        interactObjects = new WeightedSet<SpawnInteractObjectMetadata>();
        Init();
    }

    private void Init() {
        switch (Value.Type) {
            case CombineSpawnGroupType.npc:
                if (!Field.ServerTableMetadata.CombineSpawnTable.Npcs.TryGetValue(Value.GroupId, out Dictionary<int, SpawnNpcMetadata>? npcDict)) {
                    return;
                }

                foreach (SpawnNpcMetadata npc in npcDict.Values) {
                    npcs.Add(npc, npc.Weight);
                }
                break;
            case CombineSpawnGroupType.interactObject:
                if (!Field.ServerTableMetadata.CombineSpawnTable.InteractObjects.TryGetValue(Value.GroupId, out Dictionary<int, SpawnInteractObjectMetadata>? interactDict)) {
                    return;
                }

                foreach (SpawnInteractObjectMetadata interact in interactDict.Values) {
                    interactObjects.Add(interact, interact.Weight);
                }
                break;
            default:
                Log.Logger.Error("Invalid spawn group type {Type} for spawn group {GroupId}", Value.Type, Value.GroupId);
                break;
        }
    }

    public void ToggleActive(bool active) {
        this.active = active;

        if (active) {
            InitializeSpawns();
            return;
        }

        spawnIds.Clear();
    }

    private void InitializeSpawns() {
        if (Value.TotalCount <= 0) {
            return;
        }

        switch (Value.Type) {
            case CombineSpawnGroupType.npc:
                SpawnNpcs();
                return;
            case CombineSpawnGroupType.interactObject:
                SpawnInteractObjects();
                return;
        }
    }

    private void SpawnNpcs() {
        SpawnGroupRules.Fill fill = SpawnGroupRules.Refill(spawnIds, spawnId => Field.GetActorsBySpawnId(spawnId).Any(), Value.TotalCount,
            npcs.Select(entry => entry.Item1.SpawnId), () => npcs.Get().SpawnId, Field.ToggleNpcSpawnPoint);
        WarnIfGaveUp(fill);
    }

    private void SpawnInteractObjects() {
        SpawnInteractObjectMetadata? picked = null;
        SpawnGroupRules.Fill fill = SpawnGroupRules.Refill(spawnIds, spawnId => Field.GetInteractObjectsBySpawnId(spawnId).Any(), Value.TotalCount,
            interactObjects.Select(entry => entry.Item1.RegionSpawnId), () => (picked = interactObjects.Get()).RegionSpawnId,
            _ => Field.SpawnInteractObject(picked!));
        WarnIfGaveUp(fill);
    }

    private void WarnIfGaveUp(SpawnGroupRules.Fill fill) {
        if (fill.GaveUp) {
            Log.Logger.Warning("Spawn group {GroupId} on map {MapId}: {Up} of {TotalCount} up after its draws ran out",
                Value.GroupId, Field.MapId, spawnIds.Count, Value.TotalCount);
        }
    }

    public override void Update(long tickCount) {
        if (!active) {
            return;
        }

        if (tickCount < resetTick) {
            return;
        }

        InitializeSpawns();
        resetTick = Field.FieldTick + Value.ResetTick;
    }
}
