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
            case TimeEventRequest.TimeEventOneofCase.AnnounceFieldWar:
                foreach (GameSession session in server.GetSessions()) {
                    session.AnnounceFieldWar(request.AnnounceFieldWar.FieldWarId, request.AnnounceFieldWar.EventId);
                }
                return Task.FromResult(new TimeEventResponse());
            case TimeEventRequest.TimeEventOneofCase.CloseFieldWar:
                // The client has no close for the announce: a later enter finds the war gone at the World server.
                foreach (GameSession session in server.GetSessions()) {
                    if (session.FieldWarUid == request.CloseFieldWar.EventId) {
                        session.FieldWarUid = 0;
                    }
                }
                return Task.FromResult(new TimeEventResponse());
            default:
                return Task.FromResult(new TimeEventResponse());
        }
    }

    private TimeEventResponse AnnounceGlobalPortal(TimeEventRequest.Types.AnnounceGlobalPortal portal) {
        if (!serverTableMetadata.TimeEventTable.GlobalPortal.TryGetValue(portal.MetadataId, out GlobalPortalMetadata? metadata)) {
            return new TimeEventResponse();
        }
        foreach (GameSession session in server.GetSessions()) {
            if (session.Field != null && TimeEventRules.PortalAnnouncedIn(session.Field.Metadata.Property.Type) && session.GlobalPortalLevelAllowed()) {
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
        long spawnTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // One row per channel the World server announced the boss on (KMS2), this channel's alone when it named none.
        short[] channels = TimeEventRules.BossRowChannels(boss.Channels, GameServer.GetChannel());

        foreach (GameSession session in server.GetSessions()) {
            foreach (short channel in channels) {
                session.Send(WorldShareInfoPacket.BossAlive(npcId, mapId, channel, spawnTimestamp, true));
            }
            session.Send(LegionBattlePacket.Next(boss.MetadataId, npcId, boss.NextSpawnTimestamp));
        }

        // Spawn the boss NPC in each target map, on a player channel only (GetField would make the map's field).
        if (boss.NoSpawn) {
            return new TimeEventResponse();
        }
        // On the field's thread: the spawn adds to the field's NPCs and navigation crowd, and the unique check and the
        // boss id set after it are not safe from two threads (two announces could each spawn a boss).
        int eventId = boss.EventId;
        foreach (int targetMapId in metadata.TargetMapIds) {
            FieldManager? field = server.GetField(targetMapId);
            field?.RunOnField(() => field.SpawnWorldBoss(metadata, eventId));
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

            QueueCloseWorldBoss(field, npcId, targetMapId);
        }

        return new TimeEventResponse();
    }

    // On the field's thread, after a spawn the announce queued there: a close run at once could come before that
    // spawn, find no boss, and the boss would then stay with no idle monitor.
    internal void QueueCloseWorldBoss(FieldManager field, int npcId, int targetMapId) {
        field.RunOnField(() => CloseWorldBoss(field, npcId, targetMapId, Environment.TickCount64));
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

    // Each check runs on the field's thread (beside the spawn and the close queued there); this task only waits.
    private async Task MonitorExtendedBossLifetimeAsync(FieldManager field, int npcId, int targetMapId) {
        var watch = new BossWatch();
        try {
            while (!field.Disposed) {
                await Task.Delay(Constant.WorldBossMonitorInterval);

                var check = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                field.RunOnField(() => {
                    try {
                        check.TrySetResult(MonitorBossTick(field, npcId, watch, Environment.TickCount64));
                    } catch (Exception ex) {
                        check.TrySetException(ex);
                    }
                });
                // A field disposed with the check still queued never runs it.
                while (!check.Task.IsCompleted && !field.Disposed) {
                    await Task.WhenAny(check.Task, Task.Delay(Constant.WorldBossMonitorInterval));
                }
                if (!check.Task.IsCompleted || !await check.Task) {
                    return;
                }
            }
        } catch (Exception ex) {
            logger.Error(ex, "Error monitoring field boss lifetime for NPC {NpcId}", npcId);
        } finally {
            monitoringBossFields.TryRemove(targetMapId, out _);
        }
    }

    internal sealed class BossWatch {
        public bool WarningSent;
    }

    // One idle check of the monitor, on the field's thread. False when the watch ends (the boss is gone or leaves).
    internal static bool MonitorBossTick(FieldManager field, int npcId, BossWatch watch, long nowTick) {
        FieldNpc? bossNpc = field.GetWorldBossNpc();
        if (bossNpc == null) return false;

        long idleMs = bossNpc.LastDamageTick == 0
            ? long.MaxValue
            : nowTick - bossNpc.LastDamageTick;

        if (!watch.WarningSent && idleMs >= (long) Constant.WorldBossIdleWarningThreshold.TotalMilliseconds) {
            field.Broadcast(NoticePacket.Message(new InterfaceText(StringCode.s_timeevent_boss_lifetimetext1, npcId.ToString()), LifetimeNotice));
            watch.WarningSent = true;
        }

        if (idleMs >= (long) Constant.WorldBossDespawnThreshold.TotalMilliseconds) {
            field.Broadcast(NoticePacket.Message(new InterfaceText(StringCode.s_timeevent_boss_lifetimetext2, npcId.ToString()), LifetimeNotice));
            field.DespawnWorldBoss();
            return false;
        }
        return true;
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
