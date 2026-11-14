using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.Manager.Field;

public partial class FieldManager {
    // The timeEventData type 2 rows of the map (FieldRoomEventRules): at each run, at the roll of the row's prob, the
    // field opens roomCount random room portals at free points of the row for what is left of lifeTime. The field
    // reads them at its first update (a run alive then is opened for what is left of it, unless the map placed it
    // already in a field disposed since) and they stop with it.
    private sealed class RoomEventState {
        public required FieldRoomEventMetadata Row { get; init; }
        public required List<int> Points { get; init; }
        public DateTime? NextRun;
    }

    private List<RoomEventState>? roomEvents;
    // The portal each point holds; a point is free again once its portal is gone (closed or ended).
    private readonly Dictionary<int, FieldPortal> roomEventPortals = new();

    private List<RoomEventState> RoomEventStates() {
        if (roomEvents != null) {
            return roomEvents;
        }
        roomEvents = [];
        // As the field interact events: only the map's one field of the channel.
        if (this is DungeonFieldManager or HomeFieldManager || RankDuel != null || Arena != null
            || (ServerTableMetadata.InstanceFieldTable.Entries.TryGetValue(MapId, out InstanceFieldMetadata? instance)
                && instance.Type != InstanceType.channelScale)) {
            return roomEvents;
        }
        IEnumerable<FieldRoomEventMetadata> rows = ServerTableMetadata.TimeEventTable.FieldRoomEvent?.Values ?? [];
        HashSet<int> mapPoints = Entities.RegionSpawns.Keys.ToHashSet();
        foreach (FieldRoomEventMetadata row in rows.Where(row => row.MapIds.Contains(MapId)).OrderBy(row => row.Id)) {
            List<int> points = FieldRoomEventRules.Points(row, Metadata.Spawns, mapPoints);
            if (points.Count > 0) {
                roomEvents.Add(new RoomEventState { Row = row, Points = points });
            }
        }
        return roomEvents;
    }

    internal void UpdateRoomEvents(DateTime now) {
        foreach (RoomEventState state in RoomEventStates()) {
            if (state.NextRun == null) {
                if (FieldRoomEventRules.CurrentRun(state.Row, now) is { } current
                    && !(InteractEventRunsPlaced.TryGetValue((MapId, state.Row.Id), out DateTime placed) && placed == current)) {
                    RunRoomEvent(state, now, current, current + state.Row.LifeTime);
                }
                state.NextRun = FieldRoomEventRules.NextRun(state.Row, now, Random.Shared.NextDouble()) ?? DateTime.MaxValue;
                continue;
            }
            if (now < state.NextRun) {
                continue;
            }
            // An empty field is not updated (FieldManager.Update): a late run gets what is left of it.
            DateTime start = FieldInteractEventRules.DueRun(state.NextRun.Value, FieldRoomEventRules.CurrentRun(state.Row, now));
            RunRoomEvent(state, now, start, start + state.Row.LifeTime);
            state.NextRun = FieldRoomEventRules.NextRun(state.Row, now, Random.Shared.NextDouble()) ?? DateTime.MaxValue;
        }
    }

    private void RunRoomEvent(RoomEventState state, DateTime now, DateTime start, DateTime ends) {
        // The run counts as placed whether or not it opens a portal (its prob roll is not made again). The record is
        // the type 4 one: row ids are unique across timeEventData.
        InteractEventRunsPlaced[(MapId, state.Row.Id)] = FieldRoomEventRules.CurrentRun(state.Row, now) ?? start;
        int seconds = FieldTriggerEventRules.SecondsLeft(now, ends);
        if (seconds <= 0 || !FieldInteractEventRules.Runs(state.Row.Probability, Random.Shared.Next(100))) {
            return;
        }
        List<int> free = state.Points.Where(point => !roomEventPortals.TryGetValue(point, out FieldPortal? portal)
            || !fieldPortals.ContainsKey(portal.ObjectId)).ToList();
        foreach (int spawnId in FieldInteractEventRules.Pick(free, state.Row.RoomCount, Random.Shared)) {
            if (!Entities.RegionSpawns.TryGetValue(spawnId, out Ms2RegionSpawn? spawn)) {
                continue;
            }
            FieldPortal? portal = SpawnRandomRoomPortal(state.Row.RandomRoomId, seconds, spawn.Position, spawn.Rotation);
            if (portal != null) {
                roomEventPortals[spawnId] = portal;
            }
        }
    }
}
