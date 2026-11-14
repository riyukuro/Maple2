using Grpc.Core;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.World.Containers;

namespace Maple2.Server.World.Service;

public partial class WorldService {
    public override Task<TimeEventResponse> TimeEvent(TimeEventRequest request, ServerCallContext context) {
        switch (request.TimeEventCase) {
            case TimeEventRequest.TimeEventOneofCase.JoinGlobalPortal:
                return Task.FromResult(JoinGlobalPortal(request.JoinGlobalPortal));
            case TimeEventRequest.TimeEventOneofCase.GetGlobalPortal:
                return Task.FromResult(GetGlobalPortal(request.GetGlobalPortal));
            case TimeEventRequest.TimeEventOneofCase.GetActiveWorldBosses:
                return Task.FromResult(GetActiveWorldBosses());
            case TimeEventRequest.TimeEventOneofCase.WorldBossKilled:
                return Task.FromResult(OnWorldBossKilled(request.WorldBossKilled));
            case TimeEventRequest.TimeEventOneofCase.JoinFieldWar:
                return Task.FromResult(new TimeEventResponse {
                    FieldWarInfo = fieldWarLookup.Join(request.JoinFieldWar.EventId, request.JoinFieldWar.CharacterId),
                });
            case TimeEventRequest.TimeEventOneofCase.GetFieldWar:
                return Task.FromResult(new TimeEventResponse {
                    FieldWarInfo = fieldWarLookup.Get(request.GetFieldWar.CharacterId),
                });
            case TimeEventRequest.TimeEventOneofCase.WorldChampion:
                return Task.FromResult(WorldChampion(request.WorldChampion));
            default:
                return Task.FromResult(new TimeEventResponse());
        }
    }

    private TimeEventResponse WorldChampion(TimeEventRequest.Types.WorldChampion report) {
        (int season, int step, long stepDamage) = worldChampionLookup.Report(report.Season, report.Step, report.MaxHealth,
            report.Damages.Select(damage => new WorldChampionDamage(damage.AccountId, damage.CharacterId, damage.Damage)));
        return new TimeEventResponse {
            WorldChampionInfo = new WorldChampionInfo { Season = season, Step = step, StepDamage = stepDamage },
        };
    }

    private TimeEventResponse OnWorldBossKilled(TimeEventRequest.Types.WorldBossKilled kill) {
        worldBossLookup.RemoveChannel(kill.MetadataId, (short) kill.Channel);
        return new TimeEventResponse();
    }

    private TimeEventResponse GetActiveWorldBosses() {
        var response = new TimeEventResponse();
        foreach (WorldBossManager manager in worldBossLookup.GetAll()) {
            var entry = new TimeEventResponse.Types.ActiveWorldBoss {
                MetadataId = manager.Boss.MetadataId,
                EventId = manager.Boss.Id,
                SpawnTimestamp = manager.Boss.SpawnTimestamp,
                NextSpawnTimestamp = manager.Boss.NextSpawnTimestamp,
            };
            entry.AliveChannels.AddRange(manager.AliveChannels.Keys);
            response.ActiveWorldBosses.Add(entry);
        }
        return response;
    }

    private TimeEventResponse JoinGlobalPortal(TimeEventRequest.Types.JoinGlobalPortal portal) {
        if (!globalPortalLookup.TryGet(out GlobalPortalManager? manager) || manager.Portal.MetadataId != portal.EventId) {
            return new TimeEventResponse();
        }

        // The index is the button the client sent.
        if (!GlobalPortalRules.EntryValid(portal.Index, manager.Portal.Metadata.Entries.Length)) {
            return new TimeEventResponse();
        }

        GlobalPortalMetadata.Field fieldMetadata = manager.Portal.Metadata.Entries[portal.Index];

        if (fieldMetadata.MapId == 0) {
            return new TimeEventResponse();
        }

        manager.Join(fieldMetadata.MapId, portal.Index);
        int roomId = manager.RoomIds[portal.Index];
        return new TimeEventResponse {
            GlobalPortalInfo = new GlobalPortalInfo {
                Channel = manager.Channel,
                RoomId = roomId,
                MapId = fieldMetadata.MapId,
                PortalId = fieldMetadata.PortalId,
            },
        };
    }

    private TimeEventResponse GetGlobalPortal(TimeEventRequest.Types.GetGlobalPortal portal) {
        if (!globalPortalLookup.TryGet(out GlobalPortalManager? manager)) {
            return new TimeEventResponse();
        }

        return new TimeEventResponse {
            GlobalPortalInfo = new GlobalPortalInfo {
                MetadataId = manager.Portal.MetadataId,
                EventId = manager.Portal.Id,
            },
        };
    }
}
