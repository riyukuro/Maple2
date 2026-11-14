using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using WorldTimeEventRequest = Maple2.Server.World.Service.TimeEventRequest;

namespace Maple2.Server.Game.Manager.Field;

public partial class FieldManager {
    // The world champion of a season's field (WorldChampionSync), null on other fields and in homes and dungeons.
    public WorldChampionSync? WorldChampion { get; private set; }

    private sealed class WorldChampionHost(FieldManager field) : WorldChampionSync.IHost {
        public (int ObjectId, long MaxHealth)? Spawn(int npcId, int spawnPointId) {
            if (!field.Entities.RegionSpawns.TryGetValue(spawnPointId, out Ms2RegionSpawn? regionSpawn) ||
                !field.NpcMetadata.TryGet(npcId, out NpcMetadata? metadata)) {
                return null;
            }
            FieldNpc? npc = field.SpawnNpc(metadata, regionSpawn.Position, regionSpawn.Rotation);
            if (npc == null) {
                return null;
            }
            // Its health is the World server's: it does not die in the field (no drops or exp per channel).
            npc.KeepAlive = true;
            npc.PlayerDamageCallback = (player, amount) => field.WorldChampionDamaged(npc, player, amount);
            field.Broadcast(FieldPacket.AddNpc(npc));
            return (npc.ObjectId, npc.Stats.Values[BasicAttribute.Health].Total);
        }

        public void Remove(int objectId) => field.RemoveNpc(objectId);

        public void SetHealth(int objectId, long health) {
            if (!field.Mobs.TryGetValue(objectId, out FieldNpc? npc) && !field.Npcs.TryGetValue(objectId, out npc)) {
                return;
            }
            Stat stat = npc.Stats.Values[BasicAttribute.Health];
            if (stat.Current == health) {
                return;
            }
            stat.Current = Math.Clamp(health, 1, stat.Total);
            field.Broadcast(StatsPacket.Update(npc, BasicAttribute.Health));
        }
    }

    // On a plain field of a season's map, with the feature on: reports and shows the world champion each second.
    private void InitWorldChampion() {
        if (GetType() != typeof(FieldManager) || !WorldChampionSync.Runs(ServerTableMetadata.WorldChampionTable, ServerTableMetadata.FeatureTable, MapId)) {
            return;
        }
        WorldChampion = new WorldChampionSync(() => ServerTableMetadata.WorldChampionTable, () => DateTime.Now, MapId,
            Random.Shared.NextInt64(1, long.MaxValue), new WorldChampionHost(this),
            ReportWorldChampion, run => Task.Run(run), run => Scheduler.Schedule(run));
        Scheduler.ScheduleRepeated(() => WorldChampion.Tick(), WorldChampionSync.Interval);
    }

    private (int Season, int Step, long StepDamage)? ReportWorldChampion(int season, int step, long maxHealth, IReadOnlyCollection<WorldChampionDamage> damages,
        long sender, long sequence) {
        var request = new WorldTimeEventRequest.Types.WorldChampion {
            Season = season,
            Step = step,
            MaxHealth = maxHealth,
            Sender = sender,
            Sequence = sequence,
        };
        request.Damages.AddRange(damages.Select(damage => new WorldTimeEventRequest.Types.WorldChampion.Types.Hit {
            AccountId = damage.AccountId,
            CharacterId = damage.CharacterId,
            Damage = damage.Damage,
        }));
        Maple2.Server.World.Service.TimeEventResponse response = FieldFactory.World.TimeEvent(new WorldTimeEventRequest {
            WorldChampion = request,
        });
        if (response.Error != 0 || response.WorldChampionInfo == null) {
            return null;
        }
        return (response.WorldChampionInfo.Season, response.WorldChampionInfo.Step, response.WorldChampionInfo.StepDamage);
    }

    // A player hit the champion: the damage goes to the World server and counts for the season's trophies
    // (worldchampion_damage, code the season).
    private void WorldChampionDamaged(FieldNpc npc, FieldPlayer player, long amount) {
        if (WorldChampion == null || npc.ObjectId != WorldChampion.ObjectId) {
            return;
        }
        WorldChampion.Damaged(npc.ObjectId, player.Session.AccountId, player.Session.CharacterId, amount);
        player.Session.ConditionUpdate(ConditionType.worldchampion_damage, counter: amount, codeLong: WorldChampion.Season);
    }
}
