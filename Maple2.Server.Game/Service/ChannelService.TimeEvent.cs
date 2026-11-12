using System.Collections.Concurrent;
using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Channel.Service;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.Service;

public partial class ChannelService {
    private readonly ConcurrentDictionary<int, byte> monitoringBossFields = new();
    // KMS2 sends the two lifetime notices of a world boss with the flags 5.
    private const NoticePacket.Flags LifetimeNotice = NoticePacket.Flags.Message | NoticePacket.Flags.Alert;
    public override Task<TimeEventResponse> TimeEvent(TimeEventRequest request, ServerCallContext context) {
        switch (request.TimeEventCase) {
            case TimeEventRequest.TimeEventOneofCase.AnnounceGlobalPortal:
                return Task.FromResult(AnnounceGlobalPortal(request.AnnounceGlobalPortal));
            case TimeEventRequest.TimeEventOneofCase.CloseGlobalPortal:
                return Task.FromResult(CloseGlobalPortal(request.CloseGlobalPortal));
            case TimeEventRequest.TimeEventOneofCase.GetField:
                return Task.FromResult(GetField(request.GetField));
            case TimeEventRequest.TimeEventOneofCase.AnnounceWorldBoss:
                return Task.FromResult(AnnounceWorldBoss(request.AnnounceWorldBoss));
            case TimeEventRequest.TimeEventOneofCase.CloseWorldBoss:
                return Task.FromResult(CloseWorldBoss(request.CloseWorldBoss));
            case TimeEventRequest.TimeEventOneofCase.WarnWorldBoss:
                return Task.FromResult(WarnWorldBoss(request.WarnWorldBoss));
            default:
                return Task.FromResult(new TimeEventResponse());
        }
    }

    private TimeEventResponse AnnounceGlobalPortal(TimeEventRequest.Types.AnnounceGlobalPortal portal) {
        if (!serverTableMetadata.TimeEventTable.GlobalPortal.TryGetValue(portal.MetadataId, out GlobalPortalMetadata? metadata)) {
            return new TimeEventResponse();
        }
        foreach (GameSession session in server.GetSessions()) {
            if (session.Field != null && TimeEventRules.PortalAnnouncedIn(session.Field.Metadata.Property.Type)) {
                session.Send(GlobalPortalPacket.Announce(metadata, portal.EventId));
            }
        }
        return new TimeEventResponse();
    }

    private TimeEventResponse CloseGlobalPortal(TimeEventRequest.Types.CloseGlobalPortal portal) {
        foreach (GameSession session in server.GetSessions()) {
            session.Send(GlobalPortalPacket.Close());
        }
        return new TimeEventResponse();
    }

    private TimeEventResponse AnnounceWorldBoss(TimeEventRequest.Types.AnnounceWorldBoss boss) {
        if (!serverTableMetadata.TimeEventTable.WorldBoss.TryGetValue(boss.MetadataId, out WorldBossMetadata? metadata)) {
            return new TimeEventResponse();
        }

        int npcId = metadata.NpcIds.Length > 0 ? metadata.NpcIds[0] : 0;
        int mapId = metadata.TargetMapIds.Length > 0 ? metadata.TargetMapIds[0] : 0;
        short channel = GameServer.GetChannel();
        long spawnTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        foreach (GameSession session in server.GetSessions()) {
            session.Send(WorldShareInfoPacket.BossAlive(npcId, mapId, channel, spawnTimestamp, true));
            session.Send(LegionBattlePacket.Update(boss.MetadataId, npcId, boss.NextSpawnTimestamp));
        }

        // Spawn the boss NPC in each target map
        foreach (int targetMapId in metadata.TargetMapIds) {
            FieldManager? field = server.GetField(targetMapId);
            field?.SpawnWorldBoss(metadata, boss.EventId);
        }

        return new TimeEventResponse();
    }

    private TimeEventResponse WarnWorldBoss(TimeEventRequest.Types.WarnWorldBoss boss) {
        if (!serverTableMetadata.TimeEventTable.WorldBoss.TryGetValue(boss.MetadataId, out WorldBossMetadata? metadata)) {
            return new TimeEventResponse();
        }

        int npcId = metadata.NpcIds.Length > 0 ? metadata.NpcIds[0] : 0;

        foreach (int targetMapId in metadata.TargetMapIds) {
            FieldManager? field = server.GetField(targetMapId);
            if (field == null) continue;
            FieldNpc? bossNpc = field.GetWorldBossNpc();
            if (bossNpc == null) continue; // Already dead on this channel
            if (!TimeEventRules.BossLeaves(bossNpc.LastDamageTick, Environment.TickCount64, Constant.WorldBossIdleWarningThreshold)) {
                continue; // it stays: the idle monitor warns when it is about to leave
            }
            field.Broadcast(NoticePacket.Message(new InterfaceText(StringCode.s_timeevent_boss_lifetimetext1, npcId.ToString()), LifetimeNotice));
        }

        return new TimeEventResponse();
    }

    private TimeEventResponse CloseWorldBoss(TimeEventRequest.Types.CloseWorldBoss boss) {
        if (!serverTableMetadata.TimeEventTable.WorldBoss.TryGetValue(boss.MetadataId, out WorldBossMetadata? metadata)) {
            return new TimeEventResponse();
        }

        int npcId = metadata.NpcIds.Length > 0 ? metadata.NpcIds[0] : 0;

        foreach (int targetMapId in metadata.TargetMapIds) {
            FieldManager? field = server.GetField(targetMapId);
            if (field == null) continue;

            CloseWorldBoss(field, npcId, targetMapId, Environment.TickCount64);
        }

        return new TimeEventResponse();
    }

    // The close of one target field: a boss idle at the warning time leaves with the "disappeared" notice,
    // one hit since then stays and the idle monitor takes over (one monitor per field).
    internal void CloseWorldBoss(FieldManager field, int npcId, int targetMapId, long nowTick) {
        FieldNpc? bossNpc = field.GetWorldBossNpc();
        if (bossNpc == null) return;

        if (!TimeEventRules.BossLeavesAtClose(bossNpc.LastDamageTick, nowTick, Constant.WorldBossIdleWarningThreshold, Constant.WorldBossLeaveDelay)) {
            if (monitoringBossFields.TryAdd(targetMapId, 0)) {
                _ = MonitorExtendedBossLifetimeAsync(field, npcId, targetMapId);
            }
            return;
        }

        field.Broadcast(NoticePacket.Message(new InterfaceText(StringCode.s_timeevent_boss_lifetimetext2, npcId.ToString()), LifetimeNotice));
        field.DespawnWorldBoss();
    }

    private async Task MonitorExtendedBossLifetimeAsync(FieldManager field, int npcId, int targetMapId) {
        bool warningSent = false;
        try {
            while (true) {
                await Task.Delay(Constant.WorldBossMonitorInterval);

                FieldNpc? bossNpc = field.GetWorldBossNpc();
                if (bossNpc == null) return;

                long idleMs = bossNpc.LastDamageTick == 0
                    ? long.MaxValue
                    : Environment.TickCount64 - bossNpc.LastDamageTick;

                if (!warningSent && idleMs >= (long) Constant.WorldBossIdleWarningThreshold.TotalMilliseconds) {
                    field.Broadcast(NoticePacket.Message(new InterfaceText(StringCode.s_timeevent_boss_lifetimetext1, npcId.ToString()), LifetimeNotice));
                    warningSent = true;
                }

                if (idleMs >= (long) Constant.WorldBossDespawnThreshold.TotalMilliseconds) {
                    field.Broadcast(NoticePacket.Message(new InterfaceText(StringCode.s_timeevent_boss_lifetimetext2, npcId.ToString()), LifetimeNotice));
                    field.DespawnWorldBoss();
                    return;
                }
            }
        } catch (Exception ex) {
            logger.Error(ex, "Error monitoring field boss lifetime for NPC {NpcId}", npcId);
        } finally {
            monitoringBossFields.TryRemove(targetMapId, out _);
        }
    }

    private TimeEventResponse GetField(TimeEventRequest.Types.GetField field) {
        FieldManager? manager = server.GetField(field.MapId, field.RoomId);
        if (manager == null) {
            return new TimeEventResponse();
        }

        return new TimeEventResponse {
            Field = new FieldInfo {
                MapId = manager.MapId,
                RoomId = manager.RoomId,
                OwnerId = manager is HomeFieldManager homeManager ? homeManager.OwnerId : 0,
                PlayerIds = {
                    manager.Players.Values.Select(player => player.Value.Character.Id),
                },
            },
        };
    }
}
